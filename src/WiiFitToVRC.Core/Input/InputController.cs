using WiiFitToVRC.Core.Hid;
using WiiFitToVRC.Core.Motion;
using WiiFitToVRC.Core.Settings;

namespace WiiFitToVRC.Core.Input;

/// <summary>
/// Wires the direction/crouch/jump detectors to real output. Call Update() on every raw sensor
/// sample (not throttled to a UI repaint rate) so turn-via-mouse/right-stick stays smooth.
///
/// Four concrete output modes: Keyboard (turn via Q/E), KeyboardMouse (turn via mouse-look),
/// Controller (a virtual Xbox 360 pad via ViGEmBus), and Osc (VRChat's own OSC input endpoint over
/// UDP) -- VRChat turned out to filter out SendInput-synthesized keyboard/mouse input as not
/// "real" player input, so games like that need the controller path instead; some VR headset
/// setups lock input focus to the VR device and reject SendInput entirely (even the virtual
/// controller), which is what the OSC path is for. A fifth, KeyboardMouseOscAuto (the default),
/// isn't really its own mode -- see _effectiveOutputMode/VrAppDetector -- it just picks
/// KeyboardMouse or Osc automatically based on whether VRChat and SteamVR are currently running
/// together.
/// </summary>
public sealed class InputController : IDisposable
{
    // Forward/backward/turn/jump firing can transiently disturb the front-back balance enough to
    // false-trigger crouch -- block crouch for this long after any of them last fired.
    private const long CrouchCooldownMs = 500;

    // Jump/crouch are discrete taps (press then release), unlike the held movement keys. Sending
    // the down and up back-to-back with no gap in between meant the "down" edge could land and
    // clear within the same frame the target game polls input on, so VRChat never observed it --
    // this holds the tap down for a real, visible duration before releasing it. Driven off the
    // existing per-sample loop (not a background timer) so it can't race KeySender's HeldKeys
    // bookkeeping from a second thread.
    private const long TapHoldMs = 60;

    private readonly AppSettings _settings;
    private readonly DirectionClassifier _direction = new();
    private readonly CrouchDetector _crouch = new();
    private readonly JumpDetector _jump = new();
    private readonly PresenceGate _presence = new();
    private readonly VirtualControllerSender _controller = new();
    private readonly OscSender _osc = new();
    private readonly SensorCorrection _sensorCorrection = new();
    private readonly VrAppDetector _vrAppDetector = new();

    // Resolved once per Update() call from _settings.OutputMode -- OutputMode.KeyboardMouseOscAuto
    // becomes KeyboardMouse or Osc here (see VrAppDetector), and is never seen anywhere else in
    // this class. Every internal mode check below reads this instead of _settings.OutputMode
    // directly, so Auto only needs to be resolved in exactly one place. Starts as KeyboardMouse
    // (Auto's own safe default) so ReleaseOutputOnly/ReleaseAll behave sanely even if called
    // before Update() has ever run once.
    private OutputMode _effectiveOutputMode = OutputMode.KeyboardMouse;

    private Direction _lastAppliedDirection = Direction.Idle;
    private bool _lastCrouching;
    private long _lastMovementMs;
    private long _jumpReleaseAtMs = -1;
    private long _crouchReleaseAtMs = -1;

    // DashInputMode.DoubleTap state: true once the initial tap of the current Dash episode has
    // been sent, so later samples (Update() is called at HID report rate, far more often than
    // direction actually changes) don't restart the tap sequence every time. _dashTapReleaseAtMs
    // schedules the brief release+re-press that turns the initial press into a real double-tap --
    // see ReleaseAndRepressDashKeyIfDue.
    private bool _dashDoubleTapStarted;
    private long _dashTapReleaseAtMs = -1;

    // OSC and Controller output only (see ResolveHeldTurnDirection): which turn direction is
    // currently guaranteed to keep being asserted, and until when. A single confirmed turn step
    // from DirectionClassifier only lasts StepHoldMs itself (tens of ms), which wasn't reliably
    // long enough for VRChat's OSC input -- or, for consistency, the virtual controller -- to
    // register a turn; this independently holds the output at TurnHoldMs regardless of how long
    // the underlying Direction actually stays TurnRight/TurnLeft, the same "hold it for a real,
    // visible duration" reasoning as TapHoldMs above. Mouse-look and keyboard Q/E don't need this.
    private Direction _turnHoldDirection = Direction.Idle;
    private long _turnHoldReleaseAtMs = -1;

