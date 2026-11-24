namespace DriveBox;

public sealed class ExtInfo { public long Size, Count; public int Cat; }

/// <summary>Per top-level folder numbers used by the scatter / 3D charts.</summary>
public sealed class FolderAgg { public DirNode Dir = null!; public long NewestTicks, OldestTicks = long.MaxValue; public double AvgTicks; }

public sealed class Stats
{
    public static readonly string[] CatNames = { "Media", "Code", "Documents", "Archives", "System", "Other" };
    public static readonly string[] CatColors = { "#5B6CF6", "#8B5CF6", "#D45BD6", "#F2556B", "#F5B942", "#9CA3AF" };
    static readonly Dictionary<string, int> CatMap = BuildCatMap();
    static Dictionary<string, int> BuildCatMap()
    {
        var d = new Dictionary<string, int>();
        void Add(int c, string list) { foreach (var e in list.Split(' ')) d["." + e] = c; }
        Add(0, "jpg jpeg png gif webp bmp svg heic psd raw tif tiff ico mp4 mkv mov avi webm wmv flv mp3 wav flac aac ogg m4a");
        Add(1, "cs js ts tsx jsx py java cpp c h hpp go rs dart kt swift sh ps1 bat json xml html htm css scss md yml yaml toml sln csproj gradle php rb lua vue svelte map lock");
        Add(2, "pdf doc docx xls xlsx ppt pptx txt csv rtf odt ods epub xps");
        Add(3, "zip rar 7z tar gz bz2 xz iso vhd vhdx vmdk bak cab img wim");
        Add(4, "dll exe sys msi dat db sqlite log tmp cache pak bin lib obj pdb node so dylib ttf otf woff woff2 etl cat mui manifest");
        return d;
    }
    public static int CatOf(string ext) => CatMap.TryGetValue(ext, out var c) ? c : 5;

    public static readonly long[] Limits = { 1L << 10, 10L << 10, 100L << 10, 1L << 20, 10L << 20, 100L << 20, 1L << 30 };
    public long Files, Dirs, Size, Last7, Prev7, Last30, Prev30, LastYear, PrevYear, MinTicks = long.MaxValue, MaxTicks;
    public Dictionary<string, ExtInfo> Ext = new();
    public long[] Buckets = new long[8];
    public long[] CatSize = new long[6], CatCount = new long[6];
    /// <summary>files per age bucket: &lt;1 week, &lt;1 month, &lt;3 months, &lt;1 year, &lt;3 years, older</summary>
    public long[] AgeCount = new long[6], AgeSize = new long[6];
    public Dictionary<int, int> Months = new();
    /// <summary>month key (year*12+month-1) -> bytes per category</summary>
    public Dictionary<int, long[]> MonthCat = new();
    public Dictionary<int, long[]> MonthCnt = new();
    public List<(DirNode Dir, FileEntry File)> Largest = new();
    public List<FolderAgg> Folders = new();

    sealed class Ctx { public long T7, T14, T30, T60, T90, T365, T730, T1095; public PriorityQueue<(DirNode, FileEntry), long> Pq = new(); public int LastKey = int.MinValue; public long[]? LastCat, LastCnt; }

    void AddFile(Ctx c, DirNode d, FileEntry f, FolderAgg? agg)
    {
        var ext = System.IO.Path.GetExtension(f.Name).ToLowerInvariant();
        if (ext.Length == 0) ext = "(none)";
        if (!Ext.TryGetValue(ext, out var ei)) Ext[ext] = ei = new ExtInfo { Cat = CatOf(ext) };
        ei.Size += f.Size; ei.Count++;
        CatSize[ei.Cat] += f.Size; CatCount[ei.Cat]++;
        int b = 0; while (b < 7 && f.Size >= Limits[b]) b++;
        Buckets[b]++;
        long t = f.Ticks;
        if (t > 0)
        {
            if (t < MinTicks) MinTicks = t;
            if (t > MaxTicks) MaxTicks = t;
            if (t >= c.T7) Last7++; else if (t >= c.T14) Prev7++;
            if (t >= c.T30) Last30++; else if (t >= c.T60) Prev30++;
            if (t >= c.T365) LastYear++; else if (t >= c.T730) PrevYear++;
            int age = t >= c.T7 ? 0 : t >= c.T30 ? 1 : t >= c.T90 ? 2 : t >= c.T365 ? 3 : t >= c.T1095 ? 4 : 5;
            AgeCount[age]++; AgeSize[age] += f.Size;
            if (agg != null) { if (t > agg.NewestTicks) agg.NewestTicks = t; if (t < agg.OldestTicks) agg.OldestTicks = t; agg.AvgTicks += t; }
            try
            {
                var dt = new DateTime(t); int key = dt.Year * 12 + dt.Month - 1;
                if (key != c.LastKey)
                {
                    Months[key] = Months.GetValueOrDefault(key);
                    if (!MonthCat.TryGetValue(key, out c.LastCat)) { MonthCat[key] = c.LastCat = new long[6]; MonthCnt[key] = c.LastCnt = new long[6]; }
                    else c.LastCnt = MonthCnt[key];
                    c.LastKey = key;
                }
                Months[key]++; c.LastCat![ei.Cat] += f.Size; c.LastCnt![ei.Cat]++;
            }
            catch { }
        }
        if (c.Pq.Count < 10) c.Pq.Enqueue((d, f), f.Size);
        else if (c.Pq.TryPeek(out _, out var min) && f.Size > min) { c.Pq.Dequeue(); c.Pq.Enqueue((d, f), f.Size); }
    }

    public static Stats Compute(DirNode root)
    {
        var s = new Stats { Files = root.FileCount, Dirs = root.DirCount, Size = root.Size };
        var now = DateTime.Now;
        var c = new Ctx { T7 = now.AddDays(-7).Ticks, T14 = now.AddDays(-14).Ticks, T30 = now.AddDays(-30).Ticks, T60 = now.AddDays(-60).Ticks, T90 = now.AddDays(-90).Ticks,
            T365 = now.AddYears(-1).Ticks, T730 = now.AddYears(-2).Ticks, T1095 = now.AddYears(-3).Ticks };
        foreach (var f in root.Files) s.AddFile(c, root, f, null);
        var top = root.Dirs.OrderByDescending(d => d.Size).ToList();
        for (int i = 0; i < top.Count; i++)
        {
            var agg = i < 16 ? new FolderAgg { Dir = top[i] } : null;
            var stack = new Stack<DirNode>(); stack.Push(top[i]);
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                foreach (var k in d.Dirs) stack.Push(k);
                foreach (var f in d.Files) s.AddFile(c, d, f, agg);
            }
            if (agg != null)
            {
                long cnt = top[i].FileCount; agg.AvgTicks = cnt > 0 ? agg.AvgTicks / cnt : 0;
                if (agg.OldestTicks == long.MaxValue) agg.OldestTicks = 0;
                s.Folders.Add(agg);
            }
        }
        while (c.Pq.Count > 0) s.Largest.Add(c.Pq.Dequeue());
        s.Largest.Reverse();
        return s;
    }
}
