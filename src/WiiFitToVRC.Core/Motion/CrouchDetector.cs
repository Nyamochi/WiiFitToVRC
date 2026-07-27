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
    private const double EnterY = 45;
    private const double ExitY = 30;
    private const long MinHoldMs = 500;

    private long _aboveSinceMs = -1;

    public bool IsCrouching { get; private set; }

    public bool Update(double y, long nowMs)
    {
        if (!IsCrouching)
        {
            if (y > EnterY)
            {
                if (_aboveSinceMs < 0)
                {
                    _aboveSinceMs = nowMs;
                }
                if (nowMs - _aboveSinceMs >= MinHoldMs)
                {
                    IsCrouching = true;
                }
            }
            else
            {
                _aboveSinceMs = -1;
            }
        }
        else if (y < ExitY)
        {
            IsCrouching = false;
            _aboveSinceMs = -1;
        }

        return IsCrouching;
    }
}
