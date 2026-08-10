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
/// Turning is a diagonal footstep alternation: turning right by alternately stepping on the
/// back-right and front-left panels (or turning left via front-right/back-left) makes those two
/// *diagonal* corners swing between a high peak (over the turn threshold %, tunable via Gesture
/// sensitivity: Turn) and a low trough, out of phase with each other -- structurally the same
/// "alternate peak on two panels" shape as forward/backward stepping, just on the diagonal pair
/// instead of the front or back pair. The turn confirms on the *second* step of the pair (i.e. it
/// needs one full alternation, not just a single corner crossing the threshold), in either order
/// (back-right-then-front-left or front-left-then-back-right for a right turn), and behaves
/// exactly like a footstep afterward -- it holds for stepHoldMs and can be refreshed by further
/// alternating steps, same as Forward/Backward/Dash. (An earlier sustained-lean model -- X had to
/// swing to one side and stay there -- turned out to false-trigger the opposite turn during this
/// diagonal alternation's own large X swings, and was replaced outright rather than kept alongside
/// it.)
///
/// Forward/Backward/Dash and Turn don't fall back into each other: leaning forward and holding it
/// (without alternating feet) reads as Idle, not Forward.
/// </summary>
public sealed class DirectionClassifier
{
    private const long AlternationWindowMs = 900; // max gap between opposite-foot peaks to count as one walking step

    // Diagonal-alternation turn model -- baseline (Gesture sensitivity = 50) peak threshold, as a
    // plain percentage of total board weight on that single corner (not relative to a learned
    // resting reference like the footstep detector below) -- scaled by GestureSensitivityScale.
    private const double BaselineTurnDiagonalThresholdPct = 50;

    private enum Corner { None, Right, Left }
    private enum DiagonalCorner { None, TopRight, BottomRight, TopLeft, BottomLeft }

    private readonly CornerPeakTracker _topRight = new();
    private readonly CornerPeakTracker _topLeft = new();
    private readonly CornerPeakTracker _bottomRight = new();
    private readonly CornerPeakTracker _bottomLeft = new();
    private readonly ReferenceWeightCalibrator _reference = new();

    // Separate trackers for the diagonal turn model -- independent refractory/edge state from the
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

    private Direction _steppingDirection = Direction.Idle; // Forward/Dash/Backward/Turn from a confirmed footstep
    private long _steppingUntilMs;

    public Direction Current { get; private set; } = Direction.Idle;
    public bool IsWeightCalibrated => _reference.IsCalibrated;

    /// <summary>Fires when the weight reference is refreshed mid-session (see
    /// ReferenceWeightCalibrator.Refreshed).</summary>
    public event Action? WeightCalibrationRefreshed;

