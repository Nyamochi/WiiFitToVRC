using System.Text.RegularExpressions;
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

    public static PairingOutcome PairAndInstall(string nameContains = "Nintendo", bool removeExisting = true, CancellationToken cancellationToken = default)
    {
        using var btClient = new BluetoothClient();

        if (removeExisting)
        {
            var existing = btClient.DiscoverDevices(255, false, true, false);
            foreach (var item in MatchDevices(existing, nameContains, d => d.DeviceName))
            {
                BluetoothSecurity.RemoveDevice(item.DeviceAddress);
                item.SetServiceState(BluetoothService.HumanInterfaceDevice, false);
            }
        }

        // Each DiscoverDevices() call only sees the board if it's actively in SYNC mode *during*
        // that scan, so keep scanning with no attempt cap or timeout -- the caller runs this on a
        // background thread and the user cancels via the UI whenever they like, so there's no
        // reason to ever give up on our own and report "not found" while they're still trying.
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
