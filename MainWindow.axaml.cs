using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Quark.Cf;
using Quark.Net;
using Quark.Usb;

namespace Quark;

public partial class MainWindow : Window
{
    private static readonly QuarkVersion CurrentVersion = new(1, 0, 0);

    private static Config _cfg = new();
    private static readonly object CfgLock = new();

    private readonly Settings _settings = Settings.Load();

    private UsbInterface? _activeUsb;
    private readonly object _usbLock = new();

    private readonly Dictionary<string, Task> _usbTasks   = new();
    private readonly HashSet<string>          _usbKnown   = new();
    private readonly object                   _usbSetLock = new();

    private static readonly string[] SpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private DispatcherTimer? _spinnerTimer;
    private int _spinnerFrame;

    private readonly List<string> _pathNames = new();
    private readonly CancellationTokenSource _tcpCts = new();

    private const int MaxLogLines = 500;
    private readonly List<string> _logLines = new();

    private readonly ConnectionState _state = new();

    private UpdateChecker.UpdateInfo? _pendingUpdate;

    public MainWindow()
    {
        InitializeComponent();

        Title = $"Quark v{CurrentVersion} - Leaflet's USB/Network client";
        VersionLabel.Text = $"Quark.NET v{CurrentVersion}";

        CommandFramework.GetSpecialPathCountCb = GetSpecialPathCount;
        CommandFramework.GetSpecialPathCb      = GetSpecialPath;
        CommandFramework.SelectFileCb          = SelectFileBlocking;
        Console.SetOut(new LogWriter(this));
        IPLabel.Text = ResolveLocalIp();

        NewPathButton.Click    += OnAddPath;
        PathRemoveButton.Click += OnRemovePaths;
        ClearLogButton.Click   += (_, _) => { lock (_logLines) _logLines.Clear(); LogArea.Text = ""; };

        ShowLogsToggle.IsCheckedChanged += (_, _) =>
        {
            bool show = ShowLogsToggle.IsChecked == true;
            ShowLogsToggle.Content   = show ? "Hide" : "Show";
            LogArea.IsVisible        = show;
            ClearLogButton.IsVisible = show;
        };

        ReconnectButton.Click   += (_, _) => { };
        NetworkOnlyButton.Click += (_, _) => { };
        ExitButton.Click        += (_, _) => CleanShutdown();
        ReconnectUSBButton.IsVisible = false;

        Closing += (_, e) =>
        {
            if (e.CloseReason == WindowCloseReason.ApplicationShutdown)
                return;

            if (_settings.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            e.Cancel = true;
            CleanShutdown();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppendLog($"[FATAL] {args.ExceptionObject}\n");

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppendLog($"[ERROR] Unobserved: {args.Exception.Message}\n");
            args.SetObserved();
        };

        RefreshPathList();
        HideConnectionOverlay();
        InfoBannerBorder.IsVisible   = false;
        UpdateBannerBorder.IsVisible = false;
        ReconnectUSBButton.IsVisible = false;

#if LINUX
        UdevHintBorder.IsVisible = !Quark.Linux.UdevInstaller.IsInstalled();
        InstallUdevButton.Click += async (_, _) => await OnInstallUdevRuleClickAsync();
        DismissUdevButton.Click += (_, _) => UdevHintBorder.IsVisible = false;
#elif WINDOWS

        LibusbKHintBorder.IsVisible = false;
        LibusbKHintTitle.Text = $"{DriverDisplayName} driver required";
        LibusbKHintBody.Text  = $"The {DriverDisplayName} driver must be installed once before Quark can talk to Leaflet over USB.";
        InstallLibusbKButton.Click += async (_, _) => await OnInstallLibusbKDriverClickAsync();
        ZadigFallbackButton.Click += (_, _) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = "https://zadig.akeo.ie",
                UseShellExecute = true,
            });
#endif

        ThemeManager.Apply(this);
        UpdateThemeIcon();

