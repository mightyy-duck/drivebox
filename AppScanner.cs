using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PartiChart;

public sealed class AppEntry
{
    public string Name = "", Publisher = "", Source = "", InstallPath = "";
    public bool Game;
    public long InstallSize, InstallFiles, DataSize;
    public List<(string Path, long Size)> DataFolders = new();
    public long Total => InstallSize + DataSize;
}

/// <summary>
/// Finds installed apps and games (Steam, Epic, Xbox, the Windows uninstall registry) and measures
/// the size of the program itself and of its data (AppData, ProgramData, Documents, saved games).
/// The data part is matched by folder name, so it is an estimate, not an exact accounting.
/// </summary>
public static partial class AppScanner
{
    static readonly HashSet<string> SkipFolders = new()
    {
        "microsoft", "windows", "packages", "programs", "temp", "tmp", "crashdumps", "history", "d3dscache", "connecteddevicesplatform",
        "publishers", "comms", "virtualstore", "commonfiles", "desktop", "downloads", "pictures", "music", "videos", "onedrive"
    };

    static string Norm(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    static string Key(string path) => path.Trim().Trim('"').TrimEnd('\\', '/').ToLowerInvariant();

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)] private static partial Regex RxPath();
    [GeneratedRegex("\"name\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)] private static partial Regex RxName();
    [GeneratedRegex("\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)] private static partial Regex RxDir();
    [GeneratedRegex(@"[\s\-_(]*v?\d+([.\-_]\d+)*.*$")] private static partial Regex RxVersion();

    static bool IsGameLocation(string path, string publisher)
    {
        var p = path.ToLowerInvariant();
        if (p.Contains("\\steamapps\\common\\") || p.Contains("\\epic games\\") || p.Contains("\\xboxgames\\") || p.Contains("\\riot games\\") ||
            p.Contains("\\gog galaxy\\games\\") || p.Contains("\\ubisoft game launcher\\games\\") || p.Contains("\\ea games\\") || p.Contains("\\origin games\\") ||
            p.Contains("\\battle.net\\") || p.Contains("\\games\\")) return true;
        var pub = publisher.ToLowerInvariant();
        return pub.Contains("riot games") || pub.Contains("valve") || pub.Contains("epic games") || pub.Contains("ubisoft") || pub.Contains("electronic arts") ||
               pub.Contains("blizzard") || pub.Contains("gog.com") || pub.Contains("mojang") || pub.Contains("activision") || pub.Contains("bethesda") || pub.Contains("rockstar");
    }

    static bool UsablePath(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return false;
        try
        {
            p = p.Trim().Trim('"').TrimEnd('\\');
            if (!Directory.Exists(p)) return false;
            var root = Path.GetPathRoot(p)?.TrimEnd('\\') ?? "";
            if (string.Equals(p, root, StringComparison.OrdinalIgnoreCase)) return false;
            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
            if (p.StartsWith(win, StringComparison.OrdinalIgnoreCase)) return false;
            string[] bare = { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                              Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                              Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
            return !bare.Any(b => string.Equals(b.TrimEnd('\\'), p, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    static string? ExeDir(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim(); if (s.StartsWith('"')) { int e = s.IndexOf('"', 1); s = e > 0 ? s[1..e] : s.Trim('"'); } else { int c = s.IndexOf(',', StringComparison.Ordinal); if (c > 0) s = s[..c]; }
        int ex = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); if (ex > 0) s = s[..(ex + 4)];
        try { return Path.GetDirectoryName(s); } catch { return null; }
    }

    // ------------------------------------------------------------------ discovery
    static void AddIfNew(Dictionary<string, AppEntry> map, AppEntry a)
    {
        if (!UsablePath(a.InstallPath)) return;
        var k = Key(a.InstallPath); if (!map.ContainsKey(k)) map[k] = a;
    }

    static void FromSteam(Dictionary<string, AppEntry> map)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var steam = (k?.GetValue("SteamPath") as string)?.Replace('/', '\\'); if (string.IsNullOrEmpty(steam)) return;
            var libs = new List<string> { steam };
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf)) foreach (Match m in RxPath().Matches(File.ReadAllText(vdf))) libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
            foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var apps = Path.Combine(lib, "steamapps"); if (!Directory.Exists(apps)) continue;
                foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                {
                    try
                    {
                        var t = File.ReadAllText(acf); var n = RxName().Match(t); var d = RxDir().Match(t); if (!n.Success || !d.Success) continue;
                        AddIfNew(map, new AppEntry { Name = n.Groups[1].Value, Publisher = "Steam", Source = "Steam", Game = true, InstallPath = Path.Combine(apps, "common", d.Groups[1].Value) });
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    static void FromEpic(Dictionary<string, AppEntry> map)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.EnumerateFiles(dir, "*.item"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(f)); var r = doc.RootElement;
                    string? name = r.TryGetProperty("DisplayName", out var dn) ? dn.GetString() : null, loc = r.TryGetProperty("InstallLocation", out var il) ? il.GetString() : null;
                    if (name != null && loc != null) AddIfNew(map, new AppEntry { Name = name, Publisher = "Epic Games", Source = "Epic", Game = true, InstallPath = loc });
                }
                catch { }
            }
        }
        catch { }
    }

    static void FromXbox(Dictionary<string, AppEntry> map)
    {
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                var x = Path.Combine(d.Name, "XboxGames"); if (!d.IsReady || !Directory.Exists(x)) continue;
                foreach (var g in Directory.EnumerateDirectories(x))
                    AddIfNew(map, new AppEntry { Name = Path.GetFileName(g), Publisher = "Xbox", Source = "Xbox", Game = true, InstallPath = g });
            }
            catch { }
        }
    }

    static void FromRegistry(Dictionary<string, AppEntry> map)
    {
        var hives = new (RegistryKey Hive, string Sub)[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
        };
        foreach (var (hive, sub) in hives)
        {
            try
            {
                using var root = hive.OpenSubKey(sub); if (root == null) continue;
                foreach (var name in root.GetSubKeyNames())
                {
                    try
                    {
                        using var k = root.OpenSubKey(name); if (k == null) continue;
                        var dn = k.GetValue("DisplayName") as string; if (string.IsNullOrWhiteSpace(dn)) continue;
                        if (k.GetValue("SystemComponent") is int sc && sc == 1) continue;
                        if (k.GetValue("ParentKeyName") != null) continue;
                        if (Regex.IsMatch(dn, @"^(Update for|Security Update|Hotfix)|KB\d{6,}", RegexOptions.IgnoreCase)) continue;
                        var pub = k.GetValue("Publisher") as string ?? "";
                        var loc = (k.GetValue("InstallLocation") as string)?.Trim().Trim('"');
                        if (!UsablePath(loc)) loc = ExeDir(k.GetValue("DisplayIcon") as string);
                        if (!UsablePath(loc)) loc = ExeDir(k.GetValue("UninstallString") as string);
                        if (!UsablePath(loc)) continue;
                        AddIfNew(map, new AppEntry { Name = dn, Publisher = pub, Source = "Installed", Game = IsGameLocation(loc!, pub), InstallPath = loc! });
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ data folders
    static IEnumerable<string> DataRoots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[]
        {
            local, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Path.Combine(Path.GetDirectoryName(local) ?? local, "LocalLow"), docs, Path.Combine(docs, "My Games"), Path.Combine(profile, "Saved Games")
        }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    static List<string> KeysOf(AppEntry a)
    {
        var keys = new List<string>();
        void Add(string s) { var n = Norm(s); if (n.Length >= 4 && !keys.Contains(n)) keys.Add(n); }
        Add(a.Name); Add(RxVersion().Replace(a.Name, "")); Add(Path.GetFileName(a.InstallPath.TrimEnd('\\')));
        return keys;
    }

    // ------------------------------------------------------------------ main entry
    public static List<AppEntry> Collect(IProgress<(int Done, int Total, string Name)>? progress, CancellationToken ct)
    {
        var map = new Dictionary<string, AppEntry>();
        progress?.Report((0, 0, "Looking for installed apps and games…"));
        FromSteam(map); FromEpic(map); FromXbox(map); FromRegistry(map);
        var apps = map.Values.ToList();

        // 1) size of the program itself
        int done = 0, total = apps.Count;
        Parallel.ForEach(apps, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, a =>
        {
            try { var (b, f) = Scanner.SizeOf(a.InstallPath, ct); a.InstallSize = b; a.InstallFiles = f; } catch (OperationCanceledException) { throw; } catch { }
            progress?.Report((Interlocked.Increment(ref done), total, a.Name));
        });

        // 2) match data folders (AppData, ProgramData, Documents, saved games) to apps by name
        progress?.Report((total, total, "Matching data folders…"));
        var keys = apps.ToDictionary(a => a, KeysOf);
        var assigned = new Dictionary<string, (AppEntry App, int Score)>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in DataRoots())
        {
            IEnumerable<string> dirs; try { dirs = Directory.EnumerateDirectories(root).ToList(); } catch { continue; }
            foreach (var dir in dirs)
            {
                var c = Norm(Path.GetFileName(dir)); if (c.Length < 4 || SkipFolders.Contains(c)) continue;
                foreach (var a in apps)
                {
                    if (dir.StartsWith(a.InstallPath, StringComparison.OrdinalIgnoreCase) || a.InstallPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) continue;
                    int score = 0;
                    foreach (var k in keys[a])
                    {
                        if (k == c) score = Math.Max(score, 1000 + c.Length);
                        else if (c.Length >= 6 && k.Length >= 6) { if (k.Contains(c)) score = Math.Max(score, 500 + c.Length); else if (c.Contains(k)) score = Math.Max(score, 400 + k.Length); }
                    }
                    if (score > 0 && (!assigned.TryGetValue(dir, out var cur) || score > cur.Score)) assigned[dir] = (a, score);
                }
            }
        }
        ct.ThrowIfCancellationRequested();
        var work = assigned.ToList();
        Parallel.ForEach(work, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, kv =>
        {
            try
            {
                var (b, _) = Scanner.SizeOf(kv.Key, ct);
                lock (kv.Value.App) { kv.Value.App.DataSize += b; kv.Value.App.DataFolders.Add((kv.Key, b)); }
            }
            catch (OperationCanceledException) { throw; } catch { }
        });

        return apps.Where(a => a.Total >= (1L << 20)).OrderByDescending(a => a.Total).ToList();
    }
}
