using System.Runtime.InteropServices;

namespace WiiFitToVRC.Core.Input;

/// <summary>
/// Stops synthetic key-down/mouse-move output from landing on this app's own window -- while
/// live-tuning with the monitor window focused, SendInput would otherwise type into its own
/// controls (toggling buttons via their access keys, etc.) instead of reaching the intended
/// target (VRChat or whatever else has focus). Only gates the "start" actions (KeyDown,
/// MoveRelative); KeyUp is never gated, since a key that was actually sent to some other window
/// still needs releasing there even if focus has since moved back to this app.
/// </summary>
internal static class ForegroundGuard
{
    private static readonly uint OwnProcessId = (uint)Environment.ProcessId;

    public static bool IsOwnWindowForeground()
    {
        IntPtr hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }
        GetWindowThreadProcessId(hWnd, out uint processId);
        return processId == OwnProcessId;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
