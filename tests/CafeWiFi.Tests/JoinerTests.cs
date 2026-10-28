using CafeWiFi.App;
using CafeWiFi.Core;
using CafeWiFi.Platform;

namespace CafeWiFi.Tests;

internal sealed class FakeWifi : IWifi
{
    public bool HasInterface { get; set; } = true;
    public WifiLink? Link { get; set; }
    public List<Bss> Nearby { get; } = new();
    public HashSet<string> CorrectPasswords { get; } = new();
    public List<(string Bssid, string Password)> Joins { get; } = new();
    public int Scans { get; private set; }

    /// <summary>When set, TryJoin waits for this (or cancellation) before answering.</summary>
    public TaskCompletionSource? Gate { get; set; }
    public TaskCompletionSource JoinStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WifiLink? CurrentLink() => Link;

    /// <summary>The password Windows saved for the network we're on, if readable.</summary>
    public string? CurrentKey { get; set; }
    public string? CurrentPassword() => Link is null ? null : CurrentKey;

    public Task<IReadOnlyList<Bss>> ScanAsync(CancellationToken ct = default)
    {
        Scans++;
        return Task.FromResult<IReadOnlyList<Bss>>(Nearby.ToList());
    }

    /// <summary>SSIDs the test marks as open (passwordless).</summary>
    public HashSet<string> Open { get; } = new();
    public IReadOnlySet<string> OpenSsids() => Open;

    /// <summary>Models Windows finishing a WlanConnect we stopped waiting for.</summary>
    public bool JoinLandsEvenIfCancelled { get; set; }
    public int Disconnects { get; private set; }

    public void Disconnect()
    {
        Disconnects++;
        Link = null;
    }

    public async Task<bool> TryJoinAsync(Bss target, string password, CancellationToken ct = default)
    {
        Joins.Add((target.Bssid, password));
        JoinStarted.TrySetResult();
        if (JoinLandsEvenIfCancelled && CorrectPasswords.Contains(password)) Link = new WifiLink(target.Ssid, target.Bssid);
        if (Gate is { } g) await g.Task.WaitAsync(ct);
        if (!CorrectPasswords.Contains(password)) return false;
        Link = new WifiLink(target.Ssid, target.Bssid);
        return true;
    }
}

internal sealed class FakeLocator : ILocator
{
    public GeoPoint? Location { get; set; }
    public bool Authorized { get; set; } = true;
    public event Action? Changed { add { } remove { } }
    public Task<GeoPoint?> RefreshAsync() => Task.FromResult(Location);
}

internal sealed class FakeNet : IConnectivity
{
    public bool Online { get; set; }
    public Task<bool> IsOnlineAsync() => Task.FromResult(Online);
}

public class JoinerTests : IDisposable
{
    private readonly TempDir dir = new();
    public void Dispose() => dir.Dispose();

    private static Store S(string name, double lat, params string[] pw) => new(name, "", "台北市", "", lat, 121.5170, pw);

    private static readonly BrandCatalog Catalog = BrandCatalog.Load(new[]
    {
        System.Text.Json.JsonSerializer.Serialize(new Brand("louisa", "路易莎", new[] { "LouisaCoffee" }, new[]
        {
            S("延平門市", 25.0478, "a1", "a2"),
            S("北門門市", 25.0500, "b1", "b2"),
        }), BrandCatalog.Json),
    }, Array.Empty<string>());

    private static readonly GeoPoint Here = new(25.0478, 121.5170);
    private const string Strong = "aa:00:00:00:00:01", Medium = "aa:00:00:00:00:02", Weak = "aa:00:00:00:00:03";

    private readonly FakeWifi wifi = new();
    private readonly FakeLocator locator = new() { Location = Here };
    private readonly FakeNet net = new();
    private readonly List<(Status Status, bool Notify)> reports = new();
    private DateTimeOffset now = new(2026, 10, 16, 10, 0, 0, TimeSpan.FromHours(8));
    private bool autoOn = true;
    private LearnedStore? learned;

