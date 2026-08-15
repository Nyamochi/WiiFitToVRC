using WiiFitToVRC.Core.Hid;

namespace WiiFitToVRC.Core.Motion;

/// <summary>
/// Forward/Backward: each corner is watched for the moment its value crosses a configurable
/// percentage (e.g. 105%) of a *reference* resting value for that corner -- a discrete "this foot
/// just landed" event. The reference itself comes from ReferenceWeightCalibrator, which finds the
/// calmest stretch of genuinely standing still (see that class) rather than a continuously
/// self-adjusting rolling statistic -- an earlier peak/decay-based version, and later a rolling
/// median, both reacted to any excursion above a live-adapting baseline, which turned out
/// sensitive enough that a jump's whole-board weight spike could misfire it as a footstep (all 4
/// corners rising together, close enough in timing to look like an alternation). A reference
/// established from a deliberately steady moment, that only ever gets replaced by an *even*
/// steadier one, is far harder to nudge. Front-right then front-left (or vice versa) within a
/// short window is a step -- i.e. walking; the same pairing on the back corners is walking
/// backward. A fast alternation is a dash instead of a walk.
///
/// Turning has two selectable models (Settings > Gesture sensitivity > Turn > Hold/Footstep;
/// Footstep is the default):
///
/// - **Footstep** (TurnMode.Footstep): a diagonal footstep alternation. Turning right by
///   alternately stepping on the back-right and front-left panels (or turning left via
///   front-right/back-left) makes those two *diagonal* corners swing between a high peak (over the
///   turn threshold %, tunable via Gesture sensitivity: Turn) and a low trough, out of phase with
///   each other -- structurally the same "alternate peak on two panels" shape as forward/backward
///   stepping, just on the diagonal pair instead of the front or back pair. The turn confirms on
///   the *second* step of the pair (i.e. it needs one full alternation, not just a single corner
///   crossing the threshold), in either order, and always holds for stepHoldMs alone -- unlike
///   Forward/Backward/Dash, Turn never escalates to a longer continuously-bridged hold no matter
///   how many alternating steps follow, so turning always reads as one deliberate step at a time.
///   Added after an earlier version of Hold below turned out to false-trigger the opposite turn
///   during this model's own large X swings, since the two used to run simultaneously; they're
///   mutually exclusive now. The two touches don't have to land as close together as a Walk/Dash
///   step pair does, either: the pairing window is TurnContinuationMultiplier times Walk/Dash
///   continuation (see HandleDiagonalEdge), since a deliberate turn's two touches can land further
///   apart than an ordinary stride without either one being any less "a real turn."
/// - **Hold** (TurnMode.Hold): X (left-right weight) has to swing past a threshold and *stay*
///   there continuously for a sustained stretch before it's confirmed -- an ordinary step's sway
///   crosses the same instantaneous threshold plenty, but flips side to side too quickly to ever
///   hold it for the full duration. Once confirmed, a turn releases only once X drops back under a
///   lower bound (hysteresis), and it wins over stepping while active.
///
/// Forward/Backward/Dash and Turn don't fall back into each other: leaning forward and holding it
/// (without alternating feet) reads as Idle, not Forward.
/// </summary>
public sealed class DirectionClassifier
{
    // Footstep turn model -- baseline (Gesture sensitivity = 50) peak threshold, as a plain
    // percentage of total board weight on that single corner (not relative to a learned resting
    // reference like the footstep detector below) -- scaled by GestureSensitivityScale.
    private const double BaselineTurnDiagonalThresholdPct = 50;

    // Dash's real inter-step cadence tops out far below Walk's -- real recordings (see
    // debug/session_20260815_*.csv, dedicated dash-then-stop and walk-then-stop sessions) show
    // dash gaps capping around 250-300ms against walk's 550-700ms, roughly half. Sharing Walk's
    // full stepContinuationMs tail-hold for Dash too (see HoldMsForStreak) meant a stop from
    // dashing stayed asserted for up to ~800ms after the last real dash step, far longer than
    // dashing itself ever needs between steps. Scaling the tail down for Dash specifically (half,
    // by default 400ms) cuts that stop-detection lag roughly in half while still leaving
    // comfortable margin above the ~300ms worst case actually observed -- Walk/Backward/Turn are
    // unaffected.
    private const double DashContinuationFraction = 0.5;