    // Distance/step tally, driven by DirectionClassifier.StepPaired (fires once per confirmed
    // Forward/Backward/Dash/Turn step -- filtered here to Forward/Dash only, i.e. front-corner
    // spikes, per the caller's request). In-memory only, no persistence: starts at 0 on launch and
    // is never saved to or restored from settings.json, by design -- this is a session odometer,
    // not a lifetime one. Dash covers more ground per step than an ordinary walking stride, hence
    // the separate (larger) per-step distance.
    private const double WalkDistancePerStepMeters = 0.35;
    private const double DashDistancePerStepMeters = 0.5;
    private double _walkDistanceMeters;
    private double _dashDistanceMeters;
    private int _stepCount;

    // OutputMode.Osc's forward-axis magnitude (see ComputeOscForwardMagnitude): the real gap
    // since the previous Forward/Dash step, cached from StepPaired since Update() itself only
    // gets a discrete Direction, not the underlying stride timing. Starts at OscSlowestIntervalMs
    // so the very first step of a session (before any real interval exists yet) reads as ordinary
    // walking pace rather than an arbitrary default.
    private long _lastForwardIntervalMs = OscSlowestIntervalMs;

    public Direction LastDirection => _lastAppliedDirection;
    public bool IsCrouching => _lastCrouching;
    public bool IsPresent => _presence.IsPresent;
    public bool IsWeightCalibrated => _direction.IsWeightCalibrated;
    public bool IsControllerAvailable => _controller.IsAvailable;
    public string? ControllerUnavailableReason => _controller.UnavailableReason;
    public double WalkDistanceMeters => _walkDistanceMeters;
    public double DashDistanceMeters => _dashDistanceMeters;
    public double TotalDistanceMeters => _walkDistanceMeters + _dashDistanceMeters;
    public int StepCount => _stepCount;

    /// <summary>Fires on the calling (background HID) thread each time a jump is detected.</summary>
    public event Action? Jumped;

    /// <summary>Fires on the calling (background HID) thread when the weight reference refreshes
    /// mid-session (see ReferenceWeightCalibrator.Refreshed) -- e.g. a different person stepped
    /// on and stood still.</summary>
    public event Action? WeightCalibrationRefreshed;

    public InputController(AppSettings settings)
    {
        _settings = settings;
        _direction.WeightCalibrationRefreshed += () => WeightCalibrationRefreshed?.Invoke();
        _direction.StepPaired += OnStepPaired;
        // Restores the running average accumulated across previous sessions (see SensorCorrection)
        // -- this session's own first calibration still takes a fresh measurement and folds it in
        // on top, same as every calibration after it.
        _sensorCorrection.LoadHistory(
            settings.CorrectionTopRightFactor, settings.CorrectionBottomRightFactor,
            settings.CorrectionTopLeftFactor, settings.CorrectionBottomLeftFactor,
            settings.CorrectionSampleCount);
    }

    // Only Forward/Dash count -- both come from the front-corner pairing mechanism ("front panel
    // spike"), unlike Backward (back corners) and Turn (diagonal corners), which this tally
    // deliberately excludes.
    private void OnStepPaired(Direction direction, long intervalMs)
    {
        switch (direction)
        {
            case Direction.Forward:
                _walkDistanceMeters += WalkDistancePerStepMeters;
                _stepCount++;
                _lastForwardIntervalMs = intervalMs;
                break;
            case Direction.Dash:
                _dashDistanceMeters += DashDistancePerStepMeters;
                _stepCount++;
                _lastForwardIntervalMs = intervalMs;
                break;
        }
    }

