using CafeWiFi.Core;

namespace CafeWiFi.Tests;

public class CommandLineAndVersionTests
{
    [Theory]
    [InlineData(new string[0], "show", null, null)]
    [InlineData(new[] { "--show" }, "show", null, null)]
    [InlineData(new[] { "--connect", "延平" }, "connect", "延平", null)]
    [InlineData(new[] { "--connect", "大安", "復興" }, "connect", "大安 復興", null)]
    [InlineData(new[] { "--connect", "延平", "--brand", "louisa" }, "connect", "延平", "louisa")]
    [InlineData(new[] { "--brand", "louisa", "--connect", "延平" }, "connect", "延平", "louisa")]
    [InlineData(new[] { "--connect" }, "show", null, null)]
    [InlineData(new[] { "--nearest" }, "nearest", null, null)]
    [InlineData(new[] { "--stats" }, "stats", null, null)]
    [InlineData(new[] { "--update" }, "update", null, null)]
    [InlineData(new[] { "--bogus" }, "show", null, null)]
    [InlineData(new[] { "--startup" }, "startup", null, null)]
    [InlineData(new[] { "--capture-portal" }, "capture-portal", null, null)]
    public void Parse(string[] args, string kind, string? store, string? brand)
    {
        Assert.Equal(new Command(kind, store, brand), CommandLine.Parse(args));
    }

    [Theory]
    [InlineData("1.2.10", "1.2.9", 1)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("v2.0.0", "1.9.9", 1)]
    [InlineData("1.0.0-beta", "1.0.0", 0)]
    [InlineData("0.9", "0.10", -1)]
    [InlineData("1.0.0+abc123", "1.0.0", 0)]
    public void Compare(string a, string b, int sign) => Assert.Equal(sign, Math.Sign(VersionCompare.Compare(a, b)));

    [Fact]
    public void Strip_RemovesVPrefixAndBuildMetadata()
    {
        Assert.Equal("1.2.3", VersionCompare.Strip("v1.2.3"));
        Assert.Equal("1.2.3", VersionCompare.Strip("1.2.3+sha.abc"));
    }
}