    // Backward needs double the configured Steps until continuation before it escalates to the
    // long, continuously-bridged hold -- walking backward is inherently less controlled than
    // walking forward, so staying in the brief-tap-per-step mode for longer keeps it feeling more
    // deliberate, one step at a time, before it commits to a continuously-held walk. Forward/Dash
    // are unaffected.
    private const int BackwardContinuationStepCountMultiplier = 2;

    // Footstep-mode turn's own pairing window (how long the first diagonal touch waits for its
    // partner -- see HandleDiagonalEdge) is double Walk/Dash continuation, not the plain
    // configured value. Real turn recordings (debug/session_2026081*.csv) confirmed the diagonal
    // pattern itself (front touch, then the *opposite-side* back touch) is already exactly right
    // -- the gap between the two touches is what's sometimes too long for the shared 800ms default
    // to catch on a slower, more deliberate turn. Simulating a wider window against the same
    // recordings found it recovers a real pairing that 800ms missed, while producing zero change
    // in turn false-positive counts on dedicated forward recordings even at windows well beyond
    // this multiplier -- widening only this pairing window carries no measured risk to Forward.
    private const int TurnContinuationMultiplier = 2;

    // Sitting forward/backward/dash -- see the instantWeightCalibration branch in Update. A plain
    // percentage-of-total threshold on the front/back corner pairs, exactly the same mechanism as
    // BaselineTurnDiagonalThresholdPct above (which already works fine seated, just on the
    // diagonal pairs instead) -- scaled by footstepThresholdRatio rather than turnSensitivity,
    // since this is standing's forward/backward/dash slider's sitting counterpart, not turn's.
    // Reference-relative detection (the standing path below) falls apart seated: the reference
    // gets seeded from whatever single sample happens to be on hand the instant presence first
    // crosses the (much lower) Sitting threshold -- which can land mid-step, or at a corner that
    // person's seated stance never rests any weight on at all (reference 0 permanently blocks that
    // corner, see CornerPeakTracker's doc comment). 35% was tuned against real seated gait
    // recordings (see debug/sit_*.csv): front/back corner percentages during an actual step
    // cluster in the low-to-high 40s% at the default 120% footstepThresholdRatio, resting well
    // under that between steps.
    private const double BaselineSittingFrontBackThresholdPct = 35;

    // Hold turn model -- baseline (Gesture sensitivity = 50) values, scaled by
    // GestureSensitivityScale before use.
    private const double BaselineTurnEnterX = 40;   // X (left-right %) magnitude to start timing a turn candidate
    private const double BaselineTurnExitX = 25;    // below this, a confirmed turn releases (hysteresis)
    private const long BaselineTurnSustainMs = 400; // how long the lean must hold continuously before it's confirmed

    private enum Corner { None, Right, Left }
    private enum DiagonalCorner { None, TopRight, BottomRight, TopLeft, BottomLeft }

    private readonly CornerPeakTracker _topRight = new();
    private readonly CornerPeakTracker _topLeft = new();
    private readonly CornerPeakTracker _bottomRight = new();
    private readonly CornerPeakTracker _bottomLeft = new();
    private readonly ReferenceWeightCalibrator _reference = new();

    // Separate trackers for the Footstep turn model -- independent refractory/edge state from the
    // footstep trackers above, since they watch the same raw corners but against a different
    // (absolute, not reference-relative) threshold.
    private readonly CornerPeakTracker _diagTopRight = new();
    private readonly CornerPeakTracker _diagTopLeft = new();
    private readonly CornerPeakTracker _diagBottomRight = new();
    private readonly CornerPeakTracker _diagBottomLeft = new();

