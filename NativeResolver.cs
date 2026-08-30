using System.Runtime.InteropServices;

namespace Quark;

internal static class NativeResolver
{
    private static bool _registered;
    private static readonly object _lock = new();
    private static string? _wdiDllPath;

    public static void EnsureRegistered()
    {
        lock (_lock)
        {
            if (_registered) return;
            _registered = true;
            NativeLibrary.SetDllImportResolver(typeof(NativeResolver).Assembly, Resolve);
        }
    }

    public static void SetWdiDllPath(string path)
    {
        _wdiDllPath = path;
        EnsureRegistered();
    }

    private static IntPtr Resolve(string libraryName,
        System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName == "quark-wdi")
        {
            if (_wdiDllPath is not null && NativeLibrary.TryLoad(_wdiDllPath, out var h))
                return h;
            return IntPtr.Zero;
        }

        return Quark.Usb.LibUsb.ResolveLibrary(libraryName, assembly, searchPath);
    }
}
