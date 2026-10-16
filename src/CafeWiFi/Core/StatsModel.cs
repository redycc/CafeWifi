using System.Globalization;

namespace CafeWiFi.Core;

public abstract record StatsRange
{
    public sealed record PastYear : StatsRange;
    public sealed record Year(int Y) : StatsRange;

    public string Id => this is Year y ? y.Y.ToString(CultureInfo.InvariantCulture) : "past";
    public string Title => this is Year y ? y.Y.ToString(CultureInfo.InvariantCulture) : "過去一年";
    public string Heading => this is Year y ? $"{y.Y} 年" : "過去一年";

    public static StatsRange Parse(string? id) =>
        int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out int y) && y is > 1900 and < 3000
            ? new Year(y) : new PastYear();
}

/// <summary>The days a range covers and the Monday-first grid of weeks that shows them.</summary>
public sealed record Period(DateOnly First, DateOnly End, DateOnly Last, DateOnly GridStart, int Weeks)
{
    public static Period For(StatsRange range, DateOnly today)
    {
        var (first, end) = range is StatsRange.Year y
            ? (new DateOnly(y.Y, 1, 1), new DateOnly(y.Y, 12, 31))
            : (today.AddDays(-364), today);
        var last = end < today ? end : today;
        var gridStart = Monday(first);
        int weeks = (Monday(end).DayNumber - gridStart.DayNumber) / 7 + 1;
        return new Period(first, end, last, gridStart, weeks);
    }

    public static DateOnly Monday(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    /// <summary>Counted days: First through Last (never after today).</summary>
    public IEnumerable<DateOnly> Days()
    {
        for (var d = First; d <= Last; d = d.AddDays(1)) yield return d;
    }
}

// What stats.html renders. Property names become camelCase JSON.
public sealed record StatsTile(string Label, string Value);
public sealed record StatsCell(string Date, double Seconds, int Level, bool InRange, bool Future);
public sealed record StatsMonthLabel(int Week, string Label);
public sealed record StatsBar(string Id, string Label, double Seconds, string Text);
public sealed record StatsMonth(string Label, double Seconds, string Text);
public sealed record StatsYear(int Year, double Seconds, string Text);
public sealed record StatsRangeOption(string Id, string Title);
public sealed record StatsDay(string Date, string Title, string Detail);

public sealed record StatsView(
    string Heading,
    IReadOnlyList<StatsTile> Tiles,
    IReadOnlyList<IReadOnlyList<StatsCell>> Weeks,
    IReadOnlyList<StatsMonthLabel> MonthLabels,
    IReadOnlyList<StatsBar> Stores,
    IReadOnlyList<StatsMonth> Months,
    IReadOnlyList<StatsYear> Years,
    IReadOnlyList<StatsRangeOption> Ranges,
    string Range,
    string? Store,
    string? StoreLabel,
    StatsDay? Day);

/// <summary>
/// "連線統計", modeled on GitHub's contribution graph: a year of days, the selected day's
/// stores, and the time broken down by store, month or year. Optionally limited to one store.
/// </summary>
public static class StatsModel
{
    private static string Key(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static StatsView Build(IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> usage, StatsRange range,
        string? store, DateOnly today, Func<string, string> label, DateOnly? day = null)
    {
        var period = Period.For(range, today);
        // Seconds per day, limited to the selected store if any.
        var perDay = usage.ToDictionary(d => d.Key,
            d => store is null ? d.Value.Values.Sum() : d.Value.GetValueOrDefault(store));
        double Seconds(DateOnly d) => perDay.GetValueOrDefault(Key(d));

        var days = period.Days().Select(Seconds).ToList();
        double total = days.Sum();
        int visits = days.Count(s => s > 0);
        var stores = StoreTotals(usage, period);

        var tiles = new[]
        {
            new StatsTile("到訪天數", $"{visits} 天"),
            new StatsTile("到訪門市", $"{stores.Count(s => !StoreKey.Parse(s.Id).IsUnknown)} 家"),
            new StatsTile("最長連續", $"{LongestStreak(days)} 天"),
            new StatsTile("平均每天", visits > 0 ? Duration(total / visits) : "—"),
        };

        var weeks = new List<IReadOnlyList<StatsCell>>();
        var monthLabels = new List<StatsMonthLabel>();
        for (int w = 0; w < period.Weeks; w++)
        {
            var column = new List<StatsCell>();
            for (int r = 0; r < 7; r++)
            {
                var d = period.GridStart.AddDays(w * 7 + r);
                bool inRange = d >= period.First && d <= period.End;
                double s = d <= period.Last && inRange ? Seconds(d) : 0;
                column.Add(new StatsCell(Key(d), s, Level(s), inRange, d > today));
                // Label a column when a month starts in it (or the range's first column).
                if (inRange && (d.Day == 1 || d == period.First) && (monthLabels.Count == 0 || monthLabels[^1].Week != w))
                    monthLabels.Add(new StatsMonthLabel(w, $"{d.Month}月"));
            }
            weeks.Add(column);
        }
        // Drop a label crowded out by the next one (a range starting late in a month).
        for (int i = monthLabels.Count - 2; i >= 0; i--)
            if (monthLabels[i + 1].Week - monthLabels[i].Week < 3) monthLabels.RemoveAt(i);

        var lastMonth = new DateOnly(period.End.Year, period.End.Month, 1);
        var months = Enumerable.Range(0, 12).Reverse().Select(back =>
        {
            var m = lastMonth.AddMonths(-back);
            var prefix = Key(m)[..7];
            double s = perDay.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal)).Sum(p => p.Value);
            return new StatsMonth($"{m.Month}月", s, s > 0 ? Duration(s) : "無紀錄");
        }).ToList();