    // Sitting-only forward/backward/dash trackers -- see BaselineSittingFrontBackThresholdPct.
    // Independent instances (not reused from _topRight etc. above) since they run off a completely
    // different reference (fixed 1.0, i.e. a plain percentage) and would otherwise fight over
    // refractory/edge state with the reference-relative standing trackers.
    private readonly CornerPeakTracker _sittingTopRight = new();
    private readonly CornerPeakTracker _sittingTopLeft = new();
    private readonly CornerPeakTracker _sittingBottomRight = new();
    private readonly CornerPeakTracker _sittingBottomLeft = new();

    private Corner _lastFrontEdge = Corner.None;
    private long _lastFrontEdgeMs;
    private Corner _lastBackEdge = Corner.None;
    private long _lastBackEdgeMs;

    private DiagonalCorner _lastDiagonalEdge = DiagonalCorner.None;
    private long _lastDiagonalEdgeMs;

    // How many confirmed steps in a row the current front/back sequence has had -- reset to 0 in
    // Handle*Edge whenever the gap since that corner-pair's last touch exceeds stepContinuationMs
    // (a real pause), so a fresh sequence always starts over at 1. This has to be judged against
    // the raw corner-touch timing, not against Current going back to Idle -- the 1st step's own
    // hold is far shorter than stepContinuationMs, so Current already reads Idle again well before
    // the 2nd real step of an ordinary walk arrives; resetting on that would mean the streak could
    // never grow. See HoldMsForStreak: the first (continuationStepCount - 1) steps of a sequence
    // are a brief tap each (in case that's all there is -- someone taking just a few steps), and
    // only the continuationStepCount-th step onward switches to a long, continuously-bridged hold.
    // Turn (HandleDiagonalEdge) doesn't have an equivalent streak -- every confirmed turn step
    // holds for stepHoldMs alone, turning always being a deliberate one-step-at-a-time action.
    private int _frontStreak;
    private int _backStreak;

    private Direction _steppingDirection = Direction.Idle; // Forward/Dash/Backward/Turn(Footstep) from a confirmed footstep
    private long _steppingUntilMs;

    // Hold turn model state.
    private Direction _turnCandidateSide = Direction.Idle;
    private long _turnCandidateSinceMs = -1;
    private bool _turnConfirmed;
    private Direction _turnSide = Direction.Idle;

    public Direction Current { get; private set; } = Direction.Idle;
    public bool IsWeightCalibrated => _reference.IsCalibrated;

    /// <summary>Sum of the four learned per-corner reference values (0 if not yet calibrated) --
    /// exposed for JumpDetector's sitting path, which detects a jump as total weight collapsing
    /// toward zero relative to this "feet resting normally" baseline (see AppSettings.PostureMode
    /// doc comment).</summary>
    public double ReferenceTotal =>
        _reference.ReferenceTopRight + _reference.ReferenceBottomRight + _reference.ReferenceTopLeft + _reference.ReferenceBottomLeft;

    // Exposed for SensorCorrection, which needs each corner's own raw reference value (not just
    // the sum above) to compute a per-corner correction factor for AppSettings.
    // ForcedControllerCorrection.
    public double ReferenceTopRight => _reference.ReferenceTopRight;
    public double ReferenceBottomRight => _reference.ReferenceBottomRight;
    public double ReferenceTopLeft => _reference.ReferenceTopLeft;
    public double ReferenceBottomLeft => _reference.ReferenceBottomLeft;

    /// <summary>Fires when the weight reference is refreshed mid-session (see
    /// ReferenceWeightCalibrator.Refreshed).</summary>
    public event Action? WeightCalibrationRefreshed;

    /// <summary>Fires every time a Forward/Backward/Dash/Footstep-turn step is confirmed (i.e. a
    /// pairing succeeds in Handle*Edge), with the confirmed direction and the raw gap (ms) since
    /// that mechanism's previous corner touch. Diagnostic only -- tuning stepContinuationMs (the
    /// gap tolerance) needs to know real inter-step gaps from actual gait, which isn't otherwise
    /// observable from the outside since Current only reflects the smoothed/held output.</summary>
    public event Action<Direction, long>? StepPaired;

