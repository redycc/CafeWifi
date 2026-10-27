using System.Text.Json;
using CafeWiFi.Core;

namespace CafeWiFi.Tests;

public class RuckusPortalTests
{
    // The real Starbucks TW redirect URL (asia.ruckus.cloud guest portal), from a store capture.
    private const string PortalUrl =
        "https://asia.ruckus.cloud/g/ui/tenant/8277a1732108462fb842a24294f480e7/portal" +
        "?nbiIP=device.asia.ruckus.cloud&client_mac=ENCdf81fd7de93ecf42be84b3d456217bc8" +
        "&domain_name=Starbucks&reason=Un-Auth-Captive&wlanName=0e3fd04310b2404b868485849283bae4" +
        "&dn=scg.ruckuswireless.com&ssid=Starbucks+Wi-Fi&mac=80:03:84:2c:c1:f0&proxy=0&vlan=3000&wlan=1121" +
        "&uip=ENC93818a675ef2911d822018031e0abbeb" +
        "&StartURL=https%3A%2F%2Fwww.starbucks.com.tw%2Fstarbuckschannel%2Findex.jspx";

    [Fact]
    public void Authorize_BuildsAnonymousRequest_FromRedirectParams()
    {
        var req = RuckusPortal.Authorize(PortalUrl);

        Assert.NotNull(req);
        Assert.Equal(
            "https://asia.ruckus.cloud/g/guest/api/tenant/8277a1732108462fb842a24294f480e7/portal/anonymous",
            req!.Url);

        using var body = JsonDocument.Parse(req.Json);
        var root = body.RootElement;
        Assert.Equal("", root.GetProperty("captchaField").GetString());
        Assert.Equal("", root.GetProperty("captchaValue").GetString());

        var nbi = root.GetProperty("nbiData");
        Assert.Equal("device.asia.ruckus.cloud", nbi.GetProperty("nbiIp").GetString());
        // the ENC client_mac, not mac=80:03:...
        Assert.Equal("ENCdf81fd7de93ecf42be84b3d456217bc8", nbi.GetProperty("clientMac").GetString());
        Assert.Equal("ENC93818a675ef2911d822018031e0abbeb", nbi.GetProperty("clientIp").GetString());
        Assert.Equal("0", nbi.GetProperty("clientProxy").GetString());

        Assert.Equal("https://www.starbucks.com.tw/starbuckschannel/index.jspx",
            root.GetProperty("redirectUri").GetString());
        // the wlanName GUID, not wlan=1121
        Assert.Equal("0e3fd04310b2404b868485849283bae4", root.GetProperty("networkId").GetString());
        Assert.Equal("Guest", root.GetProperty("authenticationData").GetProperty("authServer").GetString());
    }

    [Fact]
    public void Authorize_ReturnsNull_ForNonRuckusPortal()
    {
        Assert.Null(RuckusPortal.Authorize("http://portal.test/terms?mac=aa-bb"));
        Assert.Null(RuckusPortal.Authorize("https://www.starbucks.com.tw/index.jspx"));
    }

    [Fact]
    public void Succeeded_TrueOnlyForResponseCode201()
    {
        Assert.True(RuckusPortal.Succeeded(
            """{"info":{"ReplyMessage":"Login succeeded","ResponseCode":"201","SessionTimeout":"14399"}}"""));
        Assert.False(RuckusPortal.Succeeded("""{"info":{"ReplyMessage":"denied","ResponseCode":"401"}}"""));
        Assert.False(RuckusPortal.Succeeded("not json"));
    }
}
