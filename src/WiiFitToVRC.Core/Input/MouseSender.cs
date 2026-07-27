using System.Runtime.InteropServices;

namespace WiiFitToVRC.Core.Input;

/// <summary>Sends relative mouse movement via SendInput, for turn-via-mouse-look output mode.</summary>
public static class MouseSender
{
    public static void MoveRelative(int dx, int dy = 0)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }
        if (ForegroundGuard.IsOwnWindowForeground())
        {
            return; // don't move the cursor while our own window has focus
        }

        var input = new INPUT
        {
            type = NativeInput.INPUT_MOUSE,
            u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = NativeInput.MOUSEEVENTF_MOVE } },
        };
        uint sent = NativeInput.SendInput(1, [input], Marshal.SizeOf<INPUT>());
        if (sent == 0)
        {
            int error = Marshal.GetLastWin32Error();
            Console.Error.WriteLine($"[MouseSender] SendInput FAILED dx={dx} dy={dy} error={error}");
        }
    }
}
