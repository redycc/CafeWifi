using System.Xml.Linq;
using CafeWiFi.Platform;

namespace CafeWiFi.Tests;

public class ProfileXmlTests
{
    private static readonly XNamespace Ns = "http://www.microsoft.com/networking/WLAN/profile/v1";

    private static XElement Root(string ssid, string password) => XDocument.Parse(ProfileXml.Build(ssid, password)).Root!;

    [Fact]
    public void Build_IsValidXml_WithProfileNameEqualToSsid()
    {
        var root = Root("LouisaCoffee", "23610718");
        Assert.Equal(Ns + "WLANProfile", root.Name);
        Assert.Equal("LouisaCoffee", root.Element(Ns + "name")!.Value);
    }

    [Fact]
    public void Build_ContainsSsidHexAndName()
    {
        var ssid = Root("咖啡", "23610718").Element(Ns + "SSIDConfig")!.Element(Ns + "SSID")!;
        Assert.Equal("E59296E595A1", ssid.Element(Ns + "hex")!.Value);
        Assert.Equal("咖啡", ssid.Element(Ns + "name")!.Value);
    }

    [Fact]
    public void Build_ManualModeWpa2Aes()
    {
        var root = Root("LouisaCoffee", "23610718");
        Assert.Equal("manual", root.Element(Ns + "connectionMode")!.Value);
        var auth = root.Descendants(Ns + "authEncryption").Single();
        Assert.Equal("WPA2PSK", auth.Element(Ns + "authentication")!.Value);
        Assert.Equal("AES", auth.Element(Ns + "encryption")!.Value);
        var key = root.Descendants(Ns + "sharedKey").Single();
        Assert.Equal("passPhrase", key.Element(Ns + "keyType")!.Value);
        Assert.Equal("false", key.Element(Ns + "protected")!.Value);
        Assert.Equal("23610718", key.Element(Ns + "keyMaterial")!.Value);
    }

    [Fact]
    public void Build_EscapesXmlSpecialCharacters()
    {
        const string password = "a&b<c>\"d'e";
        var raw = ProfileXml.Build("Cafe & <Co>", password);
        Assert.DoesNotContain("a&b<c>", raw);
        var root = XDocument.Parse(raw).Root!;
        Assert.Equal(password, root.Descendants(Ns + "keyMaterial").Single().Value);
        Assert.Equal("Cafe & <Co>", root.Element(Ns + "name")!.Value);
    }

    [Fact]
    public void Build_SixtyFourHexDigits_IsRawNetworkKey()
    {
        var key = Root("X", new string('a', 64)).Descendants(Ns + "sharedKey").Single();
        Assert.Equal("networkKey", key.Element(Ns + "keyType")!.Value);
    }
}
