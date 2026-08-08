using WiiFitToVRC.Core.Hid;
using WiiFitToVRC.Core.Motion;

if (args.Length == 0)
{
    Console.WriteLine("使い方: ClassifyTest <csvファイル...>");
    return;
}

foreach (string path in args)
{
    RunOneFile(path);
    Console.WriteLine();
}

static void RunOneFile(string path)
{
    var directionClassifier = new DirectionClassifier();
    var crouchDetector = new CrouchDetector();
    var jumpDetector = new JumpDetector();

    var directionCounts = new Dictionary<Direction, int>();
    int crouchSamples = 0;
    int jumpTriggers = 0;
    int totalSamples = 0;
    string? label = null;

    foreach (string line in File.ReadLines(path).Skip(1))
    {
        string[] cols = line.Split(',');
        if (cols.Length < 15 || cols[10].Length == 0)
        {
            continue; // no calibration data on this row
        }

        label ??= cols[1];
        long unixMs = long.Parse(cols[0]);
        var cal = new CalibratedReading
        {
            TopRight = int.Parse(cols[6]),
            BottomRight = int.Parse(cols[7]),
            TopLeft = int.Parse(cols[8]),
            BottomLeft = int.Parse(cols[9]),
            Total = int.Parse(cols[10]),
            PctTopRight = double.Parse(cols[11]),
            PctBottomRight = double.Parse(cols[12]),
            PctTopLeft = double.Parse(cols[13]),
            PctBottomLeft = double.Parse(cols[14]),
        };

        double y = DirectionClassifier.ComputeY(cal);

        var direction = directionClassifier.Update(cal, unixMs, isPresent: true, footstepThresholdRatio: 1.20, dashPeriodMs: 300, stepHoldMs: 77, turnEnabled: true);
        directionCounts[direction] = directionCounts.GetValueOrDefault(direction) + 1;

        if (crouchDetector.Update(y, unixMs))
        {
            crouchSamples++;
        }

        if (jumpDetector.Update(cal.Total, unixMs))
        {
            jumpTriggers++;
        }

        totalSamples++;
    }

    Console.WriteLine($"=== {Path.GetFileName(path)} (label={label}, rows={totalSamples}) ===");
    foreach (var (direction, count) in directionCounts.OrderByDescending(kv => kv.Value))
    {
        Console.WriteLine($"  {direction,-12} {count,6} ({100.0 * count / totalSamples,5:F1}%)");
    }
    Console.WriteLine($"  crouch: {crouchSamples} samples ({100.0 * crouchSamples / totalSamples:F1}%)");
    Console.WriteLine($"  jump triggers: {jumpTriggers}");
}
