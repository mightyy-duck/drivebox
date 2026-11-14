using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PartiChart;

/// <summary>
/// Reads the NTFS Master File Table straight from the volume (needs administrator).
/// One sequential read of the MFT replaces millions of directory lookups, so a whole drive takes seconds.
/// Returns null on any problem so the caller can fall back to the normal scanner.
/// </summary>
public static class MftScanner
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

    public static bool IsElevated
    {
        get { try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } catch { return false; } }
    }

    public static bool CanUse(string path)
    {
        if (!IsElevated) return false;
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root) || !string.Equals(root.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
            return new DriveInfo(root.Substring(0, 1)).DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static DirNode? Scan(string rootPath, CancellationToken ct)
    {
        try { return ScanCore(rootPath, ct); }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    static bool Fixup(byte[] b, int o, int recSize)
    {
        if (b[o] != 'F' || b[o + 1] != 'I' || b[o + 2] != 'L' || b[o + 3] != 'E') return false;
        int usaOff = BitConverter.ToUInt16(b, o + 4), usaCnt = BitConverter.ToUInt16(b, o + 6);
        if (usaOff + usaCnt * 2 > recSize) return false;
        ushort usn = BitConverter.ToUInt16(b, o + usaOff);
        for (int i = 1; i < usaCnt; i++)
        {
            int end = i * 512 - 2;
            if (end + 2 > recSize) break;
            if (BitConverter.ToUInt16(b, o + end) != usn) return false;
            b[o + end] = b[o + usaOff + 2 * i]; b[o + end + 1] = b[o + usaOff + 2 * i + 1];
        }
        return true;
    }

    static List<(long Lcn, long Len)> ParseRuns(byte[] b, int p, int end)
    {
        var l = new List<(long, long)>(); long lcn = 0;
        while (p < end && b[p] != 0)
        {
            int ls = b[p] & 0xF, os = b[p] >> 4; p++;
            long len = 0; for (int i = 0; i < ls; i++) len |= (long)b[p + i] << (8 * i); p += ls;
            if (os > 0)
            {
                long off = 0; for (int i = 0; i < os; i++) off |= (long)b[p + i] << (8 * i);
                if ((b[p + os - 1] & 0x80) != 0) off |= -1L << (8 * os);
                p += os; lcn += off; l.Add((lcn, len));
            }
            else l.Add((-1, len));
        }
        return l;
    }

    static DirNode? ScanCore(string rootPath, CancellationToken ct)
    {
        string vol = @"\\.\" + rootPath.Substring(0, 2);
        using var h = CreateFile(vol, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h.IsInvalid) return null;

        Scanner.Current = "Reading NTFS master file table…";
        var live = new LiveItem { Name = "NTFS master file table" };
        lock (Scanner.LiveLock) Scanner.LiveItems.Add(live);

        var boot = new byte[512];
        if (RandomAccess.Read(h, boot, 0) != 512) return null;
        int bps = BitConverter.ToUInt16(boot, 0x0B); int spc = boot[0x0D]; long clusterSize = (long)bps * spc;
        long mftLcn = BitConverter.ToInt64(boot, 0x30); sbyte cpr = (sbyte)boot[0x40];
        int recSize = cpr > 0 ? (int)(cpr * clusterSize) : 1 << (-cpr);
        if (bps < 512 || spc == 0 || recSize < 512 || recSize > 65536) return null;

        // record 0 describes where the MFT itself lives on disk
        int readRec = (recSize + bps - 1) / bps * bps;
        var rec0 = new byte[readRec];
        if (RandomAccess.Read(h, rec0, mftLcn * clusterSize) < recSize) return null;
        if (!Fixup(rec0, 0, recSize)) return null;
        List<(long Lcn, long Len)>? runs = null; long mftSize = 0;
        for (int a = BitConverter.ToUInt16(rec0, 0x14); a + 8 <= recSize;)
        {
            uint type = BitConverter.ToUInt32(rec0, a); if (type == 0xFFFFFFFF) break;
            int len = (int)BitConverter.ToUInt32(rec0, a + 4); if (len <= 0 || a + len > recSize) break;
            if (type == 0x80 && rec0[a + 8] != 0 && rec0[a + 9] == 0)
            {
                mftSize = BitConverter.ToInt64(rec0, a + 0x30);
                runs = ParseRuns(rec0, a + BitConverter.ToUInt16(rec0, a + 0x20), a + len);
            }
            a += len;
        }
        if (runs == null || runs.Count == 0 || mftSize <= 0) return null;
        long total = mftSize / recSize;
        if (total > 200_000_000) return null;
        int n = (int)total;

        var parent = new long[n]; var size = new long[n]; var ticks = new long[n];
        var flag = new byte[n]; var ns = new byte[n]; var name = new string?[n];
        for (int i = 0; i < n; i++) parent[i] = -1;
        long tzOff = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).Ticks;
        long epoch = new DateTime(1601, 1, 1).Ticks + tzOff;

        int chunk = recSize * Math.Max(1, (16 << 20) / recSize);
        var bufs = new[] { new byte[chunk + bps], new byte[chunk + bps] };
        long mftOff = 0, bytesRead = 0; int cur = 0;

        // double buffered: the next chunk is read from disk while this one is parsed on all cores
        (long Off, int Len, int Buf)? ReadNext(ref int ri, ref long pos, ref long mOff, int bi)
        {
            while (ri < runs.Count && mOff < mftSize)
            {
                var (lcn, len) = runs[ri]; long runBytes = len * clusterSize;
                if (lcn < 0 || pos >= runBytes) { ri++; pos = 0; continue; }
                int want = (int)Math.Min(chunk, Math.Min(runBytes - pos, mftSize - mOff));
                int cnt = want / recSize * recSize; if (cnt == 0) { ri++; pos = 0; continue; }
                int rl = (cnt + bps - 1) / bps * bps;
                if (RandomAccess.Read(h, bufs[bi].AsSpan(0, rl), lcn * clusterSize + pos) < cnt) throw new IOException("short read");
                var res = (mOff, cnt, bi); pos += cnt; mOff += cnt; return res;
            }
            return null;
        }

        int runIdx = 0; long runPos = 0; long readOff = 0;
        var next = ReadNext(ref runIdx, ref runPos, ref readOff, 0);
        while (next != null)
        {
            ct.ThrowIfCancellationRequested();
            var (off, cnt, bi) = next.Value; var b = bufs[bi];
            int nextBuf = 1 - bi;
            // read ahead (sequential, one thread) while parsing the current chunk
            int ri2 = runIdx; long rp2 = runPos, ro2 = readOff;
            var readTask = Task.Run(() => ReadNext(ref ri2, ref rp2, ref ro2, nextBuf));
            long firstRec = off / recSize; int recs = cnt / recSize;
            Parallel.For(0, 32, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, part =>
            {
                int s = (int)((long)recs * part / 32), e = (int)((long)recs * (part + 1) / 32);
                for (int r = s; r < e; r++) ParseRecord(b, r * recSize, (int)(firstRec + r), recSize, parent, size, ticks, flag, ns, name, epoch);
            });
            next = readTask.Result; runIdx = ri2; runPos = rp2; readOff = ro2;
            bytesRead += cnt; Interlocked.Exchange(ref live.Bytes, bytesRead); Interlocked.Exchange(ref Scanner.FileCounter, firstRec + recs);
        }
        live.Done = true;

        // ---- build the tree ----
        Scanner.Current = "Building folder tree…";
        var dirs = new DirNode?[n];
        for (int i = 0; i < n; i++)
            if ((flag[i] & 3) == 3 || i == 5) dirs[i] = new DirNode { Name = i == 5 ? rootPath : name[i] ?? "?" };
        var root = dirs[5]; if (root == null) return null;
        root.Path = rootPath.EndsWith('\\') ? rootPath : rootPath + "\\";
        for (int i = 0; i < n; i++)
        {
            var d = dirs[i]; if (d == null || i == 5) continue;
            long p = parent[i]; if (p < 0 || p >= n || p == i) continue;
            var pd = dirs[p]; if (pd == null) continue;
            d.Parent = pd; pd.Dirs.Add(d);
        }
        for (int i = 0; i < n; i++)
        {
            if ((flag[i] & 1) == 0 || (flag[i] & 2) != 0 || name[i] == null) continue;
            long p = parent[i]; if (p < 0 || p >= n) continue;
            var pd = dirs[p]; if (pd == null) continue;
            long sz = size[i] < 0 ? 0 : size[i];
            pd.Files.Add(new FileEntry(name[i]!, sz, ticks[i])); pd.OwnSize += sz;
        }
        // paths top-down, sizes bottom-up
        var order = new List<DirNode>(); var st = new Stack<DirNode>(); st.Push(root);
        while (st.Count > 0)
        {
            var d = st.Pop(); order.Add(d);
            foreach (var k in d.Dirs) { k.Path = Path.Combine(d.Path, k.Name); st.Push(k); }
        }
        long files = 0;
        for (int i = order.Count - 1; i >= 0; i--)
        {
            var d = order[i]; d.Size = d.OwnSize; d.FileCount = d.Files.Count; d.DirCount = d.Dirs.Count;
            foreach (var k in d.Dirs) { d.Size += k.Size; d.FileCount += k.FileCount; d.DirCount += k.DirCount; }
            files += d.Files.Count;
        }
        Interlocked.Exchange(ref Scanner.FileCounter, files); Interlocked.Exchange(ref Scanner.TotalBytes, root.Size);
        var items = root.Dirs.OrderByDescending(d => d.Size).Select(d => new LiveItem { Name = d.Name, Bytes = d.Size, Files = d.FileCount, Done = true }).ToList();
        lock (Scanner.LiveLock) Scanner.LiveItems.AddRange(items);
        return root;
    }

    static void ParseRecord(byte[] b, int o, int recNo, int recSize, long[] parent, long[] size, long[] ticks, byte[] flag, byte[] ns, string?[] name, long epoch)
    {
        if (b[o] != 'F' || b[o + 1] != 'I' || b[o + 2] != 'L' || b[o + 3] != 'E') return;
        ushort flags = BitConverter.ToUInt16(b, o + 0x16);
        if ((flags & 1) == 0) return;
        if (!Fixup(b, o, recSize)) return;
        long baseRef = BitConverter.ToInt64(b, o + 0x20) & 0xFFFFFFFFFFFFL;
        int t = baseRef == 0 ? recNo : (int)baseRef;
        if (t < 0 || t >= flag.Length) return;
        if (baseRef == 0) flag[t] = (byte)(1 | ((flags & 2) != 0 ? 2 : 0));
        int end = Math.Min(recSize, (int)BitConverter.ToUInt32(b, o + 0x18));
        for (int a = BitConverter.ToUInt16(b, o + 0x14); a + 8 <= end;)
        {
            uint type = BitConverter.ToUInt32(b, o + a); if (type == 0xFFFFFFFF) break;
            int len = (int)BitConverter.ToUInt32(b, o + a + 4); if (len <= 0 || a + len > recSize) break;
            bool nonRes = b[o + a + 8] != 0; int nameLen = b[o + a + 9];
            if (type == 0x10 && !nonRes)
            {
                int c = a + BitConverter.ToUInt16(b, o + a + 0x14);
                if (c + 16 <= recSize) ticks[t] = BitConverter.ToInt64(b, o + c + 8) + epoch;
            }
            else if (type == 0x30 && !nonRes)
            {
                int c = a + BitConverter.ToUInt16(b, o + a + 0x14);
                if (c + 0x42 <= recSize)
                {
                    int nl = b[o + c + 0x40]; byte space = b[o + c + 0x41];
                    if (c + 0x42 + nl * 2 <= recSize && (name[t] == null || (space != 2 && ns[t] == 2)))
                    {
                        name[t] = Encoding.Unicode.GetString(b, o + c + 0x42, nl * 2);
                        ns[t] = space; parent[t] = BitConverter.ToInt64(b, o + c) & 0xFFFFFFFFFFFFL;
                    }
                }
            }
            else if (type == 0x80 && nameLen == 0)
            {
                if (nonRes) { if (BitConverter.ToInt64(b, o + a + 0x10) == 0) size[t] = BitConverter.ToInt64(b, o + a + 0x30); }
                else size[t] = BitConverter.ToUInt32(b, o + a + 0x10);
            }
            a += len;
        }
    }
}