    public DirectionClassifier()
    {
        _reference.Refreshed += () => WeightCalibrationRefreshed?.Invoke();
    }

    public static double ComputeX(CalibratedReading cal) =>
        (cal.PctTopRight + cal.PctBottomRight) - (cal.PctTopLeft + cal.PctBottomLeft);
    public static double ComputeY(CalibratedReading cal) =>
        (cal.PctTopRight + cal.PctTopLeft) - (cal.PctBottomRight + cal.PctBottomLeft);

    /// <param name="isPresent">Whether the board currently reads someone standing on it -- only
    /// while true (and while nothing is being detected) do samples feed the weight reference.</param>
    /// <param name="footstepThresholdRatio">e.g. 1.05 for "105% of the reference weight" --
    /// configurable since how far above resting weight a real footstep reads varies by user.</param>
    /// <param name="dashPeriodMs">Peak-to-peak interval fast enough to count as a dash instead of
    /// a walk -- configurable since gait cadence varies by user. 0 (the "dash sensitivity" slider's
    /// bottom end) fully disables dash: no interval is ever faster than a zero-length window, so it
    /// always falls back to a plain Forward/Backward step instead.</param>
    /// <param name="stepHoldMs">How long a confirmed stepping direction (Forward/Backward/Dash, and
    /// Turn in Footstep mode) is held for -- the 1st step of a fresh sequence is a brief tap of
    /// just this long (in case that's all there is), and the tail coast after the *last* step of a
    /// longer sequence too. Configurable since how "sticky" a step should feel is a matter of
    /// taste.</param>
    /// <param name="stepContinuationMs">For Forward/Backward/Dash, added on top of stepHoldMs from
    /// the continuationStepCount-th confirming step of a sequence onward -- see HoldMsForStreak.
    /// Also how long a gap between two steps (of any of the three mechanisms, Turn included) is
    /// still considered the same sequence in the first place. Needs to comfortably span real stride
    /// cadence (several hundred ms between footsteps) or continuous walking would release and
    /// re-press the key between every step.</param>
    /// <param name="continuationStepCount">Forward/Backward/Dash only -- how many confirming steps
    /// of a fresh sequence are just a brief stepHoldMs tap each before HoldMsForStreak switches to
    /// the long, continuously-bridged hold -- see HoldMsForStreak. Turn doesn't use this: every
    /// confirmed turn step holds for stepHoldMs alone, regardless of how many alternate in a row. A
    /// plain step count (1-10), not a sensitivity-scaled threshold.</param>
    /// <param name="turnSensitivity">0-100, see GestureSensitivityScale -- scales whichever turn
    /// model (turnMode) is currently active (does not affect forward/backward/dash, which has its
    /// own separate footstep-threshold setting). 0 fully disables turning -- any in-progress state
    /// from either turn model is dropped immediately, and stepping is never blocked by it, so
    /// output modes can fully lock out left/right turning while leaving forward/backward/dash
    /// untouched. There is no separate "turning enabled" toggle; this is the only way to disable
    /// turning, matching how Jump/Crouch sensitivity already work.</param>
    /// <param name="footstepTurnMode">True runs the Footstep turn model, false runs Hold -- see the
    /// class doc comment. A plain bool (rather than AppSettings.TurnMode) so this class doesn't
    /// need to reference the Settings namespace, matching every other primitive parameter here.</param>
    /// <param name="instantWeightCalibration">True for AppSettings.PostureMode.Sitting -- see
    /// ReferenceWeightCalibrator.CalibrateImmediately. Seeds the reference from a single reading
    /// the moment presence is detected instead of the normal ~20+ second "stand still" wait, and
    /// suspends the usual ongoing auto-refresh entirely for as long as this stays true (a seated
    /// person's light, inconsistent resting weight isn't a good fit for that flatness-window
    /// process either way). A plain bool rather than AppSettings.PostureMode, matching
    /// footstepTurnMode above.</param>
    public Direction Update(CalibratedReading cal, long nowMs, bool isPresent, double footstepThresholdRatio, long dashPeriodMs, long stepHoldMs, long stepContinuationMs, int continuationStepCount, int turnSensitivity, bool footstepTurnMode, bool instantWeightCalibration)
    {
        bool trEdge, tlEdge, brEdge, blEdge;
        if (instantWeightCalibration)
        {
            // Sitting -- see BaselineSittingFrontBackThresholdPct.
            double sittingThresholdPct = BaselineSittingFrontBackThresholdPct * footstepThresholdRatio;
            trEdge = _sittingTopRight.Update(cal.PctTopRight, nowMs, 1.0, sittingThresholdPct);
            tlEdge = _sittingTopLeft.Update(cal.PctTopLeft, nowMs, 1.0, sittingThresholdPct);
            brEdge = _sittingBottomRight.Update(cal.PctBottomRight, nowMs, 1.0, sittingThresholdPct);
            blEdge = _sittingBottomLeft.Update(cal.PctBottomLeft, nowMs, 1.0, sittingThresholdPct);
        }
        else
        {
            trEdge = _topRight.Update(cal.TopRight, nowMs, _reference.ReferenceTopRight, footstepThresholdRatio);
            tlEdge = _topLeft.Update(cal.TopLeft, nowMs, _reference.ReferenceTopLeft, footstepThresholdRatio);
            brEdge = _bottomRight.Update(cal.BottomRight, nowMs, _reference.ReferenceBottomRight, footstepThresholdRatio);
            blEdge = _bottomLeft.Update(cal.BottomLeft, nowMs, _reference.ReferenceBottomLeft, footstepThresholdRatio);
        }

        double y = ComputeY(cal);

        // Front-corner alternation only counts while leaning at least slightly forward, back
        // alternation only while leaning at least slightly backward -- Y's sign cleanly separates
        // the two so a step doesn't get attributed to the wrong direction.
        if (trEdge && y >= 0)
        {
            HandleFrontEdge(Corner.Right, nowMs, dashPeriodMs, stepHoldMs, stepContinuationMs, continuationStepCount);
        }
        if (tlEdge && y >= 0)
        {
            HandleFrontEdge(Corner.Left, nowMs, dashPeriodMs, stepHoldMs, stepContinuationMs, continuationStepCount);
        }
        if (brEdge && y <= 0)
        {
            HandleBackEdge(Corner.Right, nowMs, stepHoldMs, stepContinuationMs, continuationStepCount);
        }
        if (blEdge && y <= 0)
        {
            HandleBackEdge(Corner.Left, nowMs, stepHoldMs, stepContinuationMs, continuationStepCount);
        }

        double x = ComputeX(cal);
        bool turnActive = !GestureSensitivityScale.IsDisabled(turnSensitivity);

        if (turnActive && footstepTurnMode)
        {
            double diagonalThresholdPct = BaselineTurnDiagonalThresholdPct * GestureSensitivityScale.ThresholdMultiplier(turnSensitivity);
            bool diagTrEdge = _diagTopRight.Update(cal.PctTopRight, nowMs, 1.0, diagonalThresholdPct);
            bool diagTlEdge = _diagTopLeft.Update(cal.PctTopLeft, nowMs, 1.0, diagonalThresholdPct);
            bool diagBrEdge = _diagBottomRight.Update(cal.PctBottomRight, nowMs, 1.0, diagonalThresholdPct);
            bool diagBlEdge = _diagBottomLeft.Update(cal.PctBottomLeft, nowMs, 1.0, diagonalThresholdPct);
            if (diagTrEdge)
            {
                HandleDiagonalEdge(DiagonalCorner.TopRight, nowMs, stepHoldMs, stepContinuationMs);
            }
            if (diagTlEdge)
            {
                HandleDiagonalEdge(DiagonalCorner.TopLeft, nowMs, stepHoldMs, stepContinuationMs);
            }
            if (diagBrEdge)
            {
                HandleDiagonalEdge(DiagonalCorner.BottomRight, nowMs, stepHoldMs, stepContinuationMs);
            }
            if (diagBlEdge)
            {
                HandleDiagonalEdge(DiagonalCorner.BottomLeft, nowMs, stepHoldMs, stepContinuationMs);
            }
        }
        else if (_lastDiagonalEdge != DiagonalCorner.None)
        {
            // Drop any pending Footstep-mode alternation state immediately -- whether because
            // turning is off or Hold mode is selected instead -- otherwise a stale first step could
            // pair up the instant Footstep mode is active again.
            _lastDiagonalEdge = DiagonalCorner.None;
        }

        if (turnActive && !footstepTurnMode)
        {
            double multiplier = GestureSensitivityScale.ThresholdMultiplier(turnSensitivity);
            UpdateTurn(x, nowMs, BaselineTurnEnterX * multiplier, BaselineTurnExitX * multiplier, (long)(BaselineTurnSustainMs * multiplier));
        }
        else if (_turnConfirmed || _turnCandidateSinceMs >= 0)
        {
            // Drop any in-progress or confirmed Hold-mode lean immediately -- whether because
            // turning is off or Footstep mode is selected instead -- otherwise a stale confirmation
            // could never release, or reappear the instant Hold mode is active again.
            _turnConfirmed = false;
            _turnCandidateSide = Direction.Idle;
            _turnCandidateSinceMs = -1;
        }

        // Forward/Backward/Dash/Turn(Footstep) all require an actual confirmed footstep
        // alternation -- see HandleFrontEdge/HandleBackEdge/HandleDiagonalEdge. Simply leaning and
        // holding it (without alternating feet) does NOT count there, but Turn(Hold) is exactly
        // that sustained lean, and wins over stepping while confirmed (see UpdateTurn).
        Current = _turnConfirmed ? _turnSide : nowMs <= _steppingUntilMs ? _steppingDirection : Direction.Idle;

        if (instantWeightCalibration)
        {
            // Sitting: skip the flat-window wait entirely -- seed once from whatever's there the
            // moment presence is detected, then leave it alone for as long as sitting mode stays
            // on (see the instantWeightCalibration doc comment above).
            if (isPresent && !_reference.IsCalibrated)
            {
                _reference.CalibrateImmediately(cal);
            }
        }
        // Feed the weight reference only from genuinely quiet moments -- present, and nothing
        // currently detected -- so an active gesture can't pollute what "resting" looks like.
        else if (isPresent && Current == Direction.Idle)
        {
            _reference.Update(cal, nowMs);
        }

        return Current;
    }

