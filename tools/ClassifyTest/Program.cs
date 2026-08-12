using WiiFitToVRC.Core.Hid;
using WiiFitToVRC.Core.Motion;

if (args.Length == 0)
{
    Console.WriteLine("使い方: ClassifyTest [--hold] <csvファイル...>");
    return;
}

bool footstepTurnMode = !args.Contains("--hold"); // Footstep is the default turn model, matching AppSettings
string[] paths = args.Where(a => a != "--hold").ToArray();

foreach (string path in paths)
{
    RunOneFile(path, footstepTurnMode);
    Console.WriteLine();
}

// These recordings are short single-gesture bursts with no ~25s calm-standing stretch, so
// ReferenceWeightCalibrator (which needs one to learn a resting reference) never calibrates on
// its own -- forward/backward footstep detection would never fire at all. Seed it instead with 5
// synthetic flat samples (evenly split across the 4 corners, since we don't have this person's
// real resting stance) well before the real data starts, so the reference reads ReferenceWeight
// per corner by the time real rows are processed. Sitting recordings (filename prefix "sit_", see
// debug/sit_*.csv) skip this entirely -- their forward/backward/dash detection doesn't depend on
// the reference at all (see DirectionClassifier's instantWeightCalibration branch), and the
// reference they DO calibrate (for jump's sittingReferenceTotal) is meant to seed from real data
// the moment presence first crosses AppSettings.EffectivePresenceWeightThreshold (500), the same
// as the real app does, not from a synthetic flat seed.
const double ReferenceWeight = 7100;
const int SittingPresenceWeightThreshold = 500;

static void RunOneFile(string path, bool footstepTurnMode)
{
    bool sitting = Path.GetFileName(path).StartsWith("sit_", StringComparison.Ordinal);
    var directionClassifier = new DirectionClassifier();
    var crouchDetector = new CrouchDetector();
    var jumpDetector = new JumpDetector();

    var directionCounts = new Dictionary<Direction, int>();
    var stepGapsByDirection = new Dictionary<Direction, List<long>>();
    int crouchSamples = 0;
    int jumpTriggers = 0;
    int totalSamples = 0;
    string? label = null;
    bool seeded = false;
    long firstUnixMs = 0;
    Direction lastTraced = Direction.Idle;

    directionClassifier.StepPaired += (direction, gapMs) =>
    {
        if (!stepGapsByDirection.TryGetValue(direction, out var gaps))
        {
            gaps = [];
            stepGapsByDirection[direction] = gaps;
        }
        gaps.Add(gapMs);
    };

    foreach (string line in File.ReadLines(path).Skip(1))
    {
        string[] cols = line.Split(',');
        if (cols.Length < 15 || cols[10].Length == 0)
        {
            continue; // no calibration data on this row
        }

        label ??= cols[1];
        long unixMs = long.Parse(cols[0]);

        if (!seeded)
        {
            if (!sitting)
            {
                SeedReference(directionClassifier, unixMs, footstepTurnMode);
            }
            seeded = true;
            firstUnixMs = unixMs;
        }
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
        bool isPresent = !sitting || cal.Total > SittingPresenceWeightThreshold;

        // Sitting jump runs ahead of (independent of) the presence gate below, matching
        // InputController.Update -- it's specifically the near-zero-weight moments the gate would
        // otherwise treat as "not present" that jump needs to see.
        if (sitting && jumpDetector.Update(cal.Total, unixMs, jumpSensitivity: 50, sittingPosture: true, directionClassifier.ReferenceTotal))
        {
            jumpTriggers++;
        }

        if (!isPresent)
        {
            totalSamples++;
            continue; // matches InputController's presence gate -- direction/crouch never run while not present
        }

        var direction = directionClassifier.Update(cal, unixMs, isPresent: true, footstepThresholdRatio: 1.20, dashPeriodMs: 300, stepHoldMs: 70, stepContinuationMs: 800, continuationStepCount: 7, turnSensitivity: 60, footstepTurnMode, instantWeightCalibration: sitting);
        if (Environment.GetEnvironmentVariable("CLASSIFYTEST_TRACE") == "1" && direction != lastTraced)
        {
            Console.WriteLine($"    t={(unixMs - firstUnixMs) / 1000.0:F2}s -> {direction}");
            lastTraced = direction;
        }
        directionCounts[direction] = directionCounts.GetValueOrDefault(direction) + 1;

        // Sitting crouch is recorded on the bottom panels instead of the top ones standing uses --
        // negating Y is exactly that swap (Y is top-minus-bottom), matching InputController.Update.
        if (crouchDetector.Update(sitting ? -y : y, unixMs, crouchSensitivity: 50))
        {
            crouchSamples++;
        }

        if (!sitting && jumpDetector.Update(cal.Total, unixMs, jumpSensitivity: 50, sittingPosture: false, 0))
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

    foreach (var (direction, gaps) in stepGapsByDirection.OrderByDescending(kv => kv.Value.Count))
    {
        gaps.Sort();
        long p95 = gaps[(int)Math.Min(gaps.Count - 1, Math.Ceiling(gaps.Count * 0.95) - 1)];
        Console.WriteLine($"  {direction,-12} step gaps: n={gaps.Count,4} min={gaps[0],4}ms p95={p95,4}ms max={gaps[^1],4}ms");
    }
}

static void SeedReference(DirectionClassifier directionClassifier, long realFirstUnixMs, bool footstepTurnMode)
{
    var flatReading = new CalibratedReading
    {
        TopRight = (int)(ReferenceWeight / 4),
        BottomRight = (int)(ReferenceWeight / 4),
        TopLeft = (int)(ReferenceWeight / 4),
        BottomLeft = (int)(ReferenceWeight / 4),
        Total = (int)ReferenceWeight,
        PctTopRight = 25,
        PctBottomRight = 25,
        PctTopLeft = 25,
        PctBottomLeft = 25,
    };

    for (int i = 5; i >= 1; i--)
    {
        long sampleMs = realFirstUnixMs - i * 5000 - 10000; // 5 samples, 5s apart, well clear of the real data
        directionClassifier.Update(flatReading, sampleMs, isPresent: true, footstepThresholdRatio: 1.20, dashPeriodMs: 300, stepHoldMs: 70, stepContinuationMs: 800, continuationStepCount: 7, turnSensitivity: 60, footstepTurnMode, instantWeightCalibration: false);
    }
}
