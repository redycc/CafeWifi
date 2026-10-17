using CafeWiFi.Core;
using CafeWiFi.Platform;

namespace CafeWiFi.App;

/// <summary>
/// Notices when the Wi-Fi we're on belongs to an independent cafe and remembers it: the cafe
/// from OpenStreetMap next to where we are (or the Wi-Fi name, if it says cafe), the router,
/// and the password Windows joined with. From then on the time there is tracked like any store.
/// </summary>
public sealed class CafeDiscovery
{
    private const double SearchMeters = 150;
    private static readonly TimeSpan RetryAfterError = TimeSpan.FromMinutes(5);

    private readonly IWifi wifi;
    private readonly ILocator locator;
    private readonly ICafeMap map;
    private readonly BrandCatalog catalog;
    private readonly LearnedStore learned;
    private readonly PlaceStore places;
    private readonly Func<bool> enabled;
    private readonly Func<DateTimeOffset> clock;
    private readonly Action<Place> found;
    private readonly Dictionary<string, DateTimeOffset> retryAt = new(); // routers whose lookup failed (offline, map down)
    private int running;

    public CafeDiscovery(IWifi wifi, ILocator locator, ICafeMap map, BrandCatalog catalog, LearnedStore learned,
        PlaceStore places, Func<bool> enabled, Func<DateTimeOffset> clock, Action<Place> found)
    {
        (this.wifi, this.locator, this.map, this.catalog, this.learned, this.places, this.enabled, this.clock, this.found) =
            (wifi, locator, map, catalog, learned, places, enabled, clock, found);
    }

    /// <summary>Called on the app's timer. Cheap unless we're on a network we've never looked up.</summary>
    public async Task CheckAsync()
    {
        if (!enabled() || wifi.CurrentLink() is not { Ssid: { Length: > 0 } ssid, Bssid: { } bssid }) return;
        if (catalog.BrandForSsid(ssid) is not null || learned.Lookup(bssid) is not null || places.IsNotCafe(ssid)) return;
        var now = clock();
        if (places.CheckedRecently(bssid, now) || retryAt.TryGetValue(bssid, out var at) && now < at) return;
        if (Interlocked.Exchange(ref running, 1) == 1) return;
        try
        {
            if (CafeMatcher.LooksLikeHotspot(ssid))
            {
                places.MarkChecked(bssid, now);
                return;
            }
            var here = await locator.RefreshAsync();
            CafeCandidate? cafe = null;
            if (here is { } h)
            {
                try { cafe = CafeMatcher.Match(ssid, h, await map.CafesNearAsync(h, SearchMeters)); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
                {
                    Log.Write($"cafe lookup failed: {ex.Message}");
                    retryAt[bssid] = now + RetryAfterError;
                    return;
                }
            }

            Place? place = cafe is not null
                ? new Place("", cafe.Name, ssid, cafe.Lat, cafe.Lng, cafe.City, cafe.Address, cafe.Id, "osm", now)
                : CafeMatcher.LooksLikeCafeName(ssid)
                    ? new Place("", ssid, ssid, here?.Lat ?? 0, here?.Lng ?? 0, null, null, null, "ssid", now)
                    : null;
            if (place is null)
            {
                places.MarkChecked(bssid, now);
                Log.Write($"{ssid} doesn't look like a cafe");
                return;
            }
            found(Remember(place, bssid));
        }
        finally { Volatile.Write(ref running, 0); }
    }

    /// <summary>"把這裡記成咖啡廳": the user says the network we're on is a cafe called name.</summary>
    public async Task<Place?> AddCurrentAsync(string name)
    {
        if (wifi.CurrentLink() is not { Ssid: { Length: > 0 } ssid, Bssid: { } bssid }) return null;
        var here = await locator.RefreshAsync();
        return Remember(new Place("", name, ssid, here?.Lat ?? 0, here?.Lng ?? 0, null, null, null, "manual", clock()), bssid);
    }

    private Place Remember(Place place, string bssid)
    {
        var saved = places.Add(place);
        learned.Remember(new[] { bssid }, saved.Key, wifi.CurrentPassword() ?? "");
        Log.Write($"remembered cafe {saved.Name} ({saved.Source}) for {bssid}");
        return saved;
    }
}
