namespace WiiFitToVRC.Core.Motion;

/// <summary>
/// Crouch is a sustained postural shift onto the front of the board, but a plain threshold on Y
/// (front-back weight) turned out to also catch jump's push-off, since committing weight forward
/// fast can cross the same level a deliberate crouch does. Rather than judging the rate of rise
/// directly, this requires Y to stay above the threshold continuously for a minimum hold before
/// confirming crouch -- a real crouch settles in and stays there, while a jump's front-loading is
/// a brief spike immediately followed by the airborne weight collapse (see JumpDetector), so it
/// can't sustain the hold. Exiting crouch (standing back up) isn't hold-gated; any drop back
/// below the lower threshold counts immediately.
/// </summary>
public sealed class CrouchDetector
{
    // Baseline (Gesture sensitivity = 50) values -- scaled by GestureSensitivityScale before use.
    private const double BaselineEnterY = 45;
    private const double BaselineExitY = 30;
    private const long BaselineMinHoldMs = 500;

    private long _aboveSinceMs = -1;

    public bool IsCrouching { get; private set; }

    /// <param name="gestureSensitivity">0-100, see GestureSensitivityScale -- scales the entry/exit
    /// Y thresholds and the minimum hold duration together (does not affect forward/backward).</param>
    public bool Update(double y, long nowMs, int gestureSensitivity)
    {
        double multiplier = GestureSensitivityScale.ThresholdMultiplier(gestureSensitivity);
        double enterY = BaselineEnterY * multiplier;
        double exitY = BaselineExitY * multiplier;
        long minHoldMs = (long)(BaselineMinHoldMs * multiplier);

        if (!IsCrouching)
        {
            if (y > enterY)
            {
                if (_aboveSinceMs < 0)
                {
                    _aboveSinceMs = nowMs;
                }
                if (nowMs - _aboveSinceMs >= minHoldMs)
                {
                    IsCrouching = true;
                }
            }
            else
            {
                _aboveSinceMs = -1;
            }
        }
        else if (y < exitY)
        {
            IsCrouching = false;
            _aboveSinceMs = -1;
        }

        return IsCrouching;
    }
}
