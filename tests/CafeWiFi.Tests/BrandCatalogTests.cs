using CafeWiFi.Core;

namespace CafeWiFi.Tests;

public class BrandCatalogTests
{
    internal static string BrandJson(string id, string ssid, params (string Name, string City, double Lat, double Lng)[] stores) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            id,
            name = id + " 咖啡",
            ssids = new[] { ssid },
            stores = stores.Select(s => new
            {
                name = s.Name, address = s.City + " 某區 某路1號", city = s.City, phone = "02-1234-5678",
                lat = s.Lat, lng = s.Lng, passwords = new[] { s.Name + "-1", s.Name + "-2" },
            }),
        });

    // Taipei Main Station and Taipei 101 are about 5.0 km apart.
    internal static readonly GeoPoint MainStation = new(25.0478, 121.5170);
    internal static readonly GeoPoint Taipei101 = new(25.0340, 121.5645);

    private static BrandCatalog Sample() => BrandCatalog.Load(new[]
    {
        BrandJson("louisa", "LouisaCoffee",
            ("車站門市", "台北市", 25.0478, 121.5170),
            ("101門市", "台北市", 25.0340, 121.5645),
            ("板橋門市", "新北市", 25.0143, 121.4672),
            ("無座標門市", "台北市", 0, 0),
            ("離島門市", "澎湖縣", 23.5655, 119.5793),
            ("外國門市", "東京都", 35.68, 139.76)),
        BrandJson("cama", "CAMA_WiFi", ("車站店", "台北市", 25.0479, 121.5171)),
    }, Array.Empty<string>());

    [Fact]
    public void Geo_Meters_MainStationTo101_IsAbout5km()
    {
        Assert.InRange(Geo.Meters(MainStation, Taipei101), 4850, 5150);
    }

    [Fact]
    public void Load_ParsesBrandsAndStores()
    {
        var c = Sample();
        Assert.Equal(new[] { "louisa", "cama" }, c.Brands.Select(b => b.Id));
        var s = c.Find(new StoreKey("louisa", "車站門市"))!;
        Assert.Equal("台北市", s.City);
        Assert.Equal(new[] { "車站門市-1", "車站門市-2" }, s.Passwords);
    }

    [Fact]
    public void Load_UserBrandOverridesBuiltInById()
    {
        var c = BrandCatalog.Load(
            new[] { BrandJson("louisa", "LouisaCoffee", ("舊門市", "台北市", 25, 121.5)) },
            new[] { BrandJson("louisa", "LouisaCoffee", ("新門市", "台北市", 25, 121.5)) });
        Assert.Single(c.Brands);
        Assert.NotNull(c.Find(new StoreKey("louisa", "新門市")));
        Assert.Null(c.Find(new StoreKey("louisa", "舊門市")));
    }

    [Fact]
    public void Load_SkipsMalformedJson()
    {
        var c = BrandCatalog.Load(new[] { "{bad", BrandJson("cama", "CAMA_WiFi") }, Array.Empty<string>());
        Assert.Equal("cama", Assert.Single(c.Brands).Id);
    }

    [Fact]
    public void LoadDefault_HasLouisaFromEmbeddedResources()
    {
        var louisa = BrandCatalog.LoadDefault(userBrandsDir: null).BrandForSsid("LouisaCoffee");
        Assert.NotNull(louisa);
        Assert.True(louisa!.Stores.Count > 400);
    }

    [Fact]
    public void Security_DefaultsToPasswordNetworks_OpenWhenSaid()
    {
        var c = BrandCatalog.Load(new[]
        {
            BrandJson("louisa", "LouisaCoffee"),
            "{\"id\":\"starbucks\",\"name\":\"星巴克\",\"ssids\":[\"Starbucks WiFi\"],\"security\":\"open\",\"stores\":[]}",
        }, Array.Empty<string>());
        Assert.False(c.Brand("louisa")!.IsOpen);
        Assert.True(c.Brand("starbucks")!.IsOpen);
    }

    [Fact]
    public void LoadDefault_HasStarbucksAsOpenNetwork()
    {
        var sbux = BrandCatalog.LoadDefault(userBrandsDir: null).BrandForSsid("Starbucks WiFi");
        Assert.True(sbux?.IsOpen);
        Assert.True(sbux!.Stores.Count > 400);
    }

    [Fact]
    public void BrandForSsid_IsCaseInsensitive_AndNullForUnknown()
    {
        var c = Sample();
        Assert.Equal("louisa", c.BrandForSsid("louisacoffee")?.Id);
        Assert.Null(c.BrandForSsid("Starbucks"));
        Assert.Null(c.BrandForSsid(""));
    }

    [Fact]
    public void Nearest_SortsByDistance_AndRespectsLimit()
    {
        var near = Sample().Nearest(Taipei101, limit: 2, brandId: "louisa");
        Assert.Equal(new[] { "101門市", "車站門市" }, near.Select(n => n.Store.Name));
        Assert.True(near[0].Meters < near[1].Meters);
    }

    [Fact]
    public void Nearest_SkipsStoresWithoutCoordinates()
    {
        var all = Sample().Nearest(new GeoPoint(0, 0), limit: 100);
        Assert.DoesNotContain(all, n => n.Store.Name == "無座標門市");
    }

    [Fact]
    public void Nearest_AcrossBrandsUnlessBrandGiven_AndAppliesFilter()
    {
        var c = Sample();
        Assert.Contains(c.Nearest(MainStation, 3), n => n.Brand.Id == "cama");
        Assert.DoesNotContain(c.Nearest(MainStation, 3, brandId: "louisa"), n => n.Brand.Id == "cama");
        var filtered = c.Nearest(MainStation, 3, (b, s) => s.Name == "板橋門市");
        Assert.Equal("板橋門市", Assert.Single(filtered).Store.Name);
    }

    [Fact]
    public void ByCity_OrdersByCityListThenName()
    {
        var louisa = Sample().Brands[0];
        var cities = BrandCatalog.ByCity(louisa.Stores).Select(g => g.City).ToArray();
        Assert.Equal(new[] { "台北市", "新北市", "澎湖縣", "東京都" }, cities);
        var taipei = BrandCatalog.ByCity(louisa.Stores)[0].Stores.Select(s => s.Name);
        Assert.Equal(taipei.OrderBy(n => n, StringComparer.Ordinal), taipei);
    }

    [Fact]
    public void Search_MatchesAllTokensInNameOrAddress()
    {
        var c = Sample();
        Assert.Equal(new[] { "車站門市", "車站店" }, c.Search("車站").Select(r => r.Store.Name));
        Assert.Equal("車站門市", Assert.Single(c.Search("台北 車站", brandId: "louisa")).Store.Name);
        Assert.Empty(c.Search("台北 板橋"));
    }

    [Fact]
    public void Resolve_PrefersExactName_ElseSearch()
    {
        var c = BrandCatalog.Load(new[]
        {
            BrandJson("louisa", "LouisaCoffee", ("延平門市", "台北市", 25, 121.5), ("延平北門市", "台北市", 25, 121.5)),
        }, Array.Empty<string>());
        Assert.Equal("延平門市", Assert.Single(c.Resolve("延平門市")).Store.Name);
        Assert.Equal(2, c.Resolve("延平").Count);
        Assert.Equal("延平北門市", Assert.Single(c.Resolve("延平北")).Store.Name);
        Assert.Empty(c.Resolve("不存在"));
    }

    [Fact]
    public void StoreKey_RoundTrips_EvenWithSlashInName()
    {
        var k = new StoreKey("louisa", "A/B門市");
        Assert.Equal("louisa/A/B門市", k.ToString());
        Assert.Equal(k, StoreKey.Parse(k.ToString()));
    }

    [Theory]
    [InlineData("A:B:C:D:E:F", "0a:0b:0c:0d:0e:0f")]
    [InlineData("AABBCCDDEEFF", "aa:bb:cc:dd:ee:ff")]
    [InlineData("aa-bb-cc-dd-ee-ff", "aa:bb:cc:dd:ee:ff")]
    [InlineData("E0:63:DA:6B:BC:51", "e0:63:da:6b:bc:51")]
    public void Bssid_Normalize(string raw, string expected)
    {
        Assert.Equal(expected, Bssid.Normalize(raw));
    }
}
