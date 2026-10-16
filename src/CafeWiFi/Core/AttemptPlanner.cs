namespace CafeWiFi.Core;

public readonly record struct Attempt(StoreKey Store, string Password);

/// <summary>
/// Decides which passwords to try, in order. A wrong password costs 15–20 s, so the
/// likeliest go first and nothing is tried twice.
/// </summary>
public static class AttemptPlanner
{
    public const int NearestStores = 3;
    public const double MaxMeters = 5000;

    /// <summary>
    /// Open networks are auto-joined only this close to a real store (or on a router we know):
    /// anyone can broadcast "Starbucks WiFi", and an open network will take anyone.
    /// </summary>
    public const double OpenMeters = 300;

    /// <summary>The open brand's store we're standing in, judged by location.</summary>
    public static StoreKey? NearestOpenStore(Brand brand, BrandCatalog catalog, GeoPoint? here) =>
        here is { } h && catalog.Nearest(h, 1, brandId: brand.Id) is [var n] && n.Meters <= OpenMeters
            ? new StoreKey(brand.Id, n.Store.Name) : null;

    /// <summary>
    /// Routers we've joined before, then the nearest stores' candidates interleaved
    /// (every store's best guess before anyone's second guess).
    /// </summary>
    /// <param name="bssids">This SSID's routers, strongest first.</param>
    public static IReadOnlyList<Attempt> ForAuto(Brand brand, IReadOnlyList<string> bssids, LearnedStore learned,
        BrandCatalog catalog, GeoPoint? here)
    {
        var tries = LearnedFor(brand, bssids, learned).ToList();
        if (brand.IsOpen)
        {
            // Nothing to guess: one open join, labeled with the router's store or the one we're at.
            if (tries.Count > 0) return new[] { tries[0] };
            return NearestOpenStore(brand, catalog, here) is { } store ? new[] { new Attempt(store, "") } : Array.Empty<Attempt>();
        }
        if (here is { } h)
        {
            var near = catalog.Nearest(h, NearestStores, brandId: brand.Id)
                .Where(n => n.Meters < MaxMeters).Select(n => n.Store).ToList();
            int depth = near.Count == 0 ? 0 : near.Max(s => s.Passwords.Count);
            for (int n = 0; n < depth; n++)
                foreach (var s in near.Where(s => n < s.Passwords.Count))
                    tries.Add(new Attempt(new StoreKey(brand.Id, s.Name), s.Passwords[n]));
        }
        return Unique(tries);
    }

    /// <summary>The password the user picked, or what worked at this store before followed by its candidates.</summary>
    public static IReadOnlyList<Attempt> ForStore(Brand brand, Store store, IReadOnlyList<string> bssids,
        LearnedStore learned, string? password)
    {
        var key = new StoreKey(brand.Id, store.Name);
        if (brand.IsOpen) return new[] { new Attempt(key, "") };
        if (password is not null) return new[] { new Attempt(key, password) };
        var tries = LearnedFor(brand, bssids, learned).Where(a => a.Store == key)
            .Concat(store.Passwords.Select(p => new Attempt(key, p)));
        return Unique(tries);
    }

    private static IEnumerable<Attempt> LearnedFor(Brand brand, IEnumerable<string> bssids, LearnedStore learned) =>
        bssids.Select(learned.Lookup).OfType<Learned>()
            .Select(l => new Attempt(StoreKey.Parse(l.Store), l.Password))
            .Where(a => a.Store.BrandId == brand.Id);

    private static IReadOnlyList<Attempt> Unique(IEnumerable<Attempt> tries)
    {
        var seen = new HashSet<string>();
        return tries.Where(t => seen.Add(t.Password)).ToList();
    }
}
