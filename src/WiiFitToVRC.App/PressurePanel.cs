using WiiFitToVRC.Core.Hid;

namespace WiiFitToVRC.App;

/// <summary>
/// Draws the 4 balance-board corners as a blue heatmap, using each corner's calibrated share of
/// the total weight (0-100%): below 20% reads as plain white, 40% and above reads as full-depth
/// blue, linear in between. Stays plain white entirely while the total is under the presence
/// threshold -- below that, percentages are just noise dividing up a near-zero total and
/// shouldn't paint any corner as "loaded".
/// </summary>
public sealed class PressurePanel : Panel
{
    private const double MinPct = 20;
    private const double MaxPct = 40;

    private readonly double[] _intensity = new double[4]; // TL, TR, BL, BR, each 0..1
    private readonly SolidBrush _brush = new(Color.White);
    private readonly Pen _borderPen = new(Color.Gray);

    public PressurePanel()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
    }

    public void SetValues(CalibratedReading? cal, int presenceWeightThreshold)
    {
        if (cal is null || cal.Total < presenceWeightThreshold)
        {
            Array.Clear(_intensity);
        }
        else
        {
            _intensity[0] = Intensity(cal.PctTopLeft);
            _intensity[1] = Intensity(cal.PctTopRight);
            _intensity[2] = Intensity(cal.PctBottomLeft);
            _intensity[3] = Intensity(cal.PctBottomRight);
        }
        Invalidate();
    }

    private static double Intensity(double pct) => Math.Clamp((pct - MinPct) / (MaxPct - MinPct), 0, 1);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int halfW = Width / 2;
        int halfH = Height / 2;
        var rects = new[]
        {
            new Rectangle(0, 0, halfW, halfH),           // TL
            new Rectangle(halfW, 0, Width - halfW, halfH), // TR
            new Rectangle(0, halfH, halfW, Height - halfH),           // BL
            new Rectangle(halfW, halfH, Width - halfW, Height - halfH), // BR
        };

        for (int i = 0; i < 4; i++)
        {
            int rg = (int)(255 - (_intensity[i] * 200));
            _brush.Color = Color.FromArgb(rg, rg, 255);
            e.Graphics.FillRectangle(_brush, rects[i]);
            e.Graphics.DrawRectangle(_borderPen, rects[i]);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _brush.Dispose();
            _borderPen.Dispose();
        }
        base.Dispose(disposing);
    }
}
