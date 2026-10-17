using System.Net;
using System.Text.Json;
using CafeWiFi.App;
using CafeWiFi.Core;
using CafeWiFi.Platform;

namespace CafeWiFi.Tests;

internal sealed class FakeMap : ICafeMap
{
    public List<CafeCandidate> Cafes { get; } = new();
    public int Queries { get; private set; }
    public bool Fail { get; set; }

    public Task<IReadOnlyList<CafeCandidate>> CafesNearAsync(GeoPoint here, double meters, CancellationToken ct = default)
    {
        Queries++;
        if (Fail) throw new HttpRequestException("offline");
        return Task.FromResult<IReadOnlyList<CafeCandidate>>(Cafes.ToList());
    }
}

public class DiscoveryTests : IDisposable
{
    private readonly TempDir dir = new();
    public void Dispose() => dir.Dispose();

    private static readonly GeoPoint Here = new(25.0440, 121.5257);
    private const string Router = "40:4a:03:90:a2:7e";

    private static readonly BrandCatalog Catalog = BrandCatalog.Load(new[]
    {
        JsonSerializer.Serialize(new Brand("louisa", "路易莎", new[] { "LouisaCoffee" }, Array.Empty<Store>()), BrandCatalog.Json),
    }, Array.Empty<string>());

    private readonly FakeWifi wifi = new() { Link = new WifiLink("andcouple", Router), CurrentKey = "hello123" };
    private readonly FakeLocator locator = new() { Location = Here };
    private readonly FakeMap map = new();
    private readonly List<Place> found = new();
    private DateTimeOffset now = new(2026, 10, 17, 14, 0, 0, TimeSpan.FromHours(8));
    private bool enabled = true;
    private LearnedStore? learned;
    private PlaceStore? places;

    private LearnedStore Learned => learned ??= new LearnedStore(dir.File("learned.json"));
    private PlaceStore Places => places ??= new PlaceStore(dir.File("places.json"));

    private CafeDiscovery Make() =>
        new(wifi, locator, map, Catalog, Learned, Places, () => enabled, () => now, p => found.Add(p));

    [Fact]
    public async Task NewNetworkNextToACafe_IsRemembered_WithPasswordAndRouter()
    {
        map.Cafes.Add(new CafeCandidate("node/42", "&Couple", Here.Lat + 0.0002, Here.Lng, "台北市", "信義路二段1號"));
        await Make().CheckAsync();

        var place = Assert.Single(Places.Places);
        Assert.Equal(("&Couple", "andcouple", "node/42", "osm"), (place.Name, place.Ssid, place.OsmId, place.Source));
        Assert.Equal(new Learned("mine/&Couple", "hello123"), Learned.Lookup(Router));
        Assert.Equal(place, Assert.Single(found));
    }

    [Fact]
    public async Task NoCafeOnTheMap_ButCafeInTheName_RememberedUnderTheWifiName()
    {
        wifi.Link = new WifiLink("Fika Fika Cafe_5G", Router);
        await Make().CheckAsync();
        var place = Assert.Single(Places.Places);
        Assert.Equal(("Fika Fika Cafe_5G", "ssid"), (place.Name, place.Source));
        Assert.Equal(Here.Lat, place.Lat);
    }

    [Fact]
    public async Task NothingFound_IsNotAskedAgainForAMonth()
    {
        wifi.Link = new WifiLink("ChungHwa-Home", Router);
        var d = Make();
        await d.CheckAsync();
        await Make().CheckAsync();                       // a later app run
        Assert.Equal(1, map.Queries);
        Assert.Empty(Places.Places);
        now = now.AddDays(31);
        await Make().CheckAsync();
        Assert.Equal(2, map.Queries);
    }

    [Fact]
    public async Task MapUnreachable_RetriesLater_NotEvery15Seconds()
    {
        map.Fail = true;
        var d = Make();
        await d.CheckAsync();
        now = now.AddSeconds(15);
        await d.CheckAsync();
        Assert.Equal(1, map.Queries);
        map.Fail = false;
        now = now.AddMinutes(6);
        await d.CheckAsync();
        Assert.Equal(2, map.Queries);
    }

    [Theory]
    [InlineData("LouisaCoffee")]           // a chain: handled by its brand
    [InlineData("Fengyi 的 iPhone")]       // a phone
    public async Task SkipsChainsAndHotspots_WithoutAskingTheMap(string ssid)
    {
        wifi.Link = new WifiLink(ssid, Router);
        await Make().CheckAsync();
        Assert.Equal(0, map.Queries);
    }

