using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;

namespace WiiFitToVRC.Core.Bluetooth;

public enum PairingResult
{
    Success,
    NoDeviceFound,
    Error,
}

public record PairingOutcome(PairingResult Result, string? DeviceAddress, string? Message);

/// <summary>
/// Pairs Wii peripherals (Balance Board, Wiimote) discovered via their SYNC button, matching
/// WiiBalanceWalker's default (Permanent Sync unchecked) behavior: no PIN/PairRequest is needed
/// at all. Simply enabling the HID service on a freshly sync-discovered device is enough --
/// Windows completes the (non-permanent) bonding itself as part of installing the HID service.
/// Explicit PairRequest with a computed PIN was tried first and consistently failed mutual
/// authentication (Windows event log BTHUSB/16) on this machine's generic Bluetooth driver, even
/// though it matches WiiBalanceWalker's optional "Permanent sync" code path exactly.
/// </summary>
public static class BalanceBoardPairing
{
    public static PairingOutcome PairAndInstall(string nameContains = "Nintendo", bool removeExisting = true, int discoveryAttempts = 100, CancellationToken cancellationToken = default)
    {
        using var btClient = new BluetoothClient();

        if (removeExisting)
        {
            var existing = btClient.DiscoverDevices(255, false, true, false);
            foreach (var item in existing)
            {
                if (!item.DeviceName.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                BluetoothSecurity.RemoveDevice(item.DeviceAddress);
                item.SetServiceState(BluetoothService.HumanInterfaceDevice, false);
            }
        }

        // Each DiscoverDevices() call only sees the board if it's actively in SYNC mode *during*
        // that scan, so retry (checking for cancellation between attempts, since a single scan
        // can't be aborted mid-flight) to tolerate a delayed button press.
        BluetoothDeviceInfo? target = null;
        int discoveredCount = 0;
        for (int attempt = 0; attempt < discoveryAttempts && target is null; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var discovered = btClient.DiscoverDevices(255, false, false, true);
            discoveredCount = discovered.Length;
            target = discovered.FirstOrDefault(d => d.DeviceName.Contains(nameContains, StringComparison.OrdinalIgnoreCase));
        }

        if (target is null)
        {
            return new PairingOutcome(PairingResult.NoDeviceFound, null, $"'{nameContains}' を含むデバイスが見つかりませんでした(未検出: {discoveredCount}件)");
        }

        try
        {
            target.SetServiceState(BluetoothService.HumanInterfaceDevice, true);

            return new PairingOutcome(PairingResult.Success, target.DeviceAddress.ToString(), null);
        }
        catch (Exception ex)
        {
            return new PairingOutcome(PairingResult.Error, target.DeviceAddress.ToString(), ex.Message);
        }
    }
}