    private LearnedStore Learned => learned ??= new LearnedStore(dir.File("learned.json"));

    private Joiner Make()
    {
        wifi.Nearby.AddRange(new[]
        {
            new Bss("LouisaCoffee", Strong, -50),
            new Bss("LouisaCoffee", Medium, -70),
            new Bss("LouisaCoffee", Weak, -80),
            new Bss("SomeoneElse", "bb:00:00:00:00:01", -40),
        });
        var j = new Joiner(wifi, locator, net, Catalog, Learned, () => autoOn, () => now);
        j.StatusChanged += (s, n) => reports.Add((s, n));
        return j;
    }

    private Status Last => reports[^1].Status;
    private Store Yanping => Catalog.Brand("louisa")!.Named("延平門市")!;
    private Brand Louisa => Catalog.Brand("louisa")!;

    [Fact]
    public async Task Auto_Disabled_DoesNothing()
    {
        var j = Make();
        autoOn = false;
        await j.AutoConnectAsync();
        Assert.Equal(0, wifi.Scans);
    }

    [Fact]
    public async Task Auto_NoInterface_DoesNothing()
    {
        var j = Make();
        wifi.HasInterface = false;
        await j.AutoConnectAsync();
        Assert.Equal(0, wifi.Scans);
        Assert.Empty(reports);
    }

    [Fact]
    public async Task Auto_AlreadyLinked_DoesNothing()
    {
        var j = Make();
        wifi.Link = new WifiLink("Home", "cc:00:00:00:00:01");
        await j.AutoConnectAsync();
        Assert.Equal(0, wifi.Scans);
    }

    [Fact]
    public async Task Auto_OnlineThroughEthernet_DoesNothing()
    {
        var j = Make();
        net.Online = true;
        await j.AutoConnectAsync();
        Assert.Equal(0, wifi.Scans);
    }

    [Fact]
    public async Task Auto_NoBrandSsid_DoesNothing()
    {
        var j = new Joiner(wifi, locator, net, Catalog, Learned, () => true, () => now);
        wifi.Nearby.Add(new Bss("SomeoneElse", "bb:00:00:00:00:01", -40));
        await j.AutoConnectAsync();
        Assert.Empty(wifi.Joins);
    }

    [Fact]
    public async Task Auto_UsesLearnedPasswordFirst_OnStrongestRouter()
    {
        Learned.Remember(new[] { Medium }, new StoreKey("louisa", "北門門市"), "b2");
        wifi.CorrectPasswords.Add("b2");
        var j = Make();
        await j.AutoConnectAsync();
        Assert.Equal(new[] { (Strong, "b2") }, wifi.Joins);
        Assert.Equal(new Status.Connected("北門門市", "LouisaCoffee"), Last);
        Assert.True(reports[^1].Notify);
    }

    [Fact]
    public async Task Auto_TriesNearestStores_RemembersStrongBssids()
    {
        wifi.CorrectPasswords.Add("b1");
        var j = Make();
        await j.AutoConnectAsync();
        Assert.Equal(new[] { "a1", "b1" }, wifi.Joins.Select(x => x.Password));
        Assert.Equal("louisa/北門門市", Learned.Lookup(Strong)?.Store);
        Assert.Equal("b1", Learned.Lookup(Medium)?.Password);
        Assert.Null(Learned.Lookup(Weak));
        Assert.Equal("北門門市", j.CurrentStoreLabel());
        Assert.Equal(new StoreKey("louisa", "北門門市"), j.CurrentStoreKey());
    }

    [Fact]
    public async Task Auto_AllFail_SnoozesRouterFor10Minutes()
    {
        var j = Make();
        await j.AutoConnectAsync();
        Assert.Equal(4, wifi.Joins.Count);
        Assert.Equal(new Status.Failed("自動連線失敗，請從選單選擇門市"), Last);

        now = now.AddSeconds(599);
        await j.AutoConnectAsync();
        Assert.Equal(4, wifi.Joins.Count);

        now = now.AddSeconds(2);
        await j.AutoConnectAsync();
        Assert.Equal(8, wifi.Joins.Count);
    }

