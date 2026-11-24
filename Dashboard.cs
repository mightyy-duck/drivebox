using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;

namespace DriveBox;

public sealed class ChartCanvas : Canvas
{
    public Action<ChartCanvas, double, double>? Draw;
    public bool Animate = true;
    public double BaseDelay;
    DateTime? first;
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(double), typeof(ChartCanvas),
        new PropertyMetadata(0.0, (d, e) => ((ChartCanvas)d).OnValue?.Invoke((double)e.NewValue)));
    public Action<double>? OnValue;
    protected override void OnRenderSizeChanged(SizeChangedInfo i) { base.OnRenderSizeChanged(i); Redraw(); }
    /// <summary>Every entrance animation registers itself here while the chart is drawn, so a hover can replay them on the existing shapes (no rebuild).</summary>
    public readonly List<Action<double>> Replays = new();
    public void Replay() { foreach (var r in Replays.ToArray()) r(0); }
    public void Redraw()
    {
        Children.Clear(); Replays.Clear();
        if (ActualWidth <= 4 || ActualHeight <= 4) return;
        first ??= DateTime.UtcNow;
        Animate = (DateTime.UtcNow - first.Value).TotalMilliseconds < 700;
        Draw?.Invoke(this, ActualWidth, ActualHeight);
    }
}

public static class G
{
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    public static SolidColorBrush Br(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }
    public static Color Col(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    public static LinearGradientBrush Grad(string a, string b, double angle = 90) { var g = new LinearGradientBrush(Col(a), Col(b), angle); g.Freeze(); return g; }
    [ThreadStatic] public static bool? Force;
    public static bool Dark => Force ?? Theme.Dark;
    static readonly Brush InkL = Br("#14151A"), InkD = Br("#F2F4F8"), GrayL = Br("#5E6378"), GrayD = Br("#9CA3B8"), FaintL = Br("#ECEEF4"), FaintD = Br("#2A2D38"),
        T2L = Br("#33374A"), T2D = Br("#CDD1DE"), TrackL = Br("#ECEDF4"), TrackD = Br("#2B2E3A"), EmptyL = Br("#F1F2F7"), EmptyD = Br("#1F222B"), SurfL = Br("#FFFFFF"), SurfD = Br("#14161D");
    public static Brush Ink => Dark ? InkD : InkL;
    public static Brush Gray => Dark ? GrayD : GrayL;
    public static Brush Faint => Dark ? FaintD : FaintL;
    public static Brush Text2 => Dark ? T2D : T2L;
    public static Brush Track => Dark ? TrackD : TrackL;
    public static Brush Empty => Dark ? EmptyD : EmptyL;
    public static Brush Surface => Dark ? SurfD : SurfL;
    public static Brush GreyGrad() => Dark ? Grad("#3E4252", "#2B2E3A") : Grad("#E3E4EB", "#C7C9D6");
    public static readonly Brush Green = Br("#22C55E");
    public static readonly System.Windows.Media.Effects.DropShadowEffect TextShadow = MakeShadow();
    static System.Windows.Media.Effects.DropShadowEffect MakeShadow()
    {
        var e = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, ShadowDepth = 1, BlurRadius = 3, Opacity = 0.85 };
        e.Freeze(); return e;
    }

