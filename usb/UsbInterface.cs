using System.Runtime.InteropServices;

namespace Quark.Usb;

public sealed class UsbInterface : IDisposable
{
    public const int VendorId  = 0x057E;
    public const int ProductId = 0x3000;

    private const int LibUsbErrorAccess = -3;

    private const byte EpOut = 0x01;
    private const byte EpIn  = 0x81;

    public bool IsDevVersion     { get; private set; }
    public QuarkVersion? ProductVersion { get; private set; }
    public string DeviceId       { get; private set; } = "";
    public string? ConsoleId     { get; private set; }
    public string AppName        { get; private set; } = "Device";

    private static int _activeDeviceCount;
    public static int ActiveDeviceCount => _activeDeviceCount;

    private static IntPtr _scanCtx    = IntPtr.Zero;
    private static readonly object _scanLock = new();

    private static IntPtr GetScanContext()
    {
        lock (_scanLock)
        {
            if (_scanCtx == IntPtr.Zero)
            {
                LibUsb.RegisterResolver();
                if (LibUsb.Init(ref _scanCtx) != LibUsb.Success)
                    _scanCtx = IntPtr.Zero;
            }
            return _scanCtx;
        }
    }

    public static void ResetScanContext()
    {
        lock (_scanLock)
        {
            if (_scanCtx != IntPtr.Zero)
            {
                LibUsb.Exit(_scanCtx);
                _scanCtx = IntPtr.Zero;
            }
        }
    }

    private IntPtr _ctx    = IntPtr.Zero;
    private IntPtr _handle = IntPtr.Zero;

