using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace CafeWiFi.Core;

/// <summary>
/// An independent cafe the user has been to, recognized automatically (or added by hand).
/// Its routers live in learned.json under StoreKey("mine", Id), like chain stores.
/// </summary>
/// <param name="Source">"osm" (found on OpenStreetMap), "ssid" (the Wi-Fi name says cafe) or "manual".</param>
public sealed record Place(string Id, string Name, string Ssid, double Lat, double Lng, string? City, string? Address,
    string? OsmId, string Source, DateTimeOffset Added)
{
    public const string BrandId = "mine";
    [JsonIgnore] public StoreKey Key => new(BrandId, Id);
    [JsonIgnore] public GeoPoint Location => new(Lat, Lng);
}

/// <summary>A cafe on the map near us, from OpenStreetMap.</summary>
public sealed record CafeCandidate(string Id, string Name, double Lat, double Lng, string? City, string? Address, string? Phone = null);

/// <summary>
/// places.json: the user's own cafes, Wi-Fi names they said aren't cafes, and routers already
/// looked up without finding a cafe (so we don't ask the map again for a month).
/// </summary>
public sealed class PlaceStore
{
    private static readonly TimeSpan RecheckAfter = TimeSpan.FromDays(30);
    private const double SamePlaceMeters = 200;

    private sealed class Data
    {
        public List<Place> Places { get; set; } = new();
        public Dictionary<string, DateTimeOffset> NotCafe { get; set; } = new();
        public Dictionary<string, DateTimeOffset> Checked { get; set; } = new();
    }

    private readonly string path;
    private readonly object gate = new();
    private readonly Data data;

    public event Action? Changed;

    public PlaceStore(string path)
    {
        this.path = path;
        data = JsonFile.Load<Data>(path);
    }

    public IReadOnlyList<Place> Places
    {
        get { lock (gate) return data.Places.ToList(); }
    }

    public Place? Find(string id)
    {
        lock (gate) return data.Places.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>Adds a cafe, or returns the one we already have for it (same map entry, or same Wi-Fi nearby).</summary>
    public Place Add(Place place)
    {
        Place result;
        lock (gate)
        {
            var existing = data.Places.FirstOrDefault(p =>
                place.OsmId is not null && p.OsmId == place.OsmId
                || p.Ssid == place.Ssid && Geo.Meters(p.Location, place.Location) < SamePlaceMeters);
            if (existing is not null) return existing;
            var id = place.Name;
            for (int n = 2; data.Places.Any(p => p.Id == id); n++) id = $"{place.Name} {n}";
            result = place with { Id = id };
            data.Places.Add(result);
            Save();
        }
        Changed?.Invoke();
        return result;
    }

    /// <summary>"不是咖啡廳": forget the place and never suggest its Wi-Fi again.</summary>
    public void MarkNotCafe(string id)
    {
        lock (gate)
        {
            var place = data.Places.FirstOrDefault(p => p.Id == id);
            if (place is null) return;
            data.Places.Remove(place);
            data.NotCafe[place.Ssid] = DateTimeOffset.Now;
            Save();
        }
        Changed?.Invoke();
    }

    public bool IsNotCafe(string ssid)
    {
        lock (gate) return data.NotCafe.ContainsKey(ssid);
    }

    public void MarkChecked(string bssid, DateTimeOffset when)
    {
        lock (gate)
        {
            data.Checked[Bssid.Normalize(bssid)] = when;
            Save();
        }
    }

    public bool CheckedRecently(string bssid, DateTimeOffset now)
    {
        lock (gate) return data.Checked.TryGetValue(Bssid.Normalize(bssid), out var at) && now - at < RecheckAfter;
    }

    private void Save()
    {
        try { JsonFile.Save(path, data); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Write($"cannot save places: {ex.Message}"); }
    }
}

/// <summary>Decides whether the network we're on belongs to a cafe, and which one.</summary>
public static class CafeMatcher
{
    /// <summary>A cafe this close is taken to be where we are, whatever its Wi-Fi is called.</summary>
    public const double NearMeters = 40;

    /// <summary>A cafe whose name matches the Wi-Fi name may be this far: location indoors is rough.</summary>
    public const double NamedMeters = 120;

    private static readonly string[] HotspotWords =
        { "iphone", "android", "galaxy", "pixel", "redmi", "xiaomi", "oppo", "vivo", "huawei", "hotspot", "熱點", "热点" };

    private static readonly string[] CafeWords =
        { "coffee", "cafe", "kaffee", "espresso", "roast", "brew", "咖啡", "珈琲", "珈琲", "咖啡館" };

    private static readonly string[] NoiseWords = { "5g", "24g", "2g", "guest", "wifi", "free", "plus", "ext" };

    /// <summary>Phones, printers and the like: never cafes, whatever the map says.</summary>
    public static bool LooksLikeHotspot(string ssid)
    {
        var s = ssid.ToLowerInvariant();
        return s.StartsWith("direct-", StringComparison.Ordinal) || HotspotWords.Any(s.Contains);
    }

    public static bool LooksLikeCafeName(string ssid)
    {
        var s = Fold(ssid);
        return CafeWords.Any(w => s.Contains(Fold(w), StringComparison.Ordinal));
    }

    public static CafeCandidate? Match(string ssid, GeoPoint here, IEnumerable<CafeCandidate> cafes)
    {
        if (LooksLikeHotspot(ssid)) return null;
        var near = cafes.Select(c => (Cafe: c, Meters: Geo.Meters(here, new GeoPoint(c.Lat, c.Lng))))
            .OrderBy(c => c.Meters).ToList();
        var named = near.FirstOrDefault(c => c.Meters <= NamedMeters && Similar(ssid, c.Cafe.Name));
        if (named.Cafe is not null) return named.Cafe;
        return near.FirstOrDefault(c => c.Meters <= NearMeters).Cafe;
    }

    /// <summary>"andcouple" ~ "&amp;Couple", "fika_5G" ~ "Fika Fika Cafe".</summary>
    public static bool Similar(string ssid, string name)
    {
        string a = Key(ssid), b = Key(name);
        if (a.Length >= 3 && b.Length >= 3 && (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal)))
            return true;
        var tokens = Tokens(name).Where(t => t.Length >= 4).ToList();
        return Tokens(ssid).Any(t => t.Length >= 4 && tokens.Contains(t));
    }

    private static string Key(string s) => string.Concat(Tokens(s));

    private static IEnumerable<string> Tokens(string s)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in Fold(s.Replace("&", " and ")))
        {
            if (char.IsLetterOrDigit(ch)) current.Append(ch);
            else Flush();
        }
        Flush();
        return words;

        void Flush()
        {
            if (current.Length > 0 && !NoiseWords.Contains(current.ToString())) words.Add(current.ToString());
            current.Clear();
        }
    }

    /// <summary>Lowercase without accents: "Café" → "cafe".</summary>
    private static string Fold(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