    public static TimeSpan Ms(double v) => TimeSpan.FromMilliseconds(v);
    /// <summary>Caches the element as a GPU texture while it animates so scale/translate run on the GPU,
    /// then drops the cache so the final picture is crisp.</summary>
    static void GpuWhile(FrameworkElement e, System.Windows.Media.Animation.Timeline last)
    {
        e.CacheMode = new BitmapCache(1.5);
        last.Completed += (_, _) => e.CacheMode = null;
    }
    /// <summary>Bubble pop: scale 0 -> 1 with overshoot, around the element centre. Replayable.</summary>
    public static void Pop(ChartCanvas c, FrameworkElement e, double delayMs, double durMs = 550)
    {
        var st = new ScaleTransform(1, 1); e.RenderTransformOrigin = new Point(0.5, 0.5); e.RenderTransform = st;
        System.Windows.Media.Animation.DoubleAnimation Play(double baseD)
        {
            st.BeginAnimation(ScaleTransform.ScaleXProperty, null); st.BeginAnimation(ScaleTransform.ScaleYProperty, null); st.ScaleX = 0; st.ScaleY = 0;
            var a = new System.Windows.Media.Animation.DoubleAnimation(0, 1, Ms(durMs)) { BeginTime = Ms(baseD + delayMs), EasingFunction = new System.Windows.Media.Animation.BackEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Amplitude = 1.2 } };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a); st.BeginAnimation(ScaleTransform.ScaleYProperty, a); return a;
        }
        c.Replays.Add(b => Play(b));
        if (c.Animate) GpuWhile(e, Play(c.BaseDelay));
    }
    /// <summary>Grow from an edge: vertical = rise from the bottom, otherwise extend from the left. Replayable.</summary>
    public static void Grow(ChartCanvas c, FrameworkElement e, double delayMs, bool vertical, double durMs = 650)
    {
        var st = new ScaleTransform(1, 1); e.RenderTransformOrigin = vertical ? new Point(0.5, 1) : new Point(0, 0.5); e.RenderTransform = st;
        var prop = vertical ? ScaleTransform.ScaleYProperty : ScaleTransform.ScaleXProperty;
        System.Windows.Media.Animation.DoubleAnimation Play(double baseD)
        {
            st.BeginAnimation(prop, null); if (vertical) st.ScaleY = 0; else st.ScaleX = 0;
            var a = new System.Windows.Media.Animation.DoubleAnimation(0, 1, Ms(durMs)) { BeginTime = Ms(baseD + delayMs), EasingFunction = new System.Windows.Media.Animation.ElasticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Oscillations = 1, Springiness = 5 } };
            st.BeginAnimation(prop, a); return a;
        }
        c.Replays.Add(b => Play(b));
        if (c.Animate) GpuWhile(e, Play(c.BaseDelay));
    }
    /// <summary>Drop in from above and bounce on landing. Replayable.</summary>
    public static void Bounce(ChartCanvas c, FrameworkElement e, double delayMs, double fromY = -120)
    {
        var tt = new TranslateTransform(0, 0); e.RenderTransform = tt;
        System.Windows.Media.Animation.DoubleAnimation Play(double baseD)
        {
            tt.BeginAnimation(TranslateTransform.YProperty, null); tt.Y = fromY; e.BeginAnimation(UIElement.OpacityProperty, null); e.Opacity = 0;
            var begin = Ms(baseD + delayMs);
            var drop = new System.Windows.Media.Animation.DoubleAnimation(fromY, 0, Ms(850)) { BeginTime = begin, EasingFunction = new System.Windows.Media.Animation.BounceEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Bounces = 3, Bounciness = 2.2 } };
            tt.BeginAnimation(TranslateTransform.YProperty, drop);
            e.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, Ms(120)) { BeginTime = begin });
            return drop;
        }
        c.Replays.Add(b => Play(b));
        if (c.Animate) GpuWhile(e, Play(c.BaseDelay)); else { tt.Y = 0; e.Opacity = 1; }
    }

    public static Shapes.Rectangle Rect(Canvas c, double x, double y, double w, double h, Brush fill, double r = 0)
    {
        var e = new Shapes.Rectangle { Width = Math.Max(0, w), Height = Math.Max(0, h), Fill = fill, RadiusX = r, RadiusY = r };
        Canvas.SetLeft(e, x); Canvas.SetTop(e, y); c.Children.Add(e); return e;
    }
    public static Shapes.Ellipse Dot(Canvas c, double cx, double cy, double r, Brush fill, Brush? stroke = null, double st = 0)
    {
        var e = new Shapes.Ellipse { Width = r * 2, Height = r * 2, Fill = fill, Stroke = stroke, StrokeThickness = st };
        Canvas.SetLeft(e, cx - r); Canvas.SetTop(e, cy - r); c.Children.Add(e); return e;
    }
    public static Shapes.Line Line(Canvas c, double x1, double y1, double x2, double y2, Brush s, double th, bool dash = false)
    {
        var l = new Shapes.Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = s, StrokeThickness = th, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        if (dash) l.StrokeDashArray = new DoubleCollection { 2, 4 };
        c.Children.Add(l); return l;
    }
    public static TextBlock Text(Canvas c, string s, double x, double y, double size, Brush fg, double width = double.NaN,
        TextAlignment al = TextAlignment.Left, bool mono = false, bool bold = false, bool shadow = false)
    {
        var t = new TextBlock { Text = s, FontSize = size, Foreground = fg, TextAlignment = al, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };
        if (shadow) t.Effect = TextShadow;
        if (!double.IsNaN(width)) t.Width = width;
        if (mono) t.FontFamily = Mono;
        if (bold) t.FontWeight = FontWeights.SemiBold;
        Canvas.SetLeft(t, x); Canvas.SetTop(t, y); c.Children.Add(t); return t;
    }
    public static Border Pill(Canvas c, string s, double cx, double cy, Brush bg, Brush fg, double size = 11)
    {
        var b = new Border { Background = bg, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 2), IsHitTestVisible = false,
            Child = new TextBlock { Text = s, FontFamily = Mono, FontSize = size, Foreground = fg } };
        b.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(b, cx - b.DesiredSize.Width / 2); Canvas.SetTop(b, cy - b.DesiredSize.Height / 2); c.Children.Add(b); return b;
    }
    public static string Size(long b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" }; double v = b; int i = 0;
        while (v >= 1024 && i < 4) { v /= 1024; i++; }
        return (i == 0 ? v.ToString("0") : v.ToString(v >= 100 ? "0" : "0.#", CultureInfo.InvariantCulture)) + " " + u[i];
    }
    public static string N(long n) => n.ToString("N0", CultureInfo.InvariantCulture);
    public static string Cnt(long n) => n >= 1_000_000 ? (n / 1e6).ToString("0.#") + "M" : n >= 1000 ? (n / 1e3).ToString("0.#") + "k" : n.ToString();
    public static string Short(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
    public static Color Lerp(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}

public sealed record Stat(string Label, string Value, string Note = "", string Dot = "#22C55E");

public sealed partial class Dashboard
{
    readonly Stats s; readonly DirNode node; readonly Action<DirNode> nav; readonly Action<string> reveal; readonly long driveUsed, driveTotal;
    static readonly string[][] Palette = {
        new[]{"#FF8AE2","#7B2FF7"}, new[]{"#FFE066","#FF5A00"}, new[]{"#7DF3FF","#3B5BFF"}, new[]{"#9DF7B2","#1FA463"},
        new[]{"#FFB3CB","#E8416F"}, new[]{"#D4C5FF","#7C5CFF"}, new[]{"#FFE9A8","#FF9A3D"}, new[]{"#B9E6FF","#2F8BFF"}};

    public Dashboard(Stats s, DirNode node, long driveUsed, long driveTotal, Action<DirNode> nav, Action<string> reveal)
    { this.s = s; this.node = node; this.driveUsed = driveUsed; this.driveTotal = driveTotal; this.nav = nav; this.reveal = reveal; }

    /// <summary>A full-width heading that groups the cards below it.</summary>
    public static FrameworkElement Section(string title, string sub)
    {
        var g = new Grid { Margin = new Thickness(2, 8, 0, 14) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new Border { Width = 5, CornerRadius = new CornerRadius(3), Background = G.Grad("#8B5CF6", "#FF7A00"), Margin = new Thickness(0, 2, 12, 2) });
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.ExtraBold, Foreground = G.Ink });
        sp.Children.Add(new TextBlock { Text = sub, FontSize = 12, Foreground = G.Gray });
        Grid.SetColumn(sp, 1); g.Children.Add(sp);
        return new Border { Tag = "wide", Child = g, Margin = new Thickness(0, 6, 18, 0) };
    }

    int cardIdx = 1;
    public List<FrameworkElement> Build()
    {
        return new List<FrameworkElement>
        {
            Section("2D charts", "disk map, flow, bars, bubbles, heatmaps, donut and more"),
            DiskMapCard(), StreamWideCard(), SummaryCard(), FoldersCard(), TypesCard(), LargestCard(), HeatCard(), TimelineCard(), DistCard(),
            AgeCard(), YearStackCard(), DonutCard(), SquaresCard(), PurpleStreamCard(), BlueStreamCard(),
            Section("3D charts", "drag to rotate · hover to spin"),
            Bubbles3DCard(), Pins3DCard(), Ridge3DCard()
        };
    }

    // ---------- card shell ----------
    /// <summary>
    /// Card chrome. Once its intro animation is over, the card body is kept as ONE GPU texture (BitmapCache),
    /// so hovering only moves / scales that texture on the compositor: no re-layout, no re-render, no new texture per hover.
    /// The deeper shadow lives outside the cached part so its fade does not invalidate the texture.
    /// </summary>
    static Border Shell(UIElement content, ChartCanvas? replay = null, bool gpuCache = true)
    {
        var g = new Grid();
        g.Children.Add(new Border { CornerRadius = new CornerRadius(26), Margin = new Thickness(-4, 0, -4, -9), Background = G.Br("#0C28285A") });
        g.Children.Add(new Border { CornerRadius = new CornerRadius(24), Margin = new Thickness(-2, 1, -2, -5), Background = G.Br("#1028285A") });
        g.Children.Add(new Border { CornerRadius = new CornerRadius(22), Margin = new Thickness(0, 2, 0, -2), Background = G.Br("#1428285A") });
        g.Children.Add(new Border { Background = G.Surface, CornerRadius = new CornerRadius(22), Child = content, BorderThickness = new Thickness(G.Dark ? 1 : 0), BorderBrush = G.Dark ? G.Br("#262A35") : null });

        var lift = new Border { CornerRadius = new CornerRadius(28), Margin = new Thickness(-6, 4, -6, -16), Background = G.Br("#2228285A"), Opacity = 0, IsHitTestVisible = false };
        var root = new Grid();
        root.Children.Add(lift); root.Children.Add(g);

        if (gpuCache)
        {
            // wait until the entrance animations of the card and its chart have finished, then freeze it into a texture
            var settle = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3300) };
            settle.Tick += (_, _) => { settle.Stop(); g.CacheMode = new BitmapCache { RenderAtScale = 1.0, SnapsToDevicePixels = true, EnableClearType = false }; };
            g.Loaded += (_, _) => settle.Start();
        }

        var sc = new ScaleTransform(1, 1); var tr = new TranslateTransform();
        var tg = new TransformGroup(); tg.Children.Add(sc); tg.Children.Add(tr);
        root.RenderTransform = tg; root.RenderTransformOrigin = new Point(0.5, 0.5);
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        var back = new System.Windows.Media.Animation.BackEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Amplitude = 0.7 };
        void To(double y, double s, double shadow, System.Windows.Media.Animation.IEasingFunction f, int ms)
        {
            var d = TimeSpan.FromMilliseconds(ms);
            tr.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(y, d) { EasingFunction = f });
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(s, d) { EasingFunction = f });
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(s, d) { EasingFunction = f });
            lift.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(shadow, d));
        }
        var lastReplay = DateTime.MinValue;
        g.MouseEnter += (_, _) =>
        {
            To(-8, 1.012, 1, back, 300);
            if (replay != null && (DateTime.UtcNow - lastReplay).TotalSeconds > 1.8) { lastReplay = DateTime.UtcNow; replay.Replay(); }
        };
        g.MouseLeave += (_, _) => To(0, 1, 0, ease, 240);
        return new Border { Margin = new Thickness(0, 0, 18, 18), Child = root };
    }
    static TextBlock Title(string t) => new() { Text = t, FontSize = 21, FontWeight = FontWeights.ExtraBold, Margin = new Thickness(22, 18, 22, 8), Foreground = G.Ink, TextTrimming = TextTrimming.CharacterEllipsis };

    FrameworkElement Card(string title, Action<ChartCanvas, double, double> draw, Stat a, Stat b, (string, string)[] rows, double chartH = 230, bool replayOnHover = true)
    {
        var sp = new StackPanel();
        sp.Children.Add(Title(title));
        var chart = new ChartCanvas { Height = chartH, ClipToBounds = true, Draw = draw, BaseDelay = 250 + Math.Min(cardIdx++, 8) * 90 };
        sp.Children.Add(chart);
        var g = new Grid { Margin = new Thickness(22, 14, 22, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(StatView(a)); var sb = StatView(b); Grid.SetColumn(sb, 1); g.Children.Add(sb);
        sp.Children.Add(g);
        foreach (var (l, v) in rows)
        {
            var r = new Grid { Margin = new Thickness(22, 0, 22, 0), Height = 34 };
            r.Children.Add(new TextBlock { Text = l, Foreground = G.Text2, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 110, 0) });
            r.Children.Add(new TextBlock { Text = v, FontFamily = G.Mono, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = G.Ink, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new Border { BorderBrush = G.Faint, BorderThickness = new Thickness(0, 1, 0, 0), Child = r });
        }
        sp.Children.Add(new Border { Height = 10 });
        return Shell(sp, replayOnHover ? chart : null);
    }
    static FrameworkElement StatView(Stat s)
    {
        var sp = new StackPanel();
        var h = new StackPanel { Orientation = Orientation.Horizontal };
        h.Children.Add(new TextBlock { Text = "● ", Foreground = G.Br(s.Dot), FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
        h.Children.Add(new TextBlock { Text = s.Label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = G.Ink });
        sp.Children.Add(h);
        sp.Children.Add(new TextBlock { Text = s.Value, FontFamily = G.Mono, FontSize = 25, FontWeight = FontWeights.Bold, Foreground = G.Ink, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        sp.Children.Add(new TextBlock { Text = s.Note, FontSize = 12, Foreground = G.Gray, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) });
        return sp;
    }
    static void Clickable(FrameworkElement e, string tip, Action click)
    {
        e.Cursor = System.Windows.Input.Cursors.Hand; e.ToolTip = tip;
        e.MouseLeftButtonUp += (_, _) => click();
        e.MouseEnter += (_, _) => e.Opacity = 0.82; e.MouseLeave += (_, _) => e.Opacity = 1;
    }

    // ---------- DISK MAP (wide) ----------
    FrameworkElement DiskMapCard()
    {
        var sp = new StackPanel();
        double free = driveTotal > 0 ? Math.Max(0, driveTotal - driveUsed) : 0;
        string sub = driveTotal > 0 ? $"{G.Size(node.Size)} in this folder · {G.Size(driveUsed)} used · {G.Size((long)free)} free of {G.Size(driveTotal)}" : $"{G.Size(node.Size)} in this folder";
        var head = new StackPanel { Margin = new Thickness(22, 18, 22, 8) };
        head.Children.Add(new TextBlock { Text = "Disk map — " + node.Path, FontSize = 22, FontWeight = FontWeights.ExtraBold, Foreground = G.Ink, TextTrimming = TextTrimming.CharacterEllipsis });
        head.Children.Add(new TextBlock { Text = sub + "   ·   click any tile to open that folder", FontSize = 12, Foreground = G.Gray, Margin = new Thickness(0, 2, 0, 0) });
        sp.Children.Add(head);
        var map = new ChartCanvas { Height = 330, ClipToBounds = true, Margin = new Thickness(18, 0, 18, 18), Draw = DrawTreemap, BaseDelay = 150 };
        sp.Children.Add(map);
        var shell = Shell(sp, map); shell.Tag = "wide"; return shell;
    }

    void DrawTreemap(ChartCanvas c, double w, double h)
    {
        double barH = 22;
        double whole = driveTotal > 0 ? driveTotal : Math.Max(1, node.Size);
        double x = 0;
        G.Rect(c, 0, 0, w, barH, G.Faint, 8);
        var kids = node.Dirs.Where(d => d.Size > 0).OrderByDescending(d => d.Size).ToList();
        int ci = 0;
        foreach (var d in kids.Take(12))
        {
            double sw = w * d.Size / whole; if (sw < 1) break;
            var r = G.Rect(c, x, 0, sw, barH, G.Grad(Palette[ci % 8][0], Palette[ci % 8][1], 0), 0);
            var dd = d;
            Clickable(r, $"{d.Name}\n{G.Size(d.Size)} · {100.0 * d.Size / whole:0.0}% of partition", () => nav(dd));
            G.Grow(c, r, ci * 60, false);
            x += sw; ci++;
        }
        double rest = node.Size - kids.Take(12).Sum(d => d.Size);
        if (rest > 0) { double sw = w * rest / whole; if (sw >= 1) G.Rect(c, x, 0, sw, barH, G.Br("#C9CCDA")); }

        var items = new List<(object tag, double v, string name, long size, int idx)>();
        int k = 0;
        foreach (var d in kids.Take(40)) items.Add((d, d.Size, d.Name, d.Size, k++));
        if (node.OwnSize > 0) items.Add(("files", node.OwnSize, "(files here)", node.OwnSize, 99));
        long other = kids.Skip(40).Sum(d => d.Size); if (other > 0) items.Add(("other", other, $"+{kids.Count - 40} more", other, 98));
        items = items.OrderByDescending(i => i.v).ToList();
        if (items.Count == 0) { G.Text(c, "This folder is empty.", 0, h / 2, 14, G.Gray, w, TextAlignment.Center); return; }
        var outp = new List<(int i, Rect r)>();
        double ty = barH + 12;
        Squarify(items.Select(i => i.v).ToList(), 0, ty, w, h - ty, outp);
        foreach (var (i, r) in outp)
        {
            var it = items[i]; var pal = it.idx == 99 ? new[] { "#E4E6EE", "#BFC3D4" } : it.idx == 98 ? new[] { "#D8DAE6", "#AEB2C6" } : Palette[it.idx % 8];
            var rr = new Rect(r.X + 1.5, r.Y + 1.5, Math.Max(0, r.Width - 3), Math.Max(0, r.Height - 3));
            // every tile is its own container so tile + text pop together
            var box = new Canvas { Width = rr.Width, Height = rr.Height, Background = null };
            Canvas.SetLeft(box, rr.X); Canvas.SetTop(box, rr.Y); c.Children.Add(box);
            var tile = G.Rect(box, 0, 0, rr.Width, rr.Height, G.Grad(pal[0], pal[1], 45), 10);
            string tip = $"{it.name}\n{G.Size(it.size)} · {100.0 * it.size / whole:0.0}% of {(driveTotal > 0 ? "partition" : "folder")}";
            if (it.tag is DirNode dn) Clickable(tile, tip, () => nav(dn)); else tile.ToolTip = tip;
            bool light = it.idx >= 98; var fg = light ? G.Br("#1E2030") : Brushes.White; bool sh = !light;
            string pct = $"{100.0 * it.size / whole:0.0}%", sz = G.Size(it.size);
            // same text size on every tile; lines that do not fit a small tile are simply left out
            const double pad = 8, fName = 14, fSize = 12, fPct = 12;
            double tw = rr.Width - pad * 1.6;
            if (rr.Width >= 46 && rr.Height >= pad + 20)
            {
                G.Text(box, it.name, pad, pad - 1, fName, fg, tw, bold: true, shadow: sh).FontWeight = FontWeights.ExtraBold;
                if (rr.Height >= pad + 20 + 18) G.Text(box, sz, pad, pad + 20, fSize, fg, tw, mono: true, bold: true, shadow: sh);
                if (rr.Height >= pad + 20 + 18 + 17) G.Text(box, pct, pad, pad + 38, fPct, fg, tw, mono: true, bold: true, shadow: sh);
            }
            G.Pop(c, box, Math.Min(i, 40) * 30);
        }
    }

    static void Squarify(List<double> vals, double x, double y, double w, double h, List<(int, Rect)> o)
    {
        double total = vals.Sum(); if (total <= 0) return;
        double scale = w * h / total; var a = vals.Select(v => v * scale).ToList();
        int start = 0;
        while (start < a.Count)
        {
            double side = Math.Min(w, h); int end = start + 1; double sum = a[start]; double best = Worst(a, start, end, sum, side);
            while (end < a.Count) { double ns = sum + a[end]; double nw = Worst(a, start, end + 1, ns, side); if (nw > best) break; sum = ns; best = nw; end++; }
            if (w >= h) { double cw = sum / h; double yy = y; for (int i = start; i < end; i++) { double ih = a[i] / cw; o.Add((i, new Rect(x, yy, cw, ih))); yy += ih; } x += cw; w -= cw; }
            else { double rh = sum / w; double xx = x; for (int i = start; i < end; i++) { double iw = a[i] / rh; o.Add((i, new Rect(xx, y, iw, rh))); xx += iw; } y += rh; h -= rh; }
            start = end;
        }
    }
    static double Worst(List<double> a, int s, int e, double sum, double side)
    {
        double mx = double.MinValue, mn = double.MaxValue; for (int i = s; i < e; i++) { mx = Math.Max(mx, a[i]); mn = Math.Min(mn, a[i]); }
        return Math.Max(side * side * mx / (sum * sum), sum * sum / (side * side * Math.Max(mn, 1e-9)));
    }

    // ---------- SUMMARY (gauge) ----------
    static Point Pt(double cx, double cy, double r, double deg) => new(cx + r * Math.Cos(deg * Math.PI / 180), cy + r * Math.Sin(deg * Math.PI / 180));
    static PathGeometry ArcGeo(double cx, double cy, double r, double a0, double sweep)
    {
        sweep = Math.Max(sweep, 1.5);
        var fig = new PathFigure { StartPoint = Pt(cx, cy, r, a0) };
        fig.Segments.Add(new ArcSegment(Pt(cx, cy, r, a0 + sweep), new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true));
        var geo = new PathGeometry(); geo.Figures.Add(fig); return geo;
    }
    static Shapes.Path Arc(double cx, double cy, double r, double a0, double sweep, Brush stroke, double th) =>
        new() { Data = ArcGeo(cx, cy, r, a0, sweep), Stroke = stroke, StrokeThickness = th, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    FrameworkElement SummaryCard()
    {
        double whole = driveTotal > 0 ? driveTotal : Math.Max(1, node.Size);
        double frac = Math.Clamp(node.Size / whole, 0, 1);
        return Card(node.Name.Length == 0 ? node.Path : node.Name, (c, w, h) =>
        {
            double cx = w / 2, cy = h / 2 + 22, r = Math.Min(w / 2 - 30, h / 2 + 10);
            c.Children.Add(Arc(cx, cy, r, 150, 240, G.Track, 18));
            var grad = new LinearGradientBrush(G.Col("#8B5CF6"), G.Col("#FF7A00"), 0);
            var fill = Arc(cx, cy, r, 150, 240 * frac, grad, 18); c.Children.Add(fill);
            G.Text(c, driveTotal > 0 ? "OF PARTITION" : "SUMMARY", cx - 60, cy - 46, 11, G.Gray, 120, TextAlignment.Center, bold: true);
            var big = G.Text(c, G.Size(node.Size), cx - 90, cy - 30, 32, G.Ink, 180, TextAlignment.Center, true, true);
            var pctT = driveTotal > 0 ? G.Text(c, $"{frac * 100:0.0}%", cx - 60, cy + 12, 15, G.Gray, 120, TextAlignment.Center, true, true) : null;
            if (frac > 0)
            {
                // sweep + count-up play together
                c.OnValue = v => { fill.Data = ArcGeo(cx, cy, r, 150, 240 * v); big.Text = G.Size((long)(node.Size * v / frac)); if (pctT != null) pctT.Text = $"{v * 100:0.0}%"; };
                void PlayGauge(double b)
                {
                    c.BeginAnimation(ChartCanvas.ValueProperty, null); c.OnValue(0);
                    c.BeginAnimation(ChartCanvas.ValueProperty, new System.Windows.Media.Animation.DoubleAnimation(0, frac, TimeSpan.FromMilliseconds(1300))
                    { BeginTime = TimeSpan.FromMilliseconds(b), EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
                }
                c.Replays.Add(PlayGauge);
                if (c.Animate) PlayGauge(c.BaseDelay);
            }
        },
        new Stat("Files", G.N(s.Files), "in all subfolders", "#22C55E"), new Stat("Folders", G.N(s.Dirs), "in all subfolders", "#F59E0B"),
        new[] { ("Partition used", driveTotal > 0 ? G.Size(driveUsed) : "—"), ("Partition free", driveTotal > 0 ? G.Size(driveTotal - driveUsed) : "—"), ("Files here (direct)", G.N(node.Files.Count)) });
    }

    // ---------- FOLDERS (bars) ----------
    FrameworkElement FoldersCard()
    {
        var items = node.Dirs.Where(d => d.Size > 0).OrderByDescending(d => d.Size).Take(16).ToList();
        var top = items.Take(3).ToList();
        return Card("Folders by size", (c, w, h) =>
        {
            if (items.Count == 0) { G.Text(c, "No subfolders", 0, h / 2 - 8, 14, G.Gray, w, TextAlignment.Center); return; }
            double padL = 18, bottom = 64, top0 = 14; int n = items.Count; double slot = (w - 2 * padL) / n, bw = Math.Min(30, slot * 0.62);
            double max = Math.Max(1, items[0].Size);
            for (int i = 1; i <= 3; i++) G.Line(c, 0, top0 + (h - top0 - bottom) * i / 3.5, w, top0 + (h - top0 - bottom) * i / 3.5, G.Faint, 1, true);
            for (int i = 0; i < n; i++)
            {
                var d = items[i]; double bh = Math.Max(5, (h - top0 - bottom) * d.Size / max);
                double x = padL + slot * i + (slot - bw) / 2, y = h - bottom - bh;
                var r = G.Rect(c, x, y, bw, bh, d.Size >= max * 0.12 ? G.Grad("#FFC21A", "#FF7A00") : G.GreyGrad(), 6);
                Clickable(r, $"{d.Name}\n{G.Size(d.Size)}", () => nav(d));
                G.Grow(c, r, i * 55, true);
                var t = G.Text(c, G.Short(d.Name, 14), x + bw / 2 - 90, h - bottom + 8, 11, G.Ink, 90, TextAlignment.Right, bold: true);
                t.RenderTransformOrigin = new Point(1, 0); t.RenderTransform = new RotateTransform(-40);
            }
        },
        new Stat("Largest", items.Count > 0 ? G.Size(items[0].Size) : "—", items.Count > 0 ? items[0].Name : "", "#22C55E"),
        new Stat("Subfolders", G.N(node.Dirs.Count), "direct children", "#F59E0B"),
        Enumerable.Range(0, 3).Select(i => i < top.Count ? (top[i].Name, G.Size(top[i].Size)) : ("—", "")).ToArray());
    }

    // ---------- FILE TYPES (bubbles) ----------
    FrameworkElement TypesCard()
    {
        var items = s.Ext.OrderByDescending(k => k.Value.Size).Take(8).ToList();
        var top = items.Take(3).ToList();
        return Card("File types", (c, w, h) =>
        {
            if (items.Count == 0) return;
            double maxSize = Math.Max(1, items[0].Value.Size), maxR = Math.Min(h * 0.42, w * 0.26);
            var placed = new List<(double x, double y, double r)>();
            for (int i = 0; i < items.Count; i++)
            {
                double r = Math.Max(14, maxR * Math.Sqrt(items[i].Value.Size / maxSize)); bool ok = false; double bx = 0, by = 0;
                while (!ok && r >= 10)
                {
                    double best = double.MaxValue;
                    for (double x = r + 4; x <= w - r - 4; x += 6) for (double y = r + 4; y <= h - r - 4; y += 6)
                    {
                        bool hit = false; foreach (var p in placed) { double dx = p.x - x, dy = p.y - y; if (Math.Sqrt(dx * dx + dy * dy) < p.r + r + 5) { hit = true; break; } }
                        if (hit) continue; double sc = (x - w / 2) * (x - w / 2) + (y - h / 2) * (y - h / 2); if (sc < best) { best = sc; bx = x; by = y; ok = true; }
                    }
                    if (!ok) r *= 0.85;
                }
                if (!ok) continue; placed.Add((bx, by, r));
                var pal = Palette[i % 8];
                var rg = new RadialGradientBrush { GradientOrigin = new Point(0.3, 0.3), Center = new Point(0.45, 0.45), RadiusX = 0.7, RadiusY = 0.7 };
                rg.GradientStops.Add(new GradientStop(G.Col(pal[0]), 0)); rg.GradientStops.Add(new GradientStop(G.Col(pal[1]), 1));
                var e = G.Dot(c, bx, by, r, rg);
                e.ToolTip = $"{items[i].Key}\n{G.Size(items[i].Value.Size)} · {G.N(items[i].Value.Count)} files";
                G.Pop(c, e, i * 110);
                if (r >= 22) G.Pop(c, G.Pill(c, items[i].Key, bx, by, Brushes.Black, Brushes.White, 12), i * 110 + 120);
            }
        },
        new Stat("Top type", items.Count > 0 ? items[0].Key : "—", items.Count > 0 ? G.Size(items[0].Value.Size) : "", "#22C55E"),
        new Stat("Types", G.N(s.Ext.Count), "distinct extensions", "#F59E0B"),
        Enumerable.Range(0, 3).Select(i => i < top.Count ? (top[i].Key, $"{G.Size(top[i].Value.Size)} · {G.Cnt(top[i].Value.Count)}") : ("—", "")).ToArray());
    }

    // ---------- LARGEST FILES (lollipops) ----------
    FrameworkElement LargestCard()
    {
        var f = s.Largest.Take(7).ToList();
        long total = s.Largest.Sum(x => x.File.Size);
        return Card("Largest files", (c, w, h) =>
        {
            if (f.Count == 0) return; double rowH = (h - 16) / f.Count, x0 = 120, x1 = w - 80; double max = Math.Max(1, f[0].File.Size);
            for (int i = 0; i < f.Count; i++)
            {
                double y = 8 + rowH * i + rowH / 2; double xe = x0 + Math.Max(8, (x1 - x0) * f[i].File.Size / max);
                G.Text(c, G.Short(f[i].File.Name, 18), 12, y - 8, 12, G.Ink, 104, bold: true);
                var ln = G.Line(c, x0, y, xe, y, G.Grad("#7C3AED", "#C084FC", 0), 4);
                G.Grow(c, ln, i * 90, false);
                G.Pop(c, G.Dot(c, x0, y, 5.5, G.Surface, G.Br("#7C3AED"), 2.5), i * 90);
                var d = G.Dot(c, xe, y, 6, G.Surface, G.Br("#A855F7"), 2.5);
                var path = f[i].Dir.Path.TrimEnd('\\') + "\\" + f[i].File.Name;
                Clickable(d, $"{path}\n{G.Size(f[i].File.Size)} (click to show in Explorer)", () => reveal(path));
                G.Pop(c, d, i * 90 + 450);
                G.Pop(c, G.Text(c, G.Size(f[i].File.Size), xe + 12, y - 8, 12, G.Ink, 72, mono: true, bold: true), i * 90 + 500);
            }
        },
        new Stat("Largest", f.Count > 0 ? G.Size(f[0].File.Size) : "—", f.Count > 0 ? f[0].File.Name : "", "#22C55E"),
        new Stat("Top 10 total", G.Size(total), s.Size > 0 ? $"{100.0 * total / s.Size:0.#}% of folder" : "", "#F59E0B"),
        new[] { ("Average file", s.Files > 0 ? G.Size(s.Size / s.Files) : "—"), ("Files over 100 MB", G.N(s.Buckets[6] + s.Buckets[7])), ("Files over 1 GB", G.N(s.Buckets[7])) });
    }

    // ---------- HEATMAP (files modified by month) ----------
    FrameworkElement HeatCard()
    {
        var now = DateTime.Now;
        // the current year and the 4 before it
        var yearList = Enumerable.Range(now.Year - 4, 5).ToList();
        int years = yearList.Count;
        const double rowH = 30, headH = 26;
        int max = 1; foreach (var yr in yearList) for (int m = 0; m < 12; m++) max = Math.Max(max, s.Months.GetValueOrDefault(yr * 12 + m));
        string[] mn = { "J", "F", "M", "A", "M", "J", "J", "A", "S", "O", "N", "D" };
        string[] mfull = CultureInfo.InvariantCulture.DateTimeFormat.MonthNames;
        var busiest = s.Months.OrderByDescending(k => k.Value).FirstOrDefault();
        return Card("Modified by month", (c, w, h) =>
        {
            double left = 44, top = 22, cw = (w - left - 14) / 12, ch = rowH;
            for (int m = 0; m < 12; m++) G.Text(c, mn[m], left + cw * m, 3, 10, G.Gray, cw, TextAlignment.Center, true);
            for (int yi = 0; yi < years; yi++)
            {
                int year = yearList[yi]; double y = top + ch * yi;
                G.Text(c, year.ToString(), 4, y + ch / 2 - 8, 10, G.Gray, mono: true);
                for (int m = 0; m < 12; m++)
                {
                    int cnt = s.Months.GetValueOrDefault(year * 12 + m); bool future = year == now.Year && m + 1 > now.Month;
                    Brush fill;
                    if (future || cnt == 0) fill = G.Empty;
                    else
                    {
                        double t = Math.Log(1 + cnt) / Math.Log(1 + max); Color a = G.Col("#4CC05A"), b = G.Col("#F7C325"), d = G.Col("#FF8A3D"), e = G.Col("#F0524F");
                        fill = new SolidColorBrush(t < 0.4 ? G.Lerp(a, b, t / 0.4) : t < 0.75 ? G.Lerp(b, d, (t - 0.4) / 0.35) : G.Lerp(d, e, (t - 0.75) / 0.25));
                    }
                    var box = new Canvas { Width = cw - 3, Height = ch - 3 }; Canvas.SetLeft(box, left + cw * m + 1.5); Canvas.SetTop(box, y + 1.5); c.Children.Add(box);
                    var r = G.Rect(box, 0, 0, cw - 3, ch - 3, fill, 8); r.ToolTip = $"{mfull[m]} {year}: {G.N(cnt)} files";
                    if (cnt > 0 && !future) { var ct = G.Cnt(cnt); G.Text(box, ct, 0, (ch - 3) / 2 - 8, ct.Length >= 4 ? 9 : 11, Brushes.White, cw - 3, TextAlignment.Center, true, true, true).TextTrimming = TextTrimming.None; }
                    G.Pop(c, box, (yi + m) * 28);
                }
            }
        },
        new Stat("Last 30 days", G.N(s.Last30), "files modified", "#22C55E"), new Stat("Last year", G.N(s.LastYear), "files modified", "#F59E0B"),
        new[] { ("Oldest file", s.MinTicks == long.MaxValue ? "—" : new DateTime(s.MinTicks).ToString("yyyy-MM-dd")), ("Newest file", s.MaxTicks == 0 ? "—" : new DateTime(s.MaxTicks).ToString("yyyy-MM-dd")),
                ("Busiest month", busiest.Value == 0 ? "—" : $"{mfull[busiest.Key % 12][..3]} {busiest.Key / 12} · {G.Cnt(busiest.Value)}") },
            headH + years * rowH + 8);
    }

    // ---------- TIMELINE ----------
    FrameworkElement TimelineCard()
    {
        var now = DateTime.Now; var vals = new List<int>(); var labels = new List<string>();
        for (int i = 23; i >= 0; i--) { var d = now.AddMonths(-i); vals.Add(s.Months.GetValueOrDefault(d.Year * 12 + d.Month - 1)); labels.Add(d.ToString("MMM yy", CultureInfo.InvariantCulture)); }
        int pk = vals.IndexOf(vals.Max());
        return Card("Activity, last 24 months", (c, w, h) =>
        {
            double l = 20, r = 20, t = 44, b = 30; double max = Math.Max(1, vals.Max()) * 1.05;
            var pts = vals.Select((v, i) => new Point(l + (w - l - r) * i / (vals.Count - 1), h - b - (h - t - b) * v / max)).ToList();
            Point P(int i) => pts[Math.Clamp(i, 0, pts.Count - 1)];
            var fig = new PathFigure { StartPoint = pts[0] };
            for (int i = 0; i < pts.Count - 1; i++)
                fig.Segments.Add(new BezierSegment(new Point(P(i).X + (P(i + 1).X - P(i - 1).X) / 6, Math.Clamp(P(i).Y + (P(i + 1).Y - P(i - 1).Y) / 6, t - 10, h - b)),
                    new Point(P(i + 1).X - (P(i + 2).X - P(i).X) / 6, Math.Clamp(P(i + 1).Y - (P(i + 2).Y - P(i).Y) / 6, t - 10, h - b)), P(i + 1), true));
            var line = new PathGeometry(); line.Figures.Add(fig);
            var area = new PathFigure { StartPoint = new Point(pts[0].X, h - b), IsClosed = true }; area.Segments.Add(new LineSegment(pts[0], true));
            foreach (var sg in fig.Segments) area.Segments.Add(sg);
            area.Segments.Add(new LineSegment(new Point(pts[^1].X, h - b), true));
            var ag = new PathGeometry(); ag.Figures.Add(area);
            var fillBr = new LinearGradientBrush(Color.FromArgb(90, 52, 199, 89), Color.FromArgb(0, 52, 199, 89), 90);
            var grp = new Canvas { Width = w, Height = h }; c.Children.Add(grp);
            grp.Children.Add(new Shapes.Path { Data = ag, Fill = fillBr });
            G.Line(c, l, h - b, w - r, h - b, G.Faint, 1);
            grp.Children.Add(new Shapes.Path { Data = line, Stroke = G.Br("#22A94A"), StrokeThickness = 4, StrokeLineJoin = PenLineJoin.Round });
            {   // the curve is drawn left to right
                var clip = new RectangleGeometry(new Rect(0, 0, w, h)); grp.Clip = clip;
                void PlayClip(double b)
                {
                    clip.BeginAnimation(RectangleGeometry.RectProperty, null); clip.Rect = new Rect(0, 0, 0, h);
                    clip.BeginAnimation(RectangleGeometry.RectProperty, new System.Windows.Media.Animation.RectAnimation(new Rect(0, 0, 0, h), new Rect(0, 0, w, h), TimeSpan.FromMilliseconds(1100))
                    { BeginTime = TimeSpan.FromMilliseconds(b), EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut } });
                }
                c.Replays.Add(PlayClip);
                if (c.Animate) PlayClip(c.BaseDelay);
            }
            int k = 0;
            foreach (int i in new[] { pk, vals.Count - 1 }.Distinct())
            {
                G.Pop(c, G.Dot(c, pts[i].X, pts[i].Y, 7, G.Surface, G.Br("#22A94A"), 3.5), 1000 + k * 200);
                G.Pop(c, G.Pill(c, G.N(vals[i]), Math.Clamp(pts[i].X, 30, w - 30), Math.Max(14, pts[i].Y - 24), G.Surface, G.Ink, 13), 1100 + k * 200); k++;
            }
            G.Text(c, labels[0], l - 6, h - 22, 11, G.Gray, mono: true, bold: true); G.Text(c, labels[^1], w - r - 56, h - 22, 11, G.Gray, 62, TextAlignment.Right, true, true);
        },
        new Stat("Peak month", G.N(vals[pk]), labels[pk], "#22C55E"), new Stat("Latest month", G.N(vals[^1]), labels[^1], "#F59E0B"),
        new[] { ("Average per month", G.N((long)vals.Average())), ("Active months", $"{vals.Count(v => v > 0)} / 24"), ("Files in range", G.N(vals.Sum(v => (long)v))) });
    }

    // ---------- SIZE DISTRIBUTION (dot columns) ----------
    FrameworkElement DistCard()
    {
        string[] lab = { "<1K", "<10K", "<100K", "<1M", "<10M", "<100M", "<1G", "1G+" };
        long small = s.Buckets[0] + s.Buckets[1] + s.Buckets[2] + s.Buckets[3], mid = s.Buckets[4] + s.Buckets[5], big = s.Buckets[6] + s.Buckets[7];
        return Card("Size distribution", (c, w, h) =>
        {
            double left = 16, bottom = 28, top = 10, slot = (w - 2 * left) / 8, dia = Math.Min(14, slot * 0.6), step = dia + 3;
            int maxDots = (int)((h - top - bottom) / step); long mx = Math.Max(1, s.Buckets.Max());
            for (int i = 0; i < 8; i++)
            {
                long cnt = s.Buckets[i]; int dots = cnt == 0 ? 0 : Math.Max(1, (int)Math.Round(maxDots * Math.Log(1 + cnt) / Math.Log(1 + mx)));
                double cx = left + slot * i + slot / 2;
                for (int d = 0; d < dots; d++)
                {   // dots fall from the top and bounce into their stack, column by column
                    double t = dots <= 1 ? 0 : (double)d / (dots - 1);
                    var dot = G.Dot(c, cx, h - bottom - dia / 2 - d * step, dia / 2, new SolidColorBrush(G.Lerp(G.Col("#FF7A00"), G.Col("#FFC21A"), t)));
                    G.Bounce(c, dot, i * 110 + d * 45, -(h - bottom - d * step));
                }
                if (dots == 0) G.Dot(c, cx, h - bottom - 3, 2.5, G.Track);
                var hit = G.Rect(c, cx - slot / 2, top, slot, h - top - bottom, Brushes.Transparent); hit.ToolTip = $"{lab[i]}: {G.N(cnt)} files";
                G.Text(c, lab[i], cx - slot / 2, h - 20, 11, G.Ink, slot, TextAlignment.Center, true, true);
            }
        },
        new Stat("Total size", G.Size(s.Size), "all files", "#22C55E"), new Stat("Average file", s.Files > 0 ? G.Size(s.Size / s.Files) : "—", "per file", "#F59E0B"),
        new[] { ("Under 1 MB", G.N(small)), ("1 – 100 MB", G.N(mid)), ("Over 100 MB", G.N(big)) });
    }
}