    public void Update(BalanceBoardSensors raw, CalibratedReading? cal, long nowMs)
    {
        if (cal is null)
        {
            return; // no calibration yet -- nothing meaningful to classify
        }

        // AppSettings.ForcedControllerCorrection: once a per-corner correction is established
        // (see SensorCorrection), every downstream consumer below -- direction, jump, crouch, and
        // the presence gate -- sees the corrected reading instead of the raw one. Each fresh
        // calibration folds one more measurement into the running average that backs it; when that
        // happens, persist the updated average immediately so it survives even if the app closes
        // before anything else would have saved settings.json.
        if (_settings.ForcedControllerCorrection)
        {
            if (_sensorCorrection.TryEstablish(_direction))
            {
                _settings.CorrectionTopRightFactor = _sensorCorrection.TopRightFactor;
                _settings.CorrectionBottomRightFactor = _sensorCorrection.BottomRightFactor;
                _settings.CorrectionTopLeftFactor = _sensorCorrection.TopLeftFactor;
                _settings.CorrectionBottomLeftFactor = _sensorCorrection.BottomLeftFactor;
                _settings.CorrectionSampleCount = _sensorCorrection.SampleCount;
                _settings.Save();
            }
            if (_sensorCorrection.IsEstablished)
            {
                cal = _sensorCorrection.Apply(cal);
            }
        }

        _effectiveOutputMode = _settings.OutputMode == OutputMode.KeyboardMouseOscAuto
            ? (RefreshVrAutoDetection(nowMs) ? OutputMode.Osc : OutputMode.KeyboardMouse)
            : _settings.OutputMode;

        if (_effectiveOutputMode == OutputMode.Controller)
        {
            _controller.Connect(); // no-op once already connected, or already failed once
        }

        bool sitting = _settings.PostureMode == PostureMode.Sitting;
        // MovementMode.WeightShift has no discrete gesture events to hang Jump/Crouch off of at
        // all -- it's continuous lean output only (see WeightShiftClassifier, and AppSettings.
        // MovementMode's own doc comment) -- so both are skipped entirely below, independent of
        // PostureMode.
        bool weightShift = _settings.MovementMode == MovementMode.WeightShift;
        bool jumped = false;

        // Sitting jump fires on total weight collapsing toward zero as the feet lift clear (see
        // JumpDetector's sittingPosture path) -- exactly the kind of momentary dip Sitting's own
        // zero-second EffectiveSleepSeconds override would otherwise read as "no longer present"
        // and gate out below, before jump detection ever got a chance to see it. Run as a fully
        // self-contained block (press-or-not, then its own release check) ahead of the presence
        // gate for that reason. Standing doesn't have this conflict -- its SleepSeconds is long
        // enough to ride out a jump's own airborne dip -- so it stays gated behind presence as
        // before, further down.
        if (sitting && !weightShift)
        {
            jumped = UpdateJump(cal, nowMs, sitting: true);
        }

        if (!_presence.Update(cal.Total, nowMs, _settings.EffectivePresenceWeightThreshold, _settings.EffectivePresenceOffWeightThreshold, _settings.EffectiveSleepSeconds))
        {
            // Nobody's on the board yet (or hasn't been long enough after stepping on/off) --
            // force everything back to Idle/released so key output and arrow lighting both go
            // dark. Deliberately NOT the full ReleaseAll(): that also resets the presence gate's
            // internal "how long have we been above threshold" timer, and this branch runs on
            // every single sample during the whole ramp-up window while presence is still
            // pending -- resetting it every time meant the timer could never actually accumulate
            // past zero, so presence could never unlock at all.
            ReleaseOutputOnly();
            return;
        }

        Direction direction = weightShift
            ? WeightShiftClassifier.Classify(cal, _settings.WeightShiftSensitivity)
            : _direction.Update(cal, nowMs, isPresent: true, _settings.FootstepThresholdPercent / 100.0, _settings.DashPeriodMs, _settings.StepHoldMs, _settings.StepContinuationMs, _settings.ContinuationStepCount, _settings.TurnSensitivity, _settings.TurnMode == TurnMode.Footstep, sitting);
        ApplyDirection(direction, nowMs);
        ReleaseAndRepressDashKeyIfDue(nowMs);

        if (weightShift)
        {
            return;
        }

        if (!sitting)
        {
            jumped = UpdateJump(cal, nowMs, sitting: false);
        }

        if (direction != Direction.Idle || jumped)
        {
            _lastMovementMs = nowMs;
        }

        if (nowMs - _lastMovementMs >= CrouchCooldownMs)
        {
            // Sitting crouch is recorded on the bottom panels instead of the top ones standing
            // uses -- negating Y is exactly that swap (Y is top-minus-bottom), with the rest of
            // CrouchDetector's own logic (hold duration, hysteresis, sensitivity scaling) left
            // completely untouched.
            double y = DirectionClassifier.ComputeY(cal) * (sitting ? -1 : 1);
            bool crouching = _crouch.Update(y, nowMs, _settings.CrouchSensitivity);
            ApplyCrouch(crouching, nowMs);
        }
        ReleaseTapIfDue(isJump: false, nowMs);
    }

