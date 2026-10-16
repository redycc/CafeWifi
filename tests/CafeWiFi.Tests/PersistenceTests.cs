using System.Globalization;
using System.Runtime.CompilerServices;
using CafeWiFi.Core;

namespace CafeWiFi.Tests;

internal static class TestSetup
{
    /// <summary>Keep tests out of the real %LOCALAPPDATA% log.</summary>
    [ModuleInitializer]
    internal static void Init() => Log.FilePath = null;
}

public sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("cafewifi-test-").FullName;
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose() => Directory.Delete(Path, recursive: true);
}

public class PersistenceTests : IDisposable
{
    private readonly TempDir dir = new();
    public void Dispose() => dir.Dispose();

    private sealed class Sample { public int N { get; set; } public List<string> Items { get; set; } = new(); }

    [Fact]
    public void JsonFile_SaveThenLoad_RoundTrips_AndLeavesNoTmp()
    {
        var path = dir.File("x.json");
        JsonFile.Save(path, new Sample { N = 3, Items = { "延平" } });
        JsonFile.Save(path, new Sample { N = 4, Items = { "延平" } });
        var back = JsonFile.Load<Sample>(path);
        Assert.Equal(4, back.N);
        Assert.Equal("延平", Assert.Single(back.Items));
        Assert.False(System.IO.File.Exists(path + ".tmp"));
    }

    [Fact]
    public void JsonFile_Load_MissingFile_ReturnsEmpty()
    {
        Assert.Equal(0, JsonFile.Load<Sample>(dir.File("none.json")).N);
    }

    [Theory]
    [InlineData("{bad")]
    [InlineData("")]
    [InlineData("null")]
    public void JsonFile_Load_CorruptFile_ReturnsEmptyAndKeepsBad(string content)
    {
        var path = dir.File("x.json");
        System.IO.File.WriteAllText(path, content);
        Assert.Equal(0, JsonFile.Load<Sample>(path).N);
        Assert.True(System.IO.File.Exists(path + ".bad"));
        Assert.False(System.IO.File.Exists(path));
    }

    [Fact]
    public void Learned_Remember_NormalizesBssid_AndPersists()
    {
        var path = dir.File("learned.json");
        new LearnedStore(path).Remember(new[] { "E0:63:DA:6B:BC:51", "e0:63:da:6c:bc:51" }, new StoreKey("louisa", "延平門市"), "23610718");
        var again = new LearnedStore(path);
        var hit = again.Lookup("e0:63:da:6b:bc:51");
        Assert.Equal(new Learned("louisa/延平門市", "23610718"), hit);
        Assert.NotNull(again.Lookup("E0:63:DA:6C:BC:51"));
        Assert.Null(again.Lookup("00:00:00:00:00:00"));
    }

    [Fact]
    public void Learned_KnownStores_ReturnsDistinctStoreKeys()
    {
        var store = new LearnedStore(dir.File("learned.json"));
        store.Remember(new[] { "aa:aa:aa:aa:aa:01", "aa:aa:aa:aa:aa:02" }, new StoreKey("louisa", "A"), "1");
        store.Remember(new[] { "aa:aa:aa:aa:aa:03" }, new StoreKey("louisa", "B"), "2");
        Assert.Equal(new[] { "louisa/A", "louisa/B" }, store.KnownStores().Order());
    }

    [Fact]
    public void Usage_Add_Accumulates_AndPersistsAcrossInstances()
    {
        var path = dir.File("usage.json");
        var day = new DateOnly(2026, 10, 16);
        var usage = new UsageStore(path);
        int changed = 0;
        usage.Changed += () => changed++;
        usage.Add(day, new StoreKey("louisa", "A"), 60);
        usage.Add(day, new StoreKey("louisa", "A"), 60);
        usage.Add(day, new StoreKey("louisa", StoreKey.Unknown), 60);
        Assert.Equal(3, changed);
        var snap = new UsageStore(path).Snapshot();
        Assert.Equal(120, snap["2026-10-16"]["louisa/A"]);
        Assert.Equal(60, snap["2026-10-16"]["louisa/未知門市"]);
    }

    [Fact]
    public void Usage_DayKey_IgnoresCurrentCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH"); // Buddhist calendar: 2569
            Assert.Equal("2026-10-16", UsageStore.DayKey(new DateOnly(2026, 10, 16)));
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void Settings_DefaultsAndRoundTrip()
    {
        var path = dir.File("settings.json");
        var s = CafeWiFi.App.Settings.Load(path);
        Assert.True(s.AutoConnect);
        Assert.False(s.OnlyKnown);
        s.AutoConnect = false;
        s.LastLat = 25.04;
        s.Save();
        var back = CafeWiFi.App.Settings.Load(path);
        Assert.False(back.AutoConnect);
        Assert.Equal(25.04, back.LastLat);
    }
}
