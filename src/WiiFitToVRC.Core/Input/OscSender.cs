using System.Net;
using System.Net.Sockets;
using System.Text;

namespace WiiFitToVRC.Core.Input;

/// <summary>
/// Sends movement/turn/jump state to VRChat's own OSC "Input Controller" endpoint
/// (https://docs.vrchat.com/docs/osc-as-input-controller) as an alternative to synthetic
/// keyboard/mouse SendInput. Some VR headsets/runtimes lock the game's input focus to the VR
/// device and ignore SendInput (keyboard, mouse, and even virtual-gamepad) entirely; OSC arrives
/// over a local UDP socket instead, so it isn't subject to that filtering.
///
/// Only float axes and int/bool buttons that VRChat's OSC input actually defines are used here --
/// notably there is no OSC address for crouch, so crouch is not sent through this path at all
/// (InputController falls back to a normal key press for it even in OSC mode).
/// </summary>
public sealed class OscSender : IDisposable
{
    public const string MoveVerticalAddress = "/input/Vertical";
    public const string MoveHorizontalAddress = "/input/Horizontal";
    public const string LookLeftAddress = "/input/LookLeft";
    public const string LookRightAddress = "/input/LookRight";
    public const string JumpAddress = "/input/Jump";
    public const string RunAddress = "/input/Run";

    // VRChat's default local OSC receive port. Not exposed as a setting since VRChat and this app
    // always run on the same machine, and changing VRChat's OSC port is not a normal user action.
    private static readonly IPEndPoint TargetEndpoint = new(IPAddress.Loopback, 9000);

    private readonly UdpClient _client = new();

    private double _lastVertical = double.NaN;
    private double _lastHorizontal = double.NaN;
    private bool? _lastLookLeft;
    private bool? _lastLookRight;
    private bool? _lastJump;
    private bool? _lastRun;

    public void SetMoveAxis(double vertical, double horizontal)
    {
        SendFloatIfChanged(MoveVerticalAddress, vertical, ref _lastVertical);
        SendFloatIfChanged(MoveHorizontalAddress, horizontal, ref _lastHorizontal);
    }

    // Turning uses the discrete LookLeft/LookRight buttons, not the LookHorizontal float axis --
    // VRChat's OSC input documentation lists LookHorizontal too, but it didn't reliably turn the
    // character in practice even while the float value was visibly changing in VRChat's own OSC
    // debug view; LookLeft/LookRight (smooth in Desktop, snap-turn in VR with Comfort Turning on)
    // are the buttons an actual VR controller's turn input maps to.
    public void SetLookLeft(bool pressed) => SendBoolIfChanged(LookLeftAddress, pressed, ref _lastLookLeft);

    public void SetLookRight(bool pressed) => SendBoolIfChanged(LookRightAddress, pressed, ref _lastLookRight);

    public void SetJump(bool pressed) => SendBoolIfChanged(JumpAddress, pressed, ref _lastJump);

    public void SetRun(bool pressed) => SendBoolIfChanged(RunAddress, pressed, ref _lastRun);

    private void SendFloatIfChanged(string address, double value, ref double last)
    {
        if (value == last)
        {
            return;
        }
        last = value;
        SendMessage(address, "f", ToBigEndian(BitConverter.GetBytes((float)value)));
    }

    private void SendBoolIfChanged(string address, bool value, ref bool? last)
    {
        if (last == value)
        {
            return;
        }
        last = value;
        SendMessage(address, "i", ToBigEndian(BitConverter.GetBytes(value ? 1 : 0)));
    }

    private static byte[] ToBigEndian(byte[] littleEndianOrNative)
    {
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(littleEndianOrNative);
        }
        return littleEndianOrNative;
    }

    private void SendMessage(string address, string typeTag, byte[] argBytes)
    {
        try
        {
            var packet = new byte[PaddedLength(address) + PaddedLength("," + typeTag) + argBytes.Length];
            int offset = 0;
            offset = WriteOscString(packet, offset, address);
            offset = WriteOscString(packet, offset, "," + typeTag);
            Buffer.BlockCopy(argBytes, 0, packet, offset, argBytes.Length);

            _client.Send(packet, packet.Length, TargetEndpoint);
        }
        catch (SocketException)
        {
            // Best-effort: a dropped OSC packet just skips one movement frame, not worth
            // surfacing as an error (VRChat may simply not be running yet).
        }
    }

    // OSC strings are ASCII, null-terminated, then padded with extra nulls so the total length is
    // a multiple of 4 -- at least one null is always present, even if the raw string is already a
    // multiple of 4 bytes long.
    private static int PaddedLength(string s)
    {
        int raw = Encoding.ASCII.GetByteCount(s);
        int remainder = raw % 4;
        return raw + (remainder == 0 ? 4 : 4 - remainder);
    }

    private static int WriteOscString(byte[] buffer, int offset, string s)
    {
        Encoding.ASCII.GetBytes(s, 0, s.Length, buffer, offset);
        return offset + PaddedLength(s); // the rest of the padded region is already zero-filled
    }

    public void Dispose() => _client.Dispose();
}