    // Only called (see the Update ternary above) while AppSettings.OutputMode is actually
    // KeyboardMouseOscAuto, so VrAppDetector's own 10-second poll never runs at all for anyone
    // using a fixed output mode.
    private bool RefreshVrAutoDetection(long nowMs)
    {
        _vrAppDetector.Update(nowMs);
        return _vrAppDetector.ShouldUseOsc;
    }

    private bool UpdateJump(CalibratedReading cal, long nowMs, bool sitting)
    {
        bool jumped = _jump.Update(cal.Total, nowMs, _settings.JumpSensitivity, sitting, sitting ? _direction.ReferenceTotal : 0);
        if (jumped)
        {
            PressTap(isJump: true, nowMs);
            Jumped?.Invoke();
        }
        ReleaseTapIfDue(isJump: true, nowMs);
        return jumped;
    }

    private void ApplyDirection(Direction direction, long nowMs)
    {
        if (_effectiveOutputMode == OutputMode.Controller)
        {
            ApplyDirectionController(direction, nowMs);
        }
        else if (_effectiveOutputMode == OutputMode.Osc)
        {
            ApplyDirectionOsc(direction, nowMs);
        }
        else
        {
            if (direction != _lastAppliedDirection)
            {
                ReleaseDirectionKeys(_lastAppliedDirection);
            }
            ApplyDirectionKeyboard(direction, nowMs);
        }

        _lastAppliedDirection = direction;
    }

    private void ApplyDirectionKeyboard(Direction direction, long nowMs)
    {
        switch (direction)
        {
            case Direction.Forward:
                KeySender.KeyDown(_settings.ForwardKey);
                break;
            case Direction.Dash:
                if (_settings.DashInputMode == DashInputMode.DoubleTap)
                {
                    if (!_dashDoubleTapStarted)
                    {
                        // First sample of this Dash episode: press the forward key (the "single
                        // press") and schedule the brief release+re-press below that reads as a
                        // genuine double-tap to the target game. Later samples while Dash stays
                        // active fall through here doing nothing further -- the key just stays
                        // held, same as any other continuous direction.
                        KeySender.KeyDown(_settings.ForwardKey);
                        _dashTapReleaseAtMs = nowMs + TapHoldMs;
                        _dashDoubleTapStarted = true;
                    }
                }
                else
                {
                    KeySender.KeyDown(_settings.DashModifierKey);
                    KeySender.KeyDown(_settings.DashKey);
                }
                break;
            case Direction.Backward:
                KeySender.KeyDown(_settings.BackwardKey);
                break;
            case Direction.TurnRight:
                if (_effectiveOutputMode == OutputMode.KeyboardMouse)
                {
                    MouseSender.MoveRelative(_settings.MouseTurnSpeed);
                }
                else
                {
                    KeySender.KeyDown(_settings.TurnRightKey);
                }
                break;
            case Direction.TurnLeft:
                if (_effectiveOutputMode == OutputMode.KeyboardMouse)
                {
                    MouseSender.MoveRelative(-_settings.MouseTurnSpeed);
                }
                else
                {
                    KeySender.KeyDown(_settings.TurnLeftKey);
                }
                break;
        }
    }

