using Microsoft.Win32;

namespace CafeWiFi.Platform;

/// <summary>"開機時啟動": the per-user Run key, pointing at wherever this exe is now.</summary>
public static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "CafeWiFi";

    private static string Command => $"\"{Environment.ProcessPath}\" --startup";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return string.Equals(key?.GetValue(Name) as string, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(Name, Command);
        else key.DeleteValue(Name, throwOnMissingValue: false);
    }
}
