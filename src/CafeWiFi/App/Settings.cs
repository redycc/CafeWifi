using System.Text.Json.Serialization;
using CafeWiFi.Core;

namespace CafeWiFi.App;

/// <summary>User preferences and the last known location. settings.json.</summary>
public sealed class Settings
{
    public bool AutoConnect { get; set; } = true;
    public bool OnlyKnown { get; set; }
    public bool HideTrayIcon { get; set; }
    public double? LastLat { get; set; }
    public double? LastLng { get; set; }
    public DateTimeOffset? LastFixTime { get; set; }
    public string? NotifiedVersion { get; set; }
    public bool LocationHintShown { get; set; }

    [JsonIgnore] private string path = Paths.SettingsFile;

    [JsonIgnore]
    public GeoPoint? LastLocation => LastLat is { } lat && LastLng is { } lng ? new GeoPoint(lat, lng) : null;

    public static Settings Load(string? path = null)
    {
        path ??= Paths.SettingsFile;
        var s = JsonFile.Load<Settings>(path);
        s.path = path;
        return s;
    }

    public void Save()
    {
        try { JsonFile.Save(path, this); }
        catch (IOException ex) { Log.Write($"cannot save settings: {ex.Message}"); }
    }
}
