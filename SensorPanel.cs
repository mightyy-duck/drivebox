using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;

namespace DriveBox;

/// <summary>Right-hand panel: live temperature and power with a selectable window (1 / 5 / 30 min, 1 / 12 / 24 h).</summary>
public sealed class SensorPanel : Border
{
    static readonly int[] Ranges = { 60, 300, 1800, 3600, 43200, 86400 };
    static readonly string[] Labels = { "1 min", "5 min", "30 min", "1 hr", "12 hr", "24 hr" };
    static readonly string[] Short = { "1m", "5m", "30m", "1h", "12h", "24h" };
    const string CCpuT = "#FF8A1F", CGpuT = "#22C55E", CCpuP = "#8B5CF6", CGpuP = "#3B82F6";

    int sel = 1;
    SensorSeries ser = new();
    readonly SensorService svc = SensorService.I;
    readonly Style pill;
    readonly Action fastMode;
    readonly List<Button> chips = new();
    readonly ChartCanvas tChart = new() { Height = 150, ClipToBounds = true }, pChart = new() { Height = 150, ClipToBounds = true };
    readonly TextBlock tCpu = Big(CCpuT), tGpu = Big(CGpuT), pCpu = Big(CCpuP), pGpu = Big(CGpuP), pTot = Big(null);
    readonly TextBlock tStats = Small(), pStats = Small(), sub = Small(), note = Small();
    readonly Button fastBtn;

    static TextBlock Big(string? color) => new() { FontFamily = G.Mono, FontSize = 21, FontWeight = FontWeights.Bold, Foreground = color == null ? G.Ink : G.Br(color) };
    static TextBlock Small() => new() { FontSize = 12, Foreground = G.Gray, TextWrapping = TextWrapping.Wrap };

    public SensorPanel(Style pillStyle, Action fastModeClick)
    {
        pill = pillStyle; fastMode = fastModeClick;
        CornerRadius = new CornerRadius(22); Padding = new Thickness(14);
        SetResourceReference(BackgroundProperty, "SidebarBg");

        var root = new StackPanel();
        var head = new Grid();
        head.Children.Add(new TextBlock { Text = "Live sensors", FontSize = 22, FontWeight = FontWeights.ExtraBold, Foreground = G.Ink });
        head.Children.Add(new TextBlock { Text = "●", Foreground = G.Green, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, ToolTip = "updates every second" });
        root.Children.Add(head);
        sub.Margin = new Thickness(0, 2, 0, 10); root.Children.Add(sub);

        var chipRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        for (int i = 0; i < Ranges.Length; i++)
        {
            int idx = i;
            var b = new Button { Content = Short[i], Style = pill, Padding = new Thickness(11, 5, 11, 5), FontWeight = FontWeights.SemiBold, FontSize = 12, ToolTip = "Last " + Labels[i] };
            b.Click += (_, _) => { sel = idx; Style_Chips(); Refresh(); };
            chips.Add(b); chipRow.Children.Add(b);
        }
        root.Children.Add(chipRow);
        Style_Chips();

        note.Margin = new Thickness(0, 0, 0, 6); root.Children.Add(note);
        fastBtn = new Button { Content = "⚡ Enable CPU sensors (restart as admin)", Style = pill, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
        fastBtn.Click += (_, _) => fastMode();
        root.Children.Add(fastBtn);

        root.Children.Add(MakeCard("Temperature", new[] { ("CPU", tCpu, CCpuT), ("GPU", tGpu, CGpuT) }, tChart, tStats));
        root.Children.Add(MakeCard("Power", new[] { ("CPU", pCpu, CCpuP), ("GPU", pGpu, CGpuP), ("Total", pTot, "#8A8FA3") }, pChart, pStats));

        Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };

        tChart.Draw = (c, w, h) => DrawChart(c, w, h, false, new[] { (ser.CpuT, CCpuT, "CPU"), (ser.GpuT, CGpuT, "GPU") }, "°C");
        pChart.Draw = (c, w, h) => DrawChart(c, w, h, true, new[] { (ser.CpuP, CCpuP, "CPU"), (ser.GpuP, CGpuP, "GPU"), (ser.TotP, "#8A8FA3", "Total") }, "W");
        HookTip(tChart, () => new[] { (ser.CpuT, "CPU", "°C"), (ser.GpuT, "GPU", "°C") });
        HookTip(pChart, () => new[] { (ser.CpuP, "CPU", "W"), (ser.GpuP, "GPU", "W"), (ser.TotP, "Total", "W") });

        svc.Updated += OnUpdated;
        Unloaded += (_, _) => svc.Updated -= OnUpdated;
        Refresh();
    }

    void OnUpdated() => Dispatcher.BeginInvoke(new Action(Refresh), System.Windows.Threading.DispatcherPriority.Background);

