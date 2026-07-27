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
    private const double SpikeMultiplier = 1.5;
    private const double AirborneFraction = 0.3;
    private const double SettledLow = 0.7;
    private const double SettledHigh = 1.3;
    private const double EmaAlpha = 0.01;
    private const long ArmedTimeoutMs = 500; // push-off to airborne dip happens within a fraction of a second

    private enum State { Idle, Armed, Landing }

    private double _baselineEma = -1;
    private State _state = State.Idle;
    private long _armedSinceMs;

    /// <summary>Returns true exactly on the sample where a jump (push-off confirmed by the
    /// following airborne dip) is detected.</summary>
    public bool Update(int total, long nowMs)
    {
        if (_baselineEma < 0)
        {
            _baselineEma = total;
            return false;
        }

        switch (_state)
        {
            case State.Idle:
                if (total > _baselineEma * SpikeMultiplier)
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