        CreditsButton.Click      += (_, _) => { CreditsOverlay.IsVisible = true; };
        CreditsCloseButton.Click += (_, _) => { CreditsOverlay.IsVisible = false; };

        MinimizeToTrayToggle.IsChecked = _settings.MinimizeToTray;
        MinimizeToTrayToggle.IsCheckedChanged += (_, _) =>
        {
            _settings.MinimizeToTray = MinimizeToTrayToggle.IsChecked == true;
            _settings.Save();
        };

        LaunchOnBootToggle.IsChecked = _settings.LaunchOnBoot;
        LaunchOnBootToggle.IsCheckedChanged += OnLaunchOnBootToggled;

        StartMinimizedOnBootToggle.IsChecked = _settings.StartMinimizedOnBoot;
        StartMinimizedOnBootToggle.IsCheckedChanged += OnStartMinimizedOnBootToggled;

#if LINUX
        AppLauncherBorder.IsVisible = true;
        AppLauncherToggle.IsChecked = Quark.Linux.DesktopLauncherInstaller.IsInstalled();
        AppLauncherToggle.IsCheckedChanged += OnAppLauncherToggled;
#endif

        UpdateStartMinimizedEnabled();

        SettingsButton.Click      += (_, _) => { SettingsOverlay.IsVisible = true; };
        SettingsCloseButton.Click += (_, _) => { SettingsOverlay.IsVisible = false; };

        UpdateNowButton.Click     += (_, _) => _ = DoUpdateAsync();
        UpdateDismissButton.Click += (_, _) => HideUpdateBanner();
        CreditsUpdateButton.Click += (_, _) =>
        {
            CreditsOverlay.IsVisible = false;
            _ = DoUpdateAsync();
        };

        ThemeToggleButton.Click += (_, _) =>
        {
            ThemeManager.Toggle(this);
            UpdateThemeIcon();
            RefreshPill();
        };

        StatusPillButton.Click += (_, _) =>
        {
            var snap = _state.GetSnapshot();
            if (!snap.IsMulti) return;
            ClientList.ItemsSource = snap.DropdownRows();
            ClientDropdown.IsOpen  = !ClientDropdown.IsOpen;
        };

        CreditsVersionLabel.Text = $"v{CurrentVersion}";

        TcpCommandLoop.OnClientConnected    = (ip, id) => { _state.AddNetwork(ip, id); RefreshPill(); };
        TcpCommandLoop.OnClientDisconnected = (ip, id) => { _state.RemoveNetwork(ip); ClearProgress(id); RefreshPill(); };
        TcpCommandLoop.ListenerFactory      = id =>
        {
            var relay = new ProgressRelay(this, id, id, isNetwork: true);
            return relay;
        };
        CommandFramework.OnConsoleIdAnnounced = (oldId, newId) =>
        {
            _state.RenameNetwork(oldId, newId);
            RenameProgress(oldId, newId);
            RefreshPill();
        };

        Task.Run(UsbLoopGuarded);
        _ = TcpCommandLoop.RunAsync(TcpInterface.DefaultPort, _tcpCts.Token);

        _ = CheckForUpdateAsync();

