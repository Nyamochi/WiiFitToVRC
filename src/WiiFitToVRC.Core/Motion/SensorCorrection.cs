using WiiFitToVRC.Core.Hid;

namespace WiiFitToVRC.Core.Motion;

/// <summary>
/// AppSettings.ForcedControllerCorrection support: per-corner multipliers that equalize a
/// permanently weak/desensitized sensor's readings against the other three, for boards where one
/// corner barely registers weight at all. A fresh raw measurement is taken from the reference
/// weight (DirectionClassifier.Reference*) every time it's freshly (re-)established -- app launch,
/// a manual sensor recalibration, or an AppSettings.PostureMode switch, each via
/// InputController.ResetWeightCalibration -- but the factor actually applied is the *running
/// average* of every measurement taken so far, not just the latest one: any single calibration can
/// land on an unusually skewed reference (mid-step, an odd stance, calibrating before fully
/// settling), and averaging across repeated measurements keeps one such outlier from producing an
/// extreme, one-off correction. The running average (and how many measurements it's built from) is
/// persisted to settings.json (see AppSettings.CorrectionTopRightFactor etc.), so it keeps
/// improving across every launch rather than starting over each time.
///
/// Within a single calibration cycle the factor stays fixed once measured -- see InputController.
/// Update, which applies it to every sample from then on, including what feeds DirectionClassifier's
/// own ongoing reference re-learning. Recomputing from an already-corrected reference within that
/// same cycle would be self-defeating: once corrected, the re-learned reference converges toward
/// the same target on every corner, so recomputing from it would trivially yield 1.0 and erase the
/// correction. Only ever taking one fresh measurement per ResetWeightCalibration (never more often
/// than that) avoids that trap while still letting the average grow over time.
/// </summary>
public sealed class SensorCorrection
{
    private double _topRightFactor = 1.0;
    private double _bottomRightFactor = 1.0;
    private double _topLeftFactor = 1.0;
    private double _bottomLeftFactor = 1.0;
    private int _sampleCount;

    // True from construction and after every Reset() until the next raw measurement is folded
    // into the running average -- distinct from IsEstablished, which (once true) never goes back
    // to false, since the running average itself is always meaningful to keep applying even while
    // waiting on the next measurement.
    private bool _awaitingSample = true;

    public bool IsEstablished => _sampleCount > 0;
    public double TopRightFactor => _topRightFactor;
    public double BottomRightFactor => _bottomRightFactor;
    public double TopLeftFactor => _topLeftFactor;
    public double BottomLeftFactor => _bottomLeftFactor;
    public int SampleCount => _sampleCount;

    /// <summary>Restores the running average accumulated in previous sessions -- see
    /// InputController's constructor. Doesn't affect whether the next calibration takes a fresh
    /// measurement (that's always true right after construction, regardless of loaded
    /// history).</summary>
    public void LoadHistory(double topRightFactor, double bottomRightFactor, double topLeftFactor, double bottomLeftFactor, int sampleCount)
    {
        _topRightFactor = topRightFactor;
        _bottomRightFactor = bottomRightFactor;
        _topLeftFactor = topLeftFactor;
        _bottomLeftFactor = bottomLeftFactor;
        _sampleCount = sampleCount;
    }

    /// <summary>Call every sample while AppSettings.ForcedControllerCorrection is on -- a cheap
    /// no-op except right after construction or a Reset(), once the reference is calibrated.</summary>
    /// <returns>True exactly on the call where a new raw measurement was folded into the running
    /// average -- the caller should persist the (now-updated) factors/SampleCount at that point
    /// (see InputController.Update).</returns>
    public bool TryEstablish(DirectionClassifier direction)
    {
        if (!_awaitingSample || !direction.IsWeightCalibrated)
        {
            return false;
        }

        // Target = the plain average of the four raw reference corners -- the only choice that
        // keeps the corrected total equal to the raw total (4 * average == the original sum), so
        // this only redistributes weight across corners rather than also silently shifting
        // total-weight-based settings like Presence weight threshold.
        double total = direction.ReferenceTopRight + direction.ReferenceBottomRight + direction.ReferenceTopLeft + direction.ReferenceBottomLeft;
        double target = total / 4.0;

        _topRightFactor = FoldIn(_topRightFactor, Factor(direction.ReferenceTopRight, target));
        _bottomRightFactor = FoldIn(_bottomRightFactor, Factor(direction.ReferenceBottomRight, target));
        _topLeftFactor = FoldIn(_topLeftFactor, Factor(direction.ReferenceTopLeft, target));
        _bottomLeftFactor = FoldIn(_bottomLeftFactor, Factor(direction.ReferenceBottomLeft, target));
        _sampleCount++;
        _awaitingSample = false;
        return true;
    }

    // Incremental running-average update -- mathematically exact (no drift), and doesn't need the
    // full measurement history kept around, just the running average and how many measurements
    // went into it so far.
    private double FoldIn(double runningAverage, double newSample) =>
        _sampleCount == 0 ? newSample : runningAverage + (newSample - runningAverage) / (_sampleCount + 1);

    // A corner whose raw reference reads exactly 0 (a fully dead sensor, not just a weak one)
    // can't be meaningfully amplified -- any multiplier of 0 is still 0 -- so it's left
    // uncorrected (1.0) rather than dividing by zero.
    private static double Factor(double referenceCorner, double target) =>
        referenceCorner > 0 ? target / referenceCorner : 1.0;

    public CalibratedReading Apply(CalibratedReading raw)
    {
        int tr = (int)Math.Round(raw.TopRight * _topRightFactor);
        int br = (int)Math.Round(raw.BottomRight * _bottomRightFactor);
        int tl = (int)Math.Round(raw.TopLeft * _topLeftFactor);
        int bl = (int)Math.Round(raw.BottomLeft * _bottomLeftFactor);
        int total = tr + br + tl + bl;

        return new CalibratedReading
        {
            TopRight = tr,
            BottomRight = br,
            TopLeft = tl,
            BottomLeft = bl,
            Total = total,
            PctTopRight = total > 0 ? 100.0 * tr / total : 0,
            PctBottomRight = total > 0 ? 100.0 * br / total : 0,
            PctTopLeft = total > 0 ? 100.0 * tl / total : 0,
            PctBottomLeft = total > 0 ? 100.0 * bl / total : 0,
        };
    }

    /// <summary>Call alongside DirectionClassifier.ResetWeightCalibration (see InputController.
    /// ResetWeightCalibration) -- arms the next calibration to take a fresh measurement. Doesn't
    /// clear the running average itself: the whole point is a running average across every
    /// calibration ever taken, not just the most recent one.</summary>
    public void Reset()
    {
        _awaitingSample = true;
    }

    /// <summary>Wipes the running average back to a neutral "no correction, no measurements yet"
    /// state -- unlike Reset() above, which just arms the next measurement while keeping history.
    /// Call when AppSettings.ForcedControllerCorrection is turned off after having been on (see
    /// SettingsForm.Save): the accumulated average describes whatever controller was in use while
    /// it was being measured, and isn't meaningful once that hardware's been repaired or
    /// replaced.</summary>
    public void ClearHistory()
    {
        _topRightFactor = 1.0;
        _bottomRightFactor = 1.0;
        _topLeftFactor = 1.0;
        _bottomLeftFactor = 1.0;
        _sampleCount = 0;
        _awaitingSample = true;
    }
}