    void Style_Chips()
    {
        for (int i = 0; i < chips.Count; i++)
        {
            if (i == sel) { chips[i].Background = G.Br("#6D4AFF"); chips[i].Foreground = Brushes.White; }
            else { chips[i].SetResourceReference(Control.BackgroundProperty, "PillBg"); chips[i].SetResourceReference(Control.ForegroundProperty, "PillFg"); }
        }
    }

    FrameworkElement MakeCard(string title, (string Label, TextBlock Value, string Color)[] reads, ChartCanvas chart, TextBlock stats)
    {
        var sp = new StackPanel { Margin = new Thickness(16, 14, 16, 12) };
        sp.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.ExtraBold, Foreground = G.Ink });
        var g = new Grid { Margin = new Thickness(0, 8, 0, 4) };
        for (int i = 0; i < reads.Length; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var col = new StackPanel();
            var h = new StackPanel { Orientation = Orientation.Horizontal };
            h.Children.Add(new TextBlock { Text = "● ", FontSize = 10, Foreground = G.Br(reads[i].Color), VerticalAlignment = VerticalAlignment.Center });
            h.Children.Add(new TextBlock { Text = reads[i].Label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = G.Ink });
            col.Children.Add(h); col.Children.Add(reads[i].Value);
            Grid.SetColumn(col, i); g.Children.Add(col);
        }
        sp.Children.Add(g); sp.Children.Add(chart);
        stats.Margin = new Thickness(0, 6, 0, 0); sp.Children.Add(stats);
        return new Border { Background = G.Surface, CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 6, 0, 8), Child = sp,
            BorderThickness = new Thickness(G.Dark ? 1 : 0), BorderBrush = G.Dark ? G.Br("#262A35") : null };
    }

    static string F(double v, string u, string fmt = "0") => double.IsNaN(v) || float.IsNaN((float)v) ? "—" : v.ToString(fmt, CultureInfo.InvariantCulture) + u;
    static (double avg, double max) AvgMax(double[] a) { var v = a.Where(x => !double.IsNaN(x)).ToList(); return v.Count == 0 ? (double.NaN, double.NaN) : (v.Average(), v.Max()); }

    void Refresh()
    {
        ser = svc.Get(Ranges[sel]);
        var l = svc.Last;
        tCpu.Text = F(l.CpuT, "°C"); tGpu.Text = F(l.GpuT, "°C");
        pCpu.Text = F(l.CpuP, " W", "0.0"); pGpu.Text = F(l.GpuP, " W", "0.0");
        double tot = double.NaN; if (!float.IsNaN(l.CpuP)) tot = l.CpuP; if (!float.IsNaN(l.GpuP)) tot = (double.IsNaN(tot) ? 0 : tot) + l.GpuP;
        pTot.Text = F(tot, " W", "0.0");

        var (ca, cm) = AvgMax(ser.CpuT); var (ga, gm) = AvgMax(ser.GpuT);
        tStats.Text = $"{Labels[sel]}  ·  CPU avg {F(ca, "°", "0.#")} max {F(cm, "°")}  ·  GPU avg {F(ga, "°", "0.#")} max {F(gm, "°")}";
        var (pa, pm) = AvgMax(ser.TotP);
        string energy = ser.EnergyWh >= 1000 ? (ser.EnergyWh / 1000).ToString("0.00", CultureInfo.InvariantCulture) + " kWh" : ser.EnergyWh.ToString("0.0", CultureInfo.InvariantCulture) + " Wh";
        pStats.Text = $"{Labels[sel]}  ·  avg {F(pa, " W", "0.#")}  ·  peak {F(pm, " W")}  ·  energy {(ser.N == 0 ? "—" : energy)}";

        string gpu = string.IsNullOrEmpty(svc.GpuName) ? "GPU" : svc.GpuName;
        sub.Text = svc.Status == "running" ? gpu + (string.IsNullOrEmpty(svc.CpuName) ? "" : "  ·  " + svc.CpuName) : svc.Status;

        bool cpuMissing = !svc.EverCpuTemp && !svc.EverCpuPower;
        if (cpuMissing && !svc.Elevated) { note.Text = "GPU is live. CPU temperature and power are only exposed to administrators."; fastBtn.Visibility = Visibility.Visible; }
        else if (cpuMissing) { note.Text = "This CPU / driver does not report temperature or power."; fastBtn.Visibility = Visibility.Collapsed; }
        else { note.Text = ""; fastBtn.Visibility = Visibility.Collapsed; }
        note.Visibility = note.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        tChart.Redraw(); pChart.Redraw();
    }

    // ---------------- line chart ----------------
    void DrawChart(ChartCanvas c, double w, double h, bool zeroBase, (double[] V, string Color, string Name)[] lines, string unit)
    {
        double L = 38, R = 8, T = 8, B = 20, pw = w - L - R, ph = h - T - B;
        if (ser.N < 2) { G.Text(c, "collecting data…", 0, h / 2 - 8, 13, G.Gray, w, TextAlignment.Center); return; }
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (var (v, _, _) in lines) foreach (var x in v) if (!double.IsNaN(x)) { lo = Math.Min(lo, x); hi = Math.Max(hi, x); }
        if (lo == double.MaxValue) { G.Text(c, "no sensor data", 0, h / 2 - 8, 13, G.Gray, w, TextAlignment.Center); return; }
        if (zeroBase) { lo = 0; hi = Math.Max(10, Math.Ceiling(hi * 1.15 / 5) * 5); }
        else { lo = Math.Floor((lo - 4) / 5) * 5; hi = Math.Ceiling((hi + 4) / 5) * 5; if (hi - lo < 20) { double mid = (hi + lo) / 2; lo = mid - 10; hi = mid + 10; } }
        for (int i = 0; i <= 3; i++)
        {
            double y = T + ph * i / 3, val = hi - (hi - lo) * i / 3;
            G.Line(c, L, y, w - R, y, G.Faint, 1, true);
            G.Text(c, val.ToString("0", CultureInfo.InvariantCulture), 0, y - 7, 10, G.Gray, L - 6, TextAlignment.Right, true);
        }
        double span = Math.Max(1, ser.To - ser.From);
        string left = "-" + Labels[sel].Replace(" ", ""), right = "now";
        G.Text(c, left, L - 4, h - 16, 10, G.Gray, 70, mono: true); G.Text(c, right, w - R - 40, h - 16, 10, G.Gray, 40, TextAlignment.Right, true);
        double X(int i) => L + pw * (ser.Ticks[i] - ser.From) / span;
        double Y(double v) => T + ph * (1 - (v - lo) / (hi - lo));
        for (int k = 0; k < lines.Length; k++)
        {
            var (vals, color, _) = lines[k]; var col = G.Col(color);
            var geo = new StreamGeometry(); var area = new StreamGeometry();
            bool open = false; double firstX = 0, lastX = 0;
            using (var g = geo.Open())
            using (var a = area.Open())
            {
                for (int i = 0; i < ser.N; i++)
                {
                    if (double.IsNaN(vals[i])) { if (open) { a.LineTo(new Point(lastX, T + ph), false, false); open = false; } continue; }
                    var p = new Point(X(i), Y(vals[i]));
                    if (!open) { g.BeginFigure(p, false, false); a.BeginFigure(new Point(p.X, T + ph), true, true); a.LineTo(p, false, false); firstX = p.X; open = true; }
                    else { g.LineTo(p, true, true); a.LineTo(p, false, false); }
                    lastX = p.X;
                }
                if (open) a.LineTo(new Point(lastX, T + ph), false, false);
            }
            geo.Freeze(); area.Freeze();
            if (k < 2) c.Children.Add(new Shapes.Path { Data = area, Fill = new LinearGradientBrush(Color.FromArgb(70, col.R, col.G, col.B), Color.FromArgb(0, col.R, col.G, col.B), 90), IsHitTestVisible = false });
            c.Children.Add(new Shapes.Path { Data = geo, Stroke = new SolidColorBrush(col), StrokeThickness = k == 2 ? 1.6 : 2.2, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false,
                StrokeDashArray = k == 2 ? new DoubleCollection { 3, 3 } : null });
        }
    }

    void HookTip(ChartCanvas c, Func<(double[] V, string Name, string Unit)[]> get)
    {
        ToolTipService.SetInitialShowDelay(c, 0); ToolTipService.SetShowDuration(c, 60000);
        c.MouseMove += (_, e) =>
        {
            if (ser.N < 2) return;
            double x = e.GetPosition(c).X, L = 38, R = 8, pw = c.ActualWidth - L - R; double span = Math.Max(1, ser.To - ser.From);
            long t = ser.From + (long)((x - L) / pw * span); int best = 0; long bd = long.MaxValue;
            for (int i = 0; i < ser.N; i++) { long d = Math.Abs(ser.Ticks[i] - t); if (d < bd) { bd = d; best = i; } }
            var sb = new System.Text.StringBuilder(new DateTime(ser.Ticks[best]).ToString(sel >= 4 ? "ddd HH:mm" : "HH:mm:ss", CultureInfo.InvariantCulture));
            foreach (var (v, n, u) in get()) sb.Append($"\n{n}  {F(v[best], " " + u, "0.#")}");
            c.ToolTip = sb.ToString();
        };
    }
}
