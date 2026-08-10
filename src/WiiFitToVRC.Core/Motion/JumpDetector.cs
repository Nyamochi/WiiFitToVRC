namespace WiiFitToVRC.Core.Motion;

/// <summary>
/// Jump is a push-off impulse followed almost immediately by a near-weightless dip as the feet
/// leave the board -- but firing on the push-off spike alone (the original design) turned out to
/// be indistinguishable from crouching, since committing weight to crouch can also spike the
/// total briefly. The rise by itself isn't enough signal; it's the rise *followed by* a rapid
/// collapse toward zero that's unique to actually leaving the ground. So this only fires once
/// that collapse is confirmed, not at the spike itself: Idle (tracking a slow baseline) -> Armed
/// (spike seen, waiting a short window for the airborne dip to confirm it) -> Landing (waiting
/// for weight to settle back to normal, ignoring any spike so the landing impact can't re-trigger).
/// If the dip never comes within the window, it wasn't a jump (e.g. a crouch push) and this drops
/// back to Idle without ever firing.
/// </summary>
public sealed class JumpDetector
{
    // Baseline (Gesture sensitivity = 50) push-off spike threshold -- scaled by
    // GestureSensitivityScale before use. The other constants below describe the shape of a real
    // jump's collapse-then-settle curve (distinguishing it from a crouch push) rather than "how
    // hard you have to move," so they're left fixed rather than tied to the sensitivity setting.
    private const double BaselineSpikeMultiplier = 1.5;
    private const double AirborneFraction = 0.3;
    private const double SettledLow = 0.7;
    private const double SettledHigh = 1.3;
    private const double EmaAlpha = 0.01;
    private const long ArmedTimeoutMs = 500; // push-off to airborne dip happens within a fraction of a second

    private enum State { Idle, Armed, Landing }

    private double _baselineEma = -1;
    private State _state = State.Idle;
    private long _armedSinceMs;

    /// <param name="jumpSensitivity">0-100, see GestureSensitivityScale -- scales how large the
    /// push-off spike must be relative to the baseline weight to arm (does not affect
    /// forward/backward). 0 fully disables jump: it can never arm, so the confirming key-press
    /// event can never fire.</param>
    /// <returns>True exactly on the sample where a jump (push-off confirmed by the following
    /// airborne dip) is detected.</returns>
    public bool Update(int total, long nowMs, int jumpSensitivity)
    {
        if (_baselineEma < 0)
        {
            _baselineEma = total;
            return false;
        }

        if (GestureSensitivityScale.IsDisabled(jumpSensitivity))
        {
            // Interrupt any in-progress arm/landing immediately rather than letting it finish, so
            // no key-press event can slip through after the setting is disabled. Baseline tracking
            // continues so it doesn't drift stale while disabled.
            _state = State.Idle;
            _baselineEma += EmaAlpha * (total - _baselineEma);
            return false;
        }

        // Anchored at 1.0 (a ratio, not a raw magnitude) -- moves the same direction as the other
        // detectors' thresholds: more sensitive pulls it toward 1.0 (a smaller spike is enough).
        double spikeMultiplier = 1.0 + (BaselineSpikeMultiplier - 1.0) * GestureSensitivityScale.ThresholdMultiplier(jumpSensitivity);

        switch (_state)
        {
            case State.Idle:
                if (total > _baselineEma * spikeMultiplier)
                {
                    _state = State.Armed;
                    _armedSinceMs = nowMs;
                    return false;
                }
                _baselineEma += EmaAlpha * (total - _baselineEma);
                return false;

            case State.Armed:
                if (total < _baselineEma * AirborneFraction)
                {
                    _state = State.Landing;
                    return true; // the rise + rapid collapse is confirmed -- this IS the jump moment
                }
                if (nowMs - _armedSinceMs > ArmedTimeoutMs)
                {
                    // The spike never collapsed into a real airborne dip (e.g. a crouch push) --
                    // not a jump.
                    _state = State.Idle;
                }
                return false;

            case State.Landing:
                if (total > _baselineEma * SettledLow && total < _baselineEma * SettledHigh)
                {
                    _state = State.Idle;
                }
                return false;

            default:
                return false;
        }
    }
}