    /// <summary>Call when the sensor zero-point itself changes (a fresh SensorCalibration pass) --
    /// every reference value here is only meaningful relative to that offset.</summary>
    public void ResetWeightCalibration() => _reference.Reset();

    private void UpdateTurn(double x, long nowMs, double enterX, double exitX, long sustainMs)
    {
        if (_turnConfirmed)
        {
            // Hysteresis: only release once the lean has fallen back under the lower bound, not
            // the moment it dips below the (higher) entry bound.
            bool stillLeaning = _turnSide == Direction.TurnRight ? x > exitX : x < -exitX;
            if (!stillLeaning)
            {
                _turnConfirmed = false;
                _turnCandidateSide = Direction.Idle;
                _turnCandidateSinceMs = -1;
            }
            return;
        }

        Direction side = x > enterX ? Direction.TurnRight : x < -enterX ? Direction.TurnLeft : Direction.Idle;

        if (side == Direction.Idle)
        {
            _turnCandidateSide = Direction.Idle;
            _turnCandidateSinceMs = -1;
            return;
        }

        if (_turnCandidateSide != side)
        {
            // A fresh lean, or the side flipped -- an ordinary step's left-right sway does this
            // constantly, which is exactly what the sustain requirement below filters out.
            _turnCandidateSide = side;
            _turnCandidateSinceMs = nowMs;
            return;
        }

        if (nowMs - _turnCandidateSinceMs >= sustainMs)
        {
            _turnConfirmed = true;
            _turnSide = side;
        }
    }

