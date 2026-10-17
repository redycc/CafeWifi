using CafeWiFi.Core;
using CafeWiFi.Platform;

namespace CafeWiFi.App;

/// <summary>
/// Finds the right password for the cafe you're at and joins it. Wi-Fi work runs one job
/// at a time because scanning and joining block; a user action supersedes whatever is
/// running (bumps the epoch and cancels the join in progress).
/// </summary>
public sealed class Joiner
{
    private static readonly TimeSpan SnoozeFor = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(15);
    private const int RememberRssi = -75;

    private readonly IWifi wifi;
    private readonly ILocator locator;
    private readonly IConnectivity net;
    private readonly BrandCatalog catalog;
    private readonly LearnedStore learned;
    private readonly Func<bool> autoEnabled;
    private readonly Func<DateTimeOffset> clock;

    private readonly SemaphoreSlim queue = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> snoozed = new(); // BSSIDs we recently failed on; queue only
    private DateTimeOffset quietUntil = DateTimeOffset.MinValue;      // our own joins fire link events; queue only
    private int autoQueued;
    private int epoch;
    private CancellationTokenSource joinCts = new();

    /// <summary>Raised on a worker thread. The flag says whether to show a notification.</summary>
    public event Action<Status, bool>? StatusChanged;

    public Joiner(IWifi wifi, ILocator locator, IConnectivity net, BrandCatalog catalog, LearnedStore learned,
        Func<bool> autoEnabled, Func<DateTimeOffset> clock)
    {
        (this.wifi, this.locator, this.net, this.catalog, this.learned, this.autoEnabled, this.clock) =
            (wifi, locator, net, catalog, learned, autoEnabled, clock);
    }

    // MARK: state

    public Status CurrentStatus()
    {
        if (!wifi.HasInterface || wifi.CurrentLink() is not { } link) return new Status.Idle();
        if (catalog.BrandForSsid(link.Ssid) is not null) return new Status.Connected(CurrentStoreLabel(), link.Ssid);
        // One of the user's own cafes.
        return CurrentStoreKey() is { } key ? new Status.Connected(catalog.Label(key), link.Ssid) : new Status.Idle();
    }

    /// <summary>
    /// The cafe we're at: a chain store (or its 未知門市 if we haven't learned this router) or one
    /// of the user's own cafes; null off cafe Wi-Fi.
    /// </summary>
    public StoreKey? CurrentStoreKey()
    {
        if (wifi.CurrentLink() is not { } link) return null;
        if (catalog.BrandForSsid(link.Ssid) is not { } brand)
        {
            return link.Bssid is not null && learned.Lookup(link.Bssid) is { } own && StoreKey.Parse(own.Store) is { BrandId: Place.BrandId } key
                ? key : null;
        }
        if (link.Bssid is not null && learned.Lookup(link.Bssid) is { } l)
        {
            var key = StoreKey.Parse(l.Store);
            if (key.BrandId == brand.Id) return key;
        }
        return new StoreKey(brand.Id, StoreKey.Unknown);
    }

    public string? CurrentStoreLabel() => CurrentStoreKey() is { IsUnknown: false } k ? catalog.Label(k) : null;

    public IReadOnlySet<string> KnownStores() => learned.KnownStores();

    // MARK: entry points

    /// <summary>Wi-Fi changes, location changes and the timer call this. Bursts collapse into one run.</summary>
    public async Task AutoConnectAsync()
    {
        if (!autoEnabled() || Interlocked.Exchange(ref autoQueued, 1) == 1) return;
        int e = Volatile.Read(ref epoch);
        await queue.WaitAsync();
        try
        {
            // A user action that arrived while we waited outranks this run.
            if (Current(e)) await RunAuto(manual: false, e);
        }
        finally
        {
            queue.Release();
            Volatile.Write(ref autoQueued, 0);
        }
    }

    /// <summary>The user picked a store, optionally with the exact password to use.</summary>
    public async Task ConnectAsync(Brand brand, Store store, string? password = null)
    {
        int e = Interrupt();
        await queue.WaitAsync();
        try
        {
            if (!Current(e)) return;
            await RunStore(brand, store, password, e);
        }
        finally { queue.Release(); }
    }

