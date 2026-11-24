using System.Globalization;
using LibreHardwareMonitor.Hardware;

namespace DriveBox;

public struct Sample
{
    public long Ticks;
    public float CpuT, CpuP, GpuT, GpuP, CpuLoad;
    public static Sample Blank(long ticks) => new() { Ticks = ticks, CpuT = float.NaN, CpuP = float.NaN, GpuT = float.NaN, GpuP = float.NaN, CpuLoad = float.NaN };
}

/// <summary>A window of history, already averaged down to a few hundred points for drawing.</summary>
public sealed class SensorSeries
{
    public long From, To;
    public long[] Ticks = Array.Empty<long>();
    public double[] CpuT = Array.Empty<double>(), GpuT = Array.Empty<double>(), CpuP = Array.Empty<double>(), GpuP = Array.Empty<double>(), TotP = Array.Empty<double>();
    public int N => Ticks.Length;
    public double EnergyWh;
    public double CoveredSeconds;
}

/// <summary>
/// Samples CPU / GPU temperature and power once a second (LibreHardwareMonitor).
/// Keeps the last hour at 1 s resolution and the last 24 h at 1 min resolution (persisted, so 12 h / 24 h views survive restarts).
/// GPU sensors work without admin rights; CPU temperature and power need an elevated process.
/// </summary>
public sealed class SensorService
{
    public static readonly SensorService I = new();
    const int SecCap = 3600, MinCap = 1440;

    readonly object gate = new();
    readonly Sample[] sec = new Sample[SecCap]; int secHead, secCount;
    readonly List<Sample> minutes = new();
    Computer? pc; System.Threading.Timer? timer; int busy;
    // current-minute accumulator
    long accKey = -1; double aCT, aCP, aGT, aGP, aL; int nCT, nCP, nGT, nGP, nL;
    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DriveBox", "sensors.csv");

    public event Action? Updated;
    public Sample Last = Sample.Blank(0);
    public string GpuName = "", CpuName = "", Status = "starting…";
    public bool EverCpuTemp, EverCpuPower, EverGpuTemp, EverGpuPower;
    public bool Elevated => MftScanner.IsElevated;