        AddHandler(KeyDownEvent, OnWindowKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    public void CleanShutdown()
    {
        lock (_usbSetLock)
            foreach (var id in _usbKnown.ToList())
                _state.RemoveUsb(id);
        lock (_usbLock) _activeUsb?.Close();
        _tcpCts.Cancel();
        _settings.Save();

        if (Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void OnLaunchOnBootToggled(object? sender, RoutedEventArgs e)
    {
        _settings.LaunchOnBoot = LaunchOnBootToggle.IsChecked == true;
        _settings.Save();
        ReRegisterAutostart();
        UpdateStartMinimizedEnabled();
    }

    private void OnStartMinimizedOnBootToggled(object? sender, RoutedEventArgs e)
    {
        _settings.StartMinimizedOnBoot = StartMinimizedOnBootToggle.IsChecked == true;
        _settings.Save();
        ReRegisterAutostart();
    }

#if LINUX
    private void OnAppLauncherToggled(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (AppLauncherToggle.IsChecked == true)
            {
                Quark.Linux.DesktopLauncherInstaller.Install();
                AppendLog("[app launcher] Added Quark to the app launcher.\n");
            }
            else
            {
                Quark.Linux.DesktopLauncherInstaller.Uninstall();
                AppendLog("[app launcher] Removed Quark from the app launcher.\n");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[app launcher] {ex.Message}\n");
        }
    }
#endif

    private void ReRegisterAutostart()
    {
        try
        {
            AutostartManager.Set(_settings.LaunchOnBoot, _settings.StartMinimizedOnBoot);
        }
        catch (Exception ex) { AppendLog($"[autostart] {ex.Message}\n"); }
    }

    private void UpdateStartMinimizedEnabled()
    {
        StartMinimizedBorder.Opacity         = _settings.LaunchOnBoot ? 1.0 : 0.4;
        StartMinimizedOnBootToggle.IsEnabled = _settings.LaunchOnBoot;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.U
            && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            SimulateUpdate();
        }
    }

    private async Task CheckForUpdateAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(3));

        try
        {
            var info = await UpdateChecker.CheckAsync(CurrentVersion);
            if (info is null) return;

            _pendingUpdate = info;
            await Dispatcher.UIThread.InvokeAsync(() => ShowUpdateBanner(info));
        }
        catch (Exception ex)
        {
            AppendLog($"[UPDATE] Check failed: {ex.Message}\n");
        }
    }

    public void SimulateUpdate(string fakeVersion = "99.0.0")
    {
        var fakeInfo = new UpdateChecker.UpdateInfo(
            QuarkVersion.TryParse(fakeVersion) ?? new QuarkVersion(99, 0, 0),
            AssetName:   $"Quark-{UpdateChecker.DetectRid()}.zip",
            DownloadUrl: "",
            AssetSize:   0);

        _pendingUpdate = fakeInfo;
        Dispatcher.UIThread.Post(() => ShowUpdateBanner(fakeInfo));
    }

    private void ShowUpdateBanner(UpdateChecker.UpdateInfo info)
    {
        string rid = UpdateChecker.DetectRid();
        UpdateBannerLabel.Text = $"v{info.LatestVersion} available ({rid})";

        CreditsUpdateButton.Content   = $"Update to v{info.LatestVersion}";
        CreditsUpdateButton.IsVisible = true;

        UpdateBannerBorder.IsVisible = true;
    }

    private void HideUpdateBanner()
    {
        UpdateBannerBorder.IsVisible = false;
    }

