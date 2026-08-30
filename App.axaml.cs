using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

namespace Quark;

public class App : Application
{
    public static WindowIcon? AppIcon { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        using var stream = AssetLoader.Open(new Uri("avares://Quark.NET/Assets/icon.png"));
        AppIcon = new WindowIcon(stream);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var win = new MainWindow();
            desktop.MainWindow = win;

            if (Program.StartMinimized)
            {
                win.ShowInTaskbar = false;
                win.Opened += OnWindowOpenedHideImmediately;
            }
            else
            {
                win.Show();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void OnWindowOpenedHideImmediately(object? sender, EventArgs e)
    {
        if (sender is not Window win) return;
        win.Opened -= OnWindowOpenedHideImmediately;
        win.Hide();
    }

    private void OnTrayIconClicked(object? sender, EventArgs e) => ShowMainWindow();

    private void OnTrayShow(object? sender, EventArgs e) => ShowMainWindow();

    private void OnTrayQuit(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            (desktop.MainWindow as MainWindow)?.CleanShutdown();
    }

    public static void ShowMainWindow()
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } win)
        {
            win.ShowInTaskbar = true;
            win.WindowState   = WindowState.Normal;
            win.Show();
            win.Activate();
        }
    }
}