    private void HandleFrontEdge(Corner corner, long nowMs, long dashPeriodMs, long stepHoldMs, long stepContinuationMs, int continuationStepCount)
    {
        // A gap longer than stepContinuationMs since the last front-corner touch means whatever
        // sequence was building has lapsed -- the *next* confirmed pair starts over at streak 1
        // (a fresh tap), not wherever the streak happened to leave off. This has to be judged
        // against the raw corner-touch timing here, not against Current/_steppingUntilMs -- the 1st
        // step's own hold is much shorter than stepContinuationMs, so Current already reads Idle
        // again well before the 2nd real step of an ordinary walk arrives.
        if (_lastFrontEdge != Corner.None && nowMs - _lastFrontEdgeMs > stepContinuationMs)
        {
            _frontStreak = 0;
        }

        if (_lastFrontEdge != Corner.None && _lastFrontEdge != corner && nowMs - _lastFrontEdgeMs <= stepContinuationMs)
        {
            long interval = nowMs - _lastFrontEdgeMs;
            _steppingDirection = interval < dashPeriodMs ? Direction.Dash : Direction.Forward;
            _frontStreak++;
            _steppingUntilMs = nowMs + HoldMsForStreak(_frontStreak, _steppingDirection, stepHoldMs, stepContinuationMs, continuationStepCount);
            StepPaired?.Invoke(_steppingDirection, interval);
        }

        _lastFrontEdge = corner;
        _lastFrontEdgeMs = nowMs;
    }

