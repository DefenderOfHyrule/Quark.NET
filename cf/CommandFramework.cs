using Quark.Cf;
using Quark.Fs;

namespace Quark.Cf;

public static class CommandFramework
{
    public const int ResultExceptionCaught    = 0xBAF1;
    public const int ResultInvalidIndex       = 0xBAF2;
    public const int ResultInvalidFileMode    = 0xBAF3;
    public const int ResultSelectionCancelled = 0xBAF4;

    public const int PathTypeInvalid   = 0;
    public const int PathTypeFile      = 1;
    public const int PathTypeDirectory = 2;

    public const int FileModeRead   = 1;
    public const int FileModeWrite  = 2;
    public const int FileModeAppend = 3;

    public interface IProgressListener
    {
        void OnProgress(string fileName, long transferred, long total);
        void OnIdle();
    }

    
    
    
    

    public sealed class CommandSession
    {
        public List<string>?      Drives           { get; set; }
        public FileStream?        ReadFile         { get; set; }
        public FileStream?        WriteFile        { get; set; }
        public long               CurFileSize      { get; set; }
        public long               CurFileTransferred { get; set; }
        public string             CurFileName      { get; set; } = "";
        public IProgressListener? Listener         { get; set; }
        public string? ConsoleId              { get; set; }

        public void StartFile(string path, int mode)
        {
            if (mode == FileModeRead)
            {
                ReadFile?.Dispose();
                ReadFile = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            else
            {
                WriteFile?.Dispose();
                WriteFile = mode == FileModeAppend
                    ? new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None)
                    : new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            }
        }

        public void EndFile(int mode)
        {
            if (mode == FileModeRead) { ReadFile?.Dispose();  ReadFile  = null; }
            else                      { WriteFile?.Dispose(); WriteFile = null; }
        }

        public void Dispose()
        {
            ReadFile?.Dispose();  ReadFile  = null;
            WriteFile?.Dispose(); WriteFile = null;
        }
    }

    

    public static Func<int>?            GetSpecialPathCountCb;
    public static Func<int, string[]?>? GetSpecialPathCb;
    public static Func<string?>?        SelectFileCb;

    private static bool IsValidFileMode(int mode) =>
        mode is FileModeRead or FileModeWrite or FileModeAppend;

    

    public static bool Dispatch(ICommandBlock block, CommandSession session)
    {
        int cmdId = block.ValidateCommand();
        foreach (var (id, handle) in Commands)
        {
            if (id == cmdId) { handle(block, session); return true; }
        }
        return false;
    }

    

