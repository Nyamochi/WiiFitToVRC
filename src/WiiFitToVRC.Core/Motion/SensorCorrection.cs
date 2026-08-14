using WiiFitToVRC.Core.Hid;

namespace WiiFitToVRC.Core.Motion;

/// <summary>
/// AppSettings.ForcedControllerCorrection support: per-corner multipliers that equalize a
/// permanently weak/desensitized sensor's readings against the other three, for boards where one
/// corner barely registers weight at all. Established once from the raw reference weight
/// (DirectionClassifier.Reference*) the moment it's first available, then frozen for the rest of
/// that calibration cycle -- see InputController.Update, which applies the frozen factors to
/// every sample from then on, including what feeds DirectionClassifier's own ongoing reference
/// re-learning. Recomputing from an already-corrected reference would be self-defeating: once
/// corrected, every corner's re-learned reference converges toward the same target, so a second
/// computation from that reference would always yield trivial 1.0 factors and silently erase the
/// original correction. Freezing avoids that; a fresh ResetWeightCalibration (manual
/// recalibration, or an AppSettings.PostureMode switch) is what starts a new correction cycle, via
/// Reset.
/// </summary>
public sealed class SensorCorrection
{
    private double _topRightFactor = 1.0;
    private double _bottomRightFactor = 1.0;
    private double _topLeftFactor = 1.0;
    private double _bottomLeftFactor = 1.0;

    public bool IsEstablished { get; private set; }

    /// <summary>Call every sample while AppSettings.ForcedControllerCorrection is on -- a cheap
    /// no-op once already established.</summary>
    public void TryEstablish(DirectionClassifier direction)
    {
        if (IsEstablished || !direction.IsWeightCalibrated)
        {
            return;
        }

        // Target = the plain average of the four raw reference corners -- the only choice that
        // keeps the corrected total equal to the raw total (4 * average == the original sum), so
        // this only redistributes weight across corners rather than also silently shifting
        // total-weight-based settings like Presence weight threshold.
        double total = direction.ReferenceTopRight + direction.ReferenceBottomRight + direction.ReferenceTopLeft + direction.ReferenceBottomLeft;
        double target = total / 4.0;

        _topRightFactor = Factor(direction.ReferenceTopRight, target);
        _bottomRightFactor = Factor(direction.ReferenceBottomRight, target);
        _topLeftFactor = Factor(direction.ReferenceTopLeft, target);
        _bottomLeftFactor = Factor(direction.ReferenceBottomLeft, target);
        IsEstablished = true;
    }

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

    public void Reset()
    {
        IsEstablished = false;
        _topRightFactor = 1.0;
        _bottomRightFactor = 1.0;
        _topLeftFactor = 1.0;
        _bottomLeftFactor = 1.0;
    }
}