    private void HandleBackEdge(Corner corner, long nowMs, long stepHoldMs, long stepContinuationMs, int continuationStepCount)
    {
        if (_lastBackEdge != Corner.None && nowMs - _lastBackEdgeMs > stepContinuationMs)
        {
            _backStreak = 0;
        }

        if (_lastBackEdge != Corner.None && _lastBackEdge != corner && nowMs - _lastBackEdgeMs <= stepContinuationMs)
        {
            long interval = nowMs - _lastBackEdgeMs;
            _backStreak++;
            ConfirmCompetingStep(Direction.Backward, nowMs, HoldMsForStreak(_backStreak, Direction.Backward, stepHoldMs, stepContinuationMs, continuationStepCount), stepContinuationMs);
            StepPaired?.Invoke(Direction.Backward, interval);
        }

        _lastBackEdge = corner;
        _lastBackEdgeMs = nowMs;
    }

    // Confirms a turn on the *second* step of a diagonal pair -- back-right paired with front-left
    // (either order) is a right turn, front-right paired with back-left (either order) is a left
    // turn. A single corner crossing the threshold is never enough by itself; it just becomes the
    // pending "first step" for the next one to potentially pair with. Unlike Forward/Backward,
    // Turn never escalates to the long continuously-bridged hold no matter how many alternating
    // steps follow -- every confirmed turn step holds for stepHoldMs alone, so turning always reads
    // as a deliberate one-step-at-a-time action instead of coasting between steps.
    private void HandleDiagonalEdge(DiagonalCorner corner, long nowMs, long stepHoldMs, long stepContinuationMs)
    {
        if (_lastDiagonalEdge != DiagonalCorner.None && nowMs - _lastDiagonalEdgeMs <= stepContinuationMs * TurnContinuationMultiplier)
        {
            Direction? confirmed = (_lastDiagonalEdge, corner) switch
            {
                (DiagonalCorner.BottomRight, DiagonalCorner.TopLeft) or (DiagonalCorner.TopLeft, DiagonalCorner.BottomRight) => Direction.TurnRight,
                (DiagonalCorner.TopRight, DiagonalCorner.BottomLeft) or (DiagonalCorner.BottomLeft, DiagonalCorner.TopRight) => Direction.TurnLeft,
                _ => null,
            };
            if (confirmed is { } direction)
            {
                ConfirmCompetingStep(direction, nowMs, stepHoldMs, stepContinuationMs);
                StepPaired?.Invoke(direction, nowMs - _lastDiagonalEdgeMs);
            }
        }

        _lastDiagonalEdge = corner;
        _lastDiagonalEdgeMs = nowMs;
    }

