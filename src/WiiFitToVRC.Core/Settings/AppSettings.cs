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

/// <summary>Which turn-detection model DirectionClassifier uses. Hold: X (left-right weight) has
/// to swing past a threshold and stay there continuously. Footstep: alternately stepping on the
/// two diagonal panels (e.g. back-right/front-left for a right turn) confirms a turn on the second
/// step -- added later since it turned out far less prone to firing during ordinary walking, and
/// is the default.</summary>
public enum TurnMode
{
    Hold,
    Footstep,
}

/// <summary>How the keyboard/keyboard+mouse output modes send Dash (Controller/Osc are unaffected
/// -- they always use the dash button / OSC Run regardless of this setting). ComboKey holds the
/// forward key plus a modifier (e.g. Shift+W) -- the original behavior, and the default. DoubleTap
/// instead taps the forward key once and then holds it, matching games whose sprint binding is
/// "double-tap forward" rather than a modifier.</summary>
public enum DashInputMode
{
    ComboKey,
    DoubleTap,
}

public sealed class AppSettings
{
    public AppLanguage Language { get; set; } = AppLanguage.Auto;
    public OutputMode OutputMode { get; set; } = OutputMode.KeyboardMouse;

    /// <summary>Pixels of relative mouse movement sent per tick while turning right/left is
    /// active (OutputMode.KeyboardMouse only). One shared value for both directions.</summary>
    public int MouseTurnSpeed { get; set; } = 5;

    /// <summary>How long (ms) a single confirmed turn step's output is independently held for on
    /// OutputMode.Osc/Controller only -- see InputController.ResolveHeldTurnDirection. A confirmed
    /// turn step from DirectionClassifier only lasts StepHoldMs itself (tens of ms), which wasn't
    /// reliably long enough for VRChat's OSC input (or, for consistency, the virtual controller) to
    /// register a turn -- unlike the mouse-look/keyboard paths, which don't need this. Provisional
    /// default of 1000ms (a full second) -- long enough to register, short enough to still feel
    /// responsive; may need further tuning.</summary>
    public int TurnHoldMs { get; set; } = 1000;

    /// <summary>Calibrated total weight that must be exceeded before forward/backward/turn output
    /// is allowed to fire (someone needs to actually be standing on the board).</summary>
    public int PresenceWeightThreshold { get; set; } = 3000;

    /// <summary>Shared timing (seconds) for the presence hysteresis in both directions: how long
    /// the board must stay above PresenceWeightThreshold before output unlocks, and how long it
    /// must stay below it before output re-locks.</summary>
    public int SleepSeconds { get; set; } = 3;

    /// <summary>Which turn-detection model is active -- see TurnMode. Footstep is the default.</summary>
    public TurnMode TurnMode { get; set; } = TurnMode.Footstep;

    /// <summary>Fed directly into GestureSensitivityScale (0-100, how easily each gesture fires
    /// individually; forward/backward/dash has its own separate footstep-threshold setting
    /// instead). 50 is neutral and reproduces the original hardcoded thresholds exactly; see
    /// GestureSensitivityScale for the exact scaling. 0 fully disables the gesture -- it never
    /// fires regardless of input, replacing what used to be a separate enabled/disabled
    /// toggle.</summary>
    public int JumpSensitivity { get; set; } = 50;
    public int CrouchSensitivity { get; set; } = 50;

    /// <summary>Also fed directly into GestureSensitivityScale like Jump/Crouch above, but unlike
    /// them the *default* isn't the neutral 50 -- real-world testing found the neutral threshold
    /// too hard to trigger, so 60 (a 10% easier-to-trigger multiplier) is the actual default here.
    /// The Settings UI hides this behind its own raw/display split (see SettingsForm's
    /// TurnRawMin/TurnRawMax) so the slider still shows a plain 50 at that default, matching every
    /// other Gesture sensitivity slider's neutral-looks-like-50 convention -- this field always
    /// holds the raw value GestureSensitivityScale actually sees, not the display percentage.</summary>
    public int TurnSensitivity { get; set; } = 60;

    /// <summary>How far above a corner's reference resting weight (see ReferenceWeightCalibrator)
    /// it must rise to count as a footstep for forward/backward detection, as a percentage
    /// (e.g. 120 = 120%).</summary>
    public int FootstepThresholdPercent { get; set; } = 120;

    /// <summary>Peak-to-peak interval (ms) between opposite-foot footsteps fast enough to count as
    /// a dash ("ダッシュ判定") instead of an ordinary walk.</summary>
    public int DashPeriodMs { get; set; } = 300;

    /// <summary>Which key sequence keyboard/keyboard+mouse output modes send for Dash -- see
    /// DashInputMode. ComboKey is the default.</summary>
    public DashInputMode DashInputMode { get; set; } = DashInputMode.ComboKey;

    /// <summary>How long (ms) a confirmed forward/backward/dash/footstep-turn direction is held
    /// for -- by itself, the 1st and 2nd steps of a fresh sequence (in case that's genuinely all
    /// there is, e.g. one or two deliberate steps), and the short coast after the *last* step of a
    /// longer sequence. See StepContinuationMs for what keeps a longer sequence held continuously in
    /// between its steps -- this setting mostly just tunes how "sticky" a single step or the tail
    /// end feels. 70 is the default -- the midpoint of the "Stride" slider's 30-110ms range
    /// (displayed as 50%).</summary>
    public int StepHoldMs { get; set; } = 70;

    /// <summary>Added on top of StepHoldMs (see DirectionClassifier.HoldMsForStreak) from the
    /// ContinuationStepCount-th confirming step of a forward/backward/dash/footstep-turn sequence
    /// onward, and also how long a gap after any confirming step is still considered the same
    /// ongoing sequence. Needs to comfortably span real stride cadence (several hundred ms between
    /// footsteps) or continuous walking/dashing would release and re-press the key between every
    /// step instead of staying held. 900 is the default -- the midpoint of the "Walk/Dash
    /// continuation" slider's 400-1400ms range (displayed as 50%).</summary>
    public int StepContinuationMs { get; set; } = 900;

    /// <summary>How many confirming steps of a fresh forward/backward/dash sequence are just a
    /// brief tap (StepHoldMs alone) before HoldMsForStreak switches to the long, continuously-
    /// bridged hold (StepHoldMs + StepContinuationMs). Doesn't apply to Turn, which always holds
    /// for StepHoldMs alone regardless of how many steps alternate. A plain step count (1-15), not
    /// a Weak/Strong or Narrow/Wide dial like the other Gesture sensitivity sliders. 7 is the
    /// default.</summary>
    public int ContinuationStepCount { get; set; } = 7;

    /// <summary>Shows the raw-data recording controls (label picker, record button) on the main
    /// window -- only useful for capturing gesture logs during tuning, so hidden by default.</summary>
    public bool DebugMode { get; set; }

    /// <summary>Where recorded CSV logs and the disconnect log are written. A relative path is
    /// resolved against the exe's own folder; an absolute path (chosen via the Browse button) is
    /// used as-is. Defaults to a "debug" folder next to the exe.</summary>
    public string DebugOutputFolder { get; set; } = "debug";

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

    /// <summary>Right-stick deflection (0-100%) while turning right/left is active
    /// (OutputMode.Controller only). One shared value for both directions.</summary>
    public int ControllerTurnSpeed { get; set; } = 60;

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
