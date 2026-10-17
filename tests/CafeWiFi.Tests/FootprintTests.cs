using CafeWiFi.Core;

namespace CafeWiFi.Tests;

public class FootprintTests
{
    private static readonly DateOnly Today = new(2026, 10, 17);

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Usage(params (string Day, string Store, double Seconds)[] rows) =>
        rows.GroupBy(r => r.Day).ToDictionary(g => g.Key,
            g => (IReadOnlyDictionary<string, double>)g.ToDictionary(r => r.Store, r => r.Seconds));

    private static FootprintPlace? Resolve(StoreKey key) => key.ToString() switch
    {
        "louisa/延平門市" => new("延平門市", 25.05, 121.51, "台北市", "chain"),
        "louisa/台中門市" => new("台中門市", 24.15, 120.67, "台中市", "chain"),
        "mine/Coffee and Couple" => new("Coffee and Couple", 25.09, 121.52, null, "own"),
        "mine/京都的店" => new("京都的店", 35.01, 135.77, "京都市", "own"),
        _ => null, // unknown stores, places without coordinates
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Sample = Usage(
        ("2026-10-01", "louisa/延平門市", 3600),
        ("2026-10-03", "louisa/延平門市", 1800),
        ("2026-10-17", "mine/Coffee and Couple", 7200),
        ("2026-05-02", "louisa/台中門市", 600),
        ("2026-03-10", "mine/京都的店", 5000),
        ("2026-10-02", "louisa/未知門市", 900),
        ("2024-01-01", "louisa/延平門市", 99999));    // outside the past year

    [Fact]
    public void Spots_OnePerMappedPlace_LongestFirst()
    {
        var view = FootprintModel.Build(Sample, new StatsRange.PastYear(), Today, Resolve);
        Assert.Equal(new[] { "Coffee and Couple", "延平門市", "京都的店", "台中門市" }, view.Spots.Select(s => s.Label));
        var yanping = view.Spots.Single(s => s.Label == "延平門市");
        Assert.Equal((5400d, 2, "10月3日", "chain", "1 小時 30 分"), (yanping.Seconds, yanping.Days, yanping.LastVisit, yanping.Kind, yanping.Text));
        Assert.Equal("own", view.Spots[0].Kind);
    }

    [Fact]
    public void Tiles_CountCafesCitiesAndAllTime()
    {
        var view = FootprintModel.Build(Sample, new StatsRange.PastYear(), Today, Resolve);
        Assert.Equal(new[] { "咖啡廳", "城市", "總共待了", "最常去" }, view.Tiles.Select(t => t.Label));
        // 4 mapped cafes; cities: 台北市, 台中市, 京都市 (one own cafe has no city); total includes the unknown store.
        Assert.Equal(new[] { "4 家", "3 個", "5 小時 18 分", "Coffee and Couple" }, view.Tiles.Select(t => t.Value));
    }

    [Fact]
    public void YearRange_LimitsSpots()
    {
        var view = FootprintModel.Build(Sample, new StatsRange.Year(2024), Today, Resolve);
        Assert.Equal("延平門市", Assert.Single(view.Spots).Label);
        Assert.Equal("2024 年的咖啡廳足跡", view.Heading);
        Assert.Equal("2024", view.Range);
        Assert.Contains(view.Ranges, r => r.Id == "past");
    }

    [Fact]
    public void Empty_HasDashTiles()
    {
        var view = FootprintModel.Build(Usage(), new StatsRange.PastYear(), Today, Resolve);
        Assert.Empty(view.Spots);
        Assert.Equal(new[] { "0 家", "0 個", "0 分", "—" }, view.Tiles.Select(t => t.Value));
    }
}
