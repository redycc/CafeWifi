using System.Net;
using System.Text.Json;
using CafeWiFi.App;
using CafeWiFi.Core;
using CafeWiFi.Platform;

namespace CafeWiFi.Tests;

/// <summary>Answers requests from a fixed url → response table, like a captive portal would.</summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = new();
    public List<string> Requested { get; } = new();
    public List<(string Url, string Body, string? Referer)> Posts { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.ToString();
        Requested.Add(url);
        if (request.Method == HttpMethod.Post)
            Posts.Add((url, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct),
                request.Headers.Referrer?.ToString()));
        return Routes.TryGetValue(url, out var make) ? make() : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    public static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Html(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    public static HttpResponseMessage Redirect(string to)
    {
        var r = new HttpResponseMessage(HttpStatusCode.Found);
        r.Headers.Location = new Uri(to, UriKind.RelativeOrAbsolute);
        return r;
    }
}

public class PortalTests : IDisposable
{
    private readonly TempDir dir = new();
    public void Dispose() => dir.Dispose();

    private const string Probe = "http://www.msftconnecttest.com/connecttest.txt";

    private static FakeHttp StarbucksLike()
    {
        var http = new FakeHttp();
        http.Routes[Probe] = () => FakeHttp.Redirect("http://portal.test/login?mac=aa-bb");
        http.Routes["http://portal.test/login?mac=aa-bb"] = () => FakeHttp.Html(
            "<html><head><meta http-equiv=\"refresh\" content=\"0; url=/terms\"></head></html>");
        http.Routes["http://portal.test/terms"] = () => FakeHttp.Html("""
            <html><body>
              <script src="/js/portal.js"></script>
              <form id="agree" action="/accept" method="post">
                <input type="hidden" name="token" value="abc">
                <input type="checkbox" name="tos">
                <button type="submit">同意並繼續</button>
              </form>
            </body></html>
            """);
        http.Routes["http://portal.test/js/portal.js"] = () => FakeHttp.Html("function agree(){}");
        return http;
    }