    private void ReleaseDirectionKeys(Direction direction)
    {
        switch (direction)
        {
            case Direction.Forward:
                KeySender.KeyUp(_settings.ForwardKey);
                break;
            case Direction.Dash:
                if (_settings.DashInputMode == DashInputMode.DoubleTap)
                {
                    KeySender.KeyUp(_settings.ForwardKey);
                    _dashDoubleTapStarted = false;
                    _dashTapReleaseAtMs = -1;
                }
                else
                {
                    KeySender.KeyUp(_settings.DashKey);
                    KeySender.KeyUp(_settings.DashModifierKey);
                }
                break;
            case Direction.Backward:
                KeySender.KeyUp(_settings.BackwardKey);
                break;
            case Direction.TurnRight:
                KeySender.KeyUp(_settings.TurnRightKey); // no-op if it was never pressed (mouse mode)
                break;
            case Direction.TurnLeft:
                KeySender.KeyUp(_settings.TurnLeftKey);
                break;
        }
    }

    // DashInputMode.DoubleTap only: fires once, TapHoldMs after the initial press, turning it into
    // an actual double-tap by releasing and immediately re-pressing (and holding, for as long as
    // Dash stays active) the forward key. Driven off the existing per-sample loop, same reasoning
    // as ReleaseTapIfDue -- a real, visible release is needed for the target game to see two
    // separate presses rather than one continuous hold.
    private void ReleaseAndRepressDashKeyIfDue(long nowMs)
    {
        if (_dashTapReleaseAtMs < 0 || nowMs < _dashTapReleaseAtMs)
        {
            return;
        }
        KeySender.KeyUp(_settings.ForwardKey);
        KeySender.KeyDown(_settings.ForwardKey);
        _dashTapReleaseAtMs = -1;
    }

    // Unlike the keyboard path, sticks/buttons are absolute state set fresh every sample, so
    // there's no separate "release the old direction" step -- moving the stick to a new position
    // (or back to center) already replaces whatever it held before, except the turn axis, which
    // goes through ResolveHeldTurnDirection for its own guaranteed-minimum-duration hold.
    private void ApplyDirectionController(Direction direction, long nowMs)
    {
        double moveY = direction switch
        {
            Direction.Forward => 0.6,
            Direction.Dash => 1.0,
            Direction.Backward => -1.0,
            _ => 0.0,
        };
        _controller.SetLeftStick(0, moveY);

        Direction turnDirection = ResolveHeldTurnDirection(direction, nowMs);
        double turnX = turnDirection switch
        {
            Direction.TurnRight => _settings.ControllerTurnSpeed / 100.0,
            Direction.TurnLeft => -_settings.ControllerTurnSpeed / 100.0,
            _ => 0.0,
        };
        _controller.SetRightStick(turnX, 0);

        // Sprint modifier, mirroring the keyboard Shift+W combo.
        _controller.SetButton(_settings.DashButton, direction == Direction.Dash);
    }

    // Like the controller path, OSC axes/buttons are absolute state resent fresh every sample --
    // no separate "release the old direction" step needed, except LookHorizontal (turn), which
    // goes through ResolveHeldTurnDirection for its own guaranteed-minimum-duration hold.
    // Forward/Dash both used to send a flat 1.0 -- the only speed distinction was the separate
    // /input/Run button below. VRChat's MoveForward axis works like an analog stick tilt, though:
    // a partial push walks, a full push runs. Reading the real stride cadence instead lets the
    // axis itself carry that continuously, from OscSlowestIntervalMs (0.5 -- ordinary walking
    // pace) up to OscFastestIntervalMs (1.0 -- flat-out dashing), rather than only ever sending
    // "half speed" or "full speed" with nothing in between. Reference points tuned against real
    // recordings: a dedicated walk-only session (debug/session_20260815_192743.csv) had a median
    // stride gap of 454ms, and dedicated dash sessions (debug/session_20260815_192625.csv,
    // .../192444.csv) had median dash gaps of 227-234ms -- 450ms/200ms bracket that pair with a
    // little headroom on the fast end for a genuinely quick dasher to reach true 1.0, while
    // ordinary walking (anything at or slower than 450ms) sits right at the 0.5 floor.
    private const long OscSlowestIntervalMs = 450;
    private const long OscFastestIntervalMs = 200;

