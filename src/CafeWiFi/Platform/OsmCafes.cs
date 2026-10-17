using System.Globalization;
using System.Text.Json;
using CafeWiFi.Core;

namespace CafeWiFi.Platform;

public interface ICafeMap
{
    Task<IReadOnlyList<CafeCandidate>> CafesNearAsync(GeoPoint here, double meters, CancellationToken ct = default);
}

/// <summary>
/// Cafes around a point from OpenStreetMap, via the public Overpass API. Sends only the
/// coordinates and radius; called once for each new Wi-Fi network, never on a timer.
/// The public servers are often slow or down, so they're tried one after another.
/// </summary>
public sealed class OsmCafes : ICafeMap
{
    public static readonly string[] Endpoints =
    {
        "https://overpass-api.de/api/interpreter",
        "https://overpass.kumi.systems/api/interpreter",
        "https://z.overpass-api.de/api/interpreter",
        "https://maps.mail.ru/osm/tools/overpass/api/interpreter",
    };

    private static readonly TimeSpan PerServer = TimeSpan.FromSeconds(30);

    private readonly HttpClient http;

    public OsmCafes(HttpMessageHandler? handler = null)
    {
        http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = Timeout.InfiniteTimeSpan; // each server gets its own deadline below
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CafeWiFi (https://github.com/redycc/CafeWifi)");
    }

    public async Task<IReadOnlyList<CafeCandidate>> CafesNearAsync(GeoPoint here, double meters, CancellationToken ct = default)
    {
        string around = string.Format(CultureInfo.InvariantCulture, "around:{0:0},{1:0.######},{2:0.######}", meters, here.Lat, here.Lng);
        var query = $"[out:json][timeout:25];(node({around})[amenity=cafe];way({around})[amenity=cafe];);out center tags;";
        Exception? last = null;
        foreach (var endpoint in Endpoints)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(PerServer);
            try
            {
                using var res = await http.PostAsync(endpoint,
                    new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = query }), deadline.Token);
                res.EnsureSuccessStatusCode();
                return Parse(await res.Content.ReadAsStringAsync(deadline.Token));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                Log.Write($"overpass {new Uri(endpoint).Host}: {ex.Message}");
                last = ex;
            }
        }
        throw new HttpRequestException("no Overpass server answered", last);
    }

    private static IReadOnlyList<CafeCandidate> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);

        var cafes = new List<CafeCandidate>();
        foreach (var e in doc.RootElement.GetProperty("elements").EnumerateArray())
        {
            var tags = e.TryGetProperty("tags", out var t) ? t : default;
            string? Tag(string key) => tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty(key, out var v) ? v.GetString() : null;
            var name = Tag("name") ?? Tag("name:zh") ?? Tag("name:en") ?? Tag("brand");
            if (string.IsNullOrWhiteSpace(name)) continue;
            var point = e.TryGetProperty("center", out var c) ? c : e;
            if (!point.TryGetProperty("lat", out var lat) || !point.TryGetProperty("lon", out var lon)) continue;
            var street = Tag("addr:street") is { } s ? s + (Tag("addr:housenumber") is { } n ? n + "號" : "") : null;
            cafes.Add(new CafeCandidate($"{e.GetProperty("type").GetString()}/{e.GetProperty("id").GetInt64()}", name.Trim(),
                lat.GetDouble(), lon.GetDouble(), Tag("addr:city")?.Replace("臺", "台"), Tag("addr:full") ?? street));
        }
        return cafes;
    }
}
