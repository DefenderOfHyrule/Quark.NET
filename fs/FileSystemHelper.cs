namespace Quark.Fs;

public static class FileSystemHelper
{
    private static readonly string HomePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    

    public static List<string> ListDrives()
    {
        var drives = new List<string> { HomePath };

        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    
                    _ = drive.DriveType;
                    if (drive.IsReady)
                        drives.Add(drive.Name[..1]); 
                }
                catch { }
            }
        }
        else
        {
            
            try
            {
                foreach (var line in File.ReadLines("/proc/mounts"))
                {
                    var parts = line.Split(' ');
                    if (parts.Length >= 2 && parts[0].StartsWith("/dev/"))
                        drives.Add(parts[1]);
                }
            }
            catch
            {
                
                foreach (var drive in DriveInfo.GetDrives())
                    try { if (drive.IsReady) drives.Add(drive.Name); } catch { }
            }
        }

        return drives;
    }

    public static string GetDriveLabel(string drive)
    {
        if (drive == HomePath) return "Home directory";

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var di = new DriveInfo(drive + ":\\");
                return string.IsNullOrEmpty(di.VolumeLabel) ? $"Drive ({drive})" : di.VolumeLabel;
            }
            catch { return $"Drive ({drive})"; }
        }
        else
        {
            if (drive == "/") return "Root directory";
            return Path.GetFileName(drive.TrimEnd('/')) is { Length: > 0 } name
                ? name
                : $"Drive ({drive})";
        }
    }

    

    public static List<string> GetFilesIn(string path)
    {
        var list = new List<string>();
        try
        {
            foreach (var f in Directory.EnumerateFiles(path))
                list.Add(Path.GetFileName(f));
        }
        catch { }
        return list;
    }

    public static List<string> GetDirectoriesIn(string path)
    {
        var list = new List<string>();
        try
        {
            foreach (var d in Directory.EnumerateDirectories(path))
                list.Add(Path.GetFileName(d));
        }
        catch { }
        return list;
    }

    

    
    
    
    
    
    public static string NormalizePath(string path)
    {
        string normalized = path.Replace('\\', '/');
        if (OperatingSystem.IsWindows())
        {
            
            
            
            normalized = normalized.Replace("//", ":");
        }
        return normalized;
    }

    
    
    
    
    
    public static string DenormalizePath(string path)
    {
        if (OperatingSystem.IsWindows())
            return path.Replace('/', '\\');

        
        return path.Replace(':', '/');
    }

    

    public static void DeletePath(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        else if (File.Exists(path))
            File.Delete(path);
    }
}
