namespace WiiFitToVRC.Core.Motion;

/// <summary>
/// Converts the single 0-100 "Gesture sensitivity" setting -- which covers turn/jump/crouch only;
/// forward/backward has its own separate footstep-threshold setting -- into a multiplier applied
/// to each detector's baseline threshold/duration constants. 50 is neutral (multiplier 1.0,
/// exactly today's hardcoded values); each point away from 50 is a 1% change, so 0 -> 1.5x
/// (thresholds/durations 50% larger, harder to trigger -- "weak") and 100 -> 0.5x (50% smaller,
/// easier to trigger -- "strong").
/// </summary>
public static class GestureSensitivityScale
{
    public static double ThresholdMultiplier(int sensitivity) => 1.0 - (sensitivity - 50) * 0.01;

    /// <summary>0 is the bottom of the "Weak" end of the scale, but doesn't just make the gesture
    /// harder to trigger -- it fully disables it, so callers should skip the detection logic
    /// entirely (rather than just plugging a very large multiplier into it) to guarantee it can
    /// never fire regardless of input.</summary>
    public static bool IsDisabled(int sensitivity) => sensitivity <= 0;
}
