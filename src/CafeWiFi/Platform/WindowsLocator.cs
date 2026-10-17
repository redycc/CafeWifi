using Windows.Devices.Geolocation;
using CafeWiFi.App;
using CafeWiFi.Core;

namespace CafeWiFi.Platform;

/// <summary>
/// Windows location, remembering the last fix in settings: a laptop has no GPS and a cafe
/// often has no internet before we connect, so a recent cached fix is the best we get.
/// </summary>
public sealed class WindowsLocator : ILocator
{
    private readonly Settings settings;
    private Geolocator? geolocator;

    public WindowsLocator(Settings settings) => this.settings = settings;

    public GeoPoint? Location => settings.LastLocation;
    public bool Authorized { get; private set; }
    public event Action? Changed;

    public async Task StartAsync()
    {
        try
        {
            Authorized = await Geolocator.RequestAccessAsync() == GeolocationAccessStatus.Allowed;
            Log.Write($"location authorized: {Authorized}");
            if (!Authorized) { Changed?.Invoke(); return; }

            geolocator = new Geolocator { DesiredAccuracy = PositionAccuracy.Default, MovementThreshold = 100 };
            geolocator.PositionChanged += (_, e) => Store(e.Position);
            geolocator.StatusChanged += (_, e) =>
            {
                bool allowed = e.Status != PositionStatus.Disabled;
                if (allowed == Authorized) return;
                Authorized = allowed;
                Log.Write($"location authorized: {Authorized}");
                Changed?.Invoke();
            };
            Store(await geolocator.GetGeopositionAsync(TimeSpan.FromHours(1), TimeSpan.FromSeconds(10)));
        }
        catch (Exception ex)
        {
            // Timeouts (no fix offline) and COM errors from a disabled location service.
            Log.Write($"location unavailable: {ex.Message}");
        }
    }

    public async Task<GeoPoint?> RefreshAsync()
    {
        if (geolocator is null) return null;
        try { Store(await geolocator.GetGeopositionAsync(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(10))); }
        catch (Exception ex) { Log.Write($"location refresh failed: {ex.Message}"); }
        // An old fix may be from somewhere else entirely; better no answer than the wrong cafe.
        return settings.LastFixTime is { } at && DateTimeOffset.Now - at < TimeSpan.FromMinutes(10) ? Location : null;
    }

    private void Store(Geoposition position)
    {
        var p = position.Coordinate.Point.Position;
        settings.LastLat = Math.Round(p.Latitude, 6);
        settings.LastLng = Math.Round(p.Longitude, 6);
        settings.LastFixTime = position.Coordinate.Timestamp;
        settings.Save();
        Changed?.Invoke();
    }
}
