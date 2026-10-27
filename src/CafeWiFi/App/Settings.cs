using System.Text.Json.Serialization;
using CafeWiFi.Core;

namespace CafeWiFi.App;

/// <summary>User preferences and the last known location. settings.json.</summary>
public sealed class Settings
{
    public bool AutoConnect { get; set; } = true;
    public bool OnlyKnown { get; set; }
    public bool HideTrayIcon { get; set; }
    /// <summary>Look up new Wi-Fi networks on OpenStreetMap and remember independent cafes.</summary>
    public bool AutoDiscover { get; set; } = true;
    /// <summary>On an open brand's Wi-Fi, accept a recognised terms page (e.g. Starbucks) automatically.</summary>
    public bool AutoAcceptTerms { get; set; } = true;
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

    /// <summary>Safe from any thread: the UI and the location callback both save.</summary>
    public void Save()
    {
        try { JsonFile.Save(path, this); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Write($"cannot save settings: {ex.Message}"); }
    }
}
