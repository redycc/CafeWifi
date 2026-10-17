using System.Globalization;

namespace CafeWiFi.Core;

/// <summary>Where a store or own cafe is, for the map. Kind is "chain" or "own".</summary>
public sealed record FootprintPlace(string Label, double Lat, double Lng, string? City, string Kind);

/// <summary>One cafe on the footprint map.</summary>
public sealed record FootprintSpot(string Id, string Label, string Kind, double Lat, double Lng, string? City,
    double Seconds, string Text, int Days, string LastVisit);

public sealed record FootprintView(string Heading, IReadOnlyList<StatsTile> Tiles, IReadOnlyList<FootprintSpot> Spots,
    IReadOnlyList<StatsRangeOption> Ranges, string Range);

/// <summary>"足跡": every cafe visited in a range, placed on a map and sized by time spent.</summary>
public static class FootprintModel
{
    public static FootprintView Build(IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> usage, StatsRange range,
        DateOnly today, Func<StoreKey, FootprintPlace?> resolve)
    {
        var period = Period.For(range, today);
        var visits = new Dictionary<string, (double Seconds, int Days, DateOnly Last)>();
        double total = 0;
        foreach (var day in period.Days())
        {
            if (!usage.TryGetValue(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out var stores)) continue;
            foreach (var (id, seconds) in stores.Where(s => s.Value > 0))
            {
                total += seconds;
                var v = visits.GetValueOrDefault(id);
                visits[id] = (v.Seconds + seconds, v.Days + 1, day);
            }
        }

        var spots = visits
            .Select(v => (v, place: resolve(StoreKey.Parse(v.Key))))
            .Where(x => x.place is { } p && (p.Lat != 0 || p.Lng != 0))
            .Select(x => new FootprintSpot(x.v.Key, x.place!.Label, x.place.Kind, x.place.Lat, x.place.Lng, x.place.City,
                x.v.Value.Seconds, StatsModel.Duration(x.v.Value.Seconds), x.v.Value.Days,
                $"{x.v.Value.Last.Month}月{x.v.Value.Last.Day}日"))
            .OrderByDescending(s => s.Seconds).ThenBy(s => s.Label, StringComparer.Ordinal)
            .ToList();

        var tiles = new[]
        {
            new StatsTile("咖啡廳", $"{spots.Count} 家"),
            new StatsTile("城市", $"{spots.Select(s => s.City).OfType<string>().Distinct().Count()} 個"),
            new StatsTile("總共待了", StatsModel.Duration(total)),
            new StatsTile("最常去", spots.FirstOrDefault()?.Label ?? "—"),
        };

        var years = usage.Keys.Where(k => k.Length >= 4 && int.TryParse(k[..4], out _))
            .Select(k => int.Parse(k[..4], CultureInfo.InvariantCulture)).Append(today.Year).Distinct().OrderDescending();
        var ranges = new[] { new StatsRangeOption("past", "過去一年") }
            .Concat(years.Select(y => new StatsRangeOption(y.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture))))
            .ToList();

        return new FootprintView($"{range.Heading}的咖啡廳足跡", tiles, spots, ranges, range.Id);
    }
}
