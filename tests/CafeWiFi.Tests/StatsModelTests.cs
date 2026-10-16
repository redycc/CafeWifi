using CafeWiFi.Core;

namespace CafeWiFi.Tests;

public class StatsModelTests
{
    private static readonly DateOnly Today = new(2026, 10, 18); // a Sunday

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Usage(params (string Day, string Store, double Seconds)[] rows) =>
        rows.GroupBy(r => r.Day).ToDictionary(g => g.Key,
            g => (IReadOnlyDictionary<string, double>)g.ToDictionary(r => r.Store, r => r.Seconds));

    private static string Label(string key) => StoreKey.Parse(key).StoreName;

    [Fact]
    public void Period_PastYear_Is365Days_EndsToday()
    {
        var p = Period.For(new StatsRange.PastYear(), Today);
        Assert.Equal(365, p.Days().Count());
        Assert.Equal(Today, p.Last);
        Assert.Equal(Today.AddDays(-364), p.First);
    }

    [Fact]
    public void Period_GridStartsOnMonday_WhenTodayIsSunday()
    {
        var p = Period.For(new StatsRange.PastYear(), Today);
        Assert.Equal(DayOfWeek.Monday, p.GridStart.DayOfWeek);
        Assert.True(p.GridStart <= p.First && p.First.DayNumber - p.GridStart.DayNumber < 7);
        // The last column holds today (Sunday) as its 7th row.
        Assert.Equal(Today, p.GridStart.AddDays(p.Weeks * 7 - 1));
    }

    [Fact]
    public void Period_Year_LastClampedToToday_GridCoversDecember()
    {
        var p = Period.For(new StatsRange.Year(2026), Today);
        Assert.Equal(new DateOnly(2026, 1, 1), p.First);
        Assert.Equal(new DateOnly(2026, 12, 31), p.End);
        Assert.Equal(Today, p.Last);
        Assert.True(p.GridStart.AddDays(p.Weeks * 7 - 1) >= p.End);
    }