    /// <summary>Connect to the nearest store, ignoring the auto-connect setting and snoozes.</summary>
    public async Task ConnectNearestAsync()
    {
        int e = Interrupt();
        await queue.WaitAsync();
        try
        {
            if (!Current(e)) return;
            snoozed.Clear();
            await RunAuto(manual: true, e);
        }
        finally { queue.Release(); }
    }

    // MARK: flows

    private async Task RunAuto(bool manual, int e)
    {
        if (!wifi.HasInterface)
        {
            if (manual) Report(new Status.Failed("請先開啟 Wi-Fi"), notify: true);
            return;
        }
        if (!manual && clock() < quietUntil) return;
        if (wifi.CurrentLink() is { } linked)
        {
            if (manual) Report(CurrentStatus(), notify: true);
            else LearnOpenByLocation(linked);
            return;
        }
        if (!manual && await net.IsOnlineAsync()) return; // e.g. on Ethernet; don't add Wi-Fi

        var bss = await wifi.ScanAsync();
        var best = bss.Where(b => catalog.BrandForSsid(b.Ssid) is not null).MaxBy(b => b.Rssi);
        if (best is null)
        {
            if (manual) Report(new Status.Idle(), notify: true);
            return;
        }
        var brand = catalog.BrandForSsid(best.Ssid)!;
        var nets = bss.Where(b => b.Ssid == best.Ssid).OrderByDescending(b => b.Rssi).ToList();
        if (IsSnoozed(nets)) return;

        var tries = AttemptPlanner.ForAuto(brand, nets.Select(n => n.Bssid).ToList(), learned, catalog, locator.Location);
        if (!Current(e)) return;
        if (tries.Count == 0 && brand.IsOpen)
        {
            // An open network with no store of that brand around: not necessarily the real one. Stay away quietly.
            Snooze(nets);
            return;
        }
        if (tries.Count == 0)
        {
            Snooze(nets);
            Report(new Status.Failed(locator.Authorized ? "無法辨識門市，請從選單選擇" : "請允許定位，以自動尋找附近門市"), notify: true);
            return;
        }

        Report(new Status.Connecting(catalog.Label(tries[0].Store)));
        foreach (var t in tries)
        {
            if (!Current(e)) return;
            if (await Join(nets, t.Password))
            {
                Remember(nets, t.Store, t.Password);
                Report(new Status.Connected(catalog.Label(t.Store), nets[0].Ssid), notify: true);
                return;
            }
        }
        if (!Current(e)) return;
        Snooze(nets);
        Report(new Status.Failed("自動連線失敗，請從選單選擇門市"), notify: true);
    }