    public static bool DevicePresent()
    {
        IntPtr ctx = GetScanContext();
        if (ctx == IntPtr.Zero) return false;
        lock (_scanLock)
        {
            try
            {
                nint count = LibUsb.GetDeviceList(ctx, out IntPtr list);
                if (count <= 0) { LibUsb.FreeDeviceList(list, 1); return false; }
                bool found = false;
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr dev = Marshal.ReadIntPtr(list, i * IntPtr.Size);
                        if (dev == IntPtr.Zero) break;
                        if (LibUsb.GetDeviceDescriptor(dev, out var desc) != LibUsb.Success) continue;
                        if (desc.idVendor == VendorId && desc.idProduct == ProductId)
                        { found = true; break; }
                    }
                }
                finally { LibUsb.FreeDeviceList(list, 1); }
                return found;
            }
            catch { return false; }
        }
    }

    public static List<UsbInterface> TryOpenAll(IReadOnlySet<string>? skipIds = null)
    {
        var result     = new List<UsbInterface>();
        var candidates = new List<(IntPtr dev, string busAddr)>();
        IntPtr ctx     = GetScanContext();
        if (ctx == IntPtr.Zero) { Console.WriteLine("[USB] libusb context init failed\n"); return result; }

        lock (_scanLock)
        {
            try
            {
                nint count = LibUsb.GetDeviceList(ctx, out IntPtr list);
                if (count <= 0) { LibUsb.FreeDeviceList(list, 1); return result; }
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr dev = Marshal.ReadIntPtr(list, i * IntPtr.Size);
                        if (dev == IntPtr.Zero) break;
                        if (LibUsb.GetDeviceDescriptor(dev, out var desc) != LibUsb.Success) continue;
                        if (desc.idVendor != VendorId || desc.idProduct != ProductId) continue;
                        byte bus  = LibUsb.GetBusNumber(dev);
                        byte addr = LibUsb.GetDeviceAddress(dev);
                        string ba = $"{bus}:{addr}";

                        if (skipIds != null && skipIds.Any(k => k == ba || k.StartsWith(ba + ":")))
                            continue;
                        candidates.Add((dev, ba));
                    }
                }
                finally { LibUsb.FreeDeviceList(list, 1); }
            }
            catch (Exception ex) { Console.WriteLine($"[USB] device list enumeration failed: {ex.Message}\n"); return result; }
        }

        foreach (var (_, busAddr) in candidates)
        {
            IntPtr devCtx = IntPtr.Zero;
            IntPtr handle = IntPtr.Zero;
            bool   inited = false;
            try
            {
                LibUsb.RegisterResolver();
                if (LibUsb.Init(ref devCtx) != LibUsb.Success) continue;
                inited = true;

                nint count2 = LibUsb.GetDeviceList(devCtx, out IntPtr list2);
                if (count2 <= 0) { LibUsb.FreeDeviceList(list2, 1); goto cleanup; }

                IntPtr target = IntPtr.Zero;
                for (int i = 0; i < count2; i++)
                {
                    IntPtr d = Marshal.ReadIntPtr(list2, i * IntPtr.Size);
                    if (d == IntPtr.Zero) break;
                    if (LibUsb.GetDeviceDescriptor(d, out var desc2) != LibUsb.Success) continue;
                    if (desc2.idVendor != VendorId || desc2.idProduct != ProductId) continue;
                    byte b = LibUsb.GetBusNumber(d);
                    byte a = LibUsb.GetDeviceAddress(d);
                    if ($"{b}:{a}" == busAddr) { target = d; break; }
                }

                if (target == IntPtr.Zero)
                {
                    LibUsb.FreeDeviceList(list2, 1);
                    goto cleanup;
                }

                int openRc = LibUsb.Open(target, out handle);
                if (openRc == LibUsbErrorAccess)
                {

                    for (int attempt = 0; attempt < 5 && openRc == LibUsbErrorAccess; attempt++)
                    {
                        Thread.Sleep(300);
                        openRc = LibUsb.Open(target, out handle);
                    }
                }
                LibUsb.FreeDeviceList(list2, 1);

                if (openRc != LibUsb.Success)
                {
                    Console.WriteLine($"[USB] found device at {busAddr} but libusb_open failed (rc={openRc})\n");
                    goto cleanup;
                }

                LibUsb.SetAutoDetachKernelDriver(handle, 1);
                int cfgRc = LibUsb.SetConfiguration(handle, 1);
                if (cfgRc != LibUsb.Success)
                    Console.WriteLine($"[USB] set_configuration failed (rc={cfgRc}), continuing anyway\n");
                if (LibUsb.ClaimInterface(handle, 0) != LibUsb.Success)
                {
                    Console.WriteLine($"[USB] claim_interface failed for {busAddr}\n");
                    goto closeHandle;
                }

                var (isDev, version, consoleId, appName) = ReadDescriptors(handle);
                string deviceId = version is not null ? $"{busAddr}:{version}" : busAddr;

                var intf = new UsbInterface
                {
                    _ctx           = devCtx,
                    _handle        = handle,
                    IsDevVersion   = isDev,
                    ProductVersion = version,
                    DeviceId       = deviceId,
                    ConsoleId      = consoleId,
                    AppName        = appName
                };
                Interlocked.Increment(ref _activeDeviceCount);
                result.Add(intf);
                continue;

                closeHandle:
                if (handle != IntPtr.Zero) LibUsb.Close(handle);
                cleanup:
                if (inited) LibUsb.Exit(devCtx);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[USB] TryOpenAll error for {busAddr}: {ex.Message}\n");
                if (handle != IntPtr.Zero) LibUsb.Close(handle);
                if (inited) LibUsb.Exit(devCtx);
            }
        }

        return result;
    }

    public static UsbInterface? TryOpen() => TryOpenAll(null).FirstOrDefault();

    private static (bool isDev, QuarkVersion? version, string? consoleId, string appName) ReadDescriptors(IntPtr handle)
    {
        string product = GetString(handle, 2);
        string serial  = GetString(handle, 3);
        Console.WriteLine($"[USB] product='{product}' serial='{serial}'");

        string appName = product.Contains("Leaflet") ? "Leaflet"
                        : product.Contains("Goldleaf") ? "Goldleaf"
                        : "Device";
        if (appName == "Device") return (false, null, null, appName);

        if (string.IsNullOrEmpty(serial)) return (false, null, null, appName);
        bool dev = serial.EndsWith("-dev");
        if (dev) serial = serial[..^4];
        string? consoleId = null;
        int slash = serial.IndexOf('/');
        if (slash >= 0)
        {
            consoleId = serial[(slash + 1)..];
            serial    = serial[..slash];
        }
        return (dev, QuarkVersion.TryParse(serial), consoleId, appName);
    }

    private static string GetString(IntPtr handle, byte index)
    {
        byte[] buf = new byte[256];
        int len = LibUsb.GetStringDescriptorAscii(handle, index, buf, buf.Length);
        return len > 0 ? System.Text.Encoding.ASCII.GetString(buf, 0, len).TrimEnd('\0') : "";
    }

    public byte[]? ReadBytes(int length)
    {
        byte[] buf = new byte[length];
        int rc = LibUsb.BulkTransfer(_handle, EpIn, buf, length, out int got, 0);
        if (rc != LibUsb.Success || got != length) return null;
        return buf;
    }

    public bool WriteBytes(byte[] data, int length = -1)
    {
        int len = length < 0 ? data.Length : length;

        if (_activeDeviceCount <= 1)
        {
            int rc = LibUsb.BulkTransfer(_handle, EpOut, data, len, out int sent, 0);
            return rc == LibUsb.Success && sent == len;
        }

        const int ChunkSize = 65536;
        int offset = 0;
        while (offset < len)
        {
            int chunkLen = Math.Min(ChunkSize, len - offset);
            int sent2;
            int rc2;
            if (offset == 0 && chunkLen == len)
            {
                rc2 = LibUsb.BulkTransfer(_handle, EpOut, data, chunkLen, out sent2, 0);
            }
            else
            {
                var chunk = System.Buffers.ArrayPool<byte>.Shared.Rent(chunkLen);
                try
                {
                    Buffer.BlockCopy(data, offset, chunk, 0, chunkLen);
                    rc2 = LibUsb.BulkTransfer(_handle, EpOut, chunk, chunkLen, out sent2, 0);
                }
                finally { System.Buffers.ArrayPool<byte>.Shared.Return(chunk); }
            }
            if (rc2 != LibUsb.Success || sent2 != chunkLen) return false;
            offset += sent2;
            Thread.Yield();
        }
        return true;
    }

    public void Close()
    {
        if (_handle != IntPtr.Zero)
        {
            Interlocked.Decrement(ref _activeDeviceCount);
            LibUsb.ReleaseInterface(_handle, 0);
            LibUsb.Close(_handle);
            _handle = IntPtr.Zero;
        }
        if (_ctx != IntPtr.Zero)
        {
            LibUsb.Exit(_ctx);
            _ctx = IntPtr.Zero;
        }
    }

    public void Dispose() => Close();
}

