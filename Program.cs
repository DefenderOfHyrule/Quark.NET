using Avalonia;

namespace Quark;

internal static class Program
{
    public static bool StartMinimized { get; private set; }

    private static FileStream? _lockFile;

    [STAThread]
    public static void Main(string[] args)
    {
#if WINDOWS
        if (args.Contains("--install-driver"))
        {
            RunHeadlessDriverInstall();
            return;
        }
#endif

        if (!AcquireSingleInstanceLock())
            return;

        StartMinimized = args.Contains("--minimized");

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

#if WINDOWS
    private static void RunHeadlessDriverInstall()
    {
        string? err = Quark.Windows.WdiInstaller.InstallDriver(IntPtr.Zero);
        if (err is null)
        {
            Environment.Exit(0);
        }
        else
        {
            try
            {
                string log = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Quark", "driver_install_error.txt");
                File.WriteAllText(log, err);
            }
            catch { }
            Environment.Exit(1);
        }
    }
#endif

    private static bool AcquireSingleInstanceLock()
    {
        string dir  = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "quark");
        string path = Path.Combine(dir, "instance.lock");

        try
        {
            Directory.CreateDirectory(dir);
            _lockFile = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static void ReleaseSingleInstanceLock()
    {
        _lockFile?.Dispose();
        _lockFile = null;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
