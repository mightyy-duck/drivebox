using System.Collections.Concurrent;

namespace DriveBox;

public readonly record struct FileEntry(string Name, long Size, long Ticks);

public sealed class DirNode
{
    public string Name = "", Path = "";
    public DirNode? Parent;
    public List<DirNode> Dirs = new();
    public List<FileEntry> Files = new();
    public long OwnSize, Size, FileCount, DirCount;
    public bool Denied;
}

public sealed class LiveItem
{
    public string Name = "";
    public long Bytes, Files;
    public volatile bool Done;
}

public static class Scanner
{
    public static long FileCounter, TotalBytes;
    public static volatile string Current = "";
    public static readonly object LiveLock = new();
    public static List<LiveItem> LiveItems = new();

    sealed class DirEnum : FileSystemEnumerator<(bool Dir, string Name, long Len, long Ticks)>
    {
        public DirEnum(string path) : base(path, new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false, BufferSize = 64 * 1024 }) { }
        protected override bool ShouldIncludeEntry(ref FileSystemEntry e) => !(e.IsDirectory && (e.Attributes & FileAttributes.ReparsePoint) != 0);
        protected override (bool, string, long, long) TransformEntry(ref FileSystemEntry e) =>
            (e.IsDirectory, e.FileName.ToString(), e.IsDirectory ? 0 : e.Length, e.IsDirectory ? 0 : e.LastWriteTimeUtc.LocalDateTime.Ticks);
    }

    /// <summary>Total size and file count of a folder (no tree is kept). Parallel at the top levels.</summary>
    public static (long Bytes, long Files) SizeOf(string path, CancellationToken ct, int depth = 0)
    {
        ct.ThrowIfCancellationRequested();
        long bytes = 0, files = 0; var subs = new List<string>();
        try
        {
            using var en = new DirEnum(path);
            while (en.MoveNext())
            {
                var (isDir, name, len, _) = en.Current;
                if (isDir) subs.Add(System.IO.Path.Combine(path, name)); else { bytes += len; files++; }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        if (depth < 2 && subs.Count > 1)
        {
            long b = 0, f = 0;
            Parallel.For(0, subs.Count, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 8 }, i =>
            {
                var r = SizeOf(subs[i], ct, depth + 1); Interlocked.Add(ref b, r.Bytes); Interlocked.Add(ref f, r.Files);
            });
            return (bytes + b, files + f);
        }
        foreach (var s in subs) { var r = SizeOf(s, ct, depth + 1); bytes += r.Bytes; files += r.Files; }
        return (bytes, files);
    }

    public static DirNode Scan(string path, CancellationToken ct)
    {
        FileCounter = 0; TotalBytes = 0;
        lock (LiveLock) LiveItems = new();
        ThreadPool.SetMinThreads(64, 64);
        return ScanDir(path, null, 0, ct, null);
    }

    static string NameOf(string p)
    {
        var t = p.TrimEnd('\\', '/');
        var n = System.IO.Path.GetFileName(t);
        return string.IsNullOrEmpty(n) ? p : n;
    }

    static DirNode ScanDir(string path, DirNode? parent, int depth, CancellationToken ct, LiveItem? live)
    {
        ct.ThrowIfCancellationRequested();
        var node = new DirNode { Path = path, Name = NameOf(path), Parent = parent };
        var subs = new List<string>();
        try
        {
            using var en = new DirEnum(path);
            while (en.MoveNext())
            {
                var (isDir, name, len, ticks) = en.Current;
                if (isDir) subs.Add(System.IO.Path.Combine(path, name));
                else { node.Files.Add(new FileEntry(name, len, ticks)); node.OwnSize += len; }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { node.Denied = true; }

        Interlocked.Add(ref FileCounter, node.Files.Count);
        Interlocked.Add(ref TotalBytes, node.OwnSize);
        if (live != null) { Interlocked.Add(ref live.Bytes, node.OwnSize); Interlocked.Add(ref live.Files, node.Files.Count); }
        Current = path;

        var kids = new DirNode[subs.Count];
        if (depth == 0)
        {
            var items = subs.Select(s => new LiveItem { Name = NameOf(s) }).ToArray();
            lock (LiveLock) LiveItems.AddRange(items);
            Parallel.For(0, subs.Count, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 16 }, i =>
            {
                kids[i] = ScanDir(subs[i], node, 1, ct, items[i]);
                items[i].Done = true;
            });
        }
        else if (subs.Count > 1 && depth < 8)
            Parallel.For(0, subs.Count, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 16 },
                i => kids[i] = ScanDir(subs[i], node, depth + 1, ct, live));
        else
            for (int i = 0; i < subs.Count; i++) kids[i] = ScanDir(subs[i], node, depth + 1, ct, live);

        node.Dirs.AddRange(kids);
        node.Size = node.OwnSize; node.FileCount = node.Files.Count; node.DirCount = kids.Length;
        foreach (var k in kids) { node.Size += k.Size; node.FileCount += k.FileCount; node.DirCount += k.DirCount; }
        return node;
    }
}