    private async Task DoUpdateAsync()
    {
        if (_pendingUpdate is null) return;

        if (string.IsNullOrEmpty(_pendingUpdate.DownloadUrl))
        {
            HideUpdateBanner();
            var dlg = new SimulatedUpdateDialog();
            ThemeManager.Apply(dlg);
            await dlg.ShowDialog(this);
            return;
        }

        HideUpdateBanner();
        CreditsOverlay.IsVisible = false;

        ConnectionTitleLabel.Text = "Update in progress";
        ConnectionButtonPanel.IsVisible = false;
        ShowConnectionOverlay($"0%  -  {_pendingUpdate.AssetName}");

        try
        {
            string newExePath = await UpdateChecker.DownloadAndInstallAsync(
                _pendingUpdate,
                pct => Dispatcher.UIThread.Post(() =>
                    ConnectionMessageLabel.Text = $"{pct}%  -  {_pendingUpdate.AssetName}"));

            ConnectionButtonPanel.IsVisible = true;
            HideConnectionOverlay();
            AppendLog($"[UPDATE] Updated to v{_pendingUpdate.LatestVersion}. Restarting…\n");

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(newExePath))
                    {
                        Program.ReleaseSingleInstanceLock();

                        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                        {
                            string bundlePath = newExePath.Contains(".app/Contents/MacOS")
                                ? Path.GetFullPath(Path.Combine(newExePath, "..", "..", ".."))
                                : newExePath;

                            var psi = new System.Diagnostics.ProcessStartInfo("open", $"-n \"{bundlePath}\"")
                            {
                                UseShellExecute       = false,
                                RedirectStandardError = true,
                            };

                            using var openProc = System.Diagnostics.Process.Start(psi);
                            if (openProc is not null)
                            {
                                string stderr = await openProc.StandardError.ReadToEndAsync();
                                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                                try { await openProc.WaitForExitAsync(cts.Token); } catch (OperationCanceledException) { }

                                if (openProc.ExitCode != 0 || !string.IsNullOrWhiteSpace(stderr))
                                    AppendLog($"[UPDATE] 'open' exited {openProc.ExitCode}: {stderr.Trim()}\n");
                            }
                        }
                        else
                        {
                            System.Diagnostics.Process.Start(
                                new System.Diagnostics.ProcessStartInfo(newExePath) { UseShellExecute = true });
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"[UPDATE] Could not relaunch automatically: {ex.Message}\n");
                }
                Close();
            });
        }
        catch (Exception ex)
        {
            ConnectionButtonPanel.IsVisible = true;
            HideConnectionOverlay();
            AppendLog($"[UPDATE] Failed: {ex.Message}\n");
            ShowInfoBanner($"Update failed: {ex.Message}");
        }
    }

    private async Task UsbLoopGuarded()
    {
        try { await UsbScanLoop(); }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] USB scan loop crashed: {ex.Message}\n");
            await Dispatcher.UIThread.InvokeAsync(() =>
                SetStatusMuted("USB loop stopped. Restart the app."));
        }
    }

    private async Task UsbScanLoop()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
            SetStatusMuted("Searching for USB device..."));

        while (true)
        {
            HashSet<string> known;
            lock (_usbSetLock) known = [.._usbKnown];

            List<UsbInterface> found;
            try   { found = UsbInterface.TryOpenAll(known); }
            catch { found = []; }

#if WINDOWS

            try
            {
                bool nothingOpen = found.Count == 0;
                lock (_usbSetLock) nothingOpen &= _usbKnown.Count == 0;
                if (nothingOpen && Quark.Windows.WdiInstaller.IsDevicePresent())
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        LibusbKHintBorder.IsVisible = true);
                }
            }
            catch { }
#endif

            foreach (var usb in found)
            {
                bool isNew;
                lock (_usbSetLock) isNew = _usbKnown.Add(usb.DeviceId);

                if (isNew)
                {
#if WINDOWS
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        LibusbKHintBorder.IsVisible = false);
#endif
                    var captured = usb;
                    var t = Task.Run(() => UsbDeviceTask(captured));
                    lock (_usbSetLock) _usbTasks[usb.DeviceId] = t;
                }
                else
                {
                    usb.Close();
                }
            }

            int delay;
            lock (_usbSetLock) delay = _usbKnown.Count > 0 ? 10000 : 2000;
            await Task.Delay(delay);
        }
    }

    private async Task UsbDeviceTask(UsbInterface usb)
    {
        var version  = usb.ProductVersion;
        var devBuild = usb.IsDevVersion;
        string versionSuffix = version is not null ? $" v{version}" : "";
        string label         = $"{usb.AppName}{versionSuffix}" + (devBuild ? " (dev)" : "");
        string usbConsoleId  = usb.ConsoleId ?? ConsoleId.Generate();
        Console.WriteLine($"[USB] Connected: {label} ({usb.DeviceId}) -> {usbConsoleId}");

        lock (_usbLock) _activeUsb = usb;
        _state.AddUsb(usb.DeviceId, label, usbConsoleId);
        RefreshPill();

        if (devBuild)
            Dispatcher.UIThread.Post(() =>
                ShowInfoBanner($"{usb.AppName} development build: v{version}, may be unstable."));

        var session = new CommandFramework.CommandSession
        {
            Listener = new ProgressRelay(this, usb.DeviceId, usbConsoleId)
        };

        while (true)
        {
            UsbCommandBlock block;
            try { block = new UsbCommandBlock(usb); }
            catch (Exception ex)
            {
                AppendLog($"[ERROR] USB read error ({usb.DeviceId}): {ex.Message}\n");
                break;
            }

            if (!block.IsValid())
            {
                Console.WriteLine($"[USB] Lost connection: {usb.DeviceId}");
                break;
            }

            try { CommandFramework.Dispatch(block, session); }
            catch (Exception ex)
            {
                AppendLog($"[ERROR] USB dispatch ({usb.DeviceId}): {ex.Message}\n");
            }
        }

        session.Dispose();
        lock (_usbLock) { if (_activeUsb == usb) _activeUsb = null; }
        lock (_usbSetLock) { _usbKnown.Remove(usb.DeviceId); _usbTasks.Remove(usb.DeviceId); }
        _state.RemoveUsb(usb.DeviceId);
        RefreshPill();

        Console.WriteLine($"[USB] Disconnected: {usb.DeviceId}");
        usb.Close();
    }

