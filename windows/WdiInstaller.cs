#if WINDOWS
using System.Runtime.InteropServices;

namespace Quark.Windows;

internal static class WdiInstaller
{
    private const int WDI_SUCCESS           =   0;
    private const int WDI_ERROR_ACCESS      =  -3;
    private const int WDI_ERROR_NEEDS_ADMIN = -15;
    private const int WDI_ERROR_UNSIGNED    = -19;

    private const string ResourceName = "Quark.quark_wdi.dll";

    private static string? _dllPath;
    private static readonly object _lock = new();

    public static bool IsDriverInstalled()
    {
        try
        {
            EnsureExtracted();
            return NativeIsInstalled() != 0;
        }
        catch { return false; }
    }

    public static bool IsDevicePresent()
    {
        try
        {
            EnsureExtracted();
            return NativeIsDevicePresent() != 0;
        }
        catch { return false; }
    }

    public static string? InstallDriver(nint hwnd)
    {
        try
        {
            EnsureExtracted();
            int r = NativeInstall(hwnd);
            return r switch
            {
                WDI_SUCCESS           => null,
                WDI_ERROR_ACCESS      => NeedsElevationSentinel,
                WDI_ERROR_NEEDS_ADMIN => NeedsElevationSentinel,
                WDI_ERROR_UNSIGNED    => NeedsElevationSentinel,
                _                     => $"Driver install failed (libwdi error {r}).",
            };
        }
        catch (DllNotFoundException)
        {
            return "quark-wdi.dll could not be loaded. Please report this as a bug.";
        }
        catch (Exception ex)
        {
            return $"Driver install error: {ex.Message}";
        }
    }

    public const string NeedsElevationSentinel = "__NEEDS_ELEVATION__";

    [DllImport("quark-wdi", EntryPoint = "QuarkInstallDriver",
               CallingConvention = CallingConvention.StdCall)]
    private static extern int NativeInstall(nint hwnd);

    [DllImport("quark-wdi", EntryPoint = "QuarkIsDriverInstalled",
               CallingConvention = CallingConvention.StdCall)]
    private static extern int NativeIsInstalled();

    [DllImport("quark-wdi", EntryPoint = "QuarkIsDevicePresent",
               CallingConvention = CallingConvention.StdCall)]
    private static extern int NativeIsDevicePresent();

    private static void EnsureExtracted()
    {
        lock (_lock)
        {
            if (_dllPath is not null) return;

            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Quark");
            Directory.CreateDirectory(dir);

            using var stream = typeof(WdiInstaller).Assembly
                .GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{ResourceName}' not found. " +
                    "Ensure quark-wdi.dll is listed as EmbeddedResource in the .csproj.");

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            byte[] bytes = ms.ToArray();

            string hash = Convert.ToHexString(
                System.Security.Cryptography.MD5.HashData(bytes))[..8];
            string path = Path.Combine(dir, $"quark-wdi-{hash}.dll");

            if (!File.Exists(path))
                File.WriteAllBytes(path, bytes);

            _dllPath = path;
            NativeResolver.SetWdiDllPath(path);
        }
    }
}
#endif