    private double ComputeOscForwardMagnitude()
    {
        double t = (double)(_lastForwardIntervalMs - OscFastestIntervalMs) / (OscSlowestIntervalMs - OscFastestIntervalMs);
        return 1.0 - Math.Clamp(t, 0.0, 1.0) * 0.5;
    }

    private void ApplyDirectionOsc(Direction direction, long nowMs)
    {
        double vertical = direction switch
        {
            Direction.Forward => ComputeOscForwardMagnitude(),
            Direction.Dash => ComputeOscForwardMagnitude(),
            Direction.Backward => -1.0,
            _ => 0.0,
        };
        _osc.SetMoveAxis(vertical, 0.0);

        // LookLeft/LookRight are plain buttons (no magnitude) -- see OscSender.SetLookLeft.
        Direction turnDirection = ResolveHeldTurnDirection(direction, nowMs);
        _osc.SetLookLeft(turnDirection == Direction.TurnLeft);
        _osc.SetLookRight(turnDirection == Direction.TurnRight);

        // Mirrors the keyboard Shift+W combo / controller sprint button via VRChat's own /input/Run.
        _osc.SetRun(direction == Direction.Dash);
    }

    // OSC and Controller output only -- a single confirmed turn step from DirectionClassifier only
    // lasts StepHoldMs itself (tens of ms), which wasn't reliably long enough for either output to
    // register a turn. Returns whichever turn direction should actually be asserted this sample:
    // a fresh turn step (re)starts a TurnHoldMs guarantee; once started, that direction keeps being
    // returned -- even after the real Direction reverts to Idle -- until TurnHoldMs elapses.
    private Direction ResolveHeldTurnDirection(Direction direction, long nowMs)
    {
        bool isTurning = direction is Direction.TurnRight or Direction.TurnLeft;
        if (isTurning && direction != _turnHoldDirection)
        {
            _turnHoldDirection = direction;
            _turnHoldReleaseAtMs = nowMs + _settings.TurnHoldMs;
            return direction;
        }
        if (!isTurning && _turnHoldDirection != Direction.Idle)
        {
            if (nowMs >= _turnHoldReleaseAtMs)
            {
                _turnHoldDirection = Direction.Idle;
                _turnHoldReleaseAtMs = -1;
                return Direction.Idle;
            }
            return _turnHoldDirection; // still within the guaranteed hold -- keep reporting it
        }
        return _turnHoldDirection; // same turn continuing (already held), or genuinely Idle
    }

    // Crouch/stand share one toggle binding in the target game, so each transition (crouch
    // starting AND crouch ending) must send exactly one tap -- a hold-style press/release pair
    // would leave the two sides unpaired and desync the game's toggle state from what the app
    // thinks it is.
    private void ApplyCrouch(bool crouching, long nowMs)
    {
        if (crouching == _lastCrouching)
        {
            return;
        }

        PressTap(isJump: false, nowMs);
        _lastCrouching = crouching;
    }

    // Presses (but doesn't yet release) the jump or crouch binding, and schedules the matching
    // release for TapHoldMs later -- see the TapHoldMs comment for why the release is delayed
    // rather than immediate.
    private void PressTap(bool isJump, long nowMs)
    {
        if (_effectiveOutputMode == OutputMode.Controller)
        {
            _controller.SetButton(isJump ? _settings.JumpButton : _settings.CrouchButton, true);
        }
        else if (isJump && _effectiveOutputMode == OutputMode.Osc)
        {
            // VRChat's OSC input has no crouch address, so only jump uses it here -- crouch
            // always falls through to the plain key press below, even in OSC mode.
            _osc.SetJump(true);
        }
        else
        {
            KeySender.KeyDown(isJump ? _settings.JumpKey : _settings.CrouchKey);
        }

        if (isJump)
        {
            _jumpReleaseAtMs = nowMs + TapHoldMs;
        }
        else
        {
            _crouchReleaseAtMs = nowMs + TapHoldMs;
        }
    }

    private void ReleaseTapIfDue(bool isJump, long nowMs)
    {
        long releaseAtMs = isJump ? _jumpReleaseAtMs : _crouchReleaseAtMs;
        if (releaseAtMs < 0 || nowMs < releaseAtMs)
        {
            return;
        }
        ReleaseTapNow(isJump);
    }