    public void Start()
    {
        if (timer != null) return;
        LoadHistory();
        try
        {
            pc = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = false, IsMemoryEnabled = false, IsStorageEnabled = false, IsNetworkEnabled = false };
            pc.Open();
            Status = "running";
        }
        catch (Exception ex) { Status = "sensors unavailable: " + ex.Message; pc = null; }
        timer = new System.Threading.Timer(_ => Tick(), null, 500, 1000);
    }

    public void Stop()
    {
        timer?.Dispose(); timer = null;
        try { FlushMinute(); pc?.Close(); } catch { }
        pc = null;
    }

    // ------------- reading -------------
    float cpuT, cpuP, gpuT, gpuP, load;
    bool gotDiscrete;

    void Tick()
    {
        if (Interlocked.Exchange(ref busy, 1) == 1) return;
        try
        {
            cpuT = cpuP = gpuT = gpuP = load = float.NaN; gotDiscrete = false;
            if (pc != null) foreach (var hw in pc.Hardware) Visit(hw);
            var s = new Sample { Ticks = DateTime.Now.Ticks, CpuT = cpuT, CpuP = cpuP, GpuT = gpuT, GpuP = gpuP, CpuLoad = load };
            if (!float.IsNaN(cpuT)) EverCpuTemp = true; if (!float.IsNaN(cpuP)) EverCpuPower = true;
            if (!float.IsNaN(gpuT)) EverGpuTemp = true; if (!float.IsNaN(gpuP)) EverGpuPower = true;
            Push(s);
            Updated?.Invoke();
        }
        catch { }
        finally { Interlocked.Exchange(ref busy, 0); }
    }

    static bool Has(string n, params string[] parts) { foreach (var p in parts) if (n.Contains(p, StringComparison.OrdinalIgnoreCase)) return true; return false; }

    void Visit(IHardware hw)
    {
        hw.Update();
        bool isCpu = hw.HardwareType == HardwareType.Cpu;
        bool isGpu = hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
        if (isCpu || isGpu)
        {
            float tPref = float.NaN, tAny = float.NaN, pPref = float.NaN, pAny = float.NaN, ld = float.NaN;
            foreach (var sn in hw.Sensors)
            {
                if (sn.Value is not float v || float.IsNaN(v)) continue;
                string n = sn.Name;
                switch (sn.SensorType)
                {
                    case SensorType.Temperature:
                        if (v <= 0 || v > 150) break;
                        tAny = float.IsNaN(tAny) ? v : Math.Max(tAny, v);
                        if (Has(n, "Package", "Tctl", "Tdie", "GPU Core", "Core Average")) tPref = v;
                        break;
                    case SensorType.Power:
                        if (v < 0 || (isCpu && v < 0.05)) break;   // a CPU reporting 0 W without admin rights means "not readable"
                        pAny = float.IsNaN(pAny) ? v : Math.Max(pAny, v);
                        if (Has(n, "Package", "GPU Power", "GPU Total", "Total")) pPref = v;
                        break;
                    case SensorType.Load:
                        if (isCpu && Has(n, "CPU Total")) ld = v;
                        break;
                }
            }
            float t = float.IsNaN(tPref) ? tAny : tPref, p = float.IsNaN(pPref) ? pAny : pPref;
            if (isCpu) { if (float.IsNaN(cpuT)) { cpuT = t; cpuP = p; load = ld; CpuName = hw.Name; } }
            else
            {
                bool discrete = hw.HardwareType != HardwareType.GpuIntel;
                if (discrete || !gotDiscrete) { if (discrete) gotDiscrete = true; if (!(float.IsNaN(t) && float.IsNaN(p))) { gpuT = t; gpuP = p; GpuName = hw.Name; } }
            }
        }
        foreach (var sub in hw.SubHardware) Visit(sub);
    }

    // ------------- history -------------
    void Push(Sample s)
    {
        lock (gate)
        {
            Last = s;
            sec[secHead] = s; secHead = (secHead + 1) % SecCap; if (secCount < SecCap) secCount++;
            long key = s.Ticks / TimeSpan.TicksPerMinute;
            if (accKey >= 0 && key != accKey) FlushMinute();
            accKey = key;
            if (!float.IsNaN(s.CpuT)) { aCT += s.CpuT; nCT++; }
            if (!float.IsNaN(s.CpuP)) { aCP += s.CpuP; nCP++; }
            if (!float.IsNaN(s.GpuT)) { aGT += s.GpuT; nGT++; }
            if (!float.IsNaN(s.GpuP)) { aGP += s.GpuP; nGP++; }
            if (!float.IsNaN(s.CpuLoad)) { aL += s.CpuLoad; nL++; }
        }
    }

    void FlushMinute()
    {
        if (accKey < 0) return;
        var m = new Sample
        {
            Ticks = accKey * TimeSpan.TicksPerMinute + TimeSpan.TicksPerSecond * 30,
            CpuT = nCT > 0 ? (float)(aCT / nCT) : float.NaN, CpuP = nCP > 0 ? (float)(aCP / nCP) : float.NaN,
            GpuT = nGT > 0 ? (float)(aGT / nGT) : float.NaN, GpuP = nGP > 0 ? (float)(aGP / nGP) : float.NaN,
            CpuLoad = nL > 0 ? (float)(aL / nL) : float.NaN
        };
        aCT = aCP = aGT = aGP = aL = 0; nCT = nCP = nGT = nGP = nL = 0;
        minutes.Add(m); while (minutes.Count > MinCap) minutes.RemoveAt(0);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string F(float v) => float.IsNaN(v) ? "" : v.ToString("0.#", CultureInfo.InvariantCulture);
            File.AppendAllText(FilePath, $"{m.Ticks},{F(m.CpuT)},{F(m.CpuP)},{F(m.GpuT)},{F(m.GpuP)},{F(m.CpuLoad)}\n");
        }
        catch { }
    }

    void LoadHistory()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            long cutoff = DateTime.Now.AddHours(-24).Ticks; var keep = new List<string>();
            float P(string t) => string.IsNullOrEmpty(t) ? float.NaN : float.Parse(t, CultureInfo.InvariantCulture);
            foreach (var line in File.ReadLines(FilePath))
            {
                var a = line.Split(',');
                if (a.Length < 6 || !long.TryParse(a[0], out var t) || t < cutoff) continue;
                minutes.Add(new Sample { Ticks = t, CpuT = P(a[1]), CpuP = P(a[2]), GpuT = P(a[3]), GpuP = P(a[4]), CpuLoad = P(a[5]) }); keep.Add(line);
                if (!float.IsNaN(minutes[^1].CpuT)) EverCpuTemp = true; if (!float.IsNaN(minutes[^1].GpuT)) EverGpuTemp = true;
            }
            File.WriteAllLines(FilePath, keep);   // drops everything older than 24 h
        }
        catch { }
    }

    // ------------- query -------------
    static double Mean(IEnumerable<float> v) { double s = 0; int n = 0; foreach (var x in v) if (!float.IsNaN(x)) { s += x; n++; } return n > 0 ? s / n : double.NaN; }

    public SensorSeries Get(int rangeSec, int maxPoints = 150)
    {
        long to = DateTime.Now.Ticks, from = to - rangeSec * TimeSpan.TicksPerSecond;
        List<Sample> src; double step;
        lock (gate)
        {
            if (rangeSec <= SecCap)
            {
                src = new List<Sample>(secCount); step = 1;
                for (int i = 0; i < secCount; i++) { var s = sec[(secHead - secCount + i + SecCap) % SecCap]; if (s.Ticks >= from) src.Add(s); }
            }
            else
            {
                src = minutes.Where(m => m.Ticks >= from).ToList(); step = 60;
                // the minute that is still being collected
                if (nCT + nCP + nGT + nGP > 0) src.Add(Last);
            }
        }
        var r = new SensorSeries { From = from, To = to };
        if (src.Count == 0) return r;
        int buckets = Math.Min(maxPoints, src.Count);
        r.Ticks = new long[buckets]; r.CpuT = new double[buckets]; r.GpuT = new double[buckets]; r.CpuP = new double[buckets]; r.GpuP = new double[buckets]; r.TotP = new double[buckets];
        for (int b = 0; b < buckets; b++)
        {
            int s0 = (int)((long)src.Count * b / buckets), s1 = Math.Max(s0 + 1, (int)((long)src.Count * (b + 1) / buckets));
            var g = src.Skip(s0).Take(s1 - s0).ToList();
            r.Ticks[b] = (long)g.Average(x => (double)x.Ticks);
            r.CpuT[b] = Mean(g.Select(x => x.CpuT)); r.GpuT[b] = Mean(g.Select(x => x.GpuT));
            r.CpuP[b] = Mean(g.Select(x => x.CpuP)); r.GpuP[b] = Mean(g.Select(x => x.GpuP));
            double tot = 0; bool any = false;
            if (!double.IsNaN(r.CpuP[b])) { tot += r.CpuP[b]; any = true; }
            if (!double.IsNaN(r.GpuP[b])) { tot += r.GpuP[b]; any = true; }
            r.TotP[b] = any ? tot : double.NaN;
        }
        double wsec = 0; foreach (var x in src) { double w = 0; bool any = false; if (!float.IsNaN(x.CpuP)) { w += x.CpuP; any = true; } if (!float.IsNaN(x.GpuP)) { w += x.GpuP; any = true; } if (any) wsec += w * step; }
        r.EnergyWh = wsec / 3600.0; r.CoveredSeconds = src.Count * step;
        return r;
    }
}
