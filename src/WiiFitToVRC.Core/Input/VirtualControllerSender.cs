using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace WiiFitToVRC.Core.Input;

/// <summary>Buttons offered for controller-mode bindings, independent of the ViGEm library's own
/// Xbox360Button type -- keeps that third-party type out of AppSettings' JSON schema.</summary>
public enum ControllerButton
{
    A,
    B,
    X,
    Y,
    LeftShoulder,
    RightShoulder,
    LeftThumb,
    RightThumb,
    Back,
    Start,
}

/// <summary>
/// VRChat (and evidently other games) filters out SendInput-synthesized keyboard/mouse events --
/// they're not treated as real player input. A virtual Xbox 360 controller, driven through the
/// ViGEmBus kernel driver, shows up to Windows (and the game) as genuine HID gamepad input
/// instead, which isn't filtered the same way.
///
/// This requires ViGEmBus to actually be installed on the machine (https://github.com/nefarius/ViGEmBus/releases)
/// -- it's a real driver, not something this app can install on its own. If it's missing,
/// connecting throws VigemBusNotFoundException; IsAvailable reports that state so the UI can show
/// a clear message instead of crashing.
/// </summary>
public sealed class VirtualControllerSender : IDisposable
{
    private ViGEmClient? _client;
    private IXbox360Controller? _controller;
    private bool _connectAttempted;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }

    public void Connect()
    {
        if (_controller is not null || _connectAttempted)
        {
            // Only ever try once -- this gets called on every HID sample (~75-100Hz) while
            // controller mode is selected, and retrying a failed driver connection that fast
            // would just spam exceptions instead of surfacing one clear "not installed" state.
            return;
        }
        _connectAttempted = true;

        try
        {
            _client = new ViGEmClient();
            _controller = _client.CreateXbox360Controller();
            _controller.Connect();
            IsAvailable = true;
            UnavailableReason = null;
        }
        catch (VigemBusNotFoundException)
        {
            UnavailableReason = "ViGEmBus driver not installed";
            Cleanup();
        }
        catch (Exception ex)
        {
            UnavailableReason = ex.Message;
            Cleanup();
        }
    }

    public void SetLeftStick(double x, double y) => SetStick(Xbox360Axis.LeftThumbX, Xbox360Axis.LeftThumbY, x, y);
    public void SetRightStick(double x, double y) => SetStick(Xbox360Axis.RightThumbX, Xbox360Axis.RightThumbY, x, y);

    private void SetStick(Xbox360Axis axisX, Xbox360Axis axisY, double x, double y)
    {
        if (_controller is null)
        {
            return;
        }
        _controller.SetAxisValue(axisX, ToAxisValue(x));
        _controller.SetAxisValue(axisY, ToAxisValue(y));
    }

    public void SetButton(ControllerButton button, bool pressed)
    {
        _controller?.SetButtonState(ToXbox360Button(button), pressed);
    }

    /// <summary>Presses and immediately releases a button -- for toggle-style bindings (crouch)
    /// where the target treats each press as a discrete event rather than a hold.</summary>
    public void TapButton(ControllerButton button)
    {
        SetButton(button, true);
        SetButton(button, false);
    }

    public void ResetAll()
    {
        if (_controller is null)
        {
            return;
        }
        SetLeftStick(0, 0);
        SetRightStick(0, 0);
        foreach (ControllerButton button in Enum.GetValues<ControllerButton>())
        {
            SetButton(button, false);
        }
    }

    private static Xbox360Button ToXbox360Button(ControllerButton button) => button switch
    {
        ControllerButton.A => Xbox360Button.A,
        ControllerButton.B => Xbox360Button.B,
        ControllerButton.X => Xbox360Button.X,
        ControllerButton.Y => Xbox360Button.Y,
        ControllerButton.LeftShoulder => Xbox360Button.LeftShoulder,
        ControllerButton.RightShoulder => Xbox360Button.RightShoulder,
        ControllerButton.LeftThumb => Xbox360Button.LeftThumb,
        ControllerButton.RightThumb => Xbox360Button.RightThumb,
        ControllerButton.Back => Xbox360Button.Back,
        ControllerButton.Start => Xbox360Button.Start,
        _ => Xbox360Button.A,
    };

    // -1..1 -> the full short range the Xbox360 axis API expects.
    private static short ToAxisValue(double v) => (short)Math.Round(Math.Clamp(v, -1, 1) * short.MaxValue);

    private void Cleanup()
    {
        _controller = null;
        _client?.Dispose();
        _client = null;
        IsAvailable = false;
    }

    public void Dispose()
    {
        try
        {
            ResetAll();
            _controller?.Disconnect();
        }
        catch (Exception)
        {
            // Best-effort on shutdown -- the driver connection may already be gone.
        }
        Cleanup();
    }
}
