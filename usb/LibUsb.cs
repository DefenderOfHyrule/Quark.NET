using System.Runtime.InteropServices;

namespace Quark.Usb;

internal static class LibUsb
{
    private const string Lib = "libusb-quark";

    [DllImport(Lib, EntryPoint = "libusb_init")]
    public static extern int Init(ref IntPtr ctx);

    [DllImport(Lib, EntryPoint = "libusb_exit")]
    public static extern void Exit(IntPtr ctx);

    [DllImport(Lib, EntryPoint = "libusb_get_device_list")]
    public static extern nint GetDeviceList(IntPtr ctx, out IntPtr list);

    [DllImport(Lib, EntryPoint = "libusb_free_device_list")]
    public static extern void FreeDeviceList(IntPtr list, int unref_devices);

    [DllImport(Lib, EntryPoint = "libusb_get_bus_number")]
    public static extern byte GetBusNumber(IntPtr dev);

    [DllImport(Lib, EntryPoint = "libusb_get_device_address")]
    public static extern byte GetDeviceAddress(IntPtr dev);

    [DllImport(Lib, EntryPoint = "libusb_get_device_descriptor")]
    public static extern int GetDeviceDescriptor(IntPtr dev, out DeviceDescriptor desc);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DeviceDescriptor
    {
        public byte   bLength;
        public byte   bDescriptorType;
        public ushort bcdUSB;
        public byte   bDeviceClass;
        public byte   bDeviceSubClass;
        public byte   bDeviceProtocol;
        public byte   bMaxPacketSize0;
        public ushort idVendor;
        public ushort idProduct;
        public ushort bcdDevice;
        public byte   iManufacturer;
        public byte   iProduct;
        public byte   iSerialNumber;
        public byte   bNumConfigurations;
    }

    [DllImport(Lib, EntryPoint = "libusb_open")]
    public static extern int Open(IntPtr dev, out IntPtr handle);

    [DllImport(Lib, EntryPoint = "libusb_close")]
    public static extern void Close(IntPtr handle);

    [DllImport(Lib, EntryPoint = "libusb_get_string_descriptor_ascii")]
    public static extern int GetStringDescriptorAscii(IntPtr handle, byte index, byte[] data, int length);

    [DllImport(Lib, EntryPoint = "libusb_set_auto_detach_kernel_driver")]
    public static extern int SetAutoDetachKernelDriver(IntPtr handle, int enable);

    [DllImport(Lib, EntryPoint = "libusb_set_configuration")]
    public static extern int SetConfiguration(IntPtr handle, int configuration);

    [DllImport(Lib, EntryPoint = "libusb_claim_interface")]
    public static extern int ClaimInterface(IntPtr handle, int interface_number);

    [DllImport(Lib, EntryPoint = "libusb_release_interface")]
    public static extern int ReleaseInterface(IntPtr handle, int interface_number);

    [DllImport(Lib, EntryPoint = "libusb_bulk_transfer")]
    public static extern int BulkTransfer(IntPtr handle, byte endpoint, byte[] data,
        int length, out int transferred, uint timeout);

    [DllImport(Lib, EntryPoint = "libusb_alloc_transfer")]
    public static extern IntPtr AllocTransfer(int iso_packets);

    [DllImport(Lib, EntryPoint = "libusb_free_transfer")]
    public static extern void FreeTransfer(IntPtr transfer);

    [DllImport(Lib, EntryPoint = "libusb_submit_transfer")]
    public static extern int SubmitTransfer(IntPtr transfer);

    [DllImport(Lib, EntryPoint = "libusb_cancel_transfer")]
    public static extern int CancelTransfer(IntPtr transfer);

    [DllImport(Lib, EntryPoint = "libusb_handle_events")]
    public static extern int HandleEvents(IntPtr ctx);

    [DllImport(Lib, EntryPoint = "libusb_handle_events_completed")]
    public static extern int HandleEventsCompleted(IntPtr ctx, ref int completed);

    [StructLayout(LayoutKind.Sequential)]
    public struct Timeval { public long tv_sec; public long tv_usec; }

    [DllImport(Lib, EntryPoint = "libusb_handle_events_timeout_completed")]
    public static extern int HandleEventsTimeoutCompleted(IntPtr ctx, ref Timeval tv, ref int completed);

    public const byte TRANSFER_TYPE_BULK = 2;
    public const int  TRANSFER_STATUS_COMPLETED = 0;
    public const int  TRANSFER_STATUS_CANCELLED = 4;

    public const int Success = 0;

    public static void RegisterResolver() => NativeResolver.EnsureRegistered();

    internal static IntPtr ResolveLibrary(string libraryName,
        System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != Lib) return IntPtr.Zero;

        if (OperatingSystem.IsWindows())
        {
            string[] candidates =
            [
                Path.Combine(AppContext.BaseDirectory, "libusb-quark.dll"),
                "libusb-1.0.dll",
                "libusb-1.0",
            ];
            foreach (var c in candidates)
                if (NativeLibrary.TryLoad(c, assembly, searchPath, out var h)) return h;
        }
        else if (OperatingSystem.IsMacOS())
        {
            string[] candidates =
            [
                Path.Combine(AppContext.BaseDirectory, "libusb-quark.dylib"),
                "/opt/homebrew/lib/libusb-1.0.dylib",
                "/usr/local/lib/libusb-1.0.dylib",
                "/opt/local/lib/libusb-1.0.dylib",
                "libusb-1.0.dylib",
                "libusb-1.0",
            ];
            foreach (var c in candidates)
                if (NativeLibrary.TryLoad(c, assembly, searchPath, out var h)) return h;
        }
        else if (OperatingSystem.IsLinux())
        {
            string[] fallbacks =
            [
                "libusb-1.0.so.0",
                "libusb-1.0.so",
                "libusb-1.0",
            ];
            foreach (var c in fallbacks)
                if (NativeLibrary.TryLoad(c, assembly, searchPath, out var h)) return h;
        }

        return IntPtr.Zero;
    }
}
