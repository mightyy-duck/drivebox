using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shapes = System.Windows.Shapes;

namespace DriveBox;

/// <summary>Second batch of cards: flow charts, age columns, stacked years, donut, rounded-square bubbles and the dark 3D cards.</summary>
public sealed partial class Dashboard
{
    static readonly string[] MonthNames = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames;

    static void SmoothTo(StreamGeometryContext ctx, IList<Point> p)
    {
        for (int i = 0; i < p.Count - 1; i++)
        {
            Point p0 = p[Math.Max(i - 1, 0)], p1 = p[i], p2 = p[i + 1], p3 = p[Math.Min(i + 2, p.Count - 1)];
            ctx.BezierTo(new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6), new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6), p2, true, true);
        }
    }

    static string Delta(long cur, long prev, out bool up)
    {
        up = cur >= prev;
        if (prev <= 0) return cur > 0 ? "new" : "—";
        double pc = (cur - prev) * 100.0 / prev;
        return (pc >= 0 ? "↑" : "↓") + Math.Abs(pc).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    // =====================================================================
    //  FLOW / STREAM chart (orange, purple, blue)
    // =====================================================================
    void DrawStream(ChartCanvas c, double w, double h, double[][] layers, string[] pal, Func<int, string> topTxt, Func<int, string> botTxt, string glow)
    {
        int n = layers.Length, T = n > 0 ? layers[0].Length : 0;
        if (T < 2) { G.Text(c, "Not enough data yet", 0, h / 2 - 8, 14, G.Gray, w, TextAlignment.Center); return; }
        var total = new double[T]; for (int t = 0; t < T; t++) total[t] = layers.Sum(l => l[t]);
        double maxTot = Math.Max(1, total.Max()), H = h * 0.62, cy = h / 2;
        var idx = Enumerable.Range(0, n).OrderByDescending(k => layers[k][T - 1]).ToList();
        var order = new List<int>();
        for (int i = 0; i < n; i++) { if (i == 0) order.Add(idx[0]); else if (i % 2 == 1) order.Insert(0, idx[i]); else order.Add(idx[i]); }
        int center = order.IndexOf(idx[0]);
        var bnd = new List<Point>[n + 1]; for (int j = 0; j <= n; j++) bnd[j] = new();
        for (int t = 0; t < T; t++)
        {
            double x = w * t / (T - 1), th = Math.Max(2, Math.Sqrt(total[t] / maxTot) * H), y = cy - th / 2; bnd[0].Add(new Point(x, y));
            for (int j = 0; j < n; j++) { y += total[t] > 0 ? layers[order[j]][t] / total[t] * th : th / n; bnd[j + 1].Add(new Point(x, y)); }
        }
        var gc = G.Col(glow);
        var gl = new RadialGradientBrush { Center = new Point(0.12, 0.5), GradientOrigin = new Point(0.12, 0.5), RadiusX = 0.55, RadiusY = 0.7 };
        gl.GradientStops.Add(new GradientStop(Color.FromArgb(G.Dark ? (byte)110 : (byte)80, gc.R, gc.G, gc.B), 0)); gl.GradientStops.Add(new GradientStop(Color.FromArgb(0, gc.R, gc.G, gc.B), 1));
        G.Rect(c, 0, 0, w, h, gl);
        var grp = new Canvas { Width = w, Height = h }; c.Children.Add(grp);
        for (int j = 0; j < n; j++)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(bnd[j][0], true, true); SmoothTo(ctx, bnd[j]);
                var back = new List<Point>(bnd[j + 1]); back.Reverse();
                ctx.LineTo(back[0], false, false); SmoothTo(ctx, back);
            }
            geo.Freeze();
            var col = G.Col(pal[Math.Min(Math.Abs(j - center), pal.Length - 1)]);
            var br = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            br.GradientStops.Add(new GradientStop(Color.FromArgb(70, col.R, col.G, col.B), 0)); br.GradientStops.Add(new GradientStop(Color.FromArgb(240, col.R, col.G, col.B), 0.3)); br.GradientStops.Add(new GradientStop(col, 1));
            grp.Children.Add(new Shapes.Path { Data = geo, Fill = br });
        }
        {
            var clip = new RectangleGeometry(new Rect(0, 0, w, h)); grp.Clip = clip;
            void PlayClip(double b)
            {
                clip.BeginAnimation(RectangleGeometry.RectProperty, null); clip.Rect = new Rect(0, 0, 0, h);
                clip.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation(new Rect(0, 0, 0, h), new Rect(0, 0, w, h), TimeSpan.FromMilliseconds(1300))
                { BeginTime = TimeSpan.FromMilliseconds(b), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
            }
            c.Replays.Add(PlayClip);
            if (c.Animate) PlayClip(c.BaseDelay);
        }
        int k2 = 0;
        foreach (double f in new[] { 0.2, 0.52, 0.84 })
        {
            int t = (int)Math.Round(f * (T - 1)); double x = w * t / (T - 1), th = Math.Max(2, Math.Sqrt(total[t] / maxTot) * H);
            double yt = Math.Max(16, cy - th / 2 - 24), yb = Math.Min(h - 16, cy + th / 2 + 24);
            var ln = G.Line(c, x, yt, x, yb, Color.FromArgb(230, 255, 255, 255) is var lc ? new SolidColorBrush(lc) : Brushes.White, 2);
            G.Grow(c, ln, 700 + k2 * 160, true);
            G.Pop(c, G.Pill(c, topTxt(t), x, yt, G.Surface, G.Ink, 12), 1000 + k2 * 160);
            G.Pop(c, G.Pill(c, botTxt(t), x, yb, G.Surface, G.Ink, 12), 1100 + k2 * 160); k2++;
        }
    }

    double[][]? CumulativeLayers(bool bySize, out int start, out int T)
    {
        int nowKey = DateTime.Now.Year * 12 + DateTime.Now.Month - 1;
        start = 0; T = 0;
        if (s.MonthCat.Count == 0) return null;
        start = Math.Clamp(Math.Min(s.MonthCat.Keys.Min(), nowKey - 11), nowKey - 143, nowKey - 11);
        T = nowKey - start + 1;
        var src = bySize ? s.MonthCat : s.MonthCnt;
        var cats = Enumerable.Range(0, 6).OrderByDescending(k => bySize ? s.CatSize[k] : s.CatCount[k]).Where(k => (bySize ? s.CatSize[k] : s.CatCount[k]) > 0).Take(5).ToList();
        if (cats.Count == 0) return null;
        var layers = new double[cats.Count][];
        for (int i = 0; i < cats.Count; i++)
        {
            layers[i] = new double[T]; int k = cats[i]; double run = 0;
            foreach (var kv in src) if (kv.Key < start) run += kv.Value[k];
            for (int t = 0; t < T; t++) { if (src.TryGetValue(start + t, out var v)) run += v[k]; layers[i][t] = run; }
        }
        return layers;
    }
    string MonthLabel(int key) => $"{MonthNames[key % 12]} {key / 12}";

    // ---------- wide orange card ----------
    FrameworkElement StreamWideCard()
    {
        var layers = CumulativeLayers(true, out int start, out int T);
        var sp = new StackPanel();
        var head = new Grid { Margin = new Thickness(24, 20, 24, 6) };
        head.Children.Add(new TextBlock { Text = "Storage growth", FontSize = 22, FontWeight = FontWeights.ExtraBold, Foreground = G.Ink });
        head.Children.Add(new TextBlock { Text = "✦", FontSize = 18, Foreground = G.Br("#22C55E"), HorizontalAlignment = HorizontalAlignment.Right });
        sp.Children.Add(head);
        var stats = new Grid { Margin = new Thickness(24, 4, 24, 6) };
        for (int i = 0; i < 3; i++) stats.ColumnDefinitions.Add(new ColumnDefinition());
        void Col(int i, string label, long cur, long prev, string cmp)
        {
            var d = Delta(cur, prev, out bool up);
            var col = new StackPanel();
            col.Children.Add(new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = G.Gray });
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new TextBlock { Text = G.N(cur), FontFamily = G.Mono, FontSize = 28, FontWeight = FontWeights.Bold, Foreground = G.Ink });
            line.Children.Add(new TextBlock { Text = "  " + d, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = up ? G.Br("#16A34A") : G.Br("#E5484D"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5) });
            col.Children.Add(line);
            col.Children.Add(new TextBlock { Text = $"{G.N(prev)} {cmp}", FontSize = 12, Foreground = G.Gray });
            Grid.SetColumn(col, i); stats.Children.Add(col);
        }
        Col(0, "Files changed · 7 days", s.Last7, s.Prev7, "the week before");
        Col(1, "Files changed · 30 days", s.Last30, s.Prev30, "the month before");
        Col(2, "Files changed · 1 year", s.LastYear, s.PrevYear, "the year before");
        sp.Children.Add(stats);
        var chart = new ChartCanvas { Height = 270, ClipToBounds = true, BaseDelay = 250, Margin = new Thickness(0, 6, 0, 0) };
        chart.Draw = (c, w, h) =>
        {
            if (layers == null) { G.Text(c, "No files with dates", 0, h / 2 - 8, 14, G.Gray, w, TextAlignment.Center); return; }
            var tot = Enumerable.Range(0, T).Select(t => layers.Sum(l => l[t])).ToArray();
            DrawStream(c, w, h, layers, new[] { "#F25C05", "#FF8A1F", "#FFB27A", "#FFD2AE" }, t => G.Size((long)tot[t]), t => MonthLabel(start + t), "#FF9A3D");
        };
        sp.Children.Add(chart);
        var top = node.Dirs.OrderByDescending(d => d.Size).Take(3).ToList();
        foreach (var d in top)
        {
            var r = new Grid { Margin = new Thickness(24, 0, 24, 0), Height = 36 };
            r.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            for (int i = 0; i < 3; i++) r.ColumnDefinitions.Add(new ColumnDefinition());
            void Cell(int i, string t, bool strong)
            {
                var tb = new TextBlock { Text = t, FontSize = i == 0 ? 15 : 14, FontWeight = strong ? FontWeights.Bold : FontWeights.Normal, Foreground = i == 0 ? G.Text2 : strong ? G.Ink : G.Gray,
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right };
                if (i > 0) tb.FontFamily = G.Mono;
                Grid.SetColumn(tb, i); r.Children.Add(tb);
            }
            Cell(0, d.Name, false); Cell(1, G.Size(d.Size), true); Cell(2, G.N(d.FileCount) + " files", true); Cell(3, (s.Size > 0 ? 100.0 * d.Size / s.Size : 0).ToString("0.0", CultureInfo.InvariantCulture) + "%", false);
            sp.Children.Add(new Border { BorderBrush = G.Faint, BorderThickness = new Thickness(0, 1, 0, 0), Child = r });
        }
        sp.Children.Add(new Border { Height = 12 });
        var shell = Shell(sp, chart); shell.Tag = "wide"; return shell;
    }

    // ---------- purple: files over time (count) ----------
    FrameworkElement PurpleStreamCard()
    {
        var layers = CumulativeLayers(false, out int start, out int T);
        var cats = Enumerable.Range(0, 6).OrderByDescending(k => s.CatCount[k]).ToList();
        return Card("Files over time", (c, w, h) =>
        {
            if (layers == null) { G.Text(c, "No files with dates", 0, h / 2 - 8, 14, G.Gray, w, TextAlignment.Center); return; }
            var tot = Enumerable.Range(0, T).Select(t => layers.Sum(l => l[t])).ToArray();
            DrawStream(c, w, h, layers, new[] { "#5B45D6", "#8F7BEA", "#B9ACF3", "#DDD6FA" }, t => G.Cnt((long)tot[t]), t => MonthLabel(start + t), "#7C5CFF");
        },
        new Stat("Total files", G.N(s.Files), "all subfolders", "#22C55E"), new Stat("Biggest group", Stats.CatNames[cats[0]], G.N(s.CatCount[cats[0]]) + " files", "#F59E0B"),
        cats.Take(3).Select(k => (Stats.CatNames[k], G.N(s.CatCount[k]) + " files")).ToArray());
    }

    // ---------- blue: monthly activity by category ----------
    FrameworkElement BlueStreamCard()
    {
        int nowKey = DateTime.Now.Year * 12 + DateTime.Now.Month - 1; const int N = 8; int st = nowKey - (N - 1);
        var cats = Enumerable.Range(0, 6).OrderByDescending(k => Enumerable.Range(st, N).Sum(m => s.MonthCnt.TryGetValue(m, out var v) ? v[k] : 0)).ToList();
        var use = cats.Where(k => Enumerable.Range(st, N).Sum(m => s.MonthCnt.TryGetValue(m, out var v) ? v[k] : 0) > 0).Take(4).ToList();
        double[][] layers = use.Select(k => Enumerable.Range(st, N).Select(m => s.MonthCnt.TryGetValue(m, out var v) ? (double)v[k] : 0).ToArray()).ToArray();
        long thisMonth = s.MonthCnt.TryGetValue(nowKey, out var cur) ? cur.Sum() : 0;
        return Card("Activity by category", (c, w, h) =>
        {
            if (layers.Length == 0) { G.Text(c, "Nothing modified recently", 0, h / 2 - 8, 14, G.Gray, w, TextAlignment.Center); return; }
            var tot = Enumerable.Range(0, N).Select(t => layers.Sum(l => l[t])).ToArray();
            DrawStream(c, w, h, layers, new[] { "#1769F0", "#2F86FF", "#6FB0FF", "#B5D6FF" }, t => G.Cnt((long)tot[t]), t => MonthNames[(st + t) % 12], "#2F86FF");
        },
        new Stat("This month", G.N(thisMonth), "files modified", "#22C55E"), new Stat("Busiest", use.Count > 0 ? Stats.CatNames[use[0]] : "—", "last 8 months", "#F59E0B"),
        use.Take(3).Select(k => (Stats.CatNames[k], G.N(Enumerable.Range(st, N).Sum(m => s.MonthCnt.TryGetValue(m, out var v) ? v[k] : 0)) + " files")).ToArray());
    }

    // =====================================================================
    //  AGE columns
    // =====================================================================
    FrameworkElement AgeCard()
    {
        string[] lab = { "<1 wk", "<1 mo", "<3 mo", "<1 yr", "<3 yr", "3 yr+" };
        long tot = Math.Max(1, s.AgeCount.Sum()), max = Math.Max(1, s.AgeCount.Max()); int hi = Array.IndexOf(s.AgeCount, s.AgeCount.Max());
        long old1 = s.AgeCount[4] + s.AgeCount[5], old1Size = s.AgeSize[4] + s.AgeSize[5];
        return Card("How old are your files?", (c, w, h) =>
        {
            double padL = 16, padB = 46, padT = 30, slot = (w - 2 * padL) / 6, bw = slot * 0.8;
            for (int i = 0; i < 6; i++)
            {
                double bh = Math.Max(8, (h - padT - padB) * s.AgeCount[i] / max), x = padL + slot * i + (slot - bw) / 2, y = h - padB - bh; bool on = i == hi;
                var fill = on ? G.Grad("#C9A9FF", "#7E3FF2") : G.Dark ? G.Grad("#3A3659", "#292744") : G.Grad("#F1ECFE", "#E1D8FB");
                var r = G.Rect(c, x, y, bw, bh, fill, 10); r.ToolTip = $"{lab[i]}: {G.N(s.AgeCount[i])} files · {G.Size(s.AgeSize[i])}";
                G.Grow(c, r, i * 80, true);
                G.Pop(c, G.Text(c, $"{100.0 * s.AgeCount[i] / tot:0.#}%", x - 4, y - 22, 13, G.Ink, bw + 8, TextAlignment.Center, true, true), 500 + i * 80);
                if (on) G.Pop(c, G.Pill(c, lab[i], x + bw / 2, h - padB + 22, G.Dark ? Brushes.White : Brushes.Black, G.Dark ? Brushes.Black : Brushes.White, 12), 700);
                else G.Text(c, lab[i], x - 4, h - padB + 14, 12, G.Gray, bw + 8, TextAlignment.Center, true);
            }
        },
        new Stat("Untouched 1+ year", G.Cnt(old1), $"{100.0 * old1 / tot:0.#}% of files", "#F59E0B"), new Stat("Changed in 30 days", G.Cnt(s.Last30), $"{100.0 * s.Last30 / tot:0.#}% of files", "#22C55E"),
        new[] { ("Space in old files (1 yr+)", G.Size(old1Size)), ("Space in 3 yr+ files", G.Size(s.AgeSize[5])), ("Most files are", lab[hi]) });
    }

    // =====================================================================
    //  STACKED YEARS with ribbons
    // =====================================================================
    FrameworkElement YearStackCard()
    {
        var yearSize = new Dictionary<int, long[]>();
        foreach (var kv in s.MonthCat) { int y = kv.Key / 12; if (!yearSize.TryGetValue(y, out var a)) yearSize[y] = a = new long[6]; for (int k = 0; k < 6; k++) a[k] += kv.Value[k]; }
        var years = yearSize.Keys.OrderBy(y => y).TakeLast(4).ToList();
        var catOrder = Enumerable.Range(0, 6).OrderByDescending(k => s.CatSize[k]).ToList();
        int bigY = yearSize.Count > 0 ? yearSize.OrderByDescending(kv => kv.Value.Sum()).First().Key : 0;
        return Card("Added per year", (c, w, h) =>
        {
            if (years.Count == 0) return;
            double top = 54, bottom = 38, colW = Math.Min(64, w / (years.Count * 2.2)), maxTot = Math.Max(1, years.Max(y => yearSize[y].Sum()));
            double lx = 14;
            foreach (var k in catOrder.Where(k => s.CatSize[k] > 0).Take(5))
            { G.Rect(c, lx, 7, 9, 9, G.Br(Stats.CatColors[k]), 3); var t = G.Text(c, Stats.CatNames[k], lx + 13, 3, 11, G.Gray); lx += 22 + Stats.CatNames[k].Length * 6.4; }
            double span = w - 2 * 30 - colW, step = years.Count > 1 ? span / (years.Count - 1) : 0;
            var segs = new List<(double x, double[] y0, double[] y1)>();
            for (int yi = 0; yi < years.Count; yi++)
            {
                var a = yearSize[years[yi]]; double tot = a.Sum(), x = 30 + (years.Count > 1 ? step * yi : span / 2), colH = (h - top - bottom) * tot / maxTot, y = h - bottom;
                var y0 = new double[6]; var y1 = new double[6];
                foreach (int k in catOrder)
                {
                    double sh = tot > 0 ? colH * a[k] / tot : 0; y1[k] = y; y -= sh; y0[k] = y;
                    if (sh >= 3)
                    {
                        var col = G.Col(Stats.CatColors[k]);
                        var seg = G.Rect(c, x, y0[k] + 1, colW, sh - 2, new LinearGradientBrush(G.Lerp(col, Colors.White, 0.25), col, 90), 6);
                        seg.ToolTip = $"{years[yi]} · {Stats.CatNames[k]}: {G.Size(a[k])}"; G.Grow(c, seg, yi * 160, true);
                    }
                }
                segs.Add((x, y0, y1));
                G.Pop(c, G.Text(c, years[yi].ToString(), x - 10, y - 22, 13, G.Ink, colW + 20, TextAlignment.Center, true, true), 500 + yi * 160);
                bool last = yi == years.Count - 1;
                G.Pop(c, G.Pill(c, G.Size((long)tot), x + colW / 2, h - bottom + 20, last ? (G.Dark ? Brushes.White : Brushes.Black) : G.Surface, last ? (G.Dark ? Brushes.Black : Brushes.White) : G.Gray, 12), 700 + yi * 160);
            }
            for (int yi = 0; yi < segs.Count - 1; yi++)
                foreach (int k in catOrder)
                {
                    var a = segs[yi]; var b = segs[yi + 1]; if (a.y1[k] - a.y0[k] < 1 && b.y1[k] - b.y0[k] < 1) continue;
                    var geo = new StreamGeometry();
                    using (var ctx = geo.Open()) { ctx.BeginFigure(new Point(a.x + colW, a.y0[k]), true, true); ctx.LineTo(new Point(b.x, b.y0[k]), false, false); ctx.LineTo(new Point(b.x, b.y1[k]), false, false); ctx.LineTo(new Point(a.x + colW, a.y1[k]), false, false); }
                    geo.Freeze(); var col = G.Col(Stats.CatColors[k]);
                    var p = new Shapes.Path { Data = geo, Fill = new SolidColorBrush(Color.FromArgb(46, col.R, col.G, col.B)), IsHitTestVisible = false };
                    c.Children.Insert(0, p);
                    int yy = yi;
                    void PlayRibbon(double b)
                    {
                        p.BeginAnimation(UIElement.OpacityProperty, null); p.Opacity = 0;
                        p.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700)) { BeginTime = TimeSpan.FromMilliseconds(b + 600 + yy * 160) });
                    }
                    c.Replays.Add(PlayRibbon);
                    if (c.Animate) PlayRibbon(c.BaseDelay);
                }
        },
        new Stat("Biggest year", bigY > 0 ? bigY.ToString() : "—", bigY > 0 ? G.Size(yearSize[bigY].Sum()) + " modified" : "", "#22C55E"),
        new Stat("Top category", Stats.CatNames[catOrder[0]], s.Size > 0 ? $"{100.0 * s.CatSize[catOrder[0]] / s.Size:0.#}% of space" : "", "#F59E0B"),
        catOrder.Take(3).Select(k => (Stats.CatNames[k], G.Size(s.CatSize[k]))).ToArray());
    }

    // =====================================================================
    //  DONUT: space by category
    // =====================================================================
    static PathGeometry Ring(double cx, double cy, double r0, double r1, double a0, double a1)
    {
        a1 = Math.Max(a1, a0 + 0.05); bool big = a1 - a0 > 180;
        var fig = new PathFigure { StartPoint = Pt(cx, cy, r1, a0), IsClosed = true };
        fig.Segments.Add(new ArcSegment(Pt(cx, cy, r1, a1), new Size(r1, r1), 0, big, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(Pt(cx, cy, r0, a1), true));
        fig.Segments.Add(new ArcSegment(Pt(cx, cy, r0, a0), new Size(r0, r0), 0, big, SweepDirection.Counterclockwise, true));
        var g = new PathGeometry(); g.Figures.Add(fig); return g;
    }

    FrameworkElement DonutCard()
    {
        var cats = Enumerable.Range(0, 6).Where(k => s.CatSize[k] > 0).OrderByDescending(k => s.CatSize[k]).ToList();
        long tot = Math.Max(1, s.CatSize.Sum());
        return Card("Space by category", (c, w, h) =>
        {
            if (cats.Count == 0) return;
            double r1 = Math.Min(h * 0.46, w * 0.27), r0 = r1 * 0.58, cx = w - r1 - 22, cy = h / 2;
            G.Text(c, "Total used by files", 18, 12, 12, G.Gray);
            G.Text(c, G.Size(tot), 18, 28, 30, G.Ink, w - 2 * r1 - 50, mono: true, bold: true);
            double ly = 78;
            foreach (int k in cats)
            { G.Rect(c, 18, ly + 3, 9, 9, G.Br(Stats.CatColors[k]), 3); G.Text(c, $"{Stats.CatNames[k]}  {100.0 * s.CatSize[k] / tot:0}%", 34, ly - 1, 12, G.Text2, w - 2 * r1 - 70); ly += 22; }
            var paths = new List<(Shapes.Path p, double f0, double f1)>(); double acc = 0; int i = 0;
            foreach (int k in cats)
            {
                double f0 = acc / tot, f1 = (acc + s.CatSize[k]) / tot; acc += s.CatSize[k];
                var col = G.Col(Stats.CatColors[k]);
                Brush fill = i == 0 ? new LinearGradientBrush(G.Lerp(col, Colors.White, 0.3), col, 45) : new SolidColorBrush(Color.FromArgb(G.Dark ? (byte)90 : (byte)70, col.R, col.G, col.B));
                var p = new Shapes.Path { Fill = fill, ToolTip = $"{Stats.CatNames[k]}: {G.Size(s.CatSize[k])} · {s.CatCount[k]:N0} files" };
                c.Children.Add(p); paths.Add((p, f0, f1)); i++;
            }
            void Set(double prog)
            {
                foreach (var (p, f0, f1) in paths)
                {
                    double e = Math.Min(f1, prog); if (e <= f0) { p.Data = Geometry.Empty; continue; }
                    p.Data = Ring(cx, cy, r0, r1, -90 + 360 * f0 + 0.8, -90 + 360 * e - 0.8);
                }
            }
            c.OnValue = Set; Set(1);
            void PlayDonut(double b)
            {
                c.BeginAnimation(ChartCanvas.ValueProperty, null); Set(0);
                c.BeginAnimation(ChartCanvas.ValueProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1200)) { BeginTime = TimeSpan.FromMilliseconds(b), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            c.Replays.Add(PlayDonut);
            if (c.Animate) PlayDonut(c.BaseDelay);
            // labels: pill on the biggest ring segment, plain numbers on the rest
            double accA = 0; int j = 0;
            foreach (int k in cats)
            {
                double mid = -90 + 360 * (accA + s.CatSize[k] / 2.0) / tot; accA += s.CatSize[k]; var pt = Pt(cx, cy, (r0 + r1) / 2, mid);
                string txt = $"{100.0 * s.CatSize[k] / tot:0}";
                if (j == 0) G.Pop(c, G.Pill(c, txt, pt.X, pt.Y, G.Dark ? Brushes.White : Brushes.Black, G.Dark ? Brushes.Black : Brushes.White, 13), 1100);
                else if (s.CatSize[k] * 100 / tot >= 4) G.Pop(c, G.Text(c, txt, pt.X - 14, pt.Y - 8, 12, G.Ink, 28, TextAlignment.Center, true), 1200 + j * 80);
                j++;
            }
        },
        new Stat("Largest", Stats.CatNames[cats[0]], G.Size(s.CatSize[cats[0]]), "#22C55E"), new Stat("Categories", cats.Count.ToString(), "with files", "#F59E0B"),
        cats.Take(3).Select(k => (Stats.CatNames[k], $"{G.Size(s.CatSize[k])} · {G.Cnt(s.CatCount[k])}")).ToArray());
    }

    // =====================================================================
    //  ROUNDED-SQUARE bubbles: folders by file count vs average file size
    // =====================================================================
    FrameworkElement SquaresCard()
    {
        var items = s.Folders.Where(f => f.Dir.FileCount > 0 && f.Dir.Size > 0).Take(12).ToList();
        long maxSize = Math.Max(1, items.Count > 0 ? items.Max(f => f.Dir.Size) : 1);
        double Xv(FolderAgg f) => Math.Log10(f.Dir.FileCount + 1);
        double Yv(FolderAgg f) => Math.Log10(f.Dir.Size / (double)f.Dir.FileCount + 1);
        var byFiles = items.OrderByDescending(f => f.Dir.FileCount).FirstOrDefault(); var byAvg = items.OrderByDescending(f => f.Dir.Size / (double)f.Dir.FileCount).FirstOrDefault();
        return Card("Folders: files vs file size", (c, w, h) =>
        {
            if (items.Count == 0) return;
            double xl = items.Min(Xv), xh = items.Max(Xv), yl = items.Min(Yv), yh = items.Max(Yv); if (xh - xl < 0.3) { xl -= 0.3; xh += 0.3; } if (yh - yl < 0.3) { yl -= 0.3; yh += 0.3; }
            double L = 52, R = 22, T = 40, B = 30, pw = w - L - R, ph = h - T - B;
            double lx = 14;
            for (int i = 0; i < Math.Min(5, items.Count); i++)
            { G.Rect(c, lx, 10, 9, 9, G.Br(Palette[i % 8][1]), 3); G.Text(c, G.Short(items[i].Dir.Name, 12), lx + 13, 5, 11, G.Gray); lx += 24 + Math.Min(12, items[i].Dir.Name.Length) * 6.2; }
            for (int i = 0; i < 4; i++)
            {
                double y = T + ph * i / 3; G.Line(c, L, y, w - R, y, G.Faint, 1, true);
                G.Text(c, G.Size((long)(Math.Pow(10, yh - (yh - yl) * i / 3) - 1)), 4, y - 7, 10, G.Gray, L - 10, TextAlignment.Right, true);
            }
            for (int i = 0; i < 4; i++) G.Text(c, G.Cnt((long)(Math.Pow(10, xl + (xh - xl) * i / 3) - 1)), L + pw * i / 3 - 24, h - B + 8, 10, G.Gray, 48, TextAlignment.Center, true);
            int n = 0;
            foreach (var f in items.OrderByDescending(f => f.Dir.Size))
            {
                int ci = items.IndexOf(f); double sz = 26 + 40 * Math.Sqrt(f.Dir.Size / (double)maxSize);
                double px = L + 0.08 * pw + 0.84 * pw * (Xv(f) - xl) / (xh - xl), py = T + 0.08 * ph + 0.84 * ph * (1 - (Yv(f) - yl) / (yh - yl));
                var box = new Canvas { Width = sz, Height = sz }; Canvas.SetLeft(box, px - sz / 2); Canvas.SetTop(box, py - sz / 2); c.Children.Add(box);
                var pal = Palette[ci % 8]; var sq = G.Rect(box, 0, 0, sz, sz, G.Grad(pal[0], pal[1], 45), sz * 0.28);
                var dn = f.Dir; Clickable(sq, $"{dn.Name}\n{G.N(dn.FileCount)} files · avg {G.Size(dn.Size / dn.FileCount)}\n{G.Size(dn.Size)} total", () => nav(dn));
                if (sz >= 34) G.Text(box, G.Cnt(dn.FileCount), 0, sz / 2 - 8, 12, Brushes.White, sz, TextAlignment.Center, true, true, true);
                G.Pop(c, box, n++ * 70);
            }
        },
        new Stat("Most files", byFiles?.Dir.Name ?? "—", byFiles != null ? G.N(byFiles.Dir.FileCount) + " files" : "", "#22C55E"),
        new Stat("Biggest files", byAvg?.Dir.Name ?? "—", byAvg != null ? "avg " + G.Size((long)(byAvg.Dir.Size / (double)byAvg.Dir.FileCount)) : "", "#F59E0B"),
        items.Take(3).Select(f => (f.Dir.Name, $"{G.Cnt(f.Dir.FileCount)} files · {G.Size(f.Dir.Size)}")).ToArray());
    }

    // =====================================================================
    //  DARK 3D cards (always dark, even in the light theme)
    // =====================================================================
    FrameworkElement DarkCard(string title, Chart3D chart, Stat a, Stat b, (string, string)[] rows)
    {
        G.Force = true;
        try
        {
            var sp = new StackPanel();
            sp.Children.Add(Title(title));
            chart.Delay = 250 + Math.Min(cardIdx++, 8) * 90; sp.Children.Add(chart);
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
            var shell = Shell(sp, null, false);
            return shell;
        }
        finally { G.Force = null; }
    }

    FrameworkElement Bubbles3DCard()
    {
        var top = s.Folders.OrderByDescending(f => f.Dir.Size).Take(3).ToList();
        return DarkCard("3D · folders", Chart3D.Bubbles(s),
            new Stat("Folders plotted", Math.Min(16, s.Folders.Count).ToString(), "size = bubble", "#2EFF8A"), new Stat("Axes", "3D", "files · avg size · age", "#F5B942"),
            Enumerable.Range(0, 3).Select(i => i < top.Count ? (top[i].Dir.Name, G.Size(top[i].Dir.Size)) : ("—", "")).ToArray());
    }
    FrameworkElement Pins3DCard()
    {
        var f = s.Largest.Take(3).ToList();
        return DarkCard("3D · largest files", Chart3D.Pins(s),
            new Stat("Largest", f.Count > 0 ? G.Size(f[0].File.Size) : "—", f.Count > 0 ? f[0].File.Name : "", "#2EFF8A"), new Stat("Top 10", G.Size(s.Largest.Sum(x => x.File.Size)), "height = size", "#F5B942"),
            Enumerable.Range(0, 3).Select(i => i < f.Count ? (f[i].File.Name, G.Size(f[i].File.Size)) : ("—", "")).ToArray());
    }
    FrameworkElement Ridge3DCard()
    {
        var busiest = s.Months.OrderByDescending(k => k.Value).FirstOrDefault();
        var yrs = s.Months.Keys.Select(k => k / 12).Distinct().Count();
        return DarkCard("3D · activity terrain", Chart3D.Ridge(s),
            new Stat("Peak month", busiest.Value > 0 ? G.Cnt(busiest.Value) : "—", busiest.Value > 0 ? MonthLabel(busiest.Key) : "", "#2EFF8A"), new Stat("Active years", yrs.ToString(), "rows = years", "#F5B942"),
            new[] { ("Last 30 days", G.N(s.Last30)), ("Last year", G.N(s.LastYear)), ("All time", G.N(s.Files)) });
    }
}