        var years = perDay.Where(p => p.Value > 0 && p.Key.Length >= 4 && int.TryParse(p.Key[..4], out _))
            .GroupBy(p => int.Parse(p.Key[..4], CultureInfo.InvariantCulture))
            .Select(g => new StatsYear(g.Key, g.Sum(p => p.Value), Duration(g.Sum(p => p.Value))))
            .OrderByDescending(y => y.Year).ToList();

        var yearIds = usage.Keys.Where(k => k.Length >= 4 && int.TryParse(k[..4], out _))
            .Select(k => int.Parse(k[..4], CultureInfo.InvariantCulture))
            .Append(today.Year).Distinct().OrderDescending();
        var ranges = new[] { new StatsRangeOption("past", "過去一年") }
            .Concat(yearIds.Select(y => new StatsRangeOption(y.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture))))
            .ToList();

        var storeLabel = store is null ? null : label(store);
        var heading = $"{(storeLabel is null ? "" : storeLabel + "：")}{range.Heading}累計連線 {Duration(total)}";

        return new StatsView(heading, tiles, weeks, monthLabels,
            stores.Select(s => new StatsBar(s.Id, label(s.Id), s.Seconds, Duration(s.Seconds))).ToList(),
            months, years, ranges, range.Id, store, storeLabel,
            day is { } dd ? DayDetail(usage, dd, label) : null);
    }

    private static List<(string Id, double Seconds)> StoreTotals(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> usage, Period period)
    {
        var totals = new Dictionary<string, double>();
        foreach (var d in period.Days())
            if (usage.TryGetValue(Key(d), out var stores))
                foreach (var (id, s) in stores)
                    if (s > 0) totals[id] = totals.GetValueOrDefault(id) + s;
        return totals.Select(t => (t.Key, t.Value)).OrderByDescending(t => t.Value).ThenBy(t => t.Key, StringComparer.Ordinal).ToList();
    }

    private static StatsDay DayDetail(IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> usage, DateOnly d,
        Func<string, string> label)
    {
        var stores = usage.GetValueOrDefault(Key(d))?.Where(s => s.Value > 0).OrderByDescending(s => s.Value).ToList();
        var detail = stores is { Count: > 0 } ? string.Join("・", stores.Select(s => $"{label(s.Key)} {Duration(s.Value)}")) : "無紀錄";
        return new StatsDay(Key(d), $"{d.Month}月{d.Day}日（{Weekday(d)}）", detail);
    }

    public static string Weekday(DateOnly d) => "星期" + "日一二三四五六"[(int)d.DayOfWeek];

    /// <summary>Heatmap shade: none, under 30 min, under 1 h, under 2 h, 2 h or more.</summary>
    public static int Level(double seconds) => seconds switch
    {
        < 1 => 0,
        < 1800 => 1,
        < 3600 => 2,
        < 7200 => 3,
        _ => 4,
    };

    public static int LongestStreak(IEnumerable<double> days)
    {
        int best = 0, run = 0;
        foreach (var s in days)
        {
            run = s > 0 ? run + 1 : 0;
            best = Math.Max(best, run);
        }
        return best;
    }

    public static string Duration(double seconds)
    {
        int minutes = (int)(seconds / 60);
        return minutes >= 60 ? $"{minutes / 60} 小時 {minutes % 60} 分" : $"{minutes} 分";
    }
}