#if WINDOWS
    private static string DriverDisplayName =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "WinUSB" : "libusbK";

    private async Task OnInstallLibusbKDriverClickAsync()
    {
        InstallLibusbKButton.IsEnabled = false;
        InstallLibusbKButton.Content   = "Installing…";
        LibusbKInstallProgress.IsVisible = true;
        AppendLog($"Installing {DriverDisplayName} driver… (this can take up to a minute, please wait)\n");

        nint hwnd = TryGetHwnd();

        string? err = await Task.Run(() => Quark.Windows.WdiInstaller.InstallDriver(hwnd));

        if (err == Quark.Windows.WdiInstaller.NeedsElevationSentinel)
        {
            AppendLog("Elevation required, relaunching as administrator…\n");

            var proc = TryRelaunchElevated("--install-driver");
            if (proc is null)
            {
                AppendLog("Driver installation cancelled.\n");
                InstallLibusbKButton.Content     = "⬇ Install Driver";
                InstallLibusbKButton.IsEnabled   = true;
                LibusbKInstallProgress.IsVisible = false;
                return;
            }

            int exitCode = await Task.Run(async () =>
            {
                await proc.WaitForExitAsync();
                return proc.ExitCode;
            });
            proc.Dispose();

            LibusbKInstallProgress.IsVisible = false;

            if (exitCode == 0)
            {
                AppendLog($"✓ {DriverDisplayName} driver installed.\n");
                LibusbKHintBorder.IsVisible = false;
                Quark.Usb.UsbInterface.ResetScanContext();
                InstallLibusbKButton.Content   = "⬇ Install Driver";
                InstallLibusbKButton.IsEnabled = true;
            }
            else
            {
                AppendLog($"[ERROR] Driver install failed (elevated process exited with code {exitCode}).\n");
                InstallLibusbKButton.Content   = "⬇ Install Driver";
                InstallLibusbKButton.IsEnabled = true;
            }
            return;
        }

        LibusbKInstallProgress.IsVisible = false;

        if (err is null)
        {
            AppendLog($"✓ {DriverDisplayName} driver installed.\n");
            LibusbKHintBorder.IsVisible = false;
            Quark.Usb.UsbInterface.ResetScanContext();
            InstallLibusbKButton.Content   = "⬇ Install Driver";
            InstallLibusbKButton.IsEnabled = true;
        }
        else
        {
            AppendLog($"[ERROR] {err}\n");
            InstallLibusbKButton.Content   = "⬇ Install Driver";
            InstallLibusbKButton.IsEnabled = true;
        }
    }

    private nint TryGetHwnd()
    {
        try
        {
            if (TryGetPlatformHandle() is { } h)
                return h.Handle;
        }
        catch { }
        return IntPtr.Zero;
    }

    private static System.Diagnostics.Process? TryRelaunchElevated(string args)
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return null;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe, args)
            {
                UseShellExecute = true,
                Verb            = "runas",
            };
            return System.Diagnostics.Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