    [Fact]
    public async Task Auto_WithinQuietPeriodAfterJoin_Skips()
    {
        wifi.CorrectPasswords.Add("a1");
        var j = Make();
        await j.AutoConnectAsync();
        wifi.Link = null; // our own join fires link events; pretend it dropped
        now = now.AddSeconds(10);
        await j.AutoConnectAsync();
        Assert.Single(wifi.Joins);
        now = now.AddSeconds(6);
        await j.AutoConnectAsync();
        Assert.Equal(2, wifi.Joins.Count);
    }

    [Fact]
    public async Task Auto_NoAttempts_NoLocationPermission_AsksForLocation()
    {
        locator.Location = null;
        locator.Authorized = false;
        var j = Make();
        await j.AutoConnectAsync();
        Assert.Empty(wifi.Joins);
        Assert.Equal(new Status.Failed("請允許定位，以自動尋找附近門市"), Last);
    }

    [Fact]
    public async Task Auto_NoAttempts_WithPermission_AsksToPickStore()
    {
        locator.Location = null;
        var j = Make();
        await j.AutoConnectAsync();
        Assert.Equal(new Status.Failed("無法辨識門市，請從選單選擇"), Last);
    }

    [Fact]
    public async Task Manual_InterruptsRunningAuto()
    {
        wifi.Gate = new TaskCompletionSource();
        wifi.CorrectPasswords.Add("typed");
        var j = Make();
        var auto = j.AutoConnectAsync();
        await wifi.JoinStarted.Task;
        var manual = j.ConnectAsync(Louisa, Yanping, "typed");
        wifi.Gate.SetResult();
        await Task.WhenAll(auto, manual);
        Assert.Equal(new[] { "a1", "typed" }, wifi.Joins.Select(x => x.Password));
        Assert.Equal(new Status.Connected("延平門市", "LouisaCoffee"), Last);
    }

    [Fact]
    public async Task Manual_ExplicitPassword_TriesOnlyThat_FailureMessage()
    {
        var j = Make();
        await j.ConnectAsync(Louisa, Yanping, "0000");
        Assert.Equal(new[] { "0000" }, wifi.Joins.Select(x => x.Password));
        Assert.Equal(new Status.Failed("密碼 0000 無法連上延平門市"), Last);
        Assert.True(reports[^1].Notify);
    }

    [Fact]
    public async Task Manual_NoPassword_TriesCandidates_FailureMessage()
    {
        var j = Make();
        await j.ConnectAsync(Louisa, Yanping);
        Assert.Equal(new[] { "a1", "a2" }, wifi.Joins.Select(x => x.Password));
        Assert.Equal(new Status.Failed("無法連上延平門市，請確認門市與密碼"), Last);
    }

    [Fact]
    public async Task Manual_AlreadyOnBrandSsid_ReportsConnected()
    {
        var j = Make();
        wifi.Link = new WifiLink("LouisaCoffee", Strong);
        await j.ConnectAsync(Louisa, Yanping);
        Assert.Empty(wifi.Joins);
        Assert.IsType<Status.Connected>(Last);
    }

    [Fact]
    public async Task Manual_OnOtherNetworkAndOnline_KeepsConnection()
    {
        var j = Make();
        wifi.Link = new WifiLink("Phone", "cc:00:00:00:00:01");
        net.Online = true;
        await j.ConnectAsync(Louisa, Yanping);
        Assert.Empty(wifi.Joins);
        Assert.Equal(new Status.Failed("目前已可上網，保留現有連線"), Last);
    }

    [Fact]
    public async Task Manual_NoBrandInRange_ReportsNotFound()
    {
        var j = new Joiner(wifi, locator, net, Catalog, Learned, () => true, () => now);
        j.StatusChanged += (s, n) => reports.Add((s, n));
        await j.ConnectAsync(Louisa, Yanping);
        Assert.Equal(new Status.Failed("附近找不到路易莎 Wi-Fi"), Last);
    }

