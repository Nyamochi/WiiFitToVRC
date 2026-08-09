using System.Text.RegularExpressions;
using System.Threading;
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
    /// <summary>
    /// Matches any of the balance board's known model-number forms, which vary by hardware
    /// revision/color code and region (e.g. "RVL-021-JPN", "RVL-A-BC-JPN-1", "(CW)RVL-A-BC-JPN",
    /// "RVL-A-BC(JPN)"), so pairing isn't limited to the single literal name string.
    /// </summary>
    private static readonly Regex BalanceBoardModelRegex = new(
        @"(\([A-Z]{1,4}\))?RVL-([0-9]{3}|[A-Z]-BC)((-[A-Z]{3,4})|(\([A-Z]{3,4}\)))?(-[0-9]+)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Matches the plain, previously-sufficient name-substring check first (e.g. "Nintendo"), and
    /// only reaches for the model-number regex as a fallback when nothing in <paramref
    /// name="devices"/> matches that way -- the substring check alone already worked reliably, so
    /// it stays the primary path and the regex only broadens matching when it comes up empty.
    /// </summary>
    private static IEnumerable<T> MatchDevices<T>(IEnumerable<T> devices, string nameContains, Func<T, string> nameOf)
    {
        var list = devices as ICollection<T> ?? devices.ToList();
        var byName = list.Where(d => nameOf(d).Contains(nameContains, StringComparison.OrdinalIgnoreCase)).ToList();
        return byName.Count > 0 ? byName : list.Where(d => BalanceBoardModelRegex.IsMatch(nameOf(d)));
    }

    // Interval between reconnect nudges in ReconnectRemembered -- see its own comment.
    private const int RememberedNudgeIntervalMs = 300;

    /// <summary>
    /// One-shot check for whether Windows already has a remembered (bonded) device record
    /// matching the board, from an earlier SYNC pairing. The caller uses this to decide upfront
    /// which of two entirely separate strategies to use -- <see cref="ReconnectRemembered"/> if
    /// one exists, <see cref="PairAndInstall"/> if not -- rather than alternating between both on
    /// a timer, which left gaps where a present profile still went unused for several seconds at
    /// a time while a SYNC-mode scan was running. Returns a plain bool (not the underlying
    /// InTheHand.Net type) so callers outside this project don't need a direct reference to it.
    /// </summary>
    public static bool HasRememberedDevice(string nameContains = "Nintendo")
    {
        using var btClient = new BluetoothClient();
        var remembered = btClient.DiscoverDevices(255, false, true, false);
        return MatchDevices(remembered, nameContains, d => d.DeviceName).Any();
    }

    /// <summary>
    /// Repeatedly re-asserts the HID service on the remembered device matching <paramref
    /// name="nameContains"/> and checks whether the Bluetooth link actually came up, indefinitely,
    /// until it does or the caller cancels. The board is only connectable for roughly 2 seconds
    /// after its plain power button is pressed, so a single attempt essentially never lands inside
    /// that window -- this keeps retrying at a steady interval instead, for as long as it takes,
    /// since there's no way to know in advance when the user will actually press power. Only call
    /// this after <see cref="HasRememberedDevice"/> confirms a match exists.
    /// </summary>
    public static PairingOutcome ReconnectRemembered(string nameContains, CancellationToken cancellationToken)
    {
        using var btClient = new BluetoothClient();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var remembered = btClient.DiscoverDevices(255, false, true, false);
                var device = MatchDevices(remembered, nameContains, d => d.DeviceName).FirstOrDefault();
                if (device is null)
                {
                    return new PairingOutcome(PairingResult.NoDeviceFound, null, null);
                }

                device.SetServiceState(BluetoothService.HumanInterfaceDevice, true);
                Thread.Sleep(RememberedNudgeIntervalMs);
                device.Refresh(); // Connected is cached at discovery time -- must refresh first
                if (device.Connected)
                {
                    return new PairingOutcome(PairingResult.Success, device.DeviceAddress.ToString(), null);
                }
            }
            catch (Exception)
            {
                // Transient Bluetooth API hiccup -- keep retrying rather than giving up, since
                // this is meant to search indefinitely until the user explicitly aborts to SYNC.
            }
        }
    }

    /// <summary>
    /// Discovers the board while it's actively in SYNC mode and pairs it. Used when Windows has no
    /// remembered profile for it at all (first-ever pairing), or when the user explicitly asks for
    /// a fresh SYNC pairing (see <see cref="ForceSyncPairAndInstall"/>).
    /// </summary>
    public static PairingOutcome PairAndInstall(string nameContains = "Nintendo", CancellationToken cancellationToken = default)
    {
        using var btClient = new BluetoothClient();

        // Each DiscoverDevices() call only sees the board if it's actively in SYNC mode *during*
        // that scan, so keep scanning with no attempt cap or timeout -- the caller runs this on a
        // background thread and the user cancels via the UI whenever they like, so there's no
        // reason to ever give up on our own.
        BluetoothDeviceInfo? target = null;
        while (target is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var discovered = btClient.DiscoverDevices(255, false, false, true);
            target = MatchDevices(discovered, nameContains, d => d.DeviceName).FirstOrDefault();
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

    /// <summary>
    /// Erases any existing remembered/bonded profile for the board and pairs fresh via SYNC mode
    /// only -- skips <see cref="PairAndInstall"/>'s remembered-profile fast path entirely. Manual
    /// escape hatch for a stuck/broken stored bond that keeps failing to actually reconnect no
    /// matter how many times the fast path nudges it (the app's "abort and use SYNC" button).
    /// </summary>
    public static PairingOutcome ForceSyncPairAndInstall(string nameContains = "Nintendo", CancellationToken cancellationToken = default)
    {
        using var btClient = new BluetoothClient();

        var existing = btClient.DiscoverDevices(255, false, true, false);
        foreach (var item in MatchDevices(existing, nameContains, d => d.DeviceName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            BluetoothSecurity.RemoveDevice(item.DeviceAddress);
            item.SetServiceState(BluetoothService.HumanInterfaceDevice, false);
        }

        BluetoothDeviceInfo? target = null;
        while (target is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var discovered = btClient.DiscoverDevices(255, false, false, true);
            target = MatchDevices(discovered, nameContains, d => d.DeviceName).FirstOrDefault();
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
