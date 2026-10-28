using System.Text.Json;
using CafeWiFi.Core;
using CafeWiFi.Platform;

namespace CafeWiFi.App;

/// <summary>
/// Picks a nearby independent cafe whose Wi-Fi password we can guess from its phone number, and
/// hands the Joiner a plan to try it. Only secured, unknown networks are considered; the OSM phone
/// is a guess, so a router without a usable cafe nearby is parked for a month, not retried.
/// </summary>
public sealed class CafeFinder
{
    private const double SearchMeters = 150;
    private static readonly TimeSpan RecheckAfter = TimeSpan.FromHours(6);

    private readonly IWifi wifi;
    private readonly ILocator locator;
    private readonly ICafeMap map;
    private readonly BrandCatalog catalog;
    private readonly LearnedStore learned;
    private readonly PlaceStore places;
    private readonly Func<bool> enabled;
    private readonly Func<DateTimeOffset> clock;
    private readonly Dictionary<string, DateTimeOffset> parked = new(); // routers with no guessable cafe; in-memory only

    public CafeFinder(IWifi wifi, ILocator locator, ICafeMap map, BrandCatalog catalog, LearnedStore learned,
        PlaceStore places, Func<bool> enabled, Func<DateTimeOffset> clock)
    {
        (this.wifi, this.locator, this.map, this.catalog, this.learned, this.places, this.enabled, this.clock) =
            (wifi, locator, map, catalog, learned, places, enabled, clock);
    }

    /// <summary>Given the networks in range, a cafe worth guessing a password for, or null.</summary>
    public async Task<Joiner.CafePlan?> FindAsync(IReadOnlyList<Bss> bss)
    {
        if (!enabled()) return null;
        var open = wifi.OpenSsids();
        var net = bss
            .Where(b => b.Ssid.Length > 0
                && catalog.BrandForSsid(b.Ssid) is null
                && learned.Lookup(b.Bssid) is null
                && !places.IsNotCafe(b.Ssid)
                && !open.Contains(b.Ssid)
                && !CafeMatcher.LooksLikeHotspot(b.Ssid))
            .OrderByDescending(b => b.Rssi)
            .FirstOrDefault(b => !Parked(b.Bssid));
        if (net is null) return null;

        var here = await locator.RefreshAsync();
        if (here is not { } h) return null;

        CafeCandidate? cafe;
        try { cafe = CafeMatcher.Match(net.Ssid, h, await map.CafesNearAsync(h, SearchMeters)); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Log.Write($"cafe guess lookup failed: {ex.Message}");
            return null; // transient (offline, map down): try again next time, don't park the router
        }

        var passwords = cafe?.Phone is { } phone ? PhonePasswords.Candidates(phone) : Array.Empty<string>();
        if (cafe is null || passwords.Count == 0)
        {
            // No cafe here, or one without a guessable phone: don't re-query this router for a while.
            // In-memory only, and separate from PlaceStore.Checked, so it never stops CafeDiscovery
            // from remembering this place if the user connects to it by hand.
            parked[net.Bssid] = clock();
            return null;
        }

        var place = new Place("", cafe.Name, net.Ssid, cafe.Lat, cafe.Lng, cafe.City, cafe.Address, cafe.Id, "osm", clock());
        return new Joiner.CafePlan(net.Ssid, cafe.Name, passwords, () => Task.FromResult(places.Add(place).Key));
    }

    private bool Parked(string bssid) => parked.TryGetValue(bssid, out var at) && clock() - at < RecheckAfter;
}
