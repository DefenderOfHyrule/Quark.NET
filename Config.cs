using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quark;

public sealed class Config
{
    public sealed class Entry
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public Entry() { }
        public Entry(string name, string path) { Name = name; Path = path; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    
    public static string ConfigFilePath { get; } = ResolveConfigPath();

    private static string ResolveConfigPath()
    {
        string configHome;
        if (OperatingSystem.IsWindows())
        {
            configHome = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }
        else
        {
            configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                         ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }
        string dir = Path.Combine(configHome, "quark");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "quark-config.json");
    }

    
    private readonly List<Entry> _entries = new();
    private readonly object _lock = new();

    public Config() => Reload();

    private void Reload()
    {
        _entries.Clear();
        if (!File.Exists(ConfigFilePath)) return;
        try
        {
            string json = File.ReadAllText(ConfigFilePath);
            var loaded = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.ListEntry) as List<Entry>;
            if (loaded != null) _entries.AddRange(loaded);
        }
        catch {  }
    }

    private void Save()
    {
        string json = JsonSerializer.Serialize(_entries, ConfigJsonContext.Default.ListEntry);
        File.WriteAllText(ConfigFilePath, json);
    }

    

    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    
    public Entry? Get(int idx)
    {
        lock (_lock)
            return (idx >= 0 && idx < _entries.Count) ? _entries[idx] : null;
    }

    
    public void ForEach(Action<string, string> action)
    {
        lock (_lock)
            foreach (var e in _entries) action(e.Name, e.Path);
    }

    public void Add(string name, string path)
    {
        lock (_lock)
        {
            int idx = _entries.FindIndex(e => e.Name == name);
            if (idx >= 0) _entries[idx] = new Entry(name, path);
            else _entries.Add(new Entry(name, path));
            Save();
        }
    }

    public void RemoveRange(IEnumerable<string> names)
    {
        var set = new HashSet<string>(names);
        lock (_lock)
        {
            _entries.RemoveAll(e => set.Contains(e.Name));
            Save();
        }
    }
}

[JsonSerializable(typeof(List<Config.Entry>))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class ConfigJsonContext : JsonSerializerContext { }