    public DirectionClassifier()
    {
        _reference.Refreshed += () => WeightCalibrationRefreshed?.Invoke();
    }

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
    /// <param name="stepHoldMs">How long a confirmed stepping direction (Forward/Backward/Dash/Turn)
    /// persists after its last confirming peak -- configurable since how "sticky" a step should
    /// feel is a matter of taste.</param>
    /// <param name="turnEnabled">When false, turning is not tracked at all (any in-progress
    /// diagonal-alternation state is dropped) and stepping is never blocked by it -- lets output
    /// modes fully lock out left/right turning while leaving forward/backward/dash untouched.</param>
    /// <param name="turnSensitivity">0-100, see GestureSensitivityScale -- scales the diagonal
    /// footstep-alternation turn model's peak threshold (does not affect forward/backward/dash,
    /// which has its own separate footstep-threshold setting). 0 fully disables turning, same as
    /// turnEnabled = false.</param>
    public Direction Update(CalibratedReading cal, long nowMs, bool isPresent, double footstepThresholdRatio, long dashPeriodMs, long stepHoldMs, bool turnEnabled, int turnSensitivity)
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
            HandleFrontEdge(Corner.Right, nowMs, dashPeriodMs, stepHoldMs);
        }
        if (tlEdge && y >= 0)
        {
            HandleFrontEdge(Corner.Left, nowMs, dashPeriodMs, stepHoldMs);
        }
        if (brEdge && y <= 0)
        {
            HandleBackEdge(Corner.Right, nowMs, stepHoldMs);
        }
        if (blEdge && y <= 0)
        {
            HandleBackEdge(Corner.Left, nowMs, stepHoldMs);
        }

        bool turnActive = turnEnabled && !GestureSensitivityScale.IsDisabled(turnSensitivity);
        if (turnActive)
        {
            double diagonalThresholdPct = BaselineTurnDiagonalThresholdPct * GestureSensitivityScale.ThresholdMultiplier(turnSensitivity);
            bool diagTrEdge = _diagTopRight.Update(cal.PctTopRight, nowMs, 1.0, diagonalThresholdPct);
            bool diagTlEdge = _diagTopLeft.Update(cal.PctTopLeft, nowMs, 1.0, diagonalThresholdPct);
            bool diagBrEdge = _diagBottomRight.Update(cal.PctBottomRight, nowMs, 1.0, diagonalThresholdPct);
            bool diagBlEdge = _diagBottomLeft.Update(cal.PctBottomLeft, nowMs, 1.0, diagonalThresholdPct);
            if (diagTrEdge)
            {
                HandleDiagonalEdge(DiagonalCorner.TopRight, nowMs, stepHoldMs);
            }
            if (diagTlEdge)
            {
                HandleDiagonalEdge(DiagonalCorner.TopLeft, nowMs, stepHoldMs);
            }
            if (diagBrEdge)
            {
                HandleDiagonalEdge(DiagonalCorner.BottomRight, nowMs, stepHoldMs);
            }
            if (diagBlEdge)
            {
                HandleDiagonalEdge(DiagonalCorner.BottomLeft, nowMs, stepHoldMs);
            }
        }
        else if (_lastDiagonalEdge != DiagonalCorner.None)
        {
            // Drop any pending diagonal-alternation state immediately -- otherwise disabling
            // turning mid-gesture could leave a stale first step that pairs up the instant it's
            // re-enabled.
            _lastDiagonalEdge = DiagonalCorner.None;
        }

        // Forward/Backward/Dash/Turn all require an actual confirmed footstep alternation -- see
        // HandleFrontEdge/HandleBackEdge/HandleDiagonalEdge. Simply leaning and holding it (without
        // alternating feet) does NOT count.
        Current = nowMs <= _steppingUntilMs ? _steppingDirection : Direction.Idle;

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

    private void HandleFrontEdge(Corner corner, long nowMs, long dashPeriodMs, long stepHoldMs)
    {
        if (_lastFrontEdge != Corner.None && _lastFrontEdge != corner && nowMs - _lastFrontEdgeMs <= AlternationWindowMs)
        {
            long interval = nowMs - _lastFrontEdgeMs;
            _steppingDirection = interval < dashPeriodMs ? Direction.Dash : Direction.Forward;
            _steppingUntilMs = nowMs + stepHoldMs;
        }

        _lastFrontEdge = corner;
        _lastFrontEdgeMs = nowMs;
    }

    private void HandleBackEdge(Corner corner, long nowMs, long stepHoldMs)
    {
        if (_lastBackEdge != Corner.None && _lastBackEdge != corner && nowMs - _lastBackEdgeMs <= AlternationWindowMs)
        {
            ConfirmCompetingStep(Direction.Backward, nowMs, stepHoldMs);
        }

        _lastBackEdge = corner;
        _lastBackEdgeMs = nowMs;
    }

    // Confirms a turn on the *second* step of a diagonal pair -- back-right paired with front-left
    // (either order) is a right turn, front-right paired with back-left (either order) is a left
    // turn. A single corner crossing the threshold is never enough by itself; it just becomes the
    // pending "first step" for the next one to potentially pair with.
    private void HandleDiagonalEdge(DiagonalCorner corner, long nowMs, long stepHoldMs)
    {
        if (_lastDiagonalEdge != DiagonalCorner.None && nowMs - _lastDiagonalEdgeMs <= AlternationWindowMs)
        {
            Direction? confirmed = (_lastDiagonalEdge, corner) switch
            {
                (DiagonalCorner.BottomRight, DiagonalCorner.TopLeft) or (DiagonalCorner.TopLeft, DiagonalCorner.BottomRight) => Direction.TurnRight,
                (DiagonalCorner.TopRight, DiagonalCorner.BottomLeft) or (DiagonalCorner.BottomLeft, DiagonalCorner.TopRight) => Direction.TurnLeft,
                _ => null,
            };
            if (confirmed is { } direction)
            {
                ConfirmCompetingStep(direction, nowMs, stepHoldMs);
            }
        }

        _lastDiagonalEdge = corner;
        _lastDiagonalEdgeMs = nowMs;
    }

    // Backward and turn both come from a different corner pair than an active forward stride (the
    // back corners or the diagonal pair, vs forward's front corners), and in practice a real
    // footstep sometimes lights up one of those other corners enough to also cross their own
    // threshold mid-stride. If forward hasn't genuinely stopped when that happens -- judged by
    // recent front-corner activity, not by the (much shorter) output-hold timer, since ordinary
    // stride cadence is slower than stepHoldMs and would otherwise look "stopped" between every
    // single step -- treat it as noise from the same stride and just keep the forward hold alive
    // rather than switching direction out from under it. Once forward actually lapses (no front
    // corner touch for a real pause), the same signal is trusted normally -- so turning or walking
    // backward still works, it just has to follow a real stop.
    private void ConfirmCompetingStep(Direction direction, long nowMs, long stepHoldMs)
    {
        bool forwardStillGoing = _steppingDirection == Direction.Forward && nowMs - _lastFrontEdgeMs <= AlternationWindowMs;
        if (forwardStillGoing)
        {
            _steppingUntilMs = nowMs + stepHoldMs;
            return;
        }

        _steppingDirection = direction;
        _steppingUntilMs = nowMs + stepHoldMs;
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
