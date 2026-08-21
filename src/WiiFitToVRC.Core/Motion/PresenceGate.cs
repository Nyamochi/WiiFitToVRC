namespace WiiFitToVRC.Core.Motion;

/// <summary>
/// Gates output on someone actually standing on the board, on top of calibration. Calibration
/// alone isn't enough -- once calibrated, an empty board still reads a small nonzero/noisy total,
/// and classifier noise on an unoccupied board was part of what caused runaway key output.
///
/// Both directions share one "sleep seconds" delay (time hysteresis): the board must stay above
/// the "on" weight threshold continuously for that long before output unlocks, and below the
/// (lower) "off" threshold continuously for the same duration before output re-locks. The two
/// thresholds themselves are also separate (amplitude hysteresis, on > off) rather than one
/// shared value -- with a single threshold, a total that dithers right on the boundary keeps
/// resetting *both* timers every sample, so neither one ever accumulates enough to fire; two
/// thresholds give the signal room to dither without ever touching either boundary. This matters
/// most for AppSettings.PostureMode.Sitting, whose EffectiveSleepSeconds is 0 (no time hysteresis
/// at all, see that property's own doc comment) -- with only one threshold, a single sample poking
/// across it toggles presence immediately. Real seated recordings (debug/sit_*.csv) confirmed
/// this happening constantly: cal_total spent 20-30% of some sessions within a couple hundred
/// units of the 500 threshold, and a from-scratch simulation of the old single-threshold logic
/// showed presence flickering off and back on up to 139 times in one continuously-seated session
/// that should have read present throughout. Splitting the threshold (on=500, off=250) collapsed
/// most of those sessions to their one legitimate sit-down transition, and cut the worst case from
/// 139 to 51 -- what's left there is genuine foot-lift dips during seated forward-gait footwork,
/// not sensor noise, so no threshold placement removes it. Standing recordings never showed any
/// difference either way (a standing person's resting weight sits thousands of units clear of
/// PresenceWeightThreshold), so the same split costs it nothing.
/// </summary>
public sealed class PresenceGate
{
    private bool _isPresent;
    private long _aboveSinceMs = -1;
    private long _belowSinceMs = -1;

    public bool IsPresent => _isPresent;

    /// <param name="onWeightThreshold">Must be exceeded to start (or keep) counting toward
    /// presence while not yet present.</param>
    /// <param name="offWeightThreshold">Must be exceeded to keep (or regain) presence once already
    /// present -- lower than onWeightThreshold, see the class doc comment.</param>
    public bool Update(int total, long nowMs, int onWeightThreshold, int offWeightThreshold, int sleepSeconds)
    {
        long delayMs = sleepSeconds * 1000L;
        int weightThreshold = _isPresent ? offWeightThreshold : onWeightThreshold;

        if (total > weightThreshold)
        {
            _belowSinceMs = -1;
            if (_aboveSinceMs < 0)
            {
                _aboveSinceMs = nowMs;
            }
            if (!_isPresent && nowMs - _aboveSinceMs >= delayMs)
            {
                _isPresent = true;
            }
        }
        else
        {
            _aboveSinceMs = -1;
            if (_belowSinceMs < 0)
            {
                _belowSinceMs = nowMs;
            }
            if (_isPresent && nowMs - _belowSinceMs >= delayMs)
            {
                _isPresent = false;
            }
        }

        return _isPresent;
    }

    public void Reset()
    {
        _isPresent = false;
        _aboveSinceMs = -1;
        _belowSinceMs = -1;
    }
}