#endif

#if LINUX
    private async Task OnInstallUdevRuleClickAsync()
    {
        InstallUdevButton.IsEnabled = false;
        InstallUdevButton.Content   = "Installing…";
        AppendLog("Setting up udev rule…\n");

        string scriptPath = Quark.Linux.UdevInstaller.GetScriptPath();
        string? error = null;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            string? password = await SudoPasswordDialog.AskAsync(this, error);
            if (password is null)
            {
                AppendLog("Udev setup cancelled.\n");
                InstallUdevButton.Content   = "⬇ Setup udev";
                InstallUdevButton.IsEnabled = true;
                return;
            }

            try
            {
                (bool success, _) = await Quark.Linux.UdevInstaller.RunElevatedAsync(
                    scriptPath, password, line => AppendLog(line + "\n"));

                if (success)
                {
                    AppendLog("✓ udev rule installed. Unplug and replug your Leaflet device.\n");
                    UdevHintBorder.IsVisible = false;
                    return;
                }

                error = "Authentication failed. Try again.";
            }
            catch (Exception ex)
            {
                AppendLog($"[ERROR] {ex.Message}\n");
                AppendLog("Falling back to opening a terminal instead…\n");
                await FallBackToTerminalAsync(scriptPath);
                return;
            }
        }

        AppendLog("[ERROR] Too many failed attempts.\n");
        await FallBackToTerminalAsync(scriptPath);
    }

    private async Task FallBackToTerminalAsync(string scriptPath)
    {
        (bool success, string log) = await Task.Run(() => Quark.Linux.UdevInstaller.OpenTerminal(scriptPath));
        InstallUdevButton.Content   = "⬇ Setup udev";
        InstallUdevButton.IsEnabled = true;

        if (success)
        {
            AppendLog("A terminal window has opened.\n");
            AppendLog("Enter your sudo password when prompted.\n");
        }
        else
        {
            AppendLog("[ERROR] Could not open a terminal either. Run the script manually:\n");
            AppendLog($"  bash \"{scriptPath}\"\n");
        }
    }