    // Backward and turn both come from a different corner pair than an active forward stride (the
    // back corners or the diagonal pair, vs forward's front corners), and in practice a real
    // footstep sometimes lights up one of those other corners enough to also cross their own
    // threshold mid-stride. If forward hasn't genuinely stopped when that happens -- judged by
    // recent front-corner activity, using the same stepContinuationMs window as everything else --
    // treat it as noise from the same stride and just keep the forward hold alive rather than
    // switching direction out from under it. Once forward actually lapses (no front corner touch
    // for a real pause), the same signal is trusted normally -- so turning or walking backward
    // still works, it just has to follow a real stop.
    private void ConfirmCompetingStep(Direction direction, long nowMs, long holdMs, long stepContinuationMs)
    {
        bool forwardStillGoing = _steppingDirection == Direction.Forward && nowMs - _lastFrontEdgeMs <= stepContinuationMs;
        if (forwardStillGoing)
        {
            _steppingUntilMs = nowMs + holdMs;
            return;
        }

        _steppingDirection = direction;
        _steppingUntilMs = nowMs + holdMs;
    }

    // The first (continuationStepCount - 1) confirmed steps of a fresh sequence are just a brief tap
    // each -- stepHoldMs alone -- in case that's genuinely all there is (someone taking just a few
    // steps). Only once the continuationStepCount-th step confirms the sequence is actually
    // continuing does it switch to a long, continuously-bridged hold: stepContinuationMs on top (or
    // DashContinuationFraction of it, for Dash specifically -- see above), comfortably spanning
    // real stride cadence (several hundred ms between footsteps) so the key stays held instead of
    // releasing and re-pressing between every subsequent step, plus stepHoldMs still as the short
    // coast after the sequence's last step. Used for Forward/Dash and Backward (Footstep-mode Turn
    // never calls this at all -- it always holds for stepHoldMs alone, see the class doc comment).
    // Backward's own threshold is BackwardContinuationStepCountMultiplier times continuationStepCount
    // instead of the plain configured value -- see that constant's own doc comment for why.
    private static long HoldMsForStreak(int streak, Direction direction, long stepHoldMs, long stepContinuationMs, int continuationStepCount)
    {
        int effectiveContinuationStepCount = direction == Direction.Backward
            ? continuationStepCount * BackwardContinuationStepCountMultiplier
            : continuationStepCount;
        if (streak < effectiveContinuationStepCount)
        {
            return stepHoldMs;
        }

        long continuation = direction == Direction.Dash
            ? (long)(stepContinuationMs * DashContinuationFraction)
            : stepContinuationMs;
        return stepHoldMs + continuation;
    }

    /// <summary>
    /// Watches one corner for the moment its value crosses thresholdRatio times a reference value
    /// supplied from outside. Used two ways: the footstep trackers pass in a learned resting
    /// reference from ReferenceWeightCalibrator (reference of 0 -- not yet calibrated -- never
    /// counts as "above", so no edges fire until a reference exists); the diagonal-turn trackers
    /// pass a fixed reference of 1.0 with thresholdRatio as a plain percentage, so it's really just
    /// "value >= thresholdRatio" -- an absolute threshold reusing the same edge/refractory logic.
    /// </summary>
    private sealed class CornerPeakTracker
    {
        private const long RefractoryMs = 150; // debounce: minimum gap between edges on the same corner

        private long _lastEdgeMs;

        public bool IsAbove { get; private set; }

        public bool Update(double value, long nowMs, double reference, double thresholdRatio)
        {
            bool wasAbove = IsAbove;
            IsAbove = reference > 0 && value >= reference * thresholdRatio;

            bool edge = IsAbove && !wasAbove && nowMs - _lastEdgeMs >= RefractoryMs;
            if (edge)
            {
                _lastEdgeMs = nowMs;
            }
            return edge;
        }
    }
}
