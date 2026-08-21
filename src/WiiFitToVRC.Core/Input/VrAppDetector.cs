using System.Diagnostics;

namespace WiiFitToVRC.Core.Input;

/// <summary>
/// Backs AppSettings.OutputMode.KeyboardMouseOscAuto: polls for VRChat.exe and vrserver.exe (the
/// actual SteamVR runtime process -- not steam.exe or vrmonitor.exe, which can be running without
/// a live VR session) every PollIntervalMs and reports whether the auto mode should currently
/// resolve to Osc (both running) or KeyboardMouse (VRChat alone, or neither running yet).
/// Process enumeration isn't cheap enough to do on every sample (InputController.Update runs at
/// HID report rate, up to ~100Hz), hence the poll interval rather than checking every call.
/// </summary>
public sealed class VrAppDetector
{
    private const int PollIntervalMs = 10_000;
    private const string VrChatProcessName = "VRChat";
    private const string SteamVrProcessName = "vrserver";

    private long _lastPollMs = -1;

    /// <summary>True once both VRChat and the SteamVR runtime are detected running together.
    /// False otherwise (VRChat alone, SteamVR alone, or neither) -- KeyboardMouse is the safe
    /// default when nothing conclusively points to a locked-input VR session.</summary>
    public bool ShouldUseOsc { get; private set; }

    /// <summary>No-op until PollIntervalMs has elapsed since the last real poll -- the very first
    /// call always polls immediately (so a fresh launch doesn't start with a stale/default
    /// KeyboardMouse guess for a full 10 seconds), see _lastPollMs's initial -1.</summary>
    public void Update(long nowMs)
    {
        if (_lastPollMs >= 0 && nowMs - _lastPollMs < PollIntervalMs)
        {
            return;
        }
        _lastPollMs = nowMs;

        ShouldUseOsc = IsProcessRunning(VrChatProcessName) && IsProcessRunning(SteamVrProcessName);
    }

    private static bool IsProcessRunning(string processName)
    {
        Process[] processes = Process.GetProcessesByName(processName);
        foreach (Process process in processes)
        {
            process.Dispose();
        }
        return processes.Length > 0;
    }
}
