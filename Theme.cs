using System.Windows;
using System.Windows.Media;

namespace DriveBox;

/// <summary>Light / dark theme: drives the window resources (XAML uses DynamicResource) and G.* brushes.</summary>
public static class Theme
{
    public static bool Dark;
    static string SettingsFile => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DriveBox", "theme.txt");

    public static void Load()
    {
        try { Dark = File.Exists(SettingsFile) && File.ReadAllText(SettingsFile).Trim() == "dark"; } catch { }
    }
    public static void Save()
    {
        try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsFile)!); File.WriteAllText(SettingsFile, Dark ? "dark" : "light"); } catch { }
    }

    static Brush B(string hex) { var b = (Brush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }
    static Brush Grad(string a, string b)
    {
        var g = new LinearGradientBrush(G.Col(a), G.Col(b), new Point(0, 0), new Point(1, 1)); g.Freeze(); return g;
    }

    public static void Apply()
    {
        var r = Application.Current.Resources;
        if (Dark)
        {
            r["WinBg"] = Grad("#090B10", "#151926");
            r["SidebarBg"] = B("#F0141720"); r["TopBarBg"] = B("#EE141720");
            r["Txt"] = B("#F2F4F8"); r["Txt2"] = B("#9CA3B8");
            r["PillBg"] = B("#262A38"); r["PillFg"] = B("#F2F4F8");
            r["InputBg"] = B("#0E1017"); r["InputBorder"] = B("#2E3342");
            r["ItemHover"] = B("#222634"); r["TrackBg"] = B("#2A2E3C");
        }
        else
        {
            r["WinBg"] = Grad("#B9BEDA", "#9EA5C7");
            r["SidebarBg"] = B("#F7FFFFFF"); r["TopBarBg"] = B("#F2FFFFFF");
            r["Txt"] = B("#14151A"); r["Txt2"] = B("#5E6378");
            r["PillBg"] = B("#EEF0F8"); r["PillFg"] = B("#14151A");
            r["InputBg"] = B("#FFFFFF"); r["InputBorder"] = B("#DCDFEA");
            r["ItemHover"] = B("#F0F1FA"); r["TrackBg"] = B("#D9DCEA");
        }
    }
}
