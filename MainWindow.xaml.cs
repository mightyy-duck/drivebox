using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PartiChart;

public sealed class Row
{
    public string Icon { get; set; } = "";
    public string Name { get; set; } = "";
    public string SizeText { get; set; } = "";
    public double Pct { get; set; }
    public DirNode? Dir;
    public string Path = "";
}

public partial class MainWindow : Window
{
    DirNode? root, current;
    CancellationTokenSource? cts;
    int navId;
    bool busyList;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(150) };

    public MainWindow()
    {
        Theme.Load();
        var cl = Environment.GetCommandLineArgs();
        if (cl.Contains("--dark")) Theme.Dark = true; else if (cl.Contains("--light")) Theme.Dark = false;
        Theme.Apply();
        InitializeComponent();
        ThemeBtn.Content = Theme.Dark ? "☀ Light" : "🌙 Dark";
        if (cl.Contains("--apps")) Loaded += (_, _) => Apps_Click(this, new RoutedEventArgs());
        SensorService.I.Start();
        BuildSensorPanel();
        Closed += (_, _) => SensorService.I.Stop();
        timer.Tick += (_, _) => LiveTick();
        LoadShortcuts();
        ShowWelcome();
        if (MftScanner.IsElevated) { FastBtn.Content = "⚡ Fast mode ON (reads NTFS directly)"; FastBtn.IsEnabled = false; FastBtn.Background = G.Br("#D9F7E2"); }
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && Directory.Exists(args[1])) Loaded += (_, _) => Open(args[1]);
    }

    void Fast_Click(object s, RoutedEventArgs e)
    {
        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
            if (current != null) psi.ArgumentList.Add(root?.Path ?? current.Path);
            Process.Start(psi); Application.Current.Shutdown();
        }
        catch { /* user declined the UAC prompt */ }
    }

    // ---------- shortcuts ----------
    // ---------- apps & games page ----------
    bool appsMode; List<AppEntry>? appsCache; CancellationTokenSource? appsCts;

    async void Apps_Click(object s, RoutedEventArgs e) => await ShowApps(false);

    async Task ShowApps(bool refresh)
    {
        appsMode = true; navId++;
        UpBtn.IsEnabled = true; Crumbs.Children.Clear();
        Crumbs.Children.Add(new TextBlock { Text = "Apps & games", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = G.Ink });
        if (appsCache == null || refresh)
        {
            appsCts?.Cancel(); appsCts = new CancellationTokenSource(); var tok = appsCts.Token;
            CardsPanel.Children.Clear();
            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            var status = new TextBlock { FontFamily = G.Mono, FontSize = 12, Foreground = G.Gray, Text = "Looking for installed apps and games…", TextTrimming = TextTrimming.CharacterEllipsis };
            var bar = new ProgressBar { Height = 6, Margin = new Thickness(0, 10, 0, 0), IsIndeterminate = true, Foreground = G.Br("#6D4AFF"), Background = G.Br("#D9DCEA"), BorderThickness = new Thickness(0) };
            sp.Children.Add(new TextBlock { Text = "Measuring apps and games", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = G.Ink });
            sp.Children.Add(status); sp.Children.Add(bar);
            CardsPanel.Children.Add(new Border { Background = G.Surface, CornerRadius = new CornerRadius(22), Child = sp, Tag = "wide", Margin = new Thickness(0, 0, 18, 18) });
            Layout();
            var prog = new Progress<(int Done, int Total, string Name)>(p =>
            {
                if (p.Total > 0) { bar.IsIndeterminate = false; bar.Maximum = p.Total; bar.Value = p.Done; }
                status.Text = p.Total > 0 ? $"{p.Done} / {p.Total}   {p.Name}" : p.Name;
            });
            try { appsCache = await Task.Run(() => AppScanner.Collect(prog, tok), tok); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Apps & games"); return; }
        }
        if (!appsMode) return;
        CardsPanel.Children.Clear();
        int ci = 0;
        foreach (var el in new AppsPage(appsCache!, (Style)FindResource("Pill"), p => { try { Process.Start("explorer.exe", $"\"{p}\""); } catch { } }).Build())
        {
            var tt = new TranslateTransform(0, 28); el.RenderTransform = tt; el.Opacity = 0;
            var begin = TimeSpan.FromMilliseconds(ci++ * 90);
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)) { BeginTime = begin });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(450)) { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            CardsPanel.Children.Add(el);
        }
        Layout(); Scroll.ScrollToTop();
    }

    void BuildSensorPanel() => SensorHost.Child = new SensorPanel((Style)FindResource("Pill"), () => Fast_Click(this, new RoutedEventArgs()));

    void SensorBtn_Click(object s, RoutedEventArgs e)
    {
        SensorHost.Visibility = SensorHost.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        Layout();
    }

    void Theme_Click(object s, RoutedEventArgs e)
    {
        Theme.Dark = !Theme.Dark; Theme.Save(); Theme.Apply(); BuildSensorPanel();
        ThemeBtn.Content = Theme.Dark ? "☀ Light" : "🌙 Dark";
        LoadShortcuts(); BuildCrumbs();
        if (appsMode) _ = ShowApps(false); else if (current != null) Navigate(current); else ShowWelcome();
    }

    void LoadShortcuts()
    {
        QuickPanel.Children.Clear(); DrivesPanel.Children.Clear();
        var quick = new (string, string)[] {
            ("Home", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("Downloads", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
            ("AppData", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)) };
        foreach (var (n, p) in quick)
        {
            var b = new Button { Content = n, Style = (Style)FindResource("Pill"), Padding = new Thickness(10, 4, 10, 4), FontSize = 12 };
            var path = p; b.Click += (_, _) => Open(path); QuickPanel.Children.Add(b);
        }
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                long used = d.TotalSize - d.AvailableFreeSpace; double pct = 100.0 * used / d.TotalSize;
                var sp = new Grid();
                sp.ColumnDefinitions.Add(new ColumnDefinition());
                sp.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                sp.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                sp.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                sp.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var nm = new TextBlock { Text = $"{d.Name.TrimEnd('\\')}  {d.VolumeLabel}", FontWeight = FontWeights.Bold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = G.Ink };
                var pc = new TextBlock { Text = $"{pct:0}%", FontFamily = G.Mono, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = pct > 90 ? G.Br("#D92D2A") : G.Ink, Margin = new Thickness(8, 0, 0, 0) };
                Grid.SetColumn(pc, 1);
                var bar = new ProgressBar { Value = pct, Maximum = 100, Height = 10, Margin = new Thickness(0, 6, 0, 5), Foreground = pct > 90 ? G.Br("#F0524F") : G.Br("#7C4DFF"), Background = G.Br("#D9DCEA"), BorderThickness = new Thickness(0) };
                Grid.SetRow(bar, 1); Grid.SetColumnSpan(bar, 2);
                var fr = new TextBlock { Text = $"{G.Size(d.AvailableFreeSpace)} free of {G.Size(d.TotalSize)}", FontSize = 12, Foreground = G.Gray };
                Grid.SetRow(fr, 2); Grid.SetColumnSpan(fr, 2);
                sp.Children.Add(nm); sp.Children.Add(pc); sp.Children.Add(bar); sp.Children.Add(fr);
                var btn = new Button { Content = sp, Style = (Style)FindResource("Pill"), HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(12, 9, 12, 9) };
                var name = d.Name; btn.Click += (_, _) => Open(name); DrivesPanel.Children.Add(btn);
            }
            catch { }
        }
    }

    void ShowWelcome()
    {
        CardsPanel.Children.Clear();
        var sp = new StackPanel { Margin = new Thickness(40) };
        sp.Children.Add(new TextBlock { Text = "Pick a partition or folder", FontSize = 28, FontWeight = FontWeights.Bold, Foreground = G.Ink });
        sp.Children.Add(new TextBlock { Text = "Click a drive on the left (for example C:\\) and PartiChart scans the whole partition once. You then see every folder's usage at the same time in the Disk map, and you can click any tile to drill in.\n\nYou can also paste a path, use Browse, or drag a folder onto this window.",
            FontSize = 14, Foreground = G.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), MaxWidth = 640 });
        CardsPanel.Children.Add(new Border { Background = G.Surface, CornerRadius = new CornerRadius(22), Child = sp, Tag = "wide", Margin = new Thickness(0, 0, 18, 18) });
        Layout();
    }

    // ---------- live scan view ----------
    readonly Stopwatch sw = new();
    StackPanel? liveRows; TextBlock? liveHead, liveSub;
    readonly List<(LiveItem Item, ScaleTransform Scale, Border Bar, TextBlock Size, TextBlock Mark)> liveUi = new();

    void BuildLiveCard(string path)
    {
        CardsPanel.Children.Clear(); liveUi.Clear();
        var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        liveHead = new TextBlock { FontSize = 22, FontWeight = FontWeights.Bold, Foreground = G.Ink, Text = "Scanning " + path };
        liveSub = new TextBlock { FontSize = 12, Foreground = G.Gray, Margin = new Thickness(0, 2, 0, 12), FontFamily = G.Mono };
        liveRows = new StackPanel();
        sp.Children.Add(liveHead); sp.Children.Add(liveSub); sp.Children.Add(liveRows);
        CardsPanel.Children.Add(new Border { Background = G.Surface, CornerRadius = new CornerRadius(22), Child = sp, Tag = "wide", Margin = new Thickness(0, 0, 18, 18) });
        Layout();
    }

    void LiveTick()
    {
        long files = Interlocked.Read(ref Scanner.FileCounter), bytes = Interlocked.Read(ref Scanner.TotalBytes);
        double secs = Math.Max(0.1, sw.Elapsed.TotalSeconds);
        ScanText.Text = $"{G.N(files)} files · {G.Size(bytes)} · {files / secs:N0} files/s   {Scanner.Current}";
        if (liveRows == null) return;
        LiveItem[] snap; lock (Scanner.LiveLock) snap = Scanner.LiveItems.ToArray();
        if (liveSub != null) liveSub.Text = $"{snap.Count(i => i.Done)} / {snap.Length} top-level folders done · {G.N(files)} files · {G.Size(bytes)} · {secs:0.0}s";
        double max = Math.Max(1, snap.Length == 0 ? 1 : snap.Max(i => i.Bytes));
        for (int n = liveUi.Count; n < snap.Length; n++)
        {
            var it = snap[n];
            var g = new Grid { Height = 28, Opacity = 0, RenderTransform = new TranslateTransform(-30, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            g.Children.Add(new TextBlock { Text = it.Name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) });
            var scale = new ScaleTransform(0, 1);
            var bar = new Border { Height = 12, CornerRadius = new CornerRadius(6), Background = G.Grad("#FFC21A", "#FF7A00", 0), RenderTransform = scale, RenderTransformOrigin = new Point(0, 0.5), VerticalAlignment = VerticalAlignment.Center };
            var track = new Border { Height = 12, CornerRadius = new CornerRadius(6), Background = G.Faint, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(track, 1); Grid.SetColumn(bar, 1); g.Children.Add(track); g.Children.Add(bar);
            var size = new TextBlock { FontFamily = G.Mono, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            Grid.SetColumn(size, 2); g.Children.Add(size);
            var mark = new TextBlock { Text = "", Foreground = G.Green, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(mark, 3); g.Children.Add(mark);
            liveRows.Children.Add(g);
            var begin = TimeSpan.FromMilliseconds(Math.Min(n, 30) * 35);
            g.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { BeginTime = begin });
            ((TranslateTransform)g.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-30, 0, TimeSpan.FromMilliseconds(380)) { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            liveUi.Add((it, scale, bar, size, mark));
        }
        foreach (var (it, scale, bar, size, mark) in liveUi)
        {
            double target = Math.Clamp(it.Bytes / max, 0.005, 1);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(250)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            size.Text = G.Size(it.Bytes);
            if (it.Done && mark.Text == "") { mark.Text = "✓"; bar.Background = G.Grad("#7DDC8B", "#22C55E", 0); }
        }
    }

    // ---------- scanning / navigation ----------
    void Open(string path)
    {
        path = path.Trim().Trim('"');
        if (path.Length == 2 && path[1] == ':') path += "\\";
        if (!Directory.Exists(path)) { MessageBox.Show(this, "Folder not found:\n" + path, "PartiChart"); return; }
        path = System.IO.Path.GetFullPath(path);
        if (root != null)
        {
            var n = Find(root, path);
            if (n != null) { Navigate(n); return; }
        }
        StartScan(path);
    }

    static DirNode? Find(DirNode r, string path)
    {
        var rp = r.Path.TrimEnd('\\');
        var p = path.TrimEnd('\\');
        if (string.Equals(rp, p, StringComparison.OrdinalIgnoreCase)) return r;
        if (!p.StartsWith(rp + "\\", StringComparison.OrdinalIgnoreCase)) return null;
        var node = r;
        foreach (var seg in p[(rp.Length + 1)..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            node = node.Dirs.FirstOrDefault(d => string.Equals(d.Name, seg, StringComparison.OrdinalIgnoreCase));
            if (node == null) return null;
        }
        return node;
    }

    async void StartScan(string path)
    {
        appsMode = false; appsCts?.Cancel();
        cts?.Cancel(); cts = new CancellationTokenSource(); var tok = cts.Token;
        ScanBar.Visibility = Visibility.Visible; ScanText.Text = "Scanning… " + path;
        BuildLiveCard(path); timer.Start(); sw.Restart();
        try
        {
            var node = await Task.Run(() => (MftScanner.CanUse(path) ? MftScanner.Scan(path, tok) : null) ?? Scanner.Scan(path, tok), tok);
            if (tok.IsCancellationRequested) return;
            LiveTick();
            root = node; Navigate(node);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Scan failed"); }
        finally { if (cts?.Token == tok) { timer.Stop(); ScanBar.Visibility = Visibility.Collapsed; } }
    }

    async void Navigate(DirNode n)
    {
        appsMode = false; appsCts?.Cancel();
        int id = ++navId; current = n;
        PathBox.Text = n.Path; UpBtn.IsEnabled = n.Parent != null || System.IO.Path.GetDirectoryName(n.Path.TrimEnd('\\')) != null;
        BuildCrumbs(); BuildContents();
        var stats = await Task.Run(() => Stats.Compute(n));
        if (id != navId) return;
        long used = 0, total = 0;
        try { var di = new DriveInfo(n.Path.Substring(0, 1)); if (di.IsReady) { total = di.TotalSize; used = total - di.AvailableFreeSpace; } } catch { }
        var dash = new Dashboard(stats, n, used, total, Navigate, Reveal);
        CardsPanel.Children.Clear(); liveRows = null; liveUi.Clear();
        int ci = 0;
        foreach (var e in dash.Build())
        {
            var tt = new TranslateTransform(0, 28); e.RenderTransform = tt; e.Opacity = 0;
            var begin = TimeSpan.FromMilliseconds(ci++ * 80);
            e.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)) { BeginTime = begin });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(450)) { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            CardsPanel.Children.Add(e);
        }
        Layout();
        Scroll.ScrollToTop();
    }

    void BuildCrumbs()
    {
        Crumbs.Children.Clear();
        var chain = new List<DirNode>(); for (var n = current; n != null; n = n.Parent) chain.Insert(0, n);
        for (int i = 0; i < chain.Count; i++)
        {
            var d = chain[i]; bool last = i == chain.Count - 1;
            var tb = new TextBlock { Text = d.Name, FontSize = 14, FontWeight = last ? FontWeights.Bold : FontWeights.Normal, Foreground = last ? G.Ink : G.Br("#6D4AFF"), Cursor = last ? Cursors.Arrow : Cursors.Hand };
            if (!last) tb.MouseLeftButtonUp += (_, _) => Navigate(d);
            Crumbs.Children.Add(tb);
            if (!last) Crumbs.Children.Add(new TextBlock { Text = "  ›  ", Foreground = G.Gray, FontSize = 14 });
        }
    }

    void BuildContents()
    {
        if (current == null) return;
        var rows = new List<Row>(); double whole = Math.Max(1, current.Size);
        foreach (var d in current.Dirs.OrderByDescending(d => d.Size).Take(400))
            rows.Add(new Row { Icon = "📁", Name = d.Name, SizeText = G.Size(d.Size), Pct = 100 * d.Size / whole, Dir = d, Path = d.Path });
        foreach (var f in current.Files.OrderByDescending(f => f.Size).Take(100))
            rows.Add(new Row { Icon = "📄", Name = f.Name, SizeText = G.Size(f.Size), Pct = 100 * f.Size / whole, Path = System.IO.Path.Combine(current.Path, f.Name) });
        busyList = true; Contents.ItemsSource = rows.OrderByDescending(r => r.Pct).ToList(); busyList = false;
    }

    // ---------- layout ----------
    void Layout()
    {
        // usable width = viewport (without the scrollbar). Every card also carries an 18 px right margin, which is part of its slot.
        double vw = Scroll.ViewportWidth > 50 ? Scroll.ViewportWidth : Scroll.ActualWidth - 17;
        double wide = Math.Floor(vw - 18); if (wide < 200) return;
        int cols = Math.Max(1, (int)(vw / 418)); double cw = Math.Floor((vw - 18 * cols) / cols);
        foreach (FrameworkElement e in CardsPanel.Children)
            e.Width = e.Tag as string == "wide" ? wide : cols == 1 ? wide : cw;
    }
    void Scroll_SizeChanged(object sender, SizeChangedEventArgs e) => Layout();
    void Scroll_ScrollChanged(object sender, ScrollChangedEventArgs e) { if (e.ViewportWidthChange != 0) Layout(); }

    // ---------- actions ----------
    void Reveal(string path) { try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { } }
    void Go_Click(object s, RoutedEventArgs e) => Open(PathBox.Text);
    void PathBox_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) Open(PathBox.Text); }
    void Browse_Click(object s, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder or drive to analyse" };
        if (dlg.ShowDialog(this) == true) Open(dlg.FolderName);
    }
    void OpenExplorer_Click(object s, RoutedEventArgs e) { if (current != null) try { Process.Start("explorer.exe", $"\"{current.Path}\""); } catch { } }
    void Rescan_Click(object s, RoutedEventArgs e) { if (current != null) StartScan(current.Path); }
    void Cancel_Click(object s, RoutedEventArgs e) { cts?.Cancel(); timer.Stop(); ScanBar.Visibility = Visibility.Collapsed; }
    void Up_Click(object s, RoutedEventArgs e)
    {
        if (appsMode)
        {
            appsMode = false; appsCts?.Cancel();
            if (current != null) Navigate(current); else { ShowWelcome(); Crumbs.Children.Clear(); UpBtn.IsEnabled = false; }
            return;
        }
        if (current == null) return;
        if (current.Parent != null) { Navigate(current.Parent); return; }
        var parent = System.IO.Path.GetDirectoryName(current.Path.TrimEnd('\\'));
        if (parent != null) StartScan(parent);
    }
    void Contents_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (busyList || Contents.SelectedItem is not Row r) return;
        if (r.Dir != null) { Navigate(r.Dir); Contents.SelectedItem = null; }
    }
    void Contents_DoubleClick(object s, MouseButtonEventArgs e)
    {
        if (Contents.SelectedItem is Row r && r.Dir == null) Reveal(r.Path);
    }
    void OnDrop(object s, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] f && f.Length > 0)
            Open(Directory.Exists(f[0]) ? f[0] : System.IO.Path.GetDirectoryName(f[0]) ?? f[0]);
    }
}