    [Fact]
    public async Task Manual_NoInterface_ReportsFailure()
    {
        var j = Make();
        wifi.HasInterface = false;
        await j.ConnectAsync(Louisa, Yanping);
        Assert.Equal(new Status.Failed("請先開啟 Wi-Fi"), Last);
    }

    [Fact]
    public async Task Nearest_IgnoresSnoozeAndAutoSetting()
    {
        var j = Make();
        await j.AutoConnectAsync(); // all fail, snoozed
        autoOn = false;
        wifi.CorrectPasswords.Add("a1");
        await j.ConnectNearestAsync();
        Assert.Equal(new Status.Connected("延平門市", "LouisaCoffee"), Last);
    }

    [Fact]
    public async Task Auto_QueuedBehindUser_ThenSuperseded_DoesNothing()
    {
        wifi.Gate = new TaskCompletionSource();
        var j = Make();
        var first = j.ConnectAsync(Louisa, Yanping, "x1");      // holds the queue
        await wifi.JoinStarted.Task;
        var auto = j.AutoConnectAsync();                         // waits behind it
        var second = j.ConnectAsync(Louisa, Yanping, "x2");     // supersedes both
        wifi.Gate.SetResult();
        await Task.WhenAll(first, auto, second);
        Assert.Equal(new[] { "x1", "x2" }, wifi.Joins.Select(x => x.Password));
        Assert.Equal(2, wifi.Scans); // the two user runs; the stale auto run never scanned
    }

    [Fact]
    public async Task Auto_SnoozeHoldsWhenStrongestRouterSwaps()
    {
        var j = Make();
        await j.AutoConnectAsync();
        int joins = wifi.Joins.Count;
        wifi.Nearby[0] = wifi.Nearby[0] with { Rssi = -72 };    // Medium (-70) is now strongest
        now = now.AddSeconds(60);
        await j.AutoConnectAsync();
        Assert.Equal(joins, wifi.Joins.Count);
    }

    [Fact]
    public async Task Manual_Failure_KeepsAutoFromTakingOver()
    {
        var j = Make();
        await j.ConnectAsync(Louisa, Yanping, "0000");
        await j.AutoConnectAsync();
        Assert.Single(wifi.Joins);
        Assert.Equal(new Status.Failed("密碼 0000 無法連上延平門市"), Last);
    }

    [Fact]
    public async Task Interrupted_JoinThatLanded_IsDisconnected()
    {
        wifi.Gate = new TaskCompletionSource();
        wifi.CorrectPasswords.Add("a1");
        wifi.JoinLandsEvenIfCancelled = true;
        var j = Make();
        var auto = j.AutoConnectAsync();
        await wifi.JoinStarted.Task;
        var manual = j.ConnectAsync(Louisa, Yanping, "typed");
        wifi.Gate.SetResult();
        await Task.WhenAll(auto, manual);
        Assert.Equal(1, wifi.Disconnects);
        Assert.Equal(new[] { "a1", "typed" }, wifi.Joins.Select(x => x.Password));
    }

    [Fact]
    public async Task Manual_OnBrandSsid_Unlearned_LearnsRouterWithWindowsKey()
    {
        var j = Make();
        wifi.Link = new WifiLink("LouisaCoffee", Strong);
        wifi.CurrentKey = "a2";
        await j.ConnectAsync(Louisa, Yanping);
        Assert.Empty(wifi.Joins);
        Assert.Equal(new Learned("louisa/延平門市", "a2"), Learned.Lookup(Strong));
        Assert.Equal("a2", Learned.Lookup(Medium)?.Password);  // a second router heard well
        Assert.Null(Learned.Lookup(Weak));
        Assert.Equal(new Status.Connected("延平門市", "LouisaCoffee"), Last);
        Assert.Equal("延平門市", j.CurrentStoreLabel());
    }

