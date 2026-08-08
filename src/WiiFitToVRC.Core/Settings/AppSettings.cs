using System.Text.Json;
using WiiFitToVRC.Core.Input;

namespace WiiFitToVRC.Core.Settings;

/// <summary>How gestures reach the target application. Keyboard/KeyboardMouse both use
/// SendInput; they differ only in how turning is sent (key vs. mouse-look). Controller emulates a
/// virtual Xbox 360 pad via ViGEmBus -- some games (VRChat included) filter out SendInput-
/// synthesized keyboard/mouse input but accept genuine-looking HID gamepad input. Osc sends
/// movement/turn/jump to VRChat's own OSC input endpoint over UDP, which bypasses SendInput (and
/// therefore window-focus filtering) entirely -- useful when the game's input is locked to a VR
/// device and won't accept SendInput at all, even the virtual controller. VRChat has no OSC
/// address for crouch, so crouch always falls back to a plain key press regardless of this mode.</summary>
public enum OutputMode
{
    Keyboard,
    KeyboardMouse,
    Controller,
    Osc,
}

public enum AppLanguage
{
    Auto,
    English,
    Japanese,
    ChineseSimplified,
    ChineseTraditional,
    Korean,
    French,
    German,
    Italian,
}

public sealed class AppSettings
{
    public AppLanguage Language { get; set; } = AppLanguage.Auto;
    public OutputMode OutputMode { get; set; } = OutputMode.KeyboardMouse;

    /// <summary>Pixels of relative mouse movement sent per tick while turning right/left is
    /// active (OutputMode.KeyboardMouse only). Split per direction since the two can need
    /// different tuning in practice.</summary>
    public int MouseTurnStrokeRight { get; set; } = 5;
    public int MouseTurnStrokeLeft { get; set; } = 5;

    /// <summary>Calibrated total weight that must be exceeded before forward/backward/turn output
    /// is allowed to fire (someone needs to actually be standing on the board).</summary>
    public int PresenceWeightThreshold { get; set; } = 3000;

    /// <summary>Shared timing (seconds) for the presence hysteresis in both directions: how long
    /// the board must stay above PresenceWeightThreshold before output unlocks, and how long it
    /// must stay below it before output re-locks.</summary>
    public int SleepSeconds { get; set; } = 3;

    public bool CrouchEnabled { get; set; } = true;
    public bool JumpEnabled { get; set; } = true;

    /// <summary>How far above a corner's reference resting weight (see ReferenceWeightCalibrator)
    /// it must rise to count as a footstep for forward/backward detection, as a percentage
    /// (e.g. 120 = 120%).</summary>
    public int FootstepThresholdPercent { get; set; } = 120;

    /// <summary>Peak-to-peak interval (ms) between opposite-foot footsteps fast enough to count as
    /// a dash ("ダッシュ判定") instead of an ordinary walk.</summary>
    public int DashPeriodMs { get; set; } = 300;

    /// <summary>How long a confirmed forward/backward/dash direction persists (ms) after its last
    /// confirming footstep before releasing back to Idle.</summary>
    public int StepHoldMs { get; set; } = 77;

    /// <summary>Shows the raw-data recording controls (label picker, record button) on the main
    /// window -- only useful for capturing gesture logs during tuning, so hidden by default.</summary>
    public bool DebugMode { get; set; }

    public VirtualKey ForwardKey { get; set; } = VirtualKey.W;
    public VirtualKey DashKey { get; set; } = VirtualKey.W;
    public VirtualKey DashModifierKey { get; set; } = VirtualKey.Shift;
    public VirtualKey BackwardKey { get; set; } = VirtualKey.S;
    public VirtualKey TurnRightKey { get; set; } = VirtualKey.E;
    public VirtualKey TurnLeftKey { get; set; } = VirtualKey.Q;
    public VirtualKey JumpKey { get; set; } = VirtualKey.Space;
    public VirtualKey CrouchKey { get; set; } = VirtualKey.C;

    // Controller-mode bindings. Movement/turn use the sticks directly (see InputController), not
    // buttons, so only jump/crouch/dash need a binding here. VRChat's own gamepad defaults: A =
    // jump, left-stick click = sprint; crouch has no default gamepad binding, B is a reasonable pick.
    public ControllerButton JumpButton { get; set; } = ControllerButton.A;
    public ControllerButton CrouchButton { get; set; } = ControllerButton.B;
    public ControllerButton DashButton { get; set; } = ControllerButton.LeftThumb;

    /// <summary>Right-stick deflection (0-100%) while turning right/left in controller mode.</summary>
    public int ControllerTurnStrokeRight { get; set; } = 60;
    public int ControllerTurnStrokeLeft { get; set; } = 60;

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Fall through to defaults -- a corrupt/unreadable settings file shouldn't block startup.
        }

        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
