using CafeWiFi.Core;
using CafeWiFi.Platform;

namespace CafeWiFi.App;

/// <summary>The user's own (non-chain) cafes, as the menu and notifications see them.</summary>
public sealed class OwnCafes
{
    private readonly PlaceStore places;
    private readonly LearnedStore learned;
    private readonly CafeDiscovery discovery;
    private readonly IWifi wifi;
    private readonly BrandCatalog catalog;

    public OwnCafes(PlaceStore places, LearnedStore learned, CafeDiscovery discovery, IWifi wifi, BrandCatalog catalog)
    {
        (this.places, this.learned, this.discovery, this.wifi, this.catalog) = (places, learned, discovery, wifi, catalog);
    }

    public IReadOnlyList<Place> Places => places.Places;

    /// <summary>The Wi-Fi password saved for this cafe; "" for an open network, null if unknown.</summary>
    public string? PasswordFor(Place place) => learned.PasswordFor(place.Key);

    /// <summary>"不是咖啡廳": forget it and never take its Wi-Fi for a cafe again.</summary>
    public void NotCafe(string id)
    {
        if (places.Find(id) is not { } place) return;
        learned.Forget(place.Key);
        places.MarkNotCafe(id);
    }

    /// <summary>The Wi-Fi we're on, if it's neither a chain nor a cafe we already know.</summary>
    public string? UnknownNetwork() =>
        wifi.CurrentLink() is { Ssid: { Length: > 0 } ssid, Bssid: { } bssid }
        && catalog.BrandForSsid(ssid) is null && learned.Lookup(bssid) is null ? ssid : null;

    public Task<Place?> AddCurrentAsync(string name) => discovery.AddCurrentAsync(name);
}