    [Fact]
    public void LongestStreak_AcrossYearEnd()
    {
        var usage = Usage(("2025-12-30", "louisa/A", 60), ("2025-12-31", "louisa/A", 60), ("2026-01-01", "louisa/B", 60), ("2026-01-03", "louisa/A", 60));
        var view = StatsModel.Build(usage, new StatsRange.PastYear(), null, Today, Label);
        Assert.Equal("3 天", view.Tiles.Single(t => t.Label == "最長連續").Value);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.5, 0)]
    [InlineData(1, 1)]
    [InlineData(1799, 1)]
    [InlineData(1800, 2)]
    [InlineData(3600, 3)]
    [InlineData(7200, 4)]
    public void Level_Thresholds(double seconds, int level) => Assert.Equal(level, StatsModel.Level(seconds));

    [Theory]
    [InlineData(59 * 60, "59 分")]
    [InlineData(3600, "1 小時 0 分")]
    [InlineData(3 * 3600 + 7 * 60 + 30, "3 小時 7 分")]
    public void Duration_Format(double seconds, string text) => Assert.Equal(text, StatsModel.Duration(seconds));

    [Fact]
    public void Build_TotalsTilesAndHeading()
    {
        var usage = Usage(("2026-10-16", "louisa/A", 3600), ("2026-10-16", "louisa/未知門市", 600), ("2026-10-17", "louisa/B", 1800),
            ("2024-01-01", "louisa/A", 999999)); // outside the range
        var view = StatsModel.Build(usage, new StatsRange.PastYear(), null, Today, Label);
        Assert.Equal("過去一年累計連線 1 小時 40 分", view.Heading);
        Assert.Equal(new[] { "2 天", "2 家", "2 天", "50 分" }, view.Tiles.Select(t => t.Value));
        Assert.Equal(new[] { "到訪天數", "到訪門市", "最長連續", "平均每天" }, view.Tiles.Select(t => t.Label));
    }

    [Fact]
    public void Build_StoreFilter_LimitsTotals_ButStoreListShowsAll()
    {
        var usage = Usage(("2026-10-16", "louisa/A", 3600), ("2026-10-17", "louisa/B", 1800));
        var view = StatsModel.Build(usage, new StatsRange.PastYear(), "louisa/B", Today, Label);
        Assert.Equal("B：過去一年累計連線 30 分", view.Heading);
        Assert.Equal(new[] { "louisa/A", "louisa/B" }, view.Stores.Select(s => s.Id));
        var cells = view.Weeks.SelectMany(w => w).Where(c => c.Seconds > 0).Select(c => c.Date);
        Assert.Equal(new[] { "2026-10-17" }, cells);
    }

    [Fact]
    public void Build_Weeks_MarkRangeAndFuture()
    {
        var view = StatsModel.Build(Usage(), new StatsRange.Year(2026), null, Today, Label);
        var cells = view.Weeks.SelectMany(w => w).ToList();
        Assert.All(view.Weeks, w => Assert.Equal(7, w.Count));
        Assert.False(cells.Single(c => c.Date == "2025-12-29").InRange);  // Monday before Jan 1
        Assert.True(cells.Single(c => c.Date == "2026-01-01").InRange);
        Assert.True(cells.Single(c => c.Date == "2026-12-01").Future);
        Assert.False(cells.Single(c => c.Date == "2026-10-18").Future);
        Assert.Equal("1月", view.MonthLabels.First().Label);
    }

    [Fact]
    public void Build_Months_Last12EndingAtPeriodEnd()
    {
        var usage = Usage(("2026-10-01", "louisa/A", 600), ("2025-11-30", "louisa/A", 300), ("2025-10-31", "louisa/A", 999));
        var view = StatsModel.Build(usage, new StatsRange.PastYear(), null, Today, Label);
        Assert.Equal(12, view.Months.Count);
        Assert.Equal("11月", view.Months[0].Label);
        Assert.Equal(300, view.Months[0].Seconds);
        Assert.Equal("10月", view.Months[^1].Label);
        Assert.Equal(600, view.Months[^1].Seconds);
    }

    [Fact]
    public void Build_YearsSortedDesc_AndRangesIncludeCurrentYear()
    {
        var usage = Usage(("2024-03-01", "louisa/A", 60), ("2026-03-01", "louisa/A", 120));
        var view = StatsModel.Build(usage, new StatsRange.PastYear(), null, Today, Label);
        Assert.Equal(new[] { 2026, 2024 }, view.Years.Select(y => y.Year));
        Assert.Equal(new[] { "past", "2026", "2024" }, view.Ranges.Select(r => r.Id));
        Assert.Equal("past", view.Range);
    }

    [Fact]
    public void Build_DayDetail()
    {
        var usage = Usage(("2026-10-16", "louisa/A", 3600), ("2026-10-16", "louisa/B", 600));
        var view = StatsModel.Build(usage, new StatsRange.PastYear(), null, Today, Label, day: new DateOnly(2026, 10, 16));
        Assert.Equal("10月16日（星期五）", view.Day!.Title);
        Assert.Equal("A 1 小時 0 分・B 10 分", view.Day.Detail);
        var empty = StatsModel.Build(usage, new StatsRange.PastYear(), null, Today, Label, day: new DateOnly(2026, 10, 15));
        Assert.Equal("無紀錄", empty.Day!.Detail);
    }

    [Theory]
    [InlineData("past", typeof(StatsRange.PastYear))]
    [InlineData("2025", typeof(StatsRange.Year))]
    [InlineData("junk", typeof(StatsRange.PastYear))]
    [InlineData(null, typeof(StatsRange.PastYear))]
    public void Range_Parse(string? id, Type type) => Assert.IsType(type, StatsRange.Parse(id));
}
