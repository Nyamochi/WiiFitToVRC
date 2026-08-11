using System.Linq;
using WiiFitToVRC.Core.Hid;

namespace WiiFitToVRC.Core.Motion;

/// <summary>
/// Continuously tracks a "resting" reference reading (each corner's calibrated value while
/// standing normally): every 5 seconds while the board reads present and nothing is currently
/// detected (Idle), take a sample; once 5 samples have accumulated, check whether that window is
/// essentially flat (its total weight's standard deviation is under FlatnessStdDev). Any window
/// that qualifies becomes the new reference outright -- not just the single steadiest window ever
/// seen. Walking spikes the total on every step, so a window spanning any real movement fails the
/// flatness check and the reference holds; standing still for 20+ seconds keeps refreshing it.
/// This deliberately lets the reference drift to a new person's weight: if a 70kg person steps
/// off and a 50kg person steps on and stands still, the very next flat window adopts their
/// weight, rather than staying anchored to whichever person happened to stand stillest first.
/// Call Reset() when the sensor zero-point itself changes (a fresh SensorCalibration pass), since
/// every value here is only meaningful relative to that offset.
/// </summary>
public sealed class ReferenceWeightCalibrator
{
    private const long SampleIntervalMs = 5000;
    private const int WindowSize = 5;

    // Below this standard deviation (same units as calibrated total weight), a 5-sample window
    // counts as "essentially flat" -- genuinely standing still rather than walking/shifting.
    private const double FlatnessStdDev = 200;

    private readonly Queue<(double total, double tr, double tl, double br, double bl)> _window = new();
    private long _lastSampleMs = -1;

    public bool IsCalibrated { get; private set; }
    public double ReferenceTopRight { get; private set; }
    public double ReferenceTopLeft { get; private set; }
    public double ReferenceBottomRight { get; private set; }
    public double ReferenceBottomLeft { get; private set; }

    /// <summary>Fires when the reference is refreshed while the app was already calibrated (i.e.
    /// not the very first calibration, which has its own "calibrating..." -> "connected" status
    /// transition) -- e.g. a different person stepped on and stood still.</summary>
    public event Action? Refreshed;

    /// <summary>Call only while the board reads present and nothing is currently detected.</summary>
    public void Update(CalibratedReading cal, long nowMs)
    {
        if (_lastSampleMs >= 0 && nowMs - _lastSampleMs < SampleIntervalMs)
        {
            return;
        }
        _lastSampleMs = nowMs;

        _window.Enqueue((cal.Total, cal.TopRight, cal.TopLeft, cal.BottomRight, cal.BottomLeft));
        if (_window.Count > WindowSize)
        {
            _window.Dequeue();
        }
        if (_window.Count < WindowSize)
        {
            return;
        }

        double meanTotal = _window.Average(s => s.total);
        double variance = _window.Sum(s => (s.total - meanTotal) * (s.total - meanTotal)) / _window.Count;
        double stdDev = Math.Sqrt(variance);

        if (stdDev <= FlatnessStdDev)
        {
            bool wasAlreadyCalibrated = IsCalibrated;
            ReferenceTopRight = _window.Average(s => s.tr);
            ReferenceTopLeft = _window.Average(s => s.tl);
            ReferenceBottomRight = _window.Average(s => s.br);
            ReferenceBottomLeft = _window.Average(s => s.bl);
            IsCalibrated = true;
            if (wasAlreadyCalibrated)
            {
                Refreshed?.Invoke();
            }
        }
    }

    public void Reset()
    {
        _window.Clear();
        _lastSampleMs = -1;
        IsCalibrated = false;
    }

    /// <summary>Seeds the reference directly from a single current reading instead of waiting for
    /// a flat window -- used for AppSettings.PostureMode.Sitting (see DirectionClassifier.Update),
    /// where a seated person's resting weight is light and inconsistent enough that the normal
    /// ~20+ second "stand still" wait isn't worth it, or reliable to begin with.</summary>
    public void CalibrateImmediately(CalibratedReading cal)
    {
        _window.Clear();
        _lastSampleMs = -1;
        ReferenceTopRight = cal.TopRight;
        ReferenceTopLeft = cal.TopLeft;
        ReferenceBottomRight = cal.BottomRight;
        ReferenceBottomLeft = cal.BottomLeft;
        IsCalibrated = true;
    }
}