    private void ReleaseTapNow(bool isJump)
    {
        if (_effectiveOutputMode == OutputMode.Controller)
        {
            _controller.SetButton(isJump ? _settings.JumpButton : _settings.CrouchButton, false);
        }
        else if (isJump && _effectiveOutputMode == OutputMode.Osc)
        {
            _osc.SetJump(false);
        }
        else
        {
            KeySender.KeyUp(isJump ? _settings.JumpKey : _settings.CrouchKey);
        }

        if (isJump)
        {
            _jumpReleaseAtMs = -1;
        }
        else
        {
            _crouchReleaseAtMs = -1;
        }
    }

    private void ReleaseOutputOnly()
    {
        if (_effectiveOutputMode == OutputMode.Controller)
        {
            _controller.SetLeftStick(0, 0);
            _controller.SetRightStick(0, 0);
            _controller.SetButton(_settings.DashButton, false);
        }
        else if (_effectiveOutputMode == OutputMode.Osc)
        {
            _osc.SetMoveAxis(0, 0);
            _osc.SetLookLeft(false);
            _osc.SetLookRight(false);
            _osc.SetRun(false);
        }
        else
        {
            ReleaseDirectionKeys(_lastAppliedDirection);
        }

        // Emergency cleanup (disconnect/presence lost) -- drop any in-progress guaranteed turn
        // hold immediately rather than letting it resurface a stale direction later.
        _turnHoldDirection = Direction.Idle;
        _turnHoldReleaseAtMs = -1;

        // If a crouch tap is still physically down (mid-press), finish it now instead of doubling
        // up with a fresh tap below -- that would send an extra, unwanted press.
        if (_crouchReleaseAtMs >= 0)
        {
            ReleaseTapNow(isJump: false);
        }
        else if (_lastCrouching)
        {
            // Already fully released -- but the game still thinks we're crouched (toggle
            // binding), so tap once more to bring it back to standing. This is an emergency
            // cleanup path (disconnect/presence lost), so an instant tap is acceptable here even
            // though ordinary taps hold for TapHoldMs.
            if (_effectiveOutputMode == OutputMode.Controller)
            {
                _controller.TapButton(_settings.CrouchButton);
            }
            else
            {
                KeySender.Tap(_settings.CrouchKey);
            }
        }
        if (_jumpReleaseAtMs >= 0)
        {
            ReleaseTapNow(isJump: true);
        }

        _lastAppliedDirection = Direction.Idle;
        _lastCrouching = false;
    }

    /// <summary>Releases every key/button this controller may be holding and resets the presence
    /// gate -- call on disconnect/exit/recalibration, not from the per-sample "not present yet"
    /// path.</summary>
    public void ReleaseAll()
    {
        ReleaseOutputOnly();
        _presence.Reset();
    }

    /// <summary>Call when the sensor zero-point changes (a fresh SensorCalibration pass) -- the
    /// weight reference is only meaningful relative to that offset. Also called on every
    /// AppSettings.PostureMode switch (see MonitorForm.SetPostureMode), since standing and sitting
    /// put very different weight on the board -- JumpDetector's own standing baseline is reset
    /// alongside DirectionClassifier's for the same reason, rather than left to slowly re-converge
    /// via its EMA. SensorCorrection resets too, so ForcedControllerCorrection takes a fresh
    /// measurement from the new raw reference this produces and folds it into its running average,
    /// rather than reusing a measurement taken against the old (now invalid) reference.</summary>
    public void ResetWeightCalibration()
    {
        _direction.ResetWeightCalibration();
        _jump.ResetBaseline();
        _sensorCorrection.Reset();
    }

    /// <summary>Call when AppSettings.ForcedControllerCorrection is turned off after having been
    /// on (see SettingsForm.Save, which clears the matching settings.json fields at the same
    /// time) -- wipes the in-memory running average so a stale one isn't silently reused if the
    /// setting gets turned back on again later in this same session, e.g. against replacement
    /// hardware the old average says nothing about.</summary>
    public void ClearForcedCorrectionHistory() => _sensorCorrection.ClearHistory();

    public void Dispose()
    {
        _controller.Dispose();
        _osc.Dispose();
    }
}
