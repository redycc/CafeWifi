using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CafeWiFi.Core;

namespace CafeWiFi.Platform;

public sealed record PortalResult(bool IsPortal, string Dir, int Hops, string? FinalUrl);

/// <summary>
/// Records a captive portal ("terms" page) so we can later learn to accept it automatically.
/// Starts where Windows does (the NCSI probe), follows HTTP, meta-refresh and JavaScript
/// redirects, and saves every hop plus the final page's forms, scripts and frames. It only
/// reads pages; it never submits anything.
/// </summary>
public sealed partial class PortalCapture
{
    public const int MaxHops = 10;
    private const string Probe = "http://www.msftconnecttest.com/connecttest.txt";
    private const string BrowserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36 Edg/130.0";

    private readonly HttpClient http;

    public PortalCapture(HttpMessageHandler? handler = null)
    {
        handler ??= new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() };
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserAgent);
    }

    public async Task<PortalResult> CaptureAsync(string dir, CancellationToken ct = default)
    {
        var hops = new List<object>();
        var pages = new List<string>();
        var visited = new HashSet<string>();
        Uri? url = new(Probe), final = null;
        string body = "";

        while (url is not null && hops.Count < MaxHops && visited.Add(url.ToString()))
        {
            using var res = await http.GetAsync(url, ct);
            body = await res.Content.ReadAsStringAsync(ct);
            if (hops.Count == 0 && res.IsSuccessStatusCode && body.Trim() == "Microsoft Connect Test")
                return new PortalResult(false, dir, 0, null); // the internet works: no portal

            var location = res.Headers.Location;
            hops.Add(new
            {
                url = url.ToString(),
                status = (int)res.StatusCode,
                location = location?.ToString(),
                headers = res.Headers.Concat(res.Content.Headers).ToDictionary(h => h.Key, h => string.Join(", ", h.Value)),
                file = $"hop{hops.Count}.html",
            });
            pages.Add(body);
            final = url;
            url = (int)res.StatusCode is >= 300 and < 400 && location is not null
                ? new Uri(url, location)
                : NextFromPage(url, body);
        }

        Directory.CreateDirectory(dir);
        for (int i = 0; i < pages.Count; i++) await File.WriteAllTextAsync(Path.Combine(dir, $"hop{i}.html"), pages[i], ct);
        var forms = Forms(body);
        await File.WriteAllTextAsync(Path.Combine(dir, "forms.json"), JsonSerializer.Serialize(forms, JsonFile.Options), ct);
        await SaveLinked(dir, final!, ScriptSrc(), "script", ".js", 10, ct);
        await SaveLinked(dir, final!, FrameSrc(), "frame", ".html", 3, ct);
        await File.WriteAllTextAsync(Path.Combine(dir, "summary.json"), JsonSerializer.Serialize(new
        {
            time = DateTimeOffset.Now,
            start = Probe,
            finalUrl = final!.ToString(),
            hops,
        }, JsonFile.Options), ct);
        Log.Write($"portal captured: {final} in {hops.Count} hops → {dir}");
        return new PortalResult(true, dir, hops.Count, final.ToString());

        async Task SaveLinked(string folder, Uri baseUrl, Regex pattern, string prefix, string ext, int max, CancellationToken token)
        {
            int n = 0;
            foreach (Match m in pattern.Matches(body))
            {
                if (n >= max) break;
                if (!Uri.TryCreate(baseUrl, WebUtility.HtmlDecode(m.Groups["src"].Value), out var src) || !src.Scheme.StartsWith("http")) continue;
                try
                {
                    var text = await http.GetStringAsync(src, token);
                    await File.WriteAllTextAsync(Path.Combine(folder, $"{prefix}{++n}{ext}"), $"/* {src} */\n{text}", token);
                }
                catch (HttpRequestException) { }
            }
        }
    }

    /// <summary>Where a page sends the browser without an HTTP redirect: meta refresh or a location script.</summary>
    private static Uri? NextFromPage(Uri current, string body)
    {
        var m = MetaRefresh().Match(body);
        if (!m.Success) m = ScriptRedirect().Match(body);
        return m.Success && Uri.TryCreate(current, WebUtility.HtmlDecode(m.Groups["to"].Value.Trim()), out var next) ? next : null;
    }

    private static List<object> Forms(string html) =>
        FormTag().Matches(html).Select(f =>
        {
            var open = f.Groups["open"].Value;
            var inner = f.Groups["inner"].Value;
            var fields = FieldTag().Matches(inner).Select(i => new
            {
                tag = i.Groups["tag"].Value.ToLowerInvariant(),
                name = Attr(i.Value, "name"),
                type = Attr(i.Value, "type"),
                value = Attr(i.Value, "value"),
            }).Where(i => i.name is not null || i.type is "submit" or "button").ToList();
            var buttons = ButtonTag().Matches(inner).Select(b => StripTags(b.Groups["text"].Value))
                .Concat(fields.Where(i => i.type is "submit" or "button" && i.value is not null).Select(i => i.value!))
                .Where(t => t.Length > 0).ToList();
            return (object)new
            {
                id = Attr(open, "id"),
                action = Attr(open, "action"),
                method = (Attr(open, "method") ?? "get").ToLowerInvariant(),
                fields,
                buttons,
            };
        }).ToList();

    private static string? Attr(string tag, string name)
    {
        var m = Regex.Match(tag, $@"\b{name}\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s>]+))", RegexOptions.IgnoreCase);
        return m.Success ? WebUtility.HtmlDecode(m.Groups["v"].Value) : null;
    }

    private static string StripTags(string s) => WebUtility.HtmlDecode(Regex.Replace(s, "<[^>]+>", "")).Trim();

    [GeneratedRegex(@"<meta[^>]+http-equiv\s*=\s*[""']?refresh[^>]*content\s*=\s*[""'][^""']*?url\s*=\s*(?<to>[^""'>]+)", RegexOptions.IgnoreCase)]
    private static partial Regex MetaRefresh();

    [GeneratedRegex(@"(?:window\.|document\.|top\.)?location(?:\.href)?\s*=\s*[""'](?<to>[^""']+)[""']|location\.(?:replace|assign)\(\s*[""'](?<to>[^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptRedirect();

    [GeneratedRegex(@"(?<open><form\b[^>]*>)(?<inner>.*?)</form>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FormTag();

    [GeneratedRegex(@"<(?<tag>input|select|textarea)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex FieldTag();

    [GeneratedRegex(@"<button\b[^>]*>(?<text>.*?)</button>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ButtonTag();

    [GeneratedRegex(@"<script\b[^>]*\bsrc\s*=\s*[""'](?<src>[^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptSrc();

    [GeneratedRegex(@"<i?frame\b[^>]*\bsrc\s*=\s*[""'](?<src>[^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex FrameSrc();
}
