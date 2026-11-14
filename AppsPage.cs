using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PartiChart;

/// <summary>The "Apps &amp; games" page: every installed program with the size of itself and the size of its data.</summary>
public sealed class AppsPage
{
    readonly List<AppEntry> all;
    readonly Style pill;
    readonly Action<string> open;
    int kind;      // 0 all, 1 games, 2 apps
    int sort;      // 0 total, 1 program, 2 data
    string query = "";
    readonly StackPanel rows = new();
    readonly TextBlock count = new() { FontSize = 12 };
    readonly List<Button> kindChips = new(), sortChips = new();

    static readonly Brush InstallBrush = G.Grad("#FFC24A", "#FF7A00", 0), DataBrush = G.Grad("#B79CFF", "#7C3AED", 0);

    public AppsPage(List<AppEntry> apps, Style pillStyle, Action<string> openFolder) { all = apps; pill = pillStyle; open = openFolder; }

    static Border Card(UIElement child)
    {
        return new Border { Background = G.Surface, CornerRadius = new CornerRadius(22), Margin = new Thickness(0, 0, 18, 18), Child = child, Tag = "wide",
            BorderThickness = new Thickness(G.Dark ? 1 : 0), BorderBrush = G.Dark ? G.Br("#262A35") : null };
    }

    public List<FrameworkElement> Build()
    {
        long inst = all.Sum(a => a.InstallSize), data = all.Sum(a => a.DataSize), total = inst + data;
        int games = all.Count(a => a.Game);
        var list = new List<FrameworkElement> { Dashboard.Section("Apps & games", "How much space each program takes, and how much more its data takes") };

        // ---------- summary ----------
        var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        var tiles = new Grid();
        for (int i = 0; i < 4; i++) tiles.ColumnDefinitions.Add(new ColumnDefinition());
        void Tile(int i, string label, string value, string note, string dot)
        {
            var t = new StackPanel();
            var h = new StackPanel { Orientation = Orientation.Horizontal };
            h.Children.Add(new TextBlock { Text = "● ", Foreground = G.Br(dot), FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
            h.Children.Add(new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = G.Gray });
            t.Children.Add(h);
            t.Children.Add(new TextBlock { Text = value, FontFamily = G.Mono, FontSize = 30, FontWeight = FontWeights.Bold, Foreground = G.Ink });
            t.Children.Add(new TextBlock { Text = note, FontSize = 12, Foreground = G.Gray });
            Grid.SetColumn(t, i); tiles.Children.Add(t);
        }
        Tile(0, "Total on disk", G.Size(total), "programs + their data", "#22C55E");
        Tile(1, "Programs themselves", G.Size(inst), total > 0 ? $"{100.0 * inst / total:0}% of the total" : "", "#FF8A1F");
        Tile(2, "Their data", G.Size(data), total > 0 ? $"{100.0 * data / total:0}% · saves, caches, settings" : "", "#8B5CF6");
        Tile(3, "Found", $"{all.Count}", $"{games} games · {all.Count - games} apps", "#3B82F6");
        sp.Children.Add(tiles);

        var bar = new Grid { Height = 16, Margin = new Thickness(0, 16, 0, 6) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1, inst), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1, data), GridUnitType.Star) });
        var b1 = new Border { Background = InstallBrush, CornerRadius = new CornerRadius(8, 0, 0, 8), Margin = new Thickness(0, 0, 1, 0), ToolTip = "Programs: " + G.Size(inst) };
        var b2 = new Border { Background = DataBrush, CornerRadius = new CornerRadius(0, 8, 8, 0), Margin = new Thickness(1, 0, 0, 0), ToolTip = "Data: " + G.Size(data) };
        Grid.SetColumn(b2, 1); bar.Children.Add(b1); bar.Children.Add(b2); sp.Children.Add(bar);
        var legend = new StackPanel { Orientation = Orientation.Horizontal };
        void Leg(string color, string text) { legend.Children.Add(new TextBlock { Text = "● ", Foreground = G.Br(color), FontSize = 11 }); legend.Children.Add(new TextBlock { Text = text + "     ", Foreground = G.Gray, FontSize = 12 }); }
        Leg("#FF8A1F", "the program itself (install folder)"); Leg("#8B5CF6", "its data (AppData, ProgramData, Documents, saved games)");
        sp.Children.Add(legend);
        sp.Children.Add(new TextBlock { Text = "Data is matched by folder name, so it is an estimate. Click a row to open its install folder; hover for the data folders found.", Foreground = G.Gray, FontSize = 11, Margin = new Thickness(0, 8, 0, 0) });
        list.Add(Card(sp));

        // ---------- list ----------
        var box = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
        var ctl = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var left = new WrapPanel();
        string[] kinds = { "All", "Games", "Apps" }, sorts = { "Total", "Program", "Data" };
        for (int i = 0; i < kinds.Length; i++) { int idx = i; var c = Chip(kinds[i]); c.Click += (_, _) => { kind = idx; Chips(); Fill(); }; kindChips.Add(c); left.Children.Add(c); }
        left.Children.Add(new TextBlock { Text = "   sort by ", Foreground = G.Gray, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 6) });
        for (int i = 0; i < sorts.Length; i++) { int idx = i; var c = Chip(sorts[i]); c.Click += (_, _) => { sort = idx; Chips(); Fill(); }; sortChips.Add(c); left.Children.Add(c); }
        var search = new TextBox { Width = 230, Padding = new Thickness(8, 6, 8, 6), FontSize = 12, ToolTip = "Filter by name or publisher", HorizontalAlignment = HorizontalAlignment.Right };
        search.SetResourceReference(Control.BackgroundProperty, "InputBg"); search.SetResourceReference(Control.ForegroundProperty, "Txt");
        search.SetResourceReference(Control.BorderBrushProperty, "InputBorder"); search.SetResourceReference(TextBox.CaretBrushProperty, "Txt");
        search.TextChanged += (_, _) => { query = search.Text.Trim(); Fill(); };
        DockPanel.SetDock(search, Dock.Right); ctl.Children.Add(search); ctl.Children.Add(left);
        box.Children.Add(ctl);
        count.Foreground = G.Gray; count.Margin = new Thickness(0, 0, 0, 6); box.Children.Add(count);
        box.Children.Add(HeaderRow());
        box.Children.Add(rows);
        Chips(); Fill();
        list.Add(Card(box));
        return list;
    }

    Button Chip(string text) => new() { Content = text, Style = pill, Padding = new Thickness(13, 5, 13, 5), FontSize = 12, FontWeight = FontWeights.SemiBold };

    void Chips()
    {
        void Paint(List<Button> l, int sel) { for (int i = 0; i < l.Count; i++) { if (i == sel) { l[i].Background = G.Br("#6D4AFF"); l[i].Foreground = Brushes.White; } else { l[i].SetResourceReference(Control.BackgroundProperty, "PillBg"); l[i].SetResourceReference(Control.ForegroundProperty, "PillFg"); } } }
        Paint(kindChips, kind); Paint(sortChips, sort);
    }

    static Grid RowGrid()
    {
        var g = new Grid { Height = 46 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
        for (int i = 0; i < 3; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        return g;
    }

    static FrameworkElement HeaderRow()
    {
        var g = RowGrid(); g.Height = 28;
        void H(int col, string t, string color, bool right)
        {
            var tb = new TextBlock { Text = t, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = color == "" ? G.Gray : G.Br(color), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left };
            Grid.SetColumn(tb, col); g.Children.Add(tb);
        }
        H(1, "Name", "", false); H(2, "Program + data", "", false); H(3, "Program", "#E8730C", true); H(4, "Data", "#7C3AED", true); H(5, "Total", "", true);
        return new Border { BorderBrush = G.Faint, BorderThickness = new Thickness(0, 0, 0, 1), Child = g };
    }

    void Fill()
    {
        IEnumerable<AppEntry> q = all;
        if (kind == 1) q = q.Where(a => a.Game); else if (kind == 2) q = q.Where(a => !a.Game);
        if (query.Length > 0) q = q.Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || a.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase));
        q = sort switch { 1 => q.OrderByDescending(a => a.InstallSize), 2 => q.OrderByDescending(a => a.DataSize), _ => q.OrderByDescending(a => a.Total) };
        var items = q.Take(300).ToList();
        count.Text = $"{items.Count} shown" + (all.Count > items.Count ? $" of {all.Count}" : "");
        rows.Children.Clear();
        long max = Math.Max(1, items.Count > 0 ? items.Max(a => a.Total) : 1);
        int n = 0;
        foreach (var a in items) rows.Children.Add(Row(a, max, n++));
    }

    FrameworkElement Row(AppEntry a, long max, int n)
    {
        var g = RowGrid();
        var icon = new TextBlock { Text = a.Game ? "🎮" : "📦", FontSize = 18, VerticalAlignment = VerticalAlignment.Center }; g.Children.Add(icon);
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        name.Children.Add(new TextBlock { Text = a.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = G.Ink, TextTrimming = TextTrimming.CharacterEllipsis });
        name.Children.Add(new TextBlock { Text = a.Publisher.Length > 0 && !string.Equals(a.Publisher, a.Source, StringComparison.OrdinalIgnoreCase) ? a.Publisher + " · " + a.Source : (a.Publisher.Length > 0 ? a.Publisher : a.Source), FontSize = 11, Foreground = G.Gray, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(name, 1); g.Children.Add(name);

        var bar = new Grid { Height = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        double scale = 1000.0 / max;
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.5, a.InstallSize * scale), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(a.DataSize * scale, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, (max - a.Total) * scale), GridUnitType.Star) });
        var b1 = new Border { Background = InstallBrush, CornerRadius = new CornerRadius(7, a.DataSize > 0 ? 0 : 7, a.DataSize > 0 ? 0 : 7, 7), MinWidth = 3 };
        var b2 = new Border { Background = DataBrush, CornerRadius = new CornerRadius(0, 7, 7, 0), Margin = new Thickness(1, 0, 0, 0) };
        Grid.SetColumn(b2, 1); bar.Children.Add(b1); bar.Children.Add(b2);
        Grid.SetColumn(bar, 2); g.Children.Add(bar);
        if (n < 30)
        {   // the bars grow in, one after another
            var st = new ScaleTransform(0, 1); bar.RenderTransform = st; bar.RenderTransformOrigin = new Point(0, 0.5);
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(650)) { BeginTime = TimeSpan.FromMilliseconds(80 + n * 35), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        void Num(int col, string text, Brush fg, bool strong)
        {
            var tb = new TextBlock { Text = text, FontFamily = G.Mono, FontSize = 13, FontWeight = strong ? FontWeights.Bold : FontWeights.Normal, Foreground = fg, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(tb, col); g.Children.Add(tb);
        }
        Num(3, G.Size(a.InstallSize), G.Text2, false); Num(4, a.DataSize > 0 ? G.Size(a.DataSize) : "—", G.Text2, false); Num(5, G.Size(a.Total), G.Ink, true);

        var tip = new System.Text.StringBuilder(a.Name + "\n" + a.InstallPath + $"\n{G.N(a.InstallFiles)} files");
        if (a.DataFolders.Count > 0) { tip.Append("\n\nData folders (estimate):"); foreach (var (p, s) in a.DataFolders.OrderByDescending(x => x.Size).Take(6)) tip.Append($"\n  {G.Size(s)}  {p}"); }
        var host = new Border { Child = g, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, CornerRadius = new CornerRadius(10), ToolTip = tip.ToString(),
            BorderBrush = G.Faint, BorderThickness = new Thickness(0, 0, 0, 1) };
        host.MouseEnter += (_, _) => host.Background = G.Faint; host.MouseLeave += (_, _) => host.Background = Brushes.Transparent;
        host.MouseLeftButtonUp += (_, _) => open(a.InstallPath);
        return host;
    }
}