    [Fact]
    public async Task SkipsKnownRouters_NotCafeNetworks_AndWhenDisabled()
    {
        Learned.Remember(new[] { Router }, new StoreKey("mine", "x"), "p");
        await Make().CheckAsync();
        Assert.Equal(0, map.Queries);

        wifi.Link = new WifiLink("MyHome", "40:4a:03:90:a2:7f");
        var place = Places.Add(new Place("", "家裡樓下", "MyHome", Here.Lat, Here.Lng, null, null, null, "osm", now));
        Places.MarkNotCafe(place.Id);
        await Make().CheckAsync();
        Assert.Equal(0, map.Queries);

        wifi.Link = new WifiLink("Other", "40:4a:03:90:a2:80");
        enabled = false;
        await Make().CheckAsync();
        Assert.Equal(0, map.Queries);
    }

    [Fact]
    public async Task AddCurrent_Manual_UsesGivenNameAndLocation()
    {
        var place = await Make().AddCurrentAsync("我的秘密咖啡店");
        Assert.Equal(("我的秘密咖啡店", "andcouple", "manual"), (place!.Name, place.Ssid, place.Source));
        Assert.Equal("mine/我的秘密咖啡店", Learned.Lookup(Router)?.Store);
    }

    // MARK: joiner sees personal cafes

    [Fact]
    public async Task Joiner_TracksPersonalCafe()
    {
        map.Cafes.Add(new CafeCandidate("node/42", "&Couple", Here.Lat, Here.Lng, null, null));
        await Make().CheckAsync();
        Catalog.PersonalName = id => Places.Find(id)?.Name;
        var joiner = new Joiner(wifi, locator, new FakeNet(), Catalog, Learned, () => true, () => now);
        Assert.Equal(new StoreKey("mine", "&Couple"), joiner.CurrentStoreKey());
        Assert.Equal(new Status.Connected("&Couple", "andcouple"), joiner.CurrentStatus());
    }

    // MARK: OpenStreetMap

    [Fact]
    public async Task Osm_ParsesNodesAndWays_SkipsUnnamed()
    {
        var http = new FakeHttp();
        http.Routes[OsmCafes.Endpoints[0]] = () => FakeHttp.Html("""
            {"elements":[
              {"type":"node","id":1,"lat":25.03,"lon":121.56,"tags":{"amenity":"cafe","name":"Cafe ACME","addr:city":"臺北市","addr:full":"信義路五段7號"}},
              {"type":"way","id":2,"center":{"lat":25.04,"lon":121.57},"tags":{"amenity":"cafe","name:en":"Fika","addr:street":"復興南路","addr:housenumber":"10"}},
              {"type":"node","id":3,"lat":25.05,"lon":121.58,"tags":{"amenity":"cafe"}}
            ]}
            """);
        var cafes = await new OsmCafes(http).CafesNearAsync(Here, 150);
        Assert.Equal(2, cafes.Count);
        Assert.Equal(new CafeCandidate("node/1", "Cafe ACME", 25.03, 121.56, "台北市", "信義路五段7號"), cafes[0]);
        Assert.Equal(new CafeCandidate("way/2", "Fika", 25.04, 121.57, null, "復興南路10號"), cafes[1]);
    }

    [Fact]
    public async Task Osm_FirstServerDown_TriesTheNext()
    {
        var http = new FakeHttp();
        http.Routes[OsmCafes.Endpoints[0]] = () => new HttpResponseMessage(HttpStatusCode.BadGateway);
        http.Routes[OsmCafes.Endpoints[1]] = () => FakeHttp.Html("""{"elements":[{"type":"node","id":7,"lat":1,"lon":2,"tags":{"name":"X"}}]}""");
        var cafes = await new OsmCafes(http).CafesNearAsync(Here, 150);
        Assert.Equal("X", Assert.Single(cafes).Name);
        Assert.Equal(OsmCafes.Endpoints.Take(2), http.Requested);
    }

    [Fact]
    public async Task Osm_AllServersDown_Throws()
    {
        var http = new FakeHttp(); // every request 404s
        await Assert.ThrowsAsync<HttpRequestException>(() => new OsmCafes(http).CafesNearAsync(Here, 150));
        Assert.Equal(OsmCafes.Endpoints, http.Requested);
    }
}
