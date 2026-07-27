using WiiFitToVRC.Core.Bluetooth;
using WiiFitToVRC.Core.Hid;

BalanceBoardDevice? device;
if (args.Length > 0 && args[0] == "--reopen")
{
    Console.WriteLine("既存のペアリングのままHIDデバイスへの再接続を試みます...");
    device = null;
    for (int i = 0; i < 10 && device is null; i++)
    {
        device = BalanceBoardDevice.TryOpen();
        if (device is null)
        {
            Thread.Sleep(500);
        }
    }
    if (device is null)
    {
        Console.WriteLine("HIDデバイスが見つかりませんでした。");
        return;
    }
}
else
{
    Console.WriteLine("バランスボードのSYNCボタンを押してから Enter キーを押してください...");
    Console.ReadLine();

    device = PairAndConnect();
    if (device is null)
    {
        return;
    }
}

int logArgIndex = Array.IndexOf(args, "--log");
string? logPath = logArgIndex >= 0 ? args[logArgIndex + 1] : null;

if (logPath is null)
{
    Console.WriteLine("HID接続成功。15秒間センサー値を表示します。");
    device.SensorsReported += s =>
        Console.WriteLine($"TR={s.TopRight,5} BR={s.BottomRight,5} TL={s.TopLeft,5} BL={s.BottomLeft,5}");
    device.Start();
    Thread.Sleep(15000);
    device.Dispose();
    return;
}

Console.WriteLine($"HID接続成功。CSVへの記録を開始します: {logPath}。終了するまでプロセスを実行し続けます。");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!);
using var writer = new StreamWriter(logPath, append: false) { AutoFlush = true };
writer.WriteLine("unix_ms,top_right,bottom_right,top_left,bottom_left");
device.SensorsReported += s =>
{
    long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    writer.WriteLine($"{ms},{s.TopRight},{s.BottomRight},{s.TopLeft},{s.BottomLeft}");
};
device.Start();

// Runs until externally terminated (the process is expected to be killed once logging is done);
// AutoFlush above means every row is safely on disk even if that happens mid-run.
Thread.Sleep(Timeout.Infinite);

static BalanceBoardDevice? PairAndConnect()
{
    Console.WriteLine("検索・ペアリング中(数十秒かかることがあります)...");

    PairingOutcome? outcome = null;
    Exception? error = null;

    var thread = new Thread(() =>
    {
        try
        {
            outcome = BalanceBoardPairing.PairAndInstall();
        }
        catch (Exception ex)
        {
            error = ex;
        }
    });
    thread.Start();

    // The native Bluetooth authentication callback appears to be delivered via a message-only
    // window, so a plain console app without a message pump never receives it. Pump messages
    // on this (STA) thread while the pairing work runs on the background thread above.
    while (thread.IsAlive)
    {
        System.Windows.Forms.Application.DoEvents();
        Thread.Sleep(20);
    }

    if (error is not null)
    {
        Console.WriteLine($"例外: {error}");
        return null;
    }

    if (outcome is null || outcome.Result != PairingResult.Success)
    {
        Console.WriteLine($"結果: {outcome?.Result} {outcome?.Message}");
        return null;
    }

    Console.WriteLine("引き続きHID接続を試みます...");
    for (int i = 0; i < 30; i++)
    {
        var device = BalanceBoardDevice.TryOpen();
        if (device is not null)
        {
            return device;
        }
        Thread.Sleep(500);
    }

    Console.WriteLine("HIDデバイスが見つかりませんでした(接続がすでに切れた可能性)。");
    return null;
}