    [Fact]
    public async Task Capture_FollowsRedirectsAndMetaRefresh_SavesPagesFormsAndScripts()
    {
        var http = StarbucksLike();
        var result = await new PortalCapture(http).CaptureAsync(dir.Path);

        Assert.True(result.IsPortal);
        Assert.Equal("http://portal.test/terms", result.FinalUrl);
        Assert.Equal(3, result.Hops);
        Assert.True(File.Exists(Path.Combine(dir.Path, "hop2.html")));
        Assert.Contains("function agree", File.ReadAllText(Path.Combine(dir.Path, "script1.js")));

        using var forms = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir.Path, "forms.json")));
        var form = forms.RootElement[0];
        Assert.Equal("/accept", form.GetProperty("action").GetString());
        Assert.Equal("post", form.GetProperty("method").GetString());
        Assert.Contains("token", form.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("name").GetString()));
        Assert.Contains("同意並繼續", form.GetProperty("buttons").EnumerateArray().Select(b => b.GetString()));

        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir.Path, "summary.json")));
        Assert.Equal(302, summary.RootElement.GetProperty("hops")[0].GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Capture_FollowsJavaScriptRedirect()
    {
        var http = new FakeHttp();
        http.Routes[Probe] = () => FakeHttp.Html("<script>window.location.href = 'http://portal.test/p';</script>");
        http.Routes["http://portal.test/p"] = () => FakeHttp.Html("<form action='/go'></form>");
        var result = await new PortalCapture(http).CaptureAsync(dir.Path);
        Assert.Equal("http://portal.test/p", result.FinalUrl);
    }

    [Fact]
    public async Task Capture_NoPortal_WhenProbeAnswersNormally()
    {
        var http = new FakeHttp();
        http.Routes[Probe] = () => FakeHttp.Html("Microsoft Connect Test");
        var result = await new PortalCapture(http).CaptureAsync(Path.Combine(dir.Path, "x"));
        Assert.False(result.IsPortal);
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "x")));
    }

    [Fact]
    public async Task Capture_StopsOnRedirectLoop()
    {
        var http = new FakeHttp();
        http.Routes[Probe] = () => FakeHttp.Redirect("http://portal.test/a");
        http.Routes["http://portal.test/a"] = () => FakeHttp.Redirect("http://portal.test/b");
        http.Routes["http://portal.test/b"] = () => FakeHttp.Redirect("http://portal.test/a");
        var result = await new PortalCapture(http).CaptureAsync(dir.Path);
        Assert.True(result.Hops <= PortalCapture.MaxHops);
    }

    // MARK: accept

    private const string RuckusPortalUrl =
        "https://asia.ruckus.cloud/g/ui/tenant/T1/portal?nbiIP=nbi.example&client_mac=ENCmac" +
        "&wlanName=NET-GUID&proxy=0&uip=ENCip&wlan=1121&StartURL=https%3A%2F%2Fexample.com%2Fhome";

    private static FakeHttp RuckusLike(string grantResponse)
    {
        var http = new FakeHttp();
        http.Routes[Probe] = () => FakeHttp.Redirect(RuckusPortalUrl);
        http.Routes[RuckusPortalUrl] = () => FakeHttp.Html("<html><body><div id=\"root\"></div></body></html>");
        http.Routes["https://asia.ruckus.cloud/g/guest/api/tenant/T1/portal/anonymous"] = () => FakeHttp.Json(grantResponse);
        return http;
    }

    [Fact]
    public async Task Accept_PostsRuckusGrant_FromRedirectParams_AndReportsSuccess()
    {
        var http = RuckusLike("""{"info":{"ReplyMessage":"Login succeeded","ResponseCode":"201"}}""");

        var accepted = await new PortalCapture(http).TryAcceptAsync();

        Assert.True(accepted);
        var post = Assert.Single(http.Posts);
        Assert.Equal("https://asia.ruckus.cloud/g/guest/api/tenant/T1/portal/anonymous", post.Url);
        Assert.Equal(RuckusPortalUrl, post.Referer); // the portal page, like the browser sends
        using var body = JsonDocument.Parse(post.Body);
        Assert.Equal("NET-GUID", body.RootElement.GetProperty("networkId").GetString());
        Assert.Equal("ENCmac", body.RootElement.GetProperty("nbiData").GetProperty("clientMac").GetString());
    }

    [Fact]
    public async Task Accept_ReturnsFalse_WhenGrantRejected()
    {
        var http = RuckusLike("""{"info":{"ResponseCode":"401"}}""");
        Assert.False(await new PortalCapture(http).TryAcceptAsync());
    }

    [Fact]
    public async Task Accept_ReturnsFalse_WhenAlreadyOnline()
    {
        var http = new FakeHttp();
        http.Routes[Probe] = () => FakeHttp.Html("Microsoft Connect Test");
        Assert.False(await new PortalCapture(http).TryAcceptAsync());
    }

    // MARK: watcher

    private static readonly BrandCatalog Catalog = BrandCatalog.Load(new[]
    {
        JsonSerializer.Serialize(new Brand("starbucks", "星巴克", new[] { "Starbucks WiFi" }, Array.Empty<Store>(), "open"), BrandCatalog.Json),
        JsonSerializer.Serialize(new Brand("louisa", "路易莎", new[] { "LouisaCoffee" }, Array.Empty<Store>()), BrandCatalog.Json),
    }, Array.Empty<string>());

    private (PortalWatcher Watcher, List<string> Captured, List<string> Notes) Watcher(
        FakeWifi wifi, FakeNet net, Func<DateTimeOffset> clock, Func<Task<bool>>? accept = null)
    {
        var captured = new List<string>();
        var notes = new List<string>();
        var w = new PortalWatcher(wifi, net, Catalog, dir.Path,
            target => { captured.Add(target); return Task.FromResult(new PortalResult(true, target, 2, "http://portal.test/")); },
            accept ?? (() => Task.FromResult(false)),
            clock, (text, folder) => notes.Add(text));
        return (w, captured, notes);
    }

    [Fact]
    public async Task Watcher_AutoAcceptsTerms_WithoutCapturing()
    {
        var wifi = new FakeWifi { Link = new WifiLink("Starbucks WiFi", "aa:00:00:00:00:01") };
        var accepted = 0;
        var (w, captured, notes) = Watcher(wifi, new FakeNet(), () => DateTimeOffset.Now,
            () => { accepted++; return Task.FromResult(true); });
        await w.CheckAsync();
        Assert.Equal(1, accepted);
        Assert.Empty(captured);
        Assert.Single(notes);
    }

    [Fact]
    public async Task Watcher_FallsBackToCapture_WhenAutoAcceptFails()
    {
        var wifi = new FakeWifi { Link = new WifiLink("Starbucks WiFi", "aa:00:00:00:00:01") };
        var (w, captured, notes) = Watcher(wifi, new FakeNet(), () => DateTimeOffset.Now, () => Task.FromResult(false));
        await w.CheckAsync();
        Assert.Single(captured);
        Assert.Single(notes);
    }

    [Fact]
    public async Task Watcher_DoesNotAccept_WhenAlreadyOnline()
    {
        var wifi = new FakeWifi { Link = new WifiLink("Starbucks WiFi", "aa:00:00:00:00:01") };
        var accepted = 0;
        var (w, captured, _) = Watcher(wifi, new FakeNet { Online = true }, () => DateTimeOffset.Now,
            () => { accepted++; return Task.FromResult(true); });
        await w.CheckAsync();
        Assert.Equal(0, accepted);
        Assert.Empty(captured);
    }

    [Fact]
    public async Task Watcher_CapturesOncePerSixHours_OnOpenBrandWhileOffline()
    {
        var now = new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.FromHours(8));
        var wifi = new FakeWifi { Link = new WifiLink("Starbucks WiFi", "aa:00:00:00:00:01") };
        var (w, captured, notes) = Watcher(wifi, new FakeNet(), () => now);
        await w.CheckAsync();
        await w.CheckAsync();
        Assert.Single(captured);
        Assert.StartsWith(Path.Combine(dir.Path, "starbucks-"), captured[0]);
        Assert.Single(notes);
        now = now.AddHours(6).AddMinutes(1);
        await w.CheckAsync();
        Assert.Equal(2, captured.Count);
    }

    [Fact]
    public async Task Watcher_IgnoresPasswordBrands_AndOnlineOpenNetworks()
    {
        var now = DateTimeOffset.Now;
        var wifi = new FakeWifi { Link = new WifiLink("LouisaCoffee", "aa:00:00:00:00:01") };
        var net = new FakeNet();
        var (w, captured, _) = Watcher(wifi, net, () => now);
        await w.CheckAsync();
        wifi.Link = new WifiLink("Starbucks WiFi", "aa:00:00:00:00:01");
        net.Online = true;
        await w.CheckAsync();
        Assert.Empty(captured);
    }
}
