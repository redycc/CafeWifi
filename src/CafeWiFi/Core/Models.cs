using System.Globalization;

namespace CafeWiFi.Core;

/// <summary>One shop. Passwords are candidates, most likely first, computed by the brand's script.</summary>
public sealed record Store(string Name, string Address, string City, string Phone, double Lat, double Lng, IReadOnlyList<string> Passwords)
{
    /// <summary>The scripts write 0,0 when a store's coordinates are unknown.</summary>
    public bool HasLocation => Lat != 0 || Lng != 0;
    public GeoPoint Location => new(Lat, Lng);
}

/// <summary>
/// A cafe chain: every store broadcasts one of <see cref="Ssids"/>. Security is "wpa2psk"
/// (the default: a password per store) or "open" (no password, usually a terms page after joining).
/// </summary>
public sealed record Brand(string Id, string Name, IReadOnlyList<string> Ssids, IReadOnlyList<Store> Stores, string? Security = null)
{
    public bool IsOpen => string.Equals(Security, "open", StringComparison.OrdinalIgnoreCase);

    public Store? Named(string name) => Stores.FirstOrDefault(s => s.Name == name);
}

/// <summary>Identifies a store across brands; stored as "brandId/storeName" in learned.json and usage.json.</summary>
public readonly record struct StoreKey(string BrandId, string StoreName)
{
    public const string Unknown = "未知門市";

    public bool IsUnknown => StoreName == Unknown;

    public override string ToString() => $"{BrandId}/{StoreName}";

    public static StoreKey Parse(string s)
    {
        int slash = s.IndexOf('/');
        return slash < 0 ? new StoreKey("", s) : new StoreKey(s[..slash], s[(slash + 1)..]);
    }
}

public readonly record struct GeoPoint(double Lat, double Lng);

public static class Geo
{
    /// <summary>Great-circle distance in meters.</summary>
    public static double Meters(GeoPoint a, GeoPoint b)
    {
        const double R = 6371008.8;
        double dLat = Rad(b.Lat - a.Lat), dLng = Rad(b.Lng - a.Lng);
        double h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Rad(a.Lat)) * Math.Cos(Rad(b.Lat)) * Math.Pow(Math.Sin(dLng / 2), 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    private static double Rad(double deg) => deg * Math.PI / 180;

    /// <summary>"350 m" or "1.2 km", for menus.</summary>
    public static string Format(double meters) =>
        meters < 1000 ? $"{(int)(meters / 10) * 10} m" : (meters / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " km";
}

public static class Bssid
{
    /// <summary>Lowercase, colon-separated, two digits per octet: "A:B:..." / "AABB..." / "aa-bb-..." → "0a:0b:...".</summary>
    public static string Normalize(string raw)
    {
        var parts = raw.Contains(':') || raw.Contains('-')
            ? raw.Split(':', '-').Select(p => p.PadLeft(2, '0'))
            : raw.Chunk(2).Select(c => new string(c));
        return string.Join(":", parts).ToLowerInvariant();
    }
}
