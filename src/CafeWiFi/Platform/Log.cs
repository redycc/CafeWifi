using System.Globalization;

namespace CafeWiFi;

/// <summary>Appends to %LOCALAPPDATA%\CafeWiFi\CafeWiFi.log, keeping one 1 MB backup.</summary>
public static class Log
{
    private static readonly object Gate = new();

    /// <summary>Overridable so tests don't write to the real log.</summary>
    public static string? FilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CafeWiFi", "CafeWiFi.log");

    public static void Write(string message)
    {
        var path = FilePath;
        if (path is null) return;
        var line = $"{DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture)} {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 1_000_000) File.Move(path, path + ".1", overwrite: true);
                File.AppendAllText(path, line);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
