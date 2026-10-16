using CafeWiFi.Core;

namespace CafeWiFi.Tests;

public class AttemptPlannerTests : IDisposable
{
    private readonly TempDir dir = new();
    public void Dispose() => dir.Dispose();

    private static Store S(string name, double lat, double lng, params string[] pw) =>
        new(name, "", "台北市", "", lat, lng, pw);

    // Around Taipei Main Station (25.0478, 121.5170); 0.01° lat ≈ 1.1 km.
    private static readonly Brand Louisa = new("louisa", "路易莎", new[] { "LouisaCoffee" }, new[]
    {
        S("A", 25.0478, 121.5170, "a1", "a2", "a3"),
        S("B", 25.0500, 121.5170, "b1", "b2"),
        S("C", 25.0550, 121.5170, "c1", "c2", "c3"),
        S("D", 25.0600, 121.5170, "d1"),          // 4th closest: never tried
        S("Far", 25.1500, 121.5170, "far1"),      // ~11 km
        S("Dup", 25.0479, 121.5171, "a1"),
    });
    private static readonly Brand Cama = new("cama", "Cama", new[] { "CAMA" }, new[] { S("X", 25.0478, 121.5170, "x1") });
    private static readonly BrandCatalog Catalog = BrandCatalog.Load(
        new[] { System.Text.Json.JsonSerializer.Serialize(Louisa, BrandCatalog.Json), System.Text.Json.JsonSerializer.Serialize(Cama, BrandCatalog.Json) },
        Array.Empty<string>());
    private static Brand L => Catalog.Brand("louisa")!;
    private static readonly GeoPoint Here = new(25.0478, 121.5170);
    private static readonly string[] Bssids = { "aa:00:00:00:00:01", "aa:00:00:00:00:02" };

    private LearnedStore Learned() => new(dir.File("learned.json"));

    private static string[] Pw(IEnumerable<Attempt> a) => a.Select(x => x.Password).ToArray();

    [Fact]
    public void Auto_InterleavesNearestThreeStores_WithinFiveKm()
    {
        var excludeDup = BrandCatalog.Load(new[] { System.Text.Json.JsonSerializer.Serialize(
            Louisa with { Stores = Louisa.Stores.Where(s => s.Name != "Dup").ToList() }, BrandCatalog.Json) }, Array.Empty<string>());
        var plan = AttemptPlanner.ForAuto(excludeDup.Brand("louisa")!, Bssids, Learned(), excludeDup, Here);
        Assert.Equal(new[] { "a1", "b1", "c1", "a2", "b2", "c2", "a3", "c3" }, Pw(plan));
        Assert.Equal(new StoreKey("louisa", "B"), plan[1].Store);
    }

    [Fact]
    public void Auto_ExcludesStoresBeyond5km()
    {
        var plan = AttemptPlanner.ForAuto(L, Bssids, Learned(), Catalog, new GeoPoint(25.1500, 121.5170));
        Assert.Equal(new[] { "far1" }, Pw(plan));
    }

    [Fact]
    public void Auto_LearnedFirst_ThenNearest()
    {
        var learned = Learned();
        learned.Remember(new[] { Bssids[1] }, new StoreKey("louisa", "C"), "c2");
        var plan = AttemptPlanner.ForAuto(L, Bssids, learned, Catalog, Here);
        Assert.Equal(new Attempt(new StoreKey("louisa", "C"), "c2"), plan[0]);
        Assert.Equal(1, Pw(plan).Count(p => p == "c2"));
    }

    [Fact]
    public void Auto_DedupesByPassword_KeepingFirst()
    {
        var plan = AttemptPlanner.ForAuto(L, Bssids, Learned(), Catalog, Here);
        Assert.Equal(1, Pw(plan).Count(p => p == "a1"));
        Assert.Equal("A", plan.First(p => p.Password == "a1").Store.StoreName);
    }

