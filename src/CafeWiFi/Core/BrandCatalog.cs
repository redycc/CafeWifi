using System.Reflection;
using System.Text.Json;

namespace CafeWiFi.Core;

/// <summary>
/// Every known brand. Built-in brands are embedded as "brands.&lt;id&gt;.json"; a JSON in the
/// user's brands folder with the same id replaces the built-in one, and new ids add brands.
/// </summary>
public sealed class BrandCatalog
{
    public IReadOnlyList<Brand> Brands { get; }

    private BrandCatalog(IReadOnlyList<Brand> brands) => Brands = brands;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static BrandCatalog Load(IEnumerable<string> builtInJson, IEnumerable<string> userJson)
    {
        var brands = new List<Brand>();
        foreach (var json in builtInJson.Concat(userJson))
        {
            var brand = Parse(json);
            if (brand is null) continue;
            int existing = brands.FindIndex(b => b.Id == brand.Id);
            if (existing >= 0) brands[existing] = brand; else brands.Add(brand);
        }
        return new BrandCatalog(brands);
    }

    public static BrandCatalog LoadDefault(string? userBrandsDir)
    {
        var asm = Assembly.GetExecutingAssembly();
        var builtIn = asm.GetManifestResourceNames()
            .Where(n => n.StartsWith("brands.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(n => { using var r = new StreamReader(asm.GetManifestResourceStream(n)!); return r.ReadToEnd(); })
            .ToList();
        var user = userBrandsDir is not null && Directory.Exists(userBrandsDir)
            ? Directory.GetFiles(userBrandsDir, "*.json").Order(StringComparer.Ordinal).Select(File.ReadAllText).ToList()
            : new List<string>();
        return Load(builtIn, user);
    }

    private static Brand? Parse(string json)
    {
        try
        {
            var b = JsonSerializer.Deserialize<Brand>(json, Json);
            if (b is null || string.IsNullOrWhiteSpace(b.Id) || b.Ssids is null) return null;
            return b with { Stores = b.Stores ?? Array.Empty<Store>() };
        }
        catch (JsonException ex)
        {
            Log.Write($"skipping brand file: {ex.Message}");
            return null;
        }
    }

    public Brand? BrandForSsid(string? ssid) => string.IsNullOrEmpty(ssid)
        ? null
        : Brands.FirstOrDefault(b => b.Ssids.Any(s => string.Equals(s, ssid, StringComparison.OrdinalIgnoreCase)));

    public Brand? Brand(string id) => Brands.FirstOrDefault(b => b.Id == id);

    public Store? Find(StoreKey key) => Brand(key.BrandId)?.Named(key.StoreName);

    /// <summary>A store's display name, prefixed with its brand once there's more than one brand.</summary>
    public string Label(StoreKey key) =>
        Brands.Count > 1 && Brand(key.BrandId) is { } b ? $"{b.Name} {key.StoreName}" : key.StoreName;

    /// <summary>Stores with coordinates, closest first.</summary>
    public IReadOnlyList<(Brand Brand, Store Store, double Meters)> Nearest(
        GeoPoint here, int limit, Func<Brand, Store, bool>? filter = null, string? brandId = null) =>
        Brands.Where(b => brandId is null || b.Id == brandId)
            .SelectMany(b => b.Stores.Where(s => s.HasLocation && (filter is null || filter(b, s)))
                .Select(s => (Brand: b, Store: s, Meters: Geo.Meters(here, s.Location))))
            .OrderBy(x => x.Meters)
            .Take(limit)
            .ToList();

    private static readonly string[] CityOrder =
    {
        "台北市", "新北市", "基隆市", "桃園市", "新竹市", "新竹縣", "苗栗縣", "台中市", "彰化縣", "南投縣", "雲林縣",
        "嘉義市", "嘉義縣", "台南市", "高雄市", "屏東縣", "宜蘭縣", "花蓮縣", "台東縣", "澎湖縣", "金門縣", "連江縣",
    };

    /// <summary>Stores grouped by city: Taiwan's usual north-to-south order, then any other city by name.</summary>
    public static IReadOnlyList<(string City, IReadOnlyList<Store> Stores)> ByCity(IEnumerable<Store> stores)
    {
        int Rank(string c) { int i = Array.IndexOf(CityOrder, c); return i < 0 ? CityOrder.Length : i; }
        return stores.GroupBy(s => string.IsNullOrWhiteSpace(s.City) ? "其他" : s.City)
            .OrderBy(g => Rank(g.Key)).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, (IReadOnlyList<Store>)g.OrderBy(s => s.Name, StringComparer.Ordinal).ToList()))
            .ToList();
    }

    /// <summary>For "--connect &lt;query&gt;": an exact store name wins, otherwise every search hit.</summary>
    public IReadOnlyList<(Brand Brand, Store Store)> Resolve(string query, string? brandId = null)
    {
        var exact = Brands.Where(b => brandId is null || b.Id == brandId)
            .SelectMany(b => b.Stores.Where(s => s.Name == query.Trim()).Select(s => (Brand: b, Store: s)))
            .ToList();
        return exact.Count > 0 ? exact : Search(query, brandId);
    }

    /// <summary>Stores whose name or address contains every whitespace-separated token.</summary>
    public IReadOnlyList<(Brand Brand, Store Store)> Search(string query, string? brandId = null)
    {
        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return Array.Empty<(Brand, Store)>();
        return Brands.Where(b => brandId is null || b.Id == brandId)
            .SelectMany(b => b.Stores.Select(s => (Brand: b, Store: s)))
            .Where(x => tokens.All(t => x.Store.Name.Contains(t, StringComparison.OrdinalIgnoreCase)
                                        || x.Store.Address.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
