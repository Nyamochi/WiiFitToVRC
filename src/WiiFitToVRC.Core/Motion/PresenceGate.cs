namespace WiiFitToVRC.Core.Motion;

/// <summary>
/// Gates output on someone actually standing on the board, on top of calibration. Calibration
/// alone isn't enough -- once calibrated, an empty board still reads a small nonzero/noisy total,
/// and classifier noise on an unoccupied board was part of what caused runaway key output.
/// Both directions share one "sleep seconds" delay: the board must stay above the weight
/// threshold continuously for that long before output unlocks (a stray brush against the board
/// shouldn't immediately start sending input), and must stay below it continuously for the same
/// duration before output re-locks (stepping off takes a moment, and momentary weight dips during
/// normal play shouldn't cut output).
/// </summary>
public sealed class PresenceGate
{
    private bool _isPresent;
    private long _aboveSinceMs = -1;
    private long _belowSinceMs = -1;

    public bool IsPresent => _isPresent;

    public bool Update(int total, long nowMs, int weightThreshold, int sleepSeconds)
    {
        long delayMs = sleepSeconds * 1000L;

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