    [Fact]
    public void Auto_NoLocation_OnlyLearned()
    {
        var learned = Learned();
        Assert.Empty(AttemptPlanner.ForAuto(L, Bssids, learned, Catalog, null));
        learned.Remember(new[] { Bssids[0] }, new StoreKey("louisa", "A"), "a2");
        Assert.Equal(new[] { "a2" }, Pw(AttemptPlanner.ForAuto(L, Bssids, learned, Catalog, null)));
    }

    [Fact]
    public void Auto_IgnoresLearnedFromOtherBrand_AndOtherBrandStores()
    {
        var learned = Learned();
        learned.Remember(new[] { Bssids[0] }, new StoreKey("cama", "X"), "x1");
        var plan = AttemptPlanner.ForAuto(L, Bssids, learned, Catalog, Here);
        Assert.DoesNotContain("x1", Pw(plan));
    }

    // Open brand: ~110 m and ~1.1 km from Here.
    private static readonly Brand Sbux = new("starbucks", "星巴克", new[] { "Starbucks WiFi" }, new[]
    {
        S("近店", 25.0488, 121.5170),
        S("遠店", 25.0578, 121.5170),
    }, "open");
    private static readonly BrandCatalog OpenCatalog = BrandCatalog.Load(
        new[] { System.Text.Json.JsonSerializer.Serialize(Sbux, BrandCatalog.Json) }, Array.Empty<string>());
    private static Brand O => OpenCatalog.Brand("starbucks")!;

    [Fact]
    public void Open_Auto_NearestStoreWithin300m_NoPassword()
    {
        var plan = AttemptPlanner.ForAuto(O, Bssids, Learned(), OpenCatalog, Here);
        Assert.Equal(new[] { new Attempt(new StoreKey("starbucks", "近店"), "") }, plan);
    }

    [Fact]
    public void Open_Auto_NothingNearby_NoAttempt()
    {
        // ~1.1 km from the nearest store: could be a fake hotspot using the name, so don't auto-join.
        Assert.Empty(AttemptPlanner.ForAuto(O, Bssids, Learned(), OpenCatalog, new GeoPoint(25.0378, 121.5170)));
        Assert.Empty(AttemptPlanner.ForAuto(O, Bssids, Learned(), OpenCatalog, null));
    }

    [Fact]
    public void Open_Auto_LearnedRouter_EvenWithoutLocation()
    {
        var learned = Learned();
        learned.Remember(new[] { Bssids[1] }, new StoreKey("starbucks", "遠店"), "");
        var plan = AttemptPlanner.ForAuto(O, Bssids, learned, OpenCatalog, null);
        Assert.Equal(new[] { new Attempt(new StoreKey("starbucks", "遠店"), "") }, plan);
    }

    [Fact]
    public void Open_Store_SingleOpenAttempt_IgnoresPassword()
    {
        Assert.Equal(new[] { new Attempt(new StoreKey("starbucks", "遠店"), "") },
            AttemptPlanner.ForStore(O, O.Named("遠店")!, Bssids, Learned(), "typed"));
    }

    [Fact]
    public void NearestOpenStore_Within300m()
    {
        Assert.Equal("近店", AttemptPlanner.NearestOpenStore(O, OpenCatalog, Here)?.StoreName);
        Assert.Null(AttemptPlanner.NearestOpenStore(O, OpenCatalog, new GeoPoint(25.0378, 121.5170)));
    }

    [Fact]
    public void Store_ExplicitPasswordOnly()
    {
        var a = L.Named("A")!;
        Assert.Equal(new[] { "typed" }, Pw(AttemptPlanner.ForStore(L, a, Bssids, Learned(), "typed")));
    }

    [Fact]
    public void Store_LearnedForThatStoreThenCandidates()
    {
        var learned = Learned();
        learned.Remember(new[] { Bssids[0] }, new StoreKey("louisa", "A"), "a3");
        learned.Remember(new[] { Bssids[1] }, new StoreKey("louisa", "B"), "b1");
        var plan = AttemptPlanner.ForStore(L, L.Named("A")!, Bssids, learned, null);
        Assert.Equal(new[] { "a3", "a1", "a2" }, Pw(plan));
        Assert.All(plan, p => Assert.Equal(new StoreKey("louisa", "A"), p.Store));
    }
}