    [Fact]
    public async Task Manual_OnBrandSsid_LearnedAsAnotherStore_IsCorrected()
    {
        Learned.Remember(new[] { Strong }, new StoreKey("louisa", "北門門市"), "b1");
        var j = Make();
        wifi.Link = new WifiLink("LouisaCoffee", Strong);
        wifi.CurrentKey = "a1";
        await j.ConnectAsync(Louisa, Yanping);
        Assert.Equal(new Learned("louisa/延平門市", "a1"), Learned.Lookup(Strong));
    }

    [Fact]
    public async Task Manual_OnBrandSsid_AlreadyLearnedAsThisStore_DoesNotRescan()
    {
        Learned.Remember(new[] { Strong }, new StoreKey("louisa", "延平門市"), "a1");
        var j = Make();
        wifi.Link = new WifiLink("LouisaCoffee", Strong);
        wifi.CurrentKey = "a1";
        await j.ConnectAsync(Louisa, Yanping);
        Assert.Equal(0, wifi.Scans);
        Assert.Equal(new Status.Connected("延平門市", "LouisaCoffee"), Last);
    }

    [Fact]
    public async Task Manual_OnBrandSsid_KeyUnreadable_OnlyReportsConnected()
    {
        var j = Make();
        wifi.Link = new WifiLink("LouisaCoffee", Strong);
        await j.ConnectAsync(Louisa, Yanping);
        Assert.Null(Learned.Lookup(Strong));
        Assert.Equal(new Status.Connected(null, "LouisaCoffee"), Last);
    }

    // MARK: open networks

    private static readonly BrandCatalog OpenCatalog = BrandCatalog.Load(new[]
    {
        System.Text.Json.JsonSerializer.Serialize(new Brand("starbucks", "星巴克", new[] { "Starbucks WiFi" }, new[]
        {
            S("站前門市", 25.0479),     // ~10 m from Here
            S("北門門市", 25.0560),     // ~900 m
        }, "open"), BrandCatalog.Json),
    }, Array.Empty<string>());

    private Joiner MakeOpen()
    {
        wifi.Nearby.AddRange(new[] { new Bss("Starbucks WiFi", Strong, -50), new Bss("Starbucks WiFi", Medium, -70) });
        wifi.CorrectPasswords.Add("");
        var j = new Joiner(wifi, locator, net, OpenCatalog, Learned, () => autoOn, () => now);
        j.StatusChanged += (s, n) => reports.Add((s, n));
        return j;
    }

    [Fact]
    public async Task Open_Auto_JoinsWithoutPassword_AndLearnsNearbyStore()
    {
        var j = MakeOpen();
        await j.AutoConnectAsync();
        Assert.Equal(new[] { (Strong, "") }, wifi.Joins);
        Assert.Equal(new Learned("starbucks/站前門市", ""), Learned.Lookup(Strong));
        Assert.Equal(new Status.Connected("站前門市", "Starbucks WiFi"), Last);
    }

    [Fact]
    public async Task Open_Auto_NoStoreNearby_StaysAwayQuietly()
    {
        locator.Location = new GeoPoint(25.0700, 121.5170); // ~2.5 km from any store
        var j = MakeOpen();
        await j.AutoConnectAsync();
        Assert.Empty(wifi.Joins);
        Assert.Empty(reports);
    }

    [Fact]
    public async Task Open_LinkedByWindows_LearnsStoreFromLocation()
    {
        var j = MakeOpen();
        wifi.Link = new WifiLink("Starbucks WiFi", Strong);
        await j.AutoConnectAsync();
        Assert.Empty(wifi.Joins);
        Assert.Equal("starbucks/站前門市", Learned.Lookup(Strong)?.Store);
        Assert.Equal(new StoreKey("starbucks", "站前門市"), j.CurrentStoreKey());
    }

