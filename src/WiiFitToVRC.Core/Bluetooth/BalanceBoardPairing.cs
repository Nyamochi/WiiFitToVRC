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

    // How many quick remembered-profile nudges to try (spaced by RememberedNudgeIntervalMs) before
    // yielding to a slower SYNC-mode scan pass and then looping back for another burst.
    private const int RememberedNudgeAttempts = 8;
    private const int RememberedNudgeIntervalMs = 300;

    public static PairingOutcome PairAndInstall(string nameContains = "Nintendo", CancellationToken cancellationToken = default)
    {
        using var btClient = new BluetoothClient();

        // The board is only connectable for a couple of seconds after its plain power button is
        // pressed -- much shorter than a SYNC hold's window, and far shorter than a single SYNC-
        // mode inquiry scan takes to run. A single SetServiceState call against a remembered
        // (bonded) profile essentially never lands inside that brief window, so this repeatedly
        // re-nudges it in quick succession instead, checking the Bluetooth link itself each time,
        // interleaved with SYNC-mode scan passes -- indefinitely, since there's no way to know in
        // advance which one (or when) will actually catch the board's window.
        while (true)
        {
            for (int i = 0; i < RememberedNudgeAttempts; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var remembered = btClient.DiscoverDevices(255, false, true, false);
                var rememberedMatch = MatchDevices(remembered, nameContains, d => d.DeviceName).FirstOrDefault();
                if (rememberedMatch is null)
                {
                    break; // nothing remembered at all -- go straight to SYNC-mode scanning below
                }

                try
                {
                    rememberedMatch.SetServiceState(BluetoothService.HumanInterfaceDevice, true);
                }
                catch (Exception)
                {
                    break; // stored bond is broken -- rely on the SYNC-mode scan below instead
                }

                Thread.Sleep(RememberedNudgeIntervalMs);
                rememberedMatch.Refresh(); // Connected is cached at discovery time -- must refresh first
                if (rememberedMatch.Connected)
                {
                    return new PairingOutcome(PairingResult.Success, rememberedMatch.DeviceAddress.ToString(), null);
                }
            }

            // One SYNC-mode discovery pass -- if the board is actively in SYNC mode (or its
            // power-on window happens to overlap this scan), it shows up here regardless of any
            // remembered profile. No attempt cap or timeout on the outer loop -- the caller runs
            // this on a background thread and the user cancels via the UI whenever they like.
            cancellationToken.ThrowIfCancellationRequested();
            var discovered = btClient.DiscoverDevices(255, false, false, true);
            var target = MatchDevices(discovered, nameContains, d => d.DeviceName).FirstOrDefault();
            if (target is null)
            {
                continue; // nothing found this cycle -- loop back and keep trying
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
}
