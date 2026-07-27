using System.Runtime.InteropServices;

namespace WiiFitToVRC.Core.Input;

public enum VirtualKey : ushort
{
    Backspace = 0x08,
    Tab = 0x09,
    Enter = 0x0D,
    Shift = 0x10,
    Ctrl = 0x11,
    Alt = 0x12,
    Escape = 0x1B,
    Space = 0x20,
    Left = 0x25,
    Up = 0x26,
    Right = 0x27,
    Down = 0x28,
    D0 = 0x30,
    D1 = 0x31,
    D2 = 0x32,
    D3 = 0x33,
    D4 = 0x34,
    D5 = 0x35,
    D6 = 0x36,
    D7 = 0x37,
    D8 = 0x38,
    D9 = 0x39,
    A = 0x41,
    B = 0x42,
    C = 0x43,
    D = 0x44,
    E = 0x45,
    F = 0x46,
    G = 0x47,
    H = 0x48,
    I = 0x49,
    J = 0x4A,
    K = 0x4B,
    L = 0x4C,
    M = 0x4D,
    N = 0x4E,
    O = 0x4F,
    P = 0x50,
    Q = 0x51,
    R = 0x52,
    S = 0x53,
    T = 0x54,
    U = 0x55,
    V = 0x56,
    W = 0x57,
    X = 0x58,
    Y = 0x59,
    Z = 0x5A,
}

/// <summary>Sends real keyboard key down/up events via SendInput, for the keybind output mode.</summary>
public static class KeySender
{
    private static readonly HashSet<VirtualKey> HeldKeys = [];
    private static bool _lastLoggedBlocked;

    public static void KeyDown(VirtualKey key)
    {
        if (ForegroundGuard.IsOwnWindowForeground())
        {
            // Edge-triggered (not every sample -- this is called at HID report rate) diagnostic
            // for tracking down reports of "keys aren't reaching the target app".
            if (!_lastLoggedBlocked)
            {
                Console.Error.WriteLine($"[KeySender] KeyDown({key}) blocked: own window is foreground");
                _lastLoggedBlocked = true;
            }
            return; // don't type into our own window -- retried every sample, so it fires for
                     // real as soon as focus moves to the intended target instead
        }
        if (_lastLoggedBlocked)
        {
            Console.Error.WriteLine("[KeySender] no longer blocked: focus moved off our window");
            _lastLoggedBlocked = false;
        }
        if (!HeldKeys.Add(key))
        {
            return; // already held
        }
        Send(key, 0);
    }

    public static void KeyUp(VirtualKey key)
    {
        if (!HeldKeys.Remove(key))
        {
            return; // wasn't held
        }
        Send(key, NativeInput.KEYEVENTF_KEYUP);
    }

    /// <summary>Presses and immediately releases a key -- for toggle-style bindings (jump, crouch)
    /// where the target treats each press as a discrete event rather than a hold.</summary>
    public static void Tap(VirtualKey key)
    {
        KeyDown(key);
        KeyUp(key);
    }

    /// <summary>Releases every key this sender currently believes is held (e.g. on disconnect/exit).</summary>
    public static void ReleaseAll()
    {
        foreach (var key in HeldKeys.ToArray())
        {
            KeyUp(key);
        }
    }

    // Extended-key scan codes (arrow cluster, etc.) need KEYEVENTF_EXTENDEDKEY or they map to the
    // wrong physical key (e.g. the numpad instead of the arrow cluster).
    private static readonly HashSet<VirtualKey> ExtendedKeys = [VirtualKey.Left, VirtualKey.Up, VirtualKey.Right, VirtualKey.Down];

    private static void Send(VirtualKey key, uint upFlag)
    {
        // A plain virtual-key SendInput event reached Notepad fine but was silently ignored by
        // VRChat -- a real physical keystroke always carries a hardware scan code, and games that
        // filter out "too synthetic" input (no scan code) apparently include VRChat. Sending the
        // scan code instead (what a real keyboard driver would report, via MapVirtualKey) matches
        // what a known-working tool (JoyToKey) does and is accepted the same way.
        uint scanCode = NativeInput.MapVirtualKey((uint)key, NativeInput.MAPVK_VK_TO_VSC);
        uint flags = NativeInput.KEYEVENTF_SCANCODE | upFlag;
        if (ExtendedKeys.Contains(key))
        {
            flags |= NativeInput.KEYEVENTF_EXTENDEDKEY;
        }

        var input = new INPUT
        {
            type = NativeInput.INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = 0, wScan = (ushort)scanCode, dwFlags = flags } },
        };
        uint sent = NativeInput.SendInput(1, [input], Marshal.SizeOf<INPUT>());
        if (sent == 0)
        {
            int error = Marshal.GetLastWin32Error();
            Console.Error.WriteLine($"[KeySender] SendInput FAILED key={key} flags={flags} error={error}");
        }
    }
}