    public static readonly (int Id, Action<ICommandBlock, CommandSession> Handle)[] Commands =
    [
        (1, (block, s) =>
        {
            s.Drives = [];
            Console.WriteLine("[cf] GetDriveCount() -> 0 (suppressed)");
            block.ResponseStart();
            block.Write32(0);
            block.ResponseEnd();
        }),

        (2, (block, s) =>
        {
            s.Drives ??= FileSystemHelper.ListDrives();
            int idx = block.Read32();
            if (idx < s.Drives.Count)
            {
                string drivePath  = s.Drives[idx];
                string driveLabel = FileSystemHelper.GetDriveLabel(drivePath);
                Console.WriteLine($"[cf] GetDriveInfo({idx}) -> '{drivePath}', '{driveLabel}'");
                block.ResponseStart();
                block.WriteString(driveLabel);
                block.WriteString(drivePath);
                block.Write64(0);
                block.Write64(0);
                block.ResponseEnd();
            }
            else { block.RespondFailure(ResultInvalidIndex); }
        }),

        (3, (block, s) =>
        {
            string path = FileSystemHelper.DenormalizePath(block.ReadString());
            try
            {
                int  pathType = PathTypeInvalid;
                long fileSize = 0;
                if (File.Exists(path))            { pathType = PathTypeFile;      fileSize = new FileInfo(path).Length; }
                else if (Directory.Exists(path))  { pathType = PathTypeDirectory; }
                Console.WriteLine($"[cf] StatPath('{path}') -> type={pathType}, size={fileSize}");
                block.ResponseStart();
                block.Write32(pathType);
                block.Write64(fileSize);
                block.ResponseEnd();
            }
            catch (Exception __ex) { Console.WriteLine($"[cf] Exception: {__ex.Message}"); block.RespondFailure(ResultExceptionCaught); }
        }),

        (4, (block, _) =>
        {
            string path  = FileSystemHelper.DenormalizePath(block.ReadString());
            int    count = FileSystemHelper.GetFilesIn(path).Count;
            Console.WriteLine($"[cf] GetFileCount('{path}') -> {count}");
            block.ResponseStart();
            block.Write32(count);
            block.ResponseEnd();
        }),

        (5, (block, _) =>
        {
            string path = FileSystemHelper.DenormalizePath(block.ReadString());
            int    idx  = block.Read32();
            var    files = FileSystemHelper.GetFilesIn(path);
            if (idx < files.Count)
            {
                Console.WriteLine($"[cf] GetFile('{path}', {idx}) -> '{files[idx]}'");
                block.ResponseStart();
                block.WriteString(files[idx]);
                block.ResponseEnd();
            }
            else { block.RespondFailure(ResultInvalidIndex); }
        }),

        (6, (block, _) =>
        {
            string path  = FileSystemHelper.DenormalizePath(block.ReadString());
            int    count = FileSystemHelper.GetDirectoriesIn(path).Count;
            Console.WriteLine($"[cf] GetDirectoryCount('{path}') -> {count}");
            block.ResponseStart();
            block.Write32(count);
            block.ResponseEnd();
        }),

        (7, (block, _) =>
        {
            string path = FileSystemHelper.DenormalizePath(block.ReadString());
            int    idx  = block.Read32();
            var    dirs = FileSystemHelper.GetDirectoriesIn(path);
            if (idx < dirs.Count)
            {
                Console.WriteLine($"[cf] GetDirectory('{path}', {idx}) -> '{dirs[idx]}'");
                block.ResponseStart();
                block.WriteString(dirs[idx]);
                block.ResponseEnd();
            }
            else { block.RespondFailure(ResultInvalidIndex); }
        }),

        (8, (block, s) =>
        {
            string path = FileSystemHelper.DenormalizePath(block.ReadString());
            int    mode = block.Read32();
            Console.WriteLine($"[cf] StartFile('{path}', mode={mode})");
            try
            {
                if (!IsValidFileMode(mode)) { block.RespondFailure(ResultInvalidFileMode); return; }
                s.StartFile(path, mode);
                s.CurFileName        = Path.GetFileName(path);
                s.CurFileTransferred = 0;
                s.CurFileSize        = File.Exists(path) ? new FileInfo(path).Length : 0;
                block.RespondEmpty();
            }
            catch (Exception __ex) { Console.WriteLine($"[cf] Exception: {__ex.Message}"); block.RespondFailure(ResultExceptionCaught); }
        }),

        (9, (block, s) =>
        {
            string path   = FileSystemHelper.DenormalizePath(block.ReadString());
            long   offset = block.Read64();
            long   size   = block.Read64();
            Console.WriteLine($"[cf] ReadFile('{path}', offset={offset}, size={size})");
            try
            {
                byte[] data = new byte[(int)size];
                int read = 0;

                if (s.ReadFile != null)
                {
                    s.ReadFile.Seek(offset, SeekOrigin.Begin);
                    while (read < (int)size)
                    {
                        int got = s.ReadFile.Read(data, read, (int)size - read);
                        if (got == 0) break;
                        read += got;
                    }
                }
                else
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 65536, FileOptions.SequentialScan);
                    while (read < (int)size)
                    {
                        int got = RandomAccess.Read(fs.SafeFileHandle,
                            data.AsSpan(read, (int)size - read), offset + read);
                        if (got == 0) break;
                        read += got;
                    }
                }

                s.CurFileTransferred = offset + read;
                if (s.Listener != null && s.CurFileSize > 0)
                    s.Listener.OnProgress(s.CurFileName, s.CurFileTransferred, s.CurFileSize);

                block.ResponseStart();
                block.Write64(read);
                block.ResponseEnd();
                block.SendBuffer(data, read);
            }
            catch (Exception __ex) { Console.WriteLine($"[cf] Exception: {__ex.Message}"); block.RespondFailure(ResultExceptionCaught); }
        }),

        (10, (block, s) =>
        {
            string path = FileSystemHelper.DenormalizePath(block.ReadString());
            long   size = block.Read64();
            Console.WriteLine($"[cf] WriteFile('{path}', size={size})");
            byte[] data = block.GetBuffer((int)size);
            try
            {
                bool closeAfter = s.WriteFile is null;
                var  stream     = s.WriteFile ?? new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
                stream.Write(data);
                if (closeAfter) stream.Dispose();

                s.CurFileTransferred += size;
                if (s.Listener != null && s.CurFileSize > 0)
                    s.Listener.OnProgress(s.CurFileName, s.CurFileTransferred, s.CurFileSize);

                block.ResponseStart();
                block.Write64(size);
                block.ResponseEnd();
            }
            catch (Exception __ex) { Console.WriteLine($"[cf] Exception: {__ex.Message}"); block.RespondFailure(ResultExceptionCaught); }
        }),

        (11, (block, s) =>
        {
            int mode = block.Read32();
            Console.WriteLine($"[cf] EndFile(mode={mode})");
            try
            {
                if (!IsValidFileMode(mode)) { block.RespondFailure(ResultInvalidFileMode); return; }
                s.EndFile(mode);
                s.Listener?.OnIdle();
                block.RespondEmpty();
            }
            catch (Exception __ex) { Console.WriteLine($"[cf] Exception: {__ex.Message}"); block.RespondFailure(ResultExceptionCaught); }
        }),

        (12, (block, _) =>
        {
            string path     = FileSystemHelper.DenormalizePath(block.ReadString());
            int    pathType = block.Read32();
            Console.WriteLine($"[cf] Create('{path}', type={pathType})");
            try
            {
                if      (pathType == PathTypeFile)      File.Create(path).Dispose();
                else if (pathType == PathTypeDirectory)  Directory.CreateDirectory(path);
                block.RespondEmpty();
            }
            catch (Exception __ex) { Console.WriteLine($"[cf] Exception: {__ex.Message}"); block.RespondFailure(ResultExceptionCaught); }
        }),

        (13, (block, _) =>
        {
            string path = FileSystemHelper.DenormalizePath(block.ReadString());
            Console.WriteLine($"[cf] Delete('{path}')");
            try { FileSystemHelper.DeletePath(path); block.RespondEmpty(); }
            catch (Exception __ex) { Console.WriteLine($"[cf] Exception: {__ex.Message}"); block.RespondFailure(ResultExceptionCaught); }
        }),

        (14, (block, _) =>
        {
            string path    = FileSystemHelper.DenormalizePath(block.ReadString());
            string newName = FileSystemHelper.DenormalizePath(block.ReadString());
            Console.WriteLine($"[cf] Rename('{path}' -> '{newName}')");
            try
            {
                string? dir     = Path.GetDirectoryName(path);
                string  newPath = dir is null ? newName : Path.Combine(dir, newName);
                if (File.Exists(path))           File.Move(path, newPath);
                else if (Directory.Exists(path)) Directory.Move(path, newPath);
                block.RespondEmpty();
            }
            catch (Exception __ex) { Console.WriteLine($"[cf] Exception: {__ex.Message}"); block.RespondFailure(ResultExceptionCaught); }
        }),

        (15, (block, _) =>
        {
            int count = GetSpecialPathCountCb?.Invoke() ?? 0;
            Console.WriteLine($"[cf] GetSpecialPathCount() -> {count}");
            block.ResponseStart();
            block.Write32(count);
            block.ResponseEnd();
        }),

        (16, (block, _) =>
        {
            int       idx  = block.Read32();
            string[]? info = GetSpecialPathCb?.Invoke(idx);
            if (info != null)
            {
                string name = info[0];
                string path = FileSystemHelper.NormalizePath(info[1]);
                Console.WriteLine($"[cf] GetSpecialPath({idx}) -> '{name}', '{path}'");
                block.ResponseStart();
                block.WriteString(name);
                block.WriteString(path);
                block.ResponseEnd();
            }
            else
            {
                Console.WriteLine($"[cf] GetSpecialPath({idx}) -> invalid index");
                block.RespondFailure(ResultInvalidIndex);
            }
        }),

        (17, (block, _) =>
        {
            string? path = SelectFileCb?.Invoke();
            if (path != null)
            {
                Console.WriteLine($"[cf] SelectFile() -> '{path}'");
                block.ResponseStart();
                block.WriteString(FileSystemHelper.NormalizePath(path));
                block.ResponseEnd();
            }
            else { block.RespondFailure(ResultSelectionCancelled); }
        }),

        (18, (block, s) =>
        {
            string announcedId = block.ReadString();
            Console.WriteLine($"[cf] AnnounceConsoleId('{announcedId}')");
            if (!string.IsNullOrEmpty(announcedId))
            {
                string oldId = s.ConsoleId ?? announcedId;
                s.ConsoleId  = announcedId;
                if (s.Listener is IProgressListenerWithId pl)
                    pl.UpdateId(announcedId);
                OnConsoleIdAnnounced?.Invoke(oldId, announcedId);
            }
            block.RespondEmpty();
        }),
    ];

    public interface IProgressListenerWithId : IProgressListener
    {
        void UpdateId(string newId);
    }

    public static Action<string, string>? OnConsoleIdAnnounced;
}
