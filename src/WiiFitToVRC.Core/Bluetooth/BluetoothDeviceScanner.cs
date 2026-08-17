using InTheHand.Net.Sockets;

namespace WiiFitToVRC.Core.Bluetooth;

public sealed record DetectedBluetoothDevice(string Address, string Name);

/// <summary>
/// Repeatedly runs a Bluetooth device inquiry on a background thread while active, accumulating
/// every device seen (both already-remembered and freshly-discoverable ones) keyed by address so
/// a later scan's name overwrites an earlier blank one. Used by MonitorForm's debug "devices"
/// recording mode: when a balance board isn't recognized (e.g. an unofficial Korean-market model
/// whose name/model number BalanceBoardPairing doesn't match), the user can instead see every
/// nearby Bluetooth device's raw name and address to identify and report it.
///
/// Each DiscoverDevices(unknown: true) call already blocks for a full inquiry cycle (~10+
/// seconds), which is what makes repeated calls behave like continuous scanning on its own -- no
/// extra delay is inserted between successful calls. Stop() never waits for an in-flight call to
/// return: it cancels and hands back whatever was accumulated so far, so the UI thread is never
/// blocked for the remainder of a scan that's already underway.
/// </summary>
public sealed class BluetoothDeviceScanner
{
    private const int ErrorRetryDelayMs = 500;

    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _task;
    private Dictionary<string, string>? _devicesByAddress;

    public bool IsRunning => _task is not null;

    public void Start()
    {
        if (_task is not null)
        {
            return;
        }

        var devicesByAddress = new Dictionary<string, string>();
        _devicesByAddress = devicesByAddress;
        var cts = new CancellationTokenSource();
        _cts = cts;
        _task = Task.Run(() => ScanLoop(devicesByAddress, cts.Token), cts.Token);
    }

    public IReadOnlyList<DetectedBluetoothDevice> Stop()
    {
        if (_task is null || _devicesByAddress is null)
        {
            return [];
        }

        _cts?.Cancel();
        List<DetectedBluetoothDevice> snapshot;
        lock (_lock)
        {
            snapshot = _devicesByAddress.Select(kv => new DetectedBluetoothDevice(kv.Key, kv.Value)).ToList();
        }

        _task = null;
        _cts = null;
        _devicesByAddress = null;
        return snapshot;
    }

    // Runs on its own background task, one per Start()/Stop() generation -- devicesByAddress and
    // token are captured per-call rather than read from fields, so a straggling scan from a
    // previous generation (still mid-inquiry when Stop() returned) can never write into the next
    // generation's dictionary.
    private void ScanLoop(Dictionary<string, string> devicesByAddress, CancellationToken token)
    {
        using var btClient = new BluetoothClient();
        while (!token.IsCancellationRequested)
        {
            try
            {
                var found = btClient.DiscoverDevices(255, authenticated: false, remembered: true, unknown: true);
                lock (_lock)
                {
                    foreach (var d in found)
                    {
                        devicesByAddress[d.DeviceAddress.ToString()] = d.DeviceName ?? "";
                    }
                }
            }
            catch (Exception)
            {
                // Transient discovery failures (radio busy, etc.) -- just retry.
                token.WaitHandle.WaitOne(ErrorRetryDelayMs);
            }
        }
    }
}
