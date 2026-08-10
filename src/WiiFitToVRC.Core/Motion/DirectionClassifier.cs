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
/// Turning is a separate, narrower model: X (left-right weight) has to swing strongly to one side
/// and *stay* there continuously for a sustained stretch before it's confirmed -- an ordinary
/// step's sway crosses the same instantaneous threshold plenty, but flips side to side too
/// quickly to ever hold it for the full duration. Once confirmed, a turn releases only once X
/// drops back under a lower bound (hysteresis), and it wins over stepping while active.
///
/// Forward/Backward/Dash and Turn don't fall back into each other: leaning forward and holding it
/// (without alternating feet) reads as Idle, not Forward.
/// </summary>
public sealed class DirectionClassifier
{
    private const long AlternationWindowMs = 900; // max gap between opposite-foot peaks to count as one walking step

    // Baseline (Gesture sensitivity = 50) values -- scaled by GestureSensitivityScale before use.
    private const double BaselineTurnEnterX = 40;   // X (left-right %) magnitude to start timing a turn candidate
    private const double BaselineTurnExitX = 25;    // below this, a confirmed turn releases (hysteresis)
    private const long BaselineTurnSustainMs = 400; // how long the lean must hold continuously before it's confirmed

    private enum Corner { None, Right, Left }

    private readonly CornerPeakTracker _topRight = new();
    private readonly CornerPeakTracker _topLeft = new();
    private readonly CornerPeakTracker _bottomRight = new();
    private readonly CornerPeakTracker _bottomLeft = new();
    private readonly ReferenceWeightCalibrator _reference = new();

    private Corner _lastFrontEdge = Corner.None;
    private long _lastFrontEdgeMs;
    private Corner _lastBackEdge = Corner.None;
    private long _lastBackEdgeMs;

    private Direction _steppingDirection = Direction.Idle; // Forward/Dash/Backward from a confirmed footstep
    private long _steppingUntilMs;

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
    /// <param name="stepHoldMs">How long a confirmed stepping direction (Forward/Backward/Dash)
    /// persists after its last confirming peak -- configurable since how "sticky" a step should
    /// feel is a matter of taste.</param>
    /// <param name="turnEnabled">When false, turning is not tracked at all (any in-progress lean
    /// is dropped) and stepping is never blocked by it -- lets output modes fully lock out
    /// left/right turning while leaving forward/backward/dash untouched.</param>
    /// <param name="turnSensitivity">0-100, see GestureSensitivityScale -- scales the turn
    /// entry/exit thresholds and sustain duration (does not affect forward/backward/dash, which
    /// has its own separate footstep-threshold setting). 0 fully disables turning, same as
    /// turnEnabled = false.</param>
    public Direction Update(CalibratedReading cal, long nowMs, bool isPresent, double footstepThresholdRatio, long dashPeriodMs, long stepHoldMs, bool turnEnabled, int turnSensitivity)
    {
        bool trEdge = _topRight.Update(cal.TopRight, nowMs, _reference.ReferenceTopRight, footstepThresholdRatio);
        bool tlEdge = _topLeft.Update(cal.TopLeft, nowMs, _reference.ReferenceTopLeft, footstepThresholdRatio);
        bool brEdge = _bottomRight.Update(cal.BottomRight, nowMs, _reference.ReferenceBottomRight, footstepThresholdRatio);
        bool blEdge = _bottomLeft.Update(cal.BottomLeft, nowMs, _reference.ReferenceBottomLeft, footstepThresholdRatio);

        double x = ComputeX(cal);
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

        if (turnEnabled && !GestureSensitivityScale.IsDisabled(turnSensitivity))
        {
            double multiplier = GestureSensitivityScale.ThresholdMultiplier(turnSensitivity);
            UpdateTurn(x, nowMs, BaselineTurnEnterX * multiplier, BaselineTurnExitX * multiplier, (long)(BaselineTurnSustainMs * multiplier));
        }
        else if (_turnConfirmed || _turnCandidateSinceMs >= 0)
        {
            // Drop any in-progress or confirmed turn immediately -- otherwise disabling turning
            // mid-lean would leave a stale confirmation that never releases (X never gets sampled
            // again while turnEnabled stays false) or re-appears the instant it's re-enabled.
            _turnConfirmed = false;
            _turnCandidateSide = Direction.Idle;
            _turnCandidateSinceMs = -1;
        }

        Direction proposed;
        if (_turnConfirmed)
        {
            // Turning is a sustained hold -- see UpdateTurn.
            proposed = _turnSide;
        }
        else if (nowMs <= _steppingUntilMs)
        {
            // Forward/Backward/Dash require an actual confirmed footstep alternation -- see
            // HandleFrontEdge/HandleBackEdge. Simply leaning forward and holding it (without
            // alternating feet) does NOT count.
            proposed = _steppingDirection;
        }
        else
        {
            proposed = Direction.Idle;
        }

        Current = proposed;

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
            _steppingDirection = Direction.Backward;
            _steppingUntilMs = nowMs + stepHoldMs;
        }

        _lastBackEdge = corner;
        _lastBackEdgeMs = nowMs;
    }

    /// <summary>
    /// Watches one corner for the moment its value crosses footstepThresholdRatio times a
    /// reference resting value supplied from outside (see ReferenceWeightCalibrator). A reference
    /// of 0 (not yet calibrated) never counts as "above" -- no edges fire until a reference exists.
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
