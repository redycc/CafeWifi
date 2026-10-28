using System.Text.Json;
using CafeWiFi.App;
using CafeWiFi.Core;
using CafeWiFi.Platform;

namespace CafeWiFi.Tests;

public class CafeFinderTests : IDisposable
{
    private readonly TempDir dir = new();
    public void Dispose() => dir.Dispose();

    private static readonly GeoPoint Here = new(25.0440, 121.5257);
    private const string Router = "cc:00:00:00:00:01";

    private static readonly BrandCatalog Catalog = BrandCatalog.Load(new[]
    {
        JsonSerializer.Serialize(new Brand("louisa", "路易莎", new[] { "LouisaCoffee" }, Array.Empty<Store>()), BrandCatalog.Json),
    }, Array.Empty<string>());

    private readonly FakeWifi wifi = new();
    private readonly FakeLocator locator = new() { Location = Here };
    private readonly FakeMap map = new();
    private DateTimeOffset now = new(2026, 10, 17, 14, 0, 0, TimeSpan.FromHours(8));
    private bool enabled = true;
    private LearnedStore? learned;
    private PlaceStore? places;
    private LearnedStore Learned => learned ??= new LearnedStore(dir.File("learned.json"));
    private PlaceStore Places => places ??= new PlaceStore(dir.File("places.json"));

    private CafeFinder Make() => new(wifi, locator, map, Catalog, Learned, Places, () => enabled, () => now);

    private static Bss Net(string ssid = "FikaFika_5G") => new(ssid, Router, -45);

    [Fact]
    public async Task NearbyCafeWithPhone_ReturnsPlanWithPhoneCandidates_AndSavePersistsThePlace()
    {
        map.Cafes.Add(new CafeCandidate("node/42", "Fika Fika Cafe", Here.Lat, Here.Lng, "台北市", null, "02-2345-8613"));

        var plan = await Make().FindAsync(new[] { Net() });

        Assert.NotNull(plan);
        Assert.Equal("FikaFika_5G", plan!.Ssid);
        Assert.Equal(new[] { "23458613", "0223458613", "223458613" }, plan.Passwords);

        var key = await plan.Save();
        Assert.Equal(new StoreKey("mine", "Fika Fika Cafe"), key);
        Assert.Single(Places.Places);
    }

    [Fact]
    public async Task CafeWithoutPhone_ReturnsNull_AndParksRouter_WithoutTouchingPlaceStore()
    {
        map.Cafes.Add(new CafeCandidate("node/42", "Fika Fika Cafe", Here.Lat, Here.Lng, "台北市", null, Phone: null));
        var f = Make();

        Assert.Null(await f.FindAsync(new[] { Net() }));
        Assert.Null(await f.FindAsync(new[] { Net() }));
        Assert.Equal(1, map.Queries);                 // parked: not re-queried
        // Must not block CafeDiscovery from remembering this cafe if the user connects to it by hand.
        Assert.False(Places.CheckedRecently(Router, now));
    }

    [Fact]
    public async Task OpenNetwork_IsSkipped_WithoutEvenQueryingTheMap()
    {
        wifi.Open.Add("FikaFika_5G");
        map.Cafes.Add(new CafeCandidate("node/42", "Fika Fika Cafe", Here.Lat, Here.Lng, null, null, "02-2345-8613"));

        Assert.Null(await Make().FindAsync(new[] { Net() }));
        Assert.Equal(0, map.Queries);
    }

    [Fact]
    public async Task LearnedNetwork_IsSkipped()
    {
        Learned.Remember(new[] { Router }, new StoreKey("mine", "somewhere"), "pw");
        map.Cafes.Add(new CafeCandidate("node/42", "Fika Fika Cafe", Here.Lat, Here.Lng, null, null, "02-2345-8613"));

        Assert.Null(await Make().FindAsync(new[] { Net() }));
    }

    [Fact]
    public async Task Disabled_ReturnsNull()
    {
        enabled = false;
        map.Cafes.Add(new CafeCandidate("node/42", "Fika Fika Cafe", Here.Lat, Here.Lng, null, null, "02-2345-8613"));
        Assert.Null(await Make().FindAsync(new[] { Net() }));
    }

    [Fact]
    public async Task LookupFails_ReturnsNull_WithoutMarkingChecked_SoItRetries()
    {
        map.Fail = true;
        Assert.Null(await Make().FindAsync(new[] { Net() }));
        Assert.False(Places.CheckedRecently(Router, now));
    }
}