    [Fact]
    public async Task Open_Manual_JoinsThatStore_AndLearnsWhenAlreadyLinked()
    {
        var j = MakeOpen();
        var brand = OpenCatalog.Brand("starbucks")!;
        await j.ConnectAsync(brand, brand.Named("北門門市")!, "ignored");
        Assert.Equal(new[] { (Strong, "") }, wifi.Joins);
        Assert.Equal(new Status.Connected("北門門市", "Starbucks WiFi"), Last);

        await j.ConnectAsync(brand, brand.Named("站前門市")!);   // already linked: just relabel
        Assert.Single(wifi.Joins);
        Assert.Equal("starbucks/站前門市", Learned.Lookup(Strong)?.Store);
    }

    [Fact]
    public void CurrentStatus_ReflectsLink()
    {
        var j = Make();
        Assert.IsType<Status.Idle>(j.CurrentStatus());
        wifi.Link = new WifiLink("LouisaCoffee", Weak);
        Assert.Equal(new Status.Connected(null, "LouisaCoffee"), j.CurrentStatus());
        Assert.Equal("已連上 LouisaCoffee", j.CurrentStatus().Text);
        Assert.Equal(new StoreKey("louisa", StoreKey.Unknown), j.CurrentStoreKey());
    }

    // MARK: independent cafe auto-join

    private Joiner Bare()
    {
        var j = new Joiner(wifi, locator, net, Catalog, Learned, () => autoOn, () => now);
        j.StatusChanged += (s, n) => reports.Add((s, n));
        return j;
    }

    private static readonly StoreKey Fika = new("mine", "Fika Fika Cafe");

    [Fact]
    public async Task Auto_NoBrandNetwork_JoinsNearbyCafeByGuess_AndRemembersPlace()
    {
        wifi.Nearby.Add(new Bss("FikaFika_5G", "cc:00:00:00:00:01", -45));
        wifi.CorrectPasswords.Add("23456789");
        var j = Bare();
        var saved = 0;
        j.FindCafe = _ => Task.FromResult<Joiner.CafePlan?>(new Joiner.CafePlan(
            "FikaFika_5G", "Fika Fika Cafe", new[] { "00000000", "23456789" },
            () => { saved++; return Task.FromResult(Fika); }));

        await j.AutoConnectAsync();

        Assert.Equal("FikaFika_5G", wifi.Link?.Ssid);
        Assert.Equal(1, saved);                              // place persisted only after the join worked
        Assert.Equal(Fika.ToString(), Learned.Lookup("cc:00:00:00:00:01")?.Store);
        Assert.Equal("23456789", Learned.Lookup("cc:00:00:00:00:01")?.Password);
    }

    [Fact]
    public async Task Auto_BrandNetworkPresent_IgnoresCafeGuess()
    {
        wifi.Nearby.AddRange(new[] { new Bss("LouisaCoffee", Strong, -50), new Bss("FikaFika_5G", "cc:00:00:00:00:01", -40) });
        wifi.CorrectPasswords.Add("a1");
        var called = 0;
        var j = Bare();
        j.FindCafe = _ => { called++; return Task.FromResult<Joiner.CafePlan?>(null); };

        await j.AutoConnectAsync();

        Assert.Equal(0, called);                              // a real brand store wins; no speculative cafe guessing
        Assert.Equal("LouisaCoffee", wifi.Link?.Ssid);
    }

    [Fact]
    public async Task Auto_CafeGuessFails_SnoozesAndDoesNotRetryImmediately()
    {
        wifi.Nearby.Add(new Bss("FikaFika_5G", "cc:00:00:00:00:01", -45));
        var calls = 0;
        var j = Bare();
        j.FindCafe = _ => { calls++; return Task.FromResult<Joiner.CafePlan?>(new Joiner.CafePlan(
            "FikaFika_5G", "Fika Fika Cafe", new[] { "00000000" }, () => Task.FromResult(Fika))); };

        await j.AutoConnectAsync();   // all guesses wrong -> snooze
        await j.AutoConnectAsync();   // snoozed -> must not even ask FindCafe again

        Assert.Null(wifi.Link);
        Assert.Equal(1, calls);
    }
}
