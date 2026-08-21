using WiiFitToVRC.Core.Hid;

namespace WiiFitToVRC.Core.Motion;

/// <summary>
/// AppSettings.MovementMode.WeightShift: an alternative to the Footstep model (see
/// DirectionClassifier) that skips footstep pairing entirely -- Forward/Backward/TurnLeft/TurnRight
/// is read directly and continuously off however far the current lean (X/Y, see
/// DirectionClassifier.ComputeX/ComputeY) sits past a single shared threshold. No footstep
/// alternation, no hold/continuation timing, no Dash (there's no pairing interval to time a dash
/// against) -- the direction is exactly whatever this instant's lean says, and releases the moment
/// the lean drops back under threshold. Jump/Crouch aren't driven from this mode at all (see
/// InputController.Update) -- weight-shift mode is movement only.
/// </summary>
public static class WeightShiftClassifier
{
    // Neutral (sensitivity 50) lean threshold, in the same percentage-point units as
    // DirectionClassifier.ComputeX/ComputeY. Tuned against a real 35-second neutral-stance
    // recording (debug/session_20260815_192404.csv, someone just standing normally, not leaning
    // any direction): X swung as far as 12.8 and Y as far as 19.4 during that whole recording,
    // purely from ordinary sway -- 25 sits comfortably above both, while real intentional leaning
    // in forward/backward/turn recordings reached 40-80+ at even moderate percentiles, leaving a
    // wide gap between "still just standing" and "clearly leaning".
    private const double BaselineThresholdPct = 25;

    public static Direction Classify(CalibratedReading cal, int sensitivity)
    {
        double threshold = BaselineThresholdPct * GestureSensitivityScale.ThresholdMultiplier(sensitivity);
        double x = DirectionClassifier.ComputeX(cal);
        double y = DirectionClassifier.ComputeY(cal);
        double absX = Math.Abs(x);
        double absY = Math.Abs(y);

        if (absX < threshold && absY < threshold)
        {
            return Direction.Idle;
        }

        // Whichever axis is leaning harder (relative to the same shared threshold) wins -- a
        // diagonal lean reads as one clean direction rather than none or both at once.
        return absY >= absX
            ? (y > 0 ? Direction.Forward : Direction.Backward)
            : (x > 0 ? Direction.TurnRight : Direction.TurnLeft);
    }
}
