using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quark;

public sealed class Settings
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "quark", "settings.json");

    public bool MinimizeToTray       { get; set; } = false;
    public bool LaunchOnBoot         { get; set; } = false;
    public bool StartMinimizedOnBoot { get; set; } = true;

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.Settings)
                       ?? new Settings();
            }
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, SettingsJsonContext.Default.Settings));
        }
        catch { }
    }
}

[JsonSerializable(typeof(Settings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class SettingsJsonContext : JsonSerializerContext { }
