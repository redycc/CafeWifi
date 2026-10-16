namespace CafeWiFi;

/// <summary>Where the app keeps its files: %APPDATA%\CafeWiFi for data, %LOCALAPPDATA%\CafeWiFi for caches.</summary>
public static class Paths
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CafeWiFi");

    public static string LocalDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CafeWiFi");

    public static string Learned => Path.Combine(DataDir, "learned.json");
    public static string Usage => Path.Combine(DataDir, "usage.json");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string Status => Path.Combine(DataDir, "status.json");
    public static string UserBrands => Path.Combine(DataDir, "brands");
}
