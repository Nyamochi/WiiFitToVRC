using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WiiFitToVRC.Core.Hid;

public sealed class BalanceBoardSensors
{
    public required ushort TopRight { get; init; }
    public required ushort BottomRight { get; init; }
    public required ushort TopLeft { get; init; }
    public required ushort BottomLeft { get; init; }
}

/// <summary>
/// Talks to the balance board over its Windows HID device path once Bluetooth pairing has
/// enabled the HID service (see BalanceBoardPairing). No raw L2CAP is needed -- Windows exposes
/// it as a standard HID device and reports are just ReadFile/WriteFile once opened.
/// </summary>
public sealed class BalanceBoardDevice : IDisposable
{
    private const ushort NintendoVendorId = 0x057e;
    private const int ReportLength = 22;

    // Reads happen continuously on a dedicated thread while writes (setup + periodic keep-alive)
    // can fire from a threadpool timer; FileStream isn't safe for concurrent read+write from
    // different threads in async mode, so reads and writes get their own handle/stream.
    private readonly FileStream _readStream;
    private readonly SafeFileHandle _readHandle;
    private readonly FileStream _writeStream;
    private readonly SafeFileHandle _writeHandle;
    private readonly Thread _readThread;
    private Timer? _keepAliveTimer;
    private volatile bool _stopping;

    public event Action<BalanceBoardSensors>? SensorsReported;
    public event Action<string>? Disconnected;

    private BalanceBoardDevice(SafeFileHandle readHandle, SafeFileHandle writeHandle)
    {
        _readHandle = readHandle;
        _writeHandle = writeHandle;
        _readStream = new FileStream(readHandle, FileAccess.Read, ReportLength, isAsync: true);
        _writeStream = new FileStream(writeHandle, FileAccess.Write, ReportLength, isAsync: true);
        _readThread = new Thread(ReadLoop) { IsBackground = true };
    }

    public static BalanceBoardDevice? TryOpen(ushort productId = 0x0306)
    {
        foreach (string path in EnumerateHidDevicePaths())
        {
            var handle = OpenHandle(path);
            if (handle.IsInvalid)
            {
                continue;
            }

            var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
            bool isMatch = NativeMethods.HidD_GetAttributes(handle, ref attributes)
                && attributes.VendorID == NintendoVendorId
                && attributes.ProductID == productId;

            if (!isMatch)
            {
                handle.Dispose();
                continue;
            }

            var writeHandle = OpenHandle(path);
            if (writeHandle.IsInvalid)
            {
                handle.Dispose();
                writeHandle.Dispose();
                continue;
            }

            return new BalanceBoardDevice(handle, writeHandle);
        }

        return null;
    }

    private static SafeFileHandle OpenHandle(string path) => NativeMethods.CreateFile(
        path,
        NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
        NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
        IntPtr.Zero,
        NativeMethods.OPEN_EXISTING,
        NativeMethods.FILE_FLAG_OVERLAPPED,
        IntPtr.Zero);

    public void Start()
    {
        // Request status, then switch the extension/data-reporting registers on so continuous
        // 0x32 reports (core buttons + 8 extension bytes = the 4 raw sensor values) start flowing.
        WriteReport(0x15, 0x00);
        WriteMemory(0xA400F0, [0x55]);
        WriteMemory(0xA400FB, [0x00]);
        WriteReport(0x12, 0x04, 0x32);

        // The board auto-disconnects a few minutes into a session if the host never writes
        // anything back, even while it's actively streaming input reports. Re-request status
        // periodically so it keeps seeing host activity. An unhandled exception here (e.g. the
        // device has genuinely gone away) would otherwise crash the whole process, since it runs
        // on a threadpool timer thread with no other handler -- the read loop already detects and
        // reports real disconnects, so just swallow write failures here.
        _keepAliveTimer = new Timer(_ =>
        {
            try
            {
                WriteReport(0x15, 0x00);
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (OperationCanceledException) { }
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        _readThread.Start();
    }

    private void WriteMemory(int address, byte[] data)
    {
        var report = new byte[22];
        report[0] = 0x16;
        report[1] = (byte)((address >> 16) & 0xFF);
        report[2] = (byte)((address >> 8) & 0xFF);
        report[3] = (byte)(address & 0xFF);
        report[4] = (byte)data.Length;
        Array.Copy(data, 0, report, 5, data.Length);
        WriteWithRetry(report);
    }

    private void WriteReport(params byte[] bytes)
    {
        var report = new byte[22];
        Array.Copy(bytes, report, bytes.Length);
        WriteWithRetry(report);
    }

    // Immediately after the handle is opened, the first write or two can spuriously throw
    // OperationCanceledException/IOException even though the device is fine -- retry a few
    // times before giving up.
    private void WriteWithRetry(byte[] report)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                _writeStream.Write(report, 0, report.Length);
                return;
            }
            catch (Exception ex) when (attempt < 3 && (ex is OperationCanceledException or IOException))
            {
                Thread.Sleep(100);
            }
        }
    }

    private void ReadLoop()
    {
        var buffer = new byte[ReportLength];
        string? disconnectReason = null;
        while (!_stopping)
        {
            int read;
            try
            {
                read = _readStream.Read(buffer, 0, buffer.Length);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                disconnectReason = $"{ex.GetType().Name}: {ex.Message}";
                break;
            }

            if (read < 9 || buffer[0] != 0x32)
            {
                continue;
            }

            // buffer[1..2] = core buttons (ignored), buffer[3..10] = 4 sensors x 2 bytes, big-endian.
            var sensors = new BalanceBoardSensors
            {
                TopRight = (ushort)((buffer[3] << 8) | buffer[4]),
                BottomRight = (ushort)((buffer[5] << 8) | buffer[6]),
                TopLeft = (ushort)((buffer[7] << 8) | buffer[8]),
                BottomLeft = (ushort)((buffer[9] << 8) | buffer[10]),
            };
            SensorsReported?.Invoke(sensors);
        }

        if (!_stopping)
        {
            Disconnected?.Invoke(disconnectReason ?? "不明な理由");
        }
    }

    private static IEnumerable<string> EnumerateHidDevicePaths()
    {
        NativeMethods.HidD_GetHidGuid(out Guid hidGuid);
        IntPtr deviceInfoSet = NativeMethods.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
        {
            yield break;
        }

        try
        {
            uint index = 0;
            while (true)
            {
                var interfaceData = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!NativeMethods.SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
                {
                    break;
                }
                index++;

                int requiredSize = 0;
                NativeMethods.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, ref requiredSize, IntPtr.Zero);

                IntPtr detailBuffer = Marshal.AllocHGlobal(requiredSize);
                try
                {
                    // SetupDiGetDeviceInterfaceDetail validates cbSize against a historical packing
                    // quirk unrelated to the struct's real size: 6 on x86, 8 on x64.
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                    if (NativeMethods.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detailBuffer, requiredSize, ref requiredSize, IntPtr.Zero))
                    {
                        string? path = Marshal.PtrToStringAuto(detailBuffer + 4);
                        if (path is not null)
                        {
                            yield return path;
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuffer);
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    public void Dispose()
    {
        _stopping = true;
        _keepAliveTimer?.Dispose();
        _readStream.Dispose();
        _readHandle.Dispose();
        _writeStream.Dispose();
        _writeHandle.Dispose();
    }
}