#endif

    private void UpdateThemeIcon()
    {
        string iconPath = ThemeManager.IsDark
            ? "avares://Quark.NET/Assets/sun.png"
            : "avares://Quark.NET/Assets/moon.png";

        ThemeIcon.Source = new Bitmap(
            AssetLoader.Open(new Uri(iconPath)));
    }

    private void RefreshPill()
    {
        var snap = _state.GetSnapshot();
        Dispatcher.UIThread.Post(() =>
        {
            USBStatusLabel.Text       = snap.PillText();
            USBStatusLabel.Foreground = new Avalonia.Media.SolidColorBrush(
                Avalonia.Media.Color.Parse(snap.PillColor()));
            PillChevron.IsVisible     = snap.IsMulti;
            if (!snap.IsMulti) ClientDropdown.IsOpen = false;
        });
    }

    private void SetStatusMuted(string text)
    {
        USBStatusLabel.Text       = text;
        USBStatusLabel.Foreground = new Avalonia.Media.SolidColorBrush(
            Avalonia.Media.Color.Parse("#607D8B"));
    }

    private void ShowConnectionOverlay(string message)
    {
        ConnectionMessageLabel.Text = message;
        ConnectionOverlay.IsVisible = true;

        if (_spinnerTimer is null)
        {
            _spinnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _spinnerTimer.Tick += (_, _) =>
            {
                _spinnerFrame = (_spinnerFrame + 1) % SpinnerFrames.Length;
                ConnectionSpinner.Text = SpinnerFrames[_spinnerFrame];
            };
        }
        _spinnerTimer.Start();
    }

    private void HideConnectionOverlay()
    {
        _spinnerTimer?.Stop();
        ConnectionOverlay.IsVisible = false;
    }

    private void ShowInfoBanner(string message)
    {
        InfoBanner.Text            = message;
        InfoBannerBorder.IsVisible = true;
        InfoBannerBorder.Opacity   = 1.0;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            for (double o = 1.0; o >= 0; o -= 0.05)
            {
                InfoBannerBorder.Opacity = o;
                await Task.Delay(50);
            }
            InfoBannerBorder.IsVisible = false;
        };
        timer.Start();
    }

    private readonly Dictionary<string, TransferEntry> _transfers = new();
    private readonly object _transferLock = new();

    internal void UpdateProgress(string deviceId, string consoleId, bool isNetwork, string fileName, long transferred, long total)
    {
        double pct = total > 0 ? (double)transferred / total : 0.0;
        lock (_transferLock)
        {
            if (!_transfers.TryGetValue(deviceId, out var entry))
            {
                entry = new TransferEntry(deviceId, consoleId, isNetwork);
                _transfers[deviceId] = entry;
            }
            entry.Label    = fileName;
            entry.Progress = pct;
            entry.PctLabel = $"{pct * 100.0:F1}%  {(isNetwork ? "Network:" : "USB:")}  {consoleId}";
        }
        RefreshTransferList();
    }

    internal void ClearProgress(string deviceId)
    {
        lock (_transferLock) _transfers.Remove(deviceId);
        RefreshTransferList();
    }

    internal void RenameProgress(string oldId, string newId)
    {
        lock (_transferLock)
        {
            if (_transfers.TryGetValue(oldId, out var entry))
            {
                _transfers.Remove(oldId);
                var renamed = new TransferEntry(newId, newId, entry.IsNetwork)
                {
                    Label    = entry.Label,
                    Progress = entry.Progress,
                    PctLabel = entry.PctLabel.Replace(oldId, newId)
                };
                _transfers[newId] = renamed;
            }
        }
        RefreshTransferList();
    }

    private void RefreshTransferList()
    {
        List<TransferEntry> snapshot;
        lock (_transferLock) snapshot = [.._transfers.Values];

        Dispatcher.UIThread.Post(() =>
        {
            bool hasAny = snapshot.Count > 0;
            TransferIdleLabel.IsVisible = !hasAny;
            TransferList.IsVisible      = hasAny;
            if (hasAny)
            {
                foreach (var e in snapshot)
                    e.EnsureBrush();
                TransferList.ItemsSource = snapshot.ToList();
            }
        });
    }

    internal void AppendLog(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            lock (_logLines)
            {
                foreach (char c in text)
                {
                    if (_logLines.Count == 0 || _logLines[^1].EndsWith('\n'))
                        _logLines.Add(string.Empty);
                    _logLines[^1] += c;
                }
                while (_logLines.Count > MaxLogLines)
                    _logLines.RemoveAt(0);
                LogArea.Text = string.Concat(_logLines);
            }
            LogArea.CaretIndex = LogArea.Text?.Length ?? 0;
        });
    }

    private void RefreshPathList()
    {
        _pathNames.Clear();
        PathList.Items.Clear();
        lock (CfgLock)
            _cfg.ForEach((name, path) =>
            {
                _pathNames.Add(name);
                PathList.Items.Add($"{name} - {path}");
            });
    }

    private async void OnAddPath(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title         = "Select folder to add as content path",
                AllowMultiple = false
            });
            if (folders.Count == 0) return;
            string dir = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;
            if (string.IsNullOrEmpty(dir)) return;

            var dlg = new NameInputDialog(
                $"Name for \"{Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar))}\"");
            string? name = await dlg.ShowDialog<string?>(this);
            if (string.IsNullOrWhiteSpace(name)) return;
            lock (CfgLock) _cfg.Add(name.Trim(), dir);
            RefreshPathList();
        }
        catch (Exception ex) { AppendLog($"[ERROR] Failed to add path: {ex.Message}\n"); }
    }

    private void OnRemovePaths(object? sender, RoutedEventArgs e)
    {
        try
        {
            var indices = PathList.Selection.SelectedIndexes
                .Where(i => i >= 0 && i < _pathNames.Count).ToList();
            var names = indices.Select(i => _pathNames[i]).ToList();
            lock (CfgLock) _cfg.RemoveRange(names);
            RefreshPathList();
        }
        catch (Exception ex) { AppendLog($"[ERROR] Failed to remove paths: {ex.Message}\n"); }
    }

    private static int GetSpecialPathCount()
    {
        lock (CfgLock) return _cfg.Count;
    }

    private static string[]? GetSpecialPath(int idx)
    {
        lock (CfgLock)
        {
            var entry = _cfg.Get(idx);
            return entry is null ? null : [entry.Name, entry.Path];
        }
    }

    private string? SelectFileBlocking()
    {
        string? result = null;
        using var gate = new SemaphoreSlim(0, 1);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title         = "Select file to send to Switch",
                    AllowMultiple = false
                });
                result = files.Count > 0
                    ? (files[0].TryGetLocalPath() ?? files[0].Path.LocalPath)
                    : null;
            }
            catch { result = null; }
            finally { gate.Release(); }
        });
        gate.Wait();
        return result;
    }

    private static string ResolveLocalIp()
    {
        string? ethernet = null, wireless = null, fallback = null;
        try
        {
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != OperationalStatus.Up ||
                    iface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var addr in iface.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string ip   = addr.Address.ToString();
                    string name = iface.Name.ToLowerInvariant();
                    if (name.StartsWith("eth") || name.StartsWith("en") ||
                        name.StartsWith("eno") || name.StartsWith("enp"))
                        ethernet ??= ip;
                    else if (name.StartsWith("wlan") || name.StartsWith("wlp") ||
                             name.StartsWith("wlo") || name.StartsWith("wi"))
                        wireless ??= ip;
                    else
                        fallback ??= ip;
                }
            }
        }
        catch { }

        string? resolved = ethernet ?? wireless ?? fallback;
        return resolved is not null ? $"Network IP: {resolved}" : "Network IP: unknown";
    }

    private sealed class LogWriter(MainWindow win) : System.IO.TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void Write(char value)        => win.AppendLog(value.ToString());
        public override void Write(string? value)     { if (value != null) win.AppendLog(value); }
        public override void WriteLine(string? value) => win.AppendLog((value ?? "") + "\n");
    }

    private sealed class ProgressRelay(MainWindow win, string deviceId, string consoleId, bool isNetwork = false) : CommandFramework.IProgressListenerWithId
    {
        private string _deviceId   = deviceId;
        private string _consoleId  = consoleId;

        public void UpdateId(string newId)
        {
            _deviceId  = newId;
            _consoleId = newId;
        }

        public void OnProgress(string fileName, long transferred, long total) =>
            win.UpdateProgress(_deviceId, _consoleId, isNetwork, fileName, transferred, total);
        public void OnIdle() => win.ClearProgress(_deviceId);
    }

    private sealed class TransferEntry(string deviceId, string consoleId, bool isNetwork)
    {
        public string DeviceId  { get; } = deviceId;
        public string ConsoleId { get; } = consoleId;
        public bool   IsNetwork { get; } = isNetwork;
        public string Label     { get; set; } = "";
        public double Progress  { get; set; } = 0.0;
        public string PctLabel  { get; set; } = "";
        public string BarColor  { get; } = isNetwork ? "#42A5F5" : "#4CAF50";

        public Avalonia.Media.SolidColorBrush? BarBrush { get; private set; }

        public void EnsureBrush() =>
            BarBrush ??= new Avalonia.Media.SolidColorBrush(
                Avalonia.Media.Color.Parse(BarColor));
    }
}

