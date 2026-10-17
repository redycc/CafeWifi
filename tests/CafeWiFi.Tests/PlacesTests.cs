using CafeWiFi.Core;

namespace CafeWiFi.Tests;

public class PlacesTests : IDisposable
{
    private readonly TempDir dir = new();
    public void Dispose() => dir.Dispose();

    private static readonly GeoPoint Here = new(25.0440, 121.5257);

    // ~0.0001° lat ≈ 11 m
    private static CafeCandidate C(string name, double dLat, string id = "") =>
        new(id == "" ? "node/" + name : id, name, Here.Lat + dLat, Here.Lng, "臺北市", null);

    // MARK: matching

    [Fact]
    public void Match_NameSimilarToSsid_WinsEvenIfFartherAway()
    {
        var cafes = new[] { C("隔壁咖啡", 0.0001), C("&Couple", 0.0008) };   // 11 m vs ~90 m
        Assert.Equal("&Couple", CafeMatcher.Match("andcouple", Here, cafes)?.Name);
    }

    [Fact]
    public void Match_NoNameMatch_TakesNearestWithin40m()
    {
        Assert.Equal("近的店", CafeMatcher.Match("TP-Link_1234", Here, new[] { C("遠的店", 0.0006), C("近的店", 0.0003) })?.Name);
        Assert.Null(CafeMatcher.Match("TP-Link_1234", Here, new[] { C("遠的店", 0.0006) }));   // ~67 m
    }

    [Fact]
    public void Match_NamedButTooFar_IsIgnored()
    {
        Assert.Null(CafeMatcher.Match("andcouple", Here, new[] { C("&Couple", 0.0020) }));     // ~220 m
    }

    [Theory]
    [InlineData("Fengyi 的 iPhone")]
    [InlineData("AndroidAP_1234")]
    [InlineData("Galaxy S24 5G")]
    [InlineData("DIRECT-12-HP OfficeJet")]
    [InlineData("小明的熱點")]
    public void Hotspots_AreNeverCafes(string ssid)
    {
        Assert.True(CafeMatcher.LooksLikeHotspot(ssid));
        Assert.Null(CafeMatcher.Match(ssid, Here, new[] { C("某咖啡", 0.0001) }));
    }

    [Theory]
    [InlineData("SimpleKaffa", false)]
    [InlineData("Fika Fika Cafe", true)]
    [InlineData("好丘咖啡_5G", true)]
    [InlineData("COFFEE LAW", true)]
    [InlineData("Café Lumière", true)]
    [InlineData("ChungHwa-Home", false)]
    public void LooksLikeCafeName(string ssid, bool cafe) => Assert.Equal(cafe, CafeMatcher.LooksLikeCafeName(ssid));

    // MARK: store

    private PlaceStore Store() => new(dir.File("places.json"));

    private static Place P(string name, string ssid, string? osm = null) =>
        new("", name, ssid, Here.Lat, Here.Lng, "台北市", null, osm, "osm", DateTimeOffset.Now);

    [Fact]
    public void Add_AssignsUniqueIds_AndPersists()
    {
        var s = Store();
        var a = s.Add(P("&Couple", "andcouple", "node/1"));
        var b = s.Add(P("&Couple", "andcouple-2F", "node/2"));
        Assert.Equal("&Couple", a.Id);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(2, Store().Places.Count);
        Assert.Equal("&Couple", Store().Find(a.Id)?.Name);
    }

    [Fact]
    public void Add_SamePlaceAgain_ReturnsExisting()
    {
        var s = Store();
        var a = s.Add(P("&Couple", "andcouple", "node/1"));
        var again = s.Add(P("&Couple", "andcouple", "node/1"));
        Assert.Equal(a, again);
        Assert.Single(s.Places);
        var bySsid = s.Add(P("Some Name", "andcouple"));            // no OSM id, same SSID nearby
        Assert.Equal(a.Id, bySsid.Id);
    }

    [Fact]
    public void NotCafe_RemovesAndRemembersSsid()
    {
        var s = Store();
        var a = s.Add(P("家裡樓下", "MyHome"));
        s.MarkNotCafe(a.Id);
        Assert.Empty(s.Places);
        Assert.True(Store().IsNotCafe("MyHome"));
    }

    [Fact]
    public void Checked_NegativeResult_ExpiresAfter30Days()
    {
        var s = Store();
        var now = DateTimeOffset.Now;
        s.MarkChecked("aa:00:00:00:00:01", now);
        Assert.True(Store().CheckedRecently("aa:00:00:00:00:01", now.AddDays(29)));
        Assert.False(Store().CheckedRecently("aa:00:00:00:00:01", now.AddDays(31)));
    }
}
