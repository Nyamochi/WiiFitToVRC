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
///   crossing the threshold), in either order, and behaves exactly like a footstep afterward -- it
///   holds for stepHoldMs and can be refreshed by further alternating steps, same as
///   Forward/Backward/Dash. Added after an earlier version of Hold below turned out to
///   false-trigger the opposite turn during this model's own large X swings, since the two used to
///   run simultaneously; they're mutually exclusive now.
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

    private Corner _lastFrontEdge = Corner.None;
    private long _lastFrontEdgeMs;
    private Corner _lastBackEdge = Corner.None;
    private long _lastBackEdgeMs;

    private DiagonalCorner _lastDiagonalEdge = DiagonalCorner.None;
    private long _lastDiagonalEdgeMs;

    // How many confirmed steps in a row the current front/back/diagonal sequence has had -- reset
    // to 0 in Handle*Edge whenever the gap since that corner-pair's last touch exceeds
    // stepContinuationMs (a real pause), so a fresh sequence always starts over at 1. This has to
    // be judged against the raw corner-touch timing, not against Current going back to Idle -- the
    // 1st step's own hold is far shorter than stepContinuationMs, so Current already reads Idle
    // again well before the 2nd real step of an ordinary walk arrives; resetting on that would mean
    // the streak could never reach 2. See HoldMsForStreak: the 1st and 2nd steps of a sequence are
    // a brief tap each (in case that's all there is -- someone taking just one or two steps), and
    // only the 3rd step onward switches to a long, continuously-bridged hold.
    private int _frontStreak;
    private int _backStreak;
    private int _diagonalStreak;

    private Direction _steppingDirection = Direction.Idle; // Forward/Dash/Backward/Turn(Footstep) from a confirmed footstep
    private long _steppingUntilMs;

    // Hold turn model state.
    private Direction _turnCandidateSide = Direction.Idle;
    private long _turnCandidateSinceMs = -1;
    private bool _turnConfirmed;
    private Direction _turnSide = Direction.Idle;

    public Direction Current { get; private set; } = Direction.Idle;
    public bool IsWeightCalibrated => _reference.IsCalibrated;

    /// <summary>Fires when the weight reference is refreshed mid-session (see
    /// ReferenceWeightCalibrator.Refreshed).</summary>
    public event Action? WeightCalibrationRefreshed;

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
    /// <param name="stepContinuationMs">Added on top of stepHoldMs from the 2nd confirming step of
    /// a sequence onward, and also how long a gap between two steps is still considered the same
    /// sequence in the first place -- see HoldMsForStreak. Needs to comfortably span real stride
    /// cadence (several hundred ms between footsteps) or continuous walking would release and
    /// re-press the key between every step.</param>
    /// <param name="turnEnabled">When false, turning is not tracked at all (any in-progress state
    /// from either turn model is dropped) and stepping is never blocked by it -- lets output modes
    /// fully lock out left/right turning while leaving forward/backward/dash untouched.</param>
    /// <param name="turnSensitivity">0-100, see GestureSensitivityScale -- scales whichever turn
    /// model (turnMode) is currently active (does not affect forward/backward/dash, which has its
    /// own separate footstep-threshold setting). 0 fully disables turning, same as
    /// turnEnabled = false.</param>
    /// <param name="footstepTurnMode">True runs the Footstep turn model, false runs Hold -- see the
    /// class doc comment. A plain bool (rather than AppSettings.TurnMode) so this class doesn't
    /// need to reference the Settings namespace, matching every other primitive parameter here.</param>
    public Direction Update(CalibratedReading cal, long nowMs, bool isPresent, double footstepThresholdRatio, long dashPeriodMs, long stepHoldMs, long stepContinuationMs, bool turnEnabled, int turnSensitivity, bool footstepTurnMode)
    {
        bool trEdge = _topRight.Update(cal.TopRight, nowMs, _reference.ReferenceTopRight, footstepThresholdRatio);
        bool tlEdge = _topLeft.Update(cal.TopLeft, nowMs, _reference.ReferenceTopLeft, footstepThresholdRatio);
        bool brEdge = _bottomRight.Update(cal.BottomRight, nowMs, _reference.ReferenceBottomRight, footstepThresholdRatio);
        bool blEdge = _bottomLeft.Update(cal.BottomLeft, nowMs, _reference.ReferenceBottomLeft, footstepThresholdRatio);

        double y = ComputeY(cal);

        // Front-corner alternation only counts while leaning at least slightly forward, back
        // alternation only while leaning at least slightly backward -- Y's sign cleanly separates
        // the two so a step doesn't get attributed to the wrong direction.
        if (trEdge && y >= 0)
        {
            HandleFrontEdge(Corner.Right, nowMs, dashPeriodMs, stepHoldMs, stepContinuationMs);
        }
        if (tlEdge && y >= 0)
        {
            HandleFrontEdge(Corner.Left, nowMs, dashPeriodMs, stepHoldMs, stepContinuationMs);
        }
        if (brEdge && y <= 0)
        {
            HandleBackEdge(Corner.Right, nowMs, stepHoldMs, stepContinuationMs);
        }
        if (blEdge && y <= 0)
        {
            HandleBackEdge(Corner.Left, nowMs, stepHoldMs, stepContinuationMs);
        }

        double x = ComputeX(cal);
        bool turnActive = turnEnabled && !GestureSensitivityScale.IsDisabled(turnSensitivity);

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

        // Feed the weight reference only from genuinely quiet moments -- present, and nothing
        // currently detected -- so an active gesture can't pollute what "resting" looks like.
        if (isPresent && Current == Direction.Idle)
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

    private void HandleFrontEdge(Corner corner, long nowMs, long dashPeriodMs, long stepHoldMs, long stepContinuationMs)
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
            _steppingUntilMs = nowMs + HoldMsForStreak(_frontStreak, stepHoldMs, stepContinuationMs);
        }

        _lastFrontEdge = corner;
        _lastFrontEdgeMs = nowMs;
    }

    private void HandleBackEdge(Corner corner, long nowMs, long stepHoldMs, long stepContinuationMs)
    {
        if (_lastBackEdge != Corner.None && nowMs - _lastBackEdgeMs > stepContinuationMs)
        {
            _backStreak = 0;
        }

        if (_lastBackEdge != Corner.None && _lastBackEdge != corner && nowMs - _lastBackEdgeMs <= stepContinuationMs)
        {
            _backStreak++;
            ConfirmCompetingStep(Direction.Backward, nowMs, HoldMsForStreak(_backStreak, stepHoldMs, stepContinuationMs), stepContinuationMs);
        }

        _lastBackEdge = corner;
        _lastBackEdgeMs = nowMs;
    }

    // Confirms a turn on the *second* step of a diagonal pair -- back-right paired with front-left
    // (either order) is a right turn, front-right paired with back-left (either order) is a left
    // turn. A single corner crossing the threshold is never enough by itself; it just becomes the
    // pending "first step" for the next one to potentially pair with.
    private void HandleDiagonalEdge(DiagonalCorner corner, long nowMs, long stepHoldMs, long stepContinuationMs)
    {
        if (_lastDiagonalEdge != DiagonalCorner.None && nowMs - _lastDiagonalEdgeMs > stepContinuationMs)
        {
            _diagonalStreak = 0;
        }

        if (_lastDiagonalEdge != DiagonalCorner.None && nowMs - _lastDiagonalEdgeMs <= stepContinuationMs)
        {
            Direction? confirmed = (_lastDiagonalEdge, corner) switch
            {
                (DiagonalCorner.BottomRight, DiagonalCorner.TopLeft) or (DiagonalCorner.TopLeft, DiagonalCorner.BottomRight) => Direction.TurnRight,
                (DiagonalCorner.TopRight, DiagonalCorner.BottomLeft) or (DiagonalCorner.BottomLeft, DiagonalCorner.TopRight) => Direction.TurnLeft,
                _ => null,
            };
            if (confirmed is { } direction)
            {
                _diagonalStreak++;
                ConfirmCompetingStep(direction, nowMs, HoldMsForStreak(_diagonalStreak, stepHoldMs, stepContinuationMs), stepContinuationMs);
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

    // The 1st and 2nd confirmed steps of a fresh sequence (streak < 3) are just a brief tap each --
    // stepHoldMs alone -- in case that's genuinely all there is (someone taking one or two steps).
    // Only once a 3rd step confirms the sequence is actually continuing does it switch to a long,
    // continuously-bridged hold: stepContinuationMs on top, comfortably spanning real stride cadence
    // (several hundred ms between footsteps) so the key stays held instead of releasing and
    // re-pressing between every subsequent step, plus stepHoldMs still as the short coast after the
    // sequence's last step. Reused identically for Forward/Dash, Backward, and Footstep-mode Turn.
    private static long HoldMsForStreak(int streak, long stepHoldMs, long stepContinuationMs) =>
        streak >= 3 ? stepHoldMs + stepContinuationMs : stepHoldMs;

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
