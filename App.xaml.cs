namespace DriveBox;
public partial class App : System.Windows.Application
{
    public App()
    {
        // 1.0.1 and older (when the app was called PartiChart) kept their settings in %AppData%\PartiChart
        try
        {
            var ad = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var old = Path.Combine(ad, "PartiChart"); var now = Path.Combine(ad, "DriveBox");
            if (Directory.Exists(old) && !Directory.Exists(now)) Directory.Move(old, now);
        }
        catch { }
    }
}
