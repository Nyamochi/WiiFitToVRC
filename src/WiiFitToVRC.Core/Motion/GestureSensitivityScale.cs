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
}
