using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PartiChart;

/// <summary>
/// Small retained-mode 3D renderer (perspective + rotation) drawn with WPF's GPU-composited DrawingContext.
/// Drag to rotate, hover for a slow spin. Intro / Spin are animated dependency properties that invalidate the render.
/// </summary>
public sealed class Chart3D : FrameworkElement
{
    public static readonly DependencyProperty IntroProperty = DependencyProperty.Register(nameof(Intro), typeof(double), typeof(Chart3D),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SpinProperty = DependencyProperty.Register(nameof(Spin), typeof(double), typeof(Chart3D),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Intro { get => (double)GetValue(IntroProperty); set => SetValue(IntroProperty, value); }
    public double Spin { get => (double)GetValue(SpinProperty); set => SetValue(SpinProperty, value); }
    public double Yaw = -0.62, Pitch = 0.50;
    public Action<Chart3D, DrawingContext, double, double>? Paint;
    public double Delay;

    Point? last;
    public Chart3D() { ClipToBounds = true; Cursor = Cursors.SizeAll; Loaded += (_, _) => PlayIntro(); }

    public void PlayIntro()
    {
        BeginAnimation(IntroProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1500)) { BeginTime = TimeSpan.FromMilliseconds(Delay), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        BeginAnimation(SpinProperty, new DoubleAnimation(-0.9, 0, TimeSpan.FromMilliseconds(1800)) { BeginTime = TimeSpan.FromMilliseconds(Delay), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        BeginAnimation(SpinProperty, new DoubleAnimation(Spin, 0.55, TimeSpan.FromMilliseconds(1400)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        BeginAnimation(SpinProperty, new DoubleAnimation(Spin, 0, TimeSpan.FromMilliseconds(1100)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); last = e.GetPosition(this); CaptureMouse();
        BeginAnimation(SpinProperty, null);   // freeze the auto spin while dragging
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (last == null) return;
        var p = e.GetPosition(this);
        Yaw += (p.X - last.Value.X) * 0.012; Pitch = Math.Clamp(Pitch + (p.Y - last.Value.Y) * 0.008, 0.12, 1.25);
        last = p; InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { base.OnMouseLeftButtonUp(e); last = null; ReleaseMouseCapture(); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));   // hit area for dragging
        if (w > 10 && h > 10) Paint?.Invoke(this, dc, w, h);
    }

    // ---------------- projection ----------------
    public readonly record struct Pr(Point P, double Z, double K);
    public Pr Proj(double x, double y, double z, double w, double h)
    {
        double yaw = Yaw + Spin, cy = Math.Cos(yaw), sy = Math.Sin(yaw), cp = Math.Cos(Pitch), sp = Math.Sin(Pitch);
        double x1 = x * cy + z * sy, z1 = -x * sy + z * cy;
        double y2 = y * cp - z1 * sp, z2 = y * sp + z1 * cp;
        double D = 4.4, k = D / (D - z2), S = Math.Min(w * 0.92, h * 1.05) * 0.30;
        return new Pr(new Point(w / 2 + x1 * k * S, h * 0.47 - y2 * k * S), z2, k * S);
    }

    // ---------------- drawing helpers ----------------
    static readonly Typeface Mono = new(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    public static readonly Brush LabelBrush = Frozen(Color.FromRgb(0x8A, 0x94, 0xA6));
    static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    static readonly Dictionary<Color, Pen> pens = new();
    public static Pen PenOf(Color c, double t) { var key = Color.FromArgb(c.A, c.R, c.G, c.B); lock (pens) { if (!pens.TryGetValue(key, out var p) || Math.Abs(p.Thickness - t) > 0.01) { p = new Pen(Frozen(c), t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; p.Freeze(); pens[key] = p; } return p; } }

    public void Label(DrawingContext dc, string s, Point p, double size, Brush b, TextAlignment al = TextAlignment.Center)
    {
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, size, b, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double x = al == TextAlignment.Center ? p.X - ft.Width / 2 : al == TextAlignment.Right ? p.X - ft.Width : p.X;
        dc.DrawText(ft, new Point(x, p.Y - ft.Height / 2));
    }

    static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    static readonly Dictionary<Color, Brush> spheres = new(), glows = new();
    public static Brush SphereBrush(Color c)
    {
        lock (spheres)
        {
            if (spheres.TryGetValue(c, out var b)) return b;
            var rg = new RadialGradientBrush { GradientOrigin = new Point(0.32, 0.28), Center = new Point(0.5, 0.5), RadiusX = 0.62, RadiusY = 0.62 };
            rg.GradientStops.Add(new GradientStop(Mix(c, Colors.White, 0.7), 0)); rg.GradientStops.Add(new GradientStop(c, 0.45)); rg.GradientStops.Add(new GradientStop(Mix(c, Colors.Black, 0.55), 1));
            rg.Freeze(); return spheres[c] = rg;
        }
    }
    public static Brush GlowBrush(Color c)
    {
        lock (glows)
        {
            if (glows.TryGetValue(c, out var b)) return b;
            var rg = new RadialGradientBrush();
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(120, c.R, c.G, c.B), 0)); rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
            rg.Freeze(); return glows[c] = rg;
        }
    }

    /// <summary>The wire-frame room: floor + the two walls behind the data, with tick labels on the visible edges.</summary>
    public void DrawBox(DrawingContext dc, double w, double h, string[] xl, string[] yl, string[] zl, Color grid)
    {
        Pr P(double x, double y, double z) => Proj(x, y, z, w, h);
        var wallDefs = new (int ax, double sg)[] { (0, -1), (0, 1), (2, -1), (2, 1) };
        var back = wallDefs.OrderBy(d => d.ax == 0 ? P(d.sg, 0, 0).Z : P(0, 0, d.sg).Z).Take(2).ToList();
        var pen = PenOf(Color.FromArgb(150, grid.R, grid.G, grid.B), 0.8);
        var fill = Frozen(Color.FromArgb(40, grid.R, grid.G, grid.B));
        const int n = 4;
        void Face(Func<double, double, Pr> f)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open()) { ctx.BeginFigure(f(-1, -1).P, true, true); ctx.LineTo(f(1, -1).P, false, false); ctx.LineTo(f(1, 1).P, false, false); ctx.LineTo(f(-1, 1).P, false, false); }
            g.Freeze(); dc.DrawGeometry(fill, null, g);
            for (int i = 0; i <= n; i++)
            {
                double t = -1 + 2.0 * i / n;
                dc.DrawLine(pen, f(t, -1).P, f(t, 1).P); dc.DrawLine(pen, f(-1, t).P, f(1, t).P);
            }
        }
        Face((a, b) => P(a, -1, b));                                  // floor
        foreach (var (ax, sg) in back) Face(ax == 0 ? (a, b) => P(sg, b, a) : (a, b) => P(a, b, sg));
        // y labels on the left-most vertical edge
        var edges = new[] { (-1.0, -1.0), (-1.0, 1.0), (1.0, -1.0), (1.0, 1.0) };
        var ye = edges.OrderBy(e => P(e.Item1, 0, e.Item2).P.X).First();
        for (int i = 0; i < yl.Length; i++)
        {
            double t = yl.Length == 1 ? 0 : -1 + 2.0 * i / (yl.Length - 1);
            var p = P(ye.Item1, t, ye.Item2).P; Label(dc, yl[i], new Point(p.X - 7, p.Y), 9, LabelBrush, TextAlignment.Right);
        }
        // x labels along the lower floor edge (z = -1 or 1)
        double zx = P(0, -1, -1).P.Y > P(0, -1, 1).P.Y ? -1 : 1;
        for (int i = 0; i < xl.Length; i++)
        {
            double t = xl.Length == 1 ? 0 : -1 + 2.0 * i / (xl.Length - 1);
            var p = P(t, -1, zx).P; Label(dc, xl[i], new Point(p.X, p.Y + 11), 9, LabelBrush);
        }
        // z labels along the lower side floor edge (x = -1 or 1)
        double xz = P(-1, -1, 0).P.Y > P(1, -1, 0).P.Y ? -1 : 1;
        for (int i = 0; i < zl.Length; i++)
        {
            double t = zl.Length == 1 ? 0 : -1 + 2.0 * i / (zl.Length - 1);
            var p = P(xz, -1, t).P; bool right = P(xz, -1, t).P.X > w / 2;
            Label(dc, zl[i], new Point(p.X + (right ? 8 : -8), p.Y + 6), 9, LabelBrush, right ? TextAlignment.Left : TextAlignment.Right);
        }
    }

    public static double Ease(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);
    static double Norm(double v, double lo, double hi) => hi - lo < 1e-9 ? 0 : (v - lo) / (hi - lo) * 2 - 1;

    // ======================= model 1: spheres (folders) =======================
    static readonly string[] SpherePal = { "#FF9A3D", "#8B5CF6", "#3EA6FF", "#2ED47A", "#F2556B", "#D45BD6", "#F5B942", "#6C7BFF" };
    public static Chart3D Bubbles(Stats s)
    {
        var items = s.Folders.Where(f => f.Dir.Size > 0).Take(16).ToList();
        var now = DateTime.Now.Ticks;
        double X(FolderAgg f) => Math.Log10(f.Dir.FileCount + 1);
        double Y(FolderAgg f) => Math.Log10((f.Dir.FileCount > 0 ? f.Dir.Size / (double)f.Dir.FileCount : 0) + 1);
        double Z(FolderAgg f) => Math.Log10((now - Math.Max(f.NewestTicks, 1)) / TimeSpan.TicksPerDay + 1);
        double xl = items.Count > 0 ? items.Min(X) : 0, xh = items.Count > 0 ? items.Max(X) : 1, yl = items.Count > 0 ? items.Min(Y) : 0, yh = items.Count > 0 ? items.Max(Y) : 1,
               zl = items.Count > 0 ? items.Min(Z) : 0, zh = items.Count > 0 ? items.Max(Z) : 1;
        long maxSize = Math.Max(1, items.Count > 0 ? items.Max(f => f.Dir.Size) : 1);
        string[] Ticks(double lo, double hi, Func<double, string> fmt, int n = 5) => Enumerable.Range(0, n).Select(i => fmt(Math.Pow(10, lo + (hi - lo) * i / (n - 1)) - 1)).ToArray();
        var c3 = new Chart3D { Height = 300 };
        c3.Paint = (c, dc, w, h) =>
        {
            c.DrawBox(dc, w, h, Ticks(xl, xh, v => G.Cnt((long)v)), Ticks(yl, yh, v => G.Size((long)v)), Ticks(zl, zh, v => ((long)v) + "d"), Color.FromRgb(0x4B, 0x5B, 0x78));
            double intro = c.Intro;
            var list = items.Select((f, i) => (f, i, p: c.Proj(Norm(X(f), xl, xh) * 0.8, Norm(Y(f), yl, yh) * 0.8, Norm(Z(f), zl, zh) * 0.8, w, h))).OrderBy(t => t.p.Z).ToList();
            foreach (var (f, i, p) in list)
            {
                double r = (0.07 + 0.17 * Math.Sqrt(f.Dir.Size / (double)maxSize)) * p.K * Ease(intro * 1.4 - i * 0.04);
                if (r < 1) continue;
                var col = G.Col(SpherePal[i % SpherePal.Length]);
                dc.DrawEllipse(GlowBrush(col), null, p.P, r * 1.9, r * 1.9);
                dc.DrawEllipse(SphereBrush(col), null, p.P, r, r);
                if (i < 3 && r > 9) c.Label(dc, G.Short(f.Dir.Name, 12), new Point(p.P.X, p.P.Y - r - 9), 10, Brushes.White);
            }
        };
        return c3;
    }

    // ======================= model 2: glowing pins (largest files) =======================
    public static Chart3D Pins(Stats s)
    {
        var items = s.Largest.Take(10).Select(l => (l.File, l.Dir, Cat: Stats.CatOf(System.IO.Path.GetExtension(l.File.Name).ToLowerInvariant()))).ToList();
        long tMin = items.Count > 0 ? items.Min(i => i.File.Ticks) : 0, tMax = items.Count > 0 ? items.Max(i => i.File.Ticks) : 1;
        long sMax = Math.Max(1, items.Count > 0 ? items.Max(i => i.File.Size) : 1);
        string[] xt = Enumerable.Range(0, 5).Select(i => new DateTime(tMin + (tMax - tMin) * i / 4).ToString("MMM yy", CultureInfo.InvariantCulture)).ToArray();
        string[] yt = Enumerable.Range(0, 5).Select(i => G.Size(sMax * i / 4)).ToArray();
        var c3 = new Chart3D { Height = 300 };
        c3.Paint = (c, dc, w, h) =>
        {
            c.DrawBox(dc, w, h, xt, yt, Stats.CatNames.Select(n => G.Short(n, 6)).ToArray(), Color.FromRgb(0x2E, 0x6B, 0x4F));
            var green = Color.FromRgb(0x2E, 0xFF, 0x8A);
            var list = items.Select((it, i) => (it, i, floor: c.Proj(Norm(it.File.Ticks, tMin, tMax) * 0.8, -1, (it.Cat / 5.0 * 2 - 1) * 0.8, w, h), top: c.Proj(Norm(it.File.Ticks, tMin, tMax) * 0.8,
                -1 + 2 * (it.File.Size / (double)sMax) * Ease(c.Intro * 1.3 - i * 0.05), (it.Cat / 5.0 * 2 - 1) * 0.8, w, h))).OrderBy(t => t.top.Z).ToList();
            foreach (var (it, i, floor, top) in list)
            {
                dc.DrawEllipse(GlowBrush(green), null, floor.P, 9, 4);
                dc.DrawLine(PenOf(Color.FromArgb(34, green.R, green.G, green.B), 8), floor.P, top.P);
                dc.DrawLine(PenOf(Color.FromArgb(90, green.R, green.G, green.B), 3.5), floor.P, top.P);
                dc.DrawLine(PenOf(Color.FromRgb(0x7D, 0xFF, 0xB4), 1.2), floor.P, top.P);
                double r = 5.5 * top.K / 120 + 3.5;
                dc.DrawEllipse(GlowBrush(green), null, top.P, r * 3, r * 3);
                dc.DrawEllipse(SphereBrush(green), null, top.P, r, r);
                if (i == 0) c.Label(dc, G.Size(it.File.Size), new Point(top.P.X, top.P.Y - r - 12), 10, Brushes.White);
            }
        };
        return c3;
    }

    // ======================= model 3: ridge terrain (files modified, year x month) =======================
    public static Chart3D Ridge(Stats s)
    {
        int nowYear = DateTime.Now.Year;
        var years = s.Months.Keys.Select(k => k / 12).Append(nowYear).Distinct().OrderBy(y => y).TakeLast(9).ToList();
        int max = 1; foreach (var y in years) for (int m = 0; m < 12; m++) max = Math.Max(max, s.Months.GetValueOrDefault(y * 12 + m));
        const int sub = 4;
        double[][] rows = years.Select(y =>
        {
            var v = Enumerable.Range(0, 12).Select(m => (double)s.Months.GetValueOrDefault(y * 12 + m) / max).ToArray();
            var o = new double[11 * sub + 1];
            for (int i = 0; i < o.Length; i++)
            {
                double f = (double)i / sub; int a = (int)Math.Floor(f); double t = f - a;
                double p0 = v[Math.Max(a - 1, 0)], p1 = v[a], p2 = v[Math.Min(a + 1, 11)], p3 = v[Math.Min(a + 2, 11)];
                o[i] = Math.Max(0, 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t));
            }
            return o;
        }).ToArray();
        string[] xt = { "Jan", "Mar", "May", "Jul", "Sep", "Nov" }, yt = Enumerable.Range(0, 5).Select(i => G.Cnt((long)(max * i / 4.0))).ToArray();
        string[] zt = years.Count <= 5 ? years.Select(y => y.ToString()).ToArray() : Enumerable.Range(0, 5).Select(i => years[(int)Math.Round((years.Count - 1) * i / 4.0)].ToString()).ToArray();
        Color Ramp(double t) => t < 0.35 ? Mix(Color.FromRgb(0x4B, 0x2B, 0xFF), Color.FromRgb(0x2F, 0x7B, 0xFF), t / 0.35)
                              : t < 0.7 ? Mix(Color.FromRgb(0x2F, 0x7B, 0xFF), Color.FromRgb(0x19, 0xE5, 0xC0), (t - 0.35) / 0.35)
                              : Mix(Color.FromRgb(0x19, 0xE5, 0xC0), Color.FromRgb(0x7B, 0xFF, 0x4A), (t - 0.7) / 0.3);
        var dark = Frozen(Color.FromArgb(235, 6, 8, 12));
        var c3 = new Chart3D { Height = 300 };
        c3.Paint = (c, dc, w, h) =>
        {
            c.DrawBox(dc, w, h, xt, yt, zt, Color.FromRgb(0x2E, 0x6B, 0x4F));
            double rise = Ease(c.Intro * 1.2);
            int n = rows.Length;
            var order = Enumerable.Range(0, n).OrderBy(r => c.Proj(0, -1, n == 1 ? 0 : (r / (double)(n - 1) * 2 - 1) * 0.8, w, h).Z).ToList();
            foreach (int r in order)
            {
                double z = n == 1 ? 0 : (r / (double)(n - 1) * 2 - 1) * 0.8;
                var row = rows[r]; var pts = new Pr[row.Length]; var floorPts = new Pr[row.Length];
                for (int i = 0; i < row.Length; i++)
                {
                    double x = (i / (double)(row.Length - 1) * 2 - 1) * 0.9;
                    pts[i] = c.Proj(x, -1 + 1.7 * row[i] * rise, z, w, h); floorPts[i] = c.Proj(x, -1, z, w, h);
                }
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(floorPts[0].P, true, true);
                    for (int i = 0; i < pts.Length; i++) ctx.LineTo(pts[i].P, false, false);
                    ctx.LineTo(floorPts[^1].P, false, false);
                }
                g.Freeze(); dc.DrawGeometry(dark, null, g);
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    double t = (row[i] + row[i + 1]) / 2;
                    dc.DrawLine(PenOf(Ramp(Math.Pow(t, 0.8)), 1.6), pts[i].P, pts[i + 1].P);
                }
            }
        };
        return c3;
    }
}
