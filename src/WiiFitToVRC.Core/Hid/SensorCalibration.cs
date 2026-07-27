namespace WiiFitToVRC.Core.Hid;

public sealed class CalibratedReading
{
    public required int TopRight { get; init; }
    public required int BottomRight { get; init; }
    public required int TopLeft { get; init; }
    public required int BottomLeft { get; init; }
    public required int Total { get; init; }
    public required double PctTopRight { get; init; }
    public required double PctBottomRight { get; init; }
    public required double PctTopLeft { get; init; }
    public required double PctBottomLeft { get; init; }
}

/// <summary>
/// Zeroes each of the 4 raw sensor values against the most frequent ("mode") value seen during
/// a sampling window (e.g. 10s of standing still), rather than any fixed/absolute value -- since
/// the board's resting distribution isn't centered (uneven floor, off-center stance), only a
/// per-corner "value it sat at most" offset makes sense as a zero reference. Mode is more robust
/// to a stray low outlier during sampling than a plain minimum would be.
/// </summary>
public sealed class SensorCalibration
{
    private readonly Dictionary<ushort, int> _countsTopRight = [];
    private readonly Dictionary<ushort, int> _countsBottomRight = [];
    private readonly Dictionary<ushort, int> _countsTopLeft = [];
    private readonly Dictionary<ushort, int> _countsBottomLeft = [];

    public bool IsSampling { get; private set; }
    public bool IsCalibrated { get; private set; }
    public ushort OffsetTopRight { get; private set; }
    public ushort OffsetBottomRight { get; private set; }
    public ushort OffsetTopLeft { get; private set; }
    public ushort OffsetBottomLeft { get; private set; }

    public void BeginSampling()
    {
        _countsTopRight.Clear();
        _countsBottomRight.Clear();
        _countsTopLeft.Clear();
        _countsBottomLeft.Clear();
        IsSampling = true;
        IsCalibrated = false;
    }

    public void Sample(BalanceBoardSensors s)
    {
        if (!IsSampling)
        {
            return;
        }

        Increment(_countsTopRight, s.TopRight);
        Increment(_countsBottomRight, s.BottomRight);
        Increment(_countsTopLeft, s.TopLeft);
        Increment(_countsBottomLeft, s.BottomLeft);
    }

    private static void Increment(Dictionary<ushort, int> counts, ushort value)
    {
        counts[value] = counts.GetValueOrDefault(value) + 1;
    }

    private static ushort Mode(Dictionary<ushort, int> counts)
    {
        ushort best = 0;
        int bestCount = -1;
        foreach (var (value, count) in counts)
        {
            if (count > bestCount)
            {
                best = value;
                bestCount = count;
            }
        }
        return best;
    }

    public void FinishSampling()
    {
        OffsetTopRight = Mode(_countsTopRight);
        OffsetBottomRight = Mode(_countsBottomRight);
        OffsetTopLeft = Mode(_countsTopLeft);
        OffsetBottomLeft = Mode(_countsBottomLeft);
        IsSampling = false;
        IsCalibrated = true;
    }

    public CalibratedReading Apply(BalanceBoardSensors s)
    {
        int tr = Math.Max(0, s.TopRight - OffsetTopRight);
        int br = Math.Max(0, s.BottomRight - OffsetBottomRight);
        int tl = Math.Max(0, s.TopLeft - OffsetTopLeft);
        int bl = Math.Max(0, s.BottomLeft - OffsetBottomLeft);
        int total = tr + br + tl + bl;

        return new CalibratedReading
        {
            TopRight = tr,
            BottomRight = br,
            TopLeft = tl,
            BottomLeft = bl,
            Total = total,
            PctTopRight = total > 0 ? tr * 100.0 / total : 0,
            PctBottomRight = total > 0 ? br * 100.0 / total : 0,
            PctTopLeft = total > 0 ? tl * 100.0 / total : 0,
            PctBottomLeft = total > 0 ? bl * 100.0 / total : 0,
        };
    }
}