    private async Task RunStore(Brand brand, Store store, string? password, int e)
    {
        var label = catalog.Label(new StoreKey(brand.Id, store.Name));
        if (!wifi.HasInterface)
        {
            Report(new Status.Failed("請先開啟 Wi-Fi"), notify: true);
            return;
        }
        // Trying passwords drops the current connection, so don't if it already works.
        if (wifi.CurrentLink() is { } link)
        {
            if (brand.Ssids.Contains(link.Ssid ?? "", StringComparer.OrdinalIgnoreCase))
            {
                await LearnCurrent(link, new StoreKey(brand.Id, store.Name));
                Report(CurrentStatus(), notify: true);
                return;
            }
            if (await net.IsOnlineAsync())
            {
                Report(new Status.Failed("目前已可上網，保留現有連線"), notify: true);
                return;
            }
        }

        Report(new Status.Connecting(label));
        var nets = (await wifi.ScanAsync())
            .Where(b => brand.Ssids.Contains(b.Ssid, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(b => b.Rssi).ToList();
        if (nets.Count == 0)
        {
            Report(new Status.Failed($"附近找不到{brand.Name} Wi-Fi"), notify: true);
            return;
        }
        // Several SSIDs per brand: stick to the strongest one's routers.
        nets = nets.Where(n => n.Ssid == nets[0].Ssid).ToList();

        foreach (var t in AttemptPlanner.ForStore(brand, store, nets.Select(n => n.Bssid).ToList(), learned, password))
        {
            if (!Current(e)) return;
            if (await Join(nets, t.Password))
            {
                Remember(nets, t.Store, t.Password);
                Report(new Status.Connected(label, nets[0].Ssid), notify: true);
                return;
            }
        }
        if (!Current(e)) return;
        // Let the user read why and pick again; auto shouldn't retry this store underneath them.
        Snooze(nets);
        Report(new Status.Failed(brand.IsOpen ? $"無法連上{label}"
            : password is not null ? $"密碼 {password} 無法連上{label}" : $"無法連上{label}，請確認門市與密碼"), notify: true);
    }

    /// <summary>
    /// Already on the brand's Wi-Fi (joined by Windows, not us) and the user says which store
    /// this is: remember this router (with the password Windows used, if any) so it's recognized from now on.
    /// </summary>
    private async Task LearnCurrent(WifiLink link, StoreKey store)
    {
        if (link.Bssid is null || learned.Lookup(link.Bssid)?.Store == store.ToString()) return;
        // Open networks have no password to learn; otherwise use the one Windows joined with.
        var password = catalog.Brand(store.BrandId)?.IsOpen == true ? "" : wifi.CurrentPassword();
        if (password is null || password.Length == 0 && catalog.Brand(store.BrandId)?.IsOpen != true)
        {
            Log.Write("cannot read the current network's password; nothing learned");
            return;
        }
        var nets = (await wifi.ScanAsync()).Where(b => b.Ssid == link.Ssid && b.Rssi > RememberRssi).Select(b => b.Bssid);
        learned.Remember(nets.Prepend(link.Bssid).Distinct(), store, password);
        Log.Write($"learned {link.Bssid} as {store}");
    }

    /// <summary>On an open brand's network that Windows joined: name the router after the store we're standing in.</summary>
    private void LearnOpenByLocation(WifiLink link)
    {
        if (link.Bssid is null || catalog.BrandForSsid(link.Ssid) is not { IsOpen: true } brand) return;
        if (learned.Lookup(link.Bssid) is not null) return;
        if (AttemptPlanner.NearestOpenStore(brand, catalog, locator.Location) is not { } store) return;
        learned.Remember(new[] { link.Bssid }, store, "");
        Log.Write($"learned {link.Bssid} as {store} by location");
    }

    // MARK: helpers

    private async Task<bool> Join(IReadOnlyList<Bss> nets, string password)
    {
        CancellationToken ct;
        lock (this) ct = joinCts.Token;
        try
        {
            if (!await wifi.TryJoinAsync(nets[0], password, ct)) return false;
            quietUntil = clock() + Quiet;
            return true;
        }
        catch (OperationCanceledException)
        {
            // We stopped waiting, but Windows may still finish the join with this password.
            // Drop it so the user's action starts clean instead of finding us "connected".
            if (wifi.CurrentLink()?.Ssid == nets[0].Ssid) wifi.Disconnect();
            return false;
        }
    }

    /// <summary>A store has several routers whose signal order changes, so snooze all of them.</summary>
    private void Snooze(IEnumerable<Bss> nets)
    {
        foreach (var n in nets) snoozed[n.Bssid] = clock() + SnoozeFor;
    }

    private bool IsSnoozed(IEnumerable<Bss> nets) =>
        nets.Any(n => snoozed.TryGetValue(n.Bssid, out var until) && until > clock());

    /// <summary>Remember the strongest router plus every router heard well, so the store is recognized from any of them.</summary>
    private void Remember(IReadOnlyList<Bss> nets, StoreKey store, string password) =>
        learned.Remember(nets.Where((n, i) => i == 0 || n.Rssi > RememberRssi).Select(n => n.Bssid), store, password);

    private int Interrupt()
    {
        lock (this)
        {
            // Bump first: Cancel() can run the interrupted loop's continuation inline,
            // and that loop must already see it's been superseded.
            int e = Interlocked.Increment(ref epoch);
            var old = joinCts;
            joinCts = new CancellationTokenSource();
            old.Cancel();
            old.Dispose();
            return e;
        }
    }

    private bool Current(int e) => Volatile.Read(ref epoch) == e;

    private void Report(Status status, bool notify = false)
    {
        Log.Write(status.Text);
        StatusChanged?.Invoke(status, notify);
    }
}
