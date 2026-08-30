#if LINUX
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Quark.Linux;

internal static class UdevInstaller
{
    private const string RulesFile = "/etc/udev/rules.d/51-quark.rules";
    private const string VendorId  = "057e";
    private const string ProductId = "3000";

    public static bool IsInstalled()
    {
        try
        {
            return File.Exists(RulesFile)
                && File.ReadAllText(RulesFile).Contains(VendorId)
                && File.ReadAllText(RulesFile).Contains(ProductId);
        }
        catch { return false; }
    }

    public static string GetScriptPath()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Quark");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "setup_udev.sh");

        var sb = new StringBuilder();
        sb.AppendLine("#!/bin/bash");
        sb.AppendLine("echo 'Quark udev setup - enter your password when prompted'");
        sb.AppendLine("echo ''");
        sb.AppendLine("mkdir -p /etc/udev/rules.d");
        sb.AppendLine($"echo 'SUBSYSTEM==\"usb\", ATTR{{idVendor}}==\"{VendorId}\", ATTR{{idProduct}}==\"{ProductId}\", MODE=\"0666\"' | tee {RulesFile}");
        sb.AppendLine("chmod 644 " + RulesFile);
        sb.AppendLine("udevadm control --reload-rules");
        sb.AppendLine("udevadm trigger");
        sb.AppendLine("echo ''");
        sb.AppendLine("echo 'Done! Unplug and replug your Leaflet device to pick up the new rule.'");
        sb.AppendLine("echo 'This window will close in 5 seconds...'");
        sb.AppendLine("sleep 5");

        File.WriteAllText(path, sb.ToString());
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        return path;
    }

    public static async Task<(bool Success, string Output)> RunElevatedAsync(
        string scriptPath, string password, Action<string>? onOutput = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("sudo")
        {
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };
        psi.ArgumentList.Add("-S");
        psi.ArgumentList.Add("-k");
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add("");
        psi.ArgumentList.Add("bash");
        psi.ArgumentList.Add(scriptPath);

        using var proc = Process.Start(psi);
        if (proc is null) return (false, "Could not start sudo.");

        var output = new StringBuilder();
        void Capture(object? _, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            output.AppendLine(e.Data);
            onOutput?.Invoke(e.Data);
        }
        proc.OutputDataReceived += Capture;
        proc.ErrorDataReceived  += Capture;
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.StandardInput.WriteLineAsync(password);
        await proc.StandardInput.FlushAsync(ct);
        proc.StandardInput.Close();

        await proc.WaitForExitAsync(ct);
        return (proc.ExitCode == 0, output.ToString());
    }

    public static (bool Success, string Log) OpenTerminal(string scriptPath)
    {
        var log = new StringBuilder();

        (string exe, string[] args)[] terminals =
        [

            ("konsole",            ["-e", "sudo", "bash", scriptPath]),

            ("gnome-terminal",     ["--", "sudo", "bash", scriptPath]),

            ("xfce4-terminal",     ["-e", $"sudo bash {scriptPath}"]),

            ("x-terminal-emulator",["-e", $"sudo bash {scriptPath}"]),

            ("lxterminal",         ["-e", $"sudo bash {scriptPath}"]),

            ("mate-terminal",      ["-e", $"sudo bash {scriptPath}"]),

            ("tilix",              ["-e", "sudo", "bash", scriptPath]),

            ("xterm",              ["-e", "sudo", "bash", scriptPath]),

            ("kitty",              ["sudo", "bash", scriptPath]),
            ("alacritty",         ["-e", "sudo", "bash", scriptPath]),
            ("wezterm",            ["start", "--", "sudo", "bash", scriptPath]),
            ("foot",               ["sudo", "bash", scriptPath]),
        ];

        foreach (var (exe, args) in terminals)
        {
            if (Which(exe) is null) continue;

            try
            {
                var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
                foreach (var a in args) psi.ArgumentList.Add(a);

                var proc = Process.Start(psi);
                if (proc is not null)
                {
                    log.AppendLine($"{exe} started (PID {proc.Id})");
                    return (true, log.ToString().Trim());
                }
            }
            catch (Exception ex)
            {
                log.AppendLine($"{exe} failed: {ex.Message}");
            }
        }

        log.AppendLine("No terminal emulator found.");
        log.AppendLine("Tried: " + string.Join(", ", terminals.Select(t => t.exe)));
        return (false, log.ToString().Trim());
    }

    private static string? Which(string program)
    {
        try
        {
            var psi = new ProcessStartInfo("which", program)
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            string output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return p.ExitCode == 0 && !string.IsNullOrEmpty(output) ? output : null;
        }
        catch { return null; }
    }
}
#endif
