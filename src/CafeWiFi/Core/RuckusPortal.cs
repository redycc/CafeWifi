using System.Text.Json;

namespace CafeWiFi.Core;

/// <summary>The one request that accepts a Ruckus guest portal's terms: where to POST, and the JSON body.</summary>
public sealed record PortalAuthorize(string Url, string Json);

/// <summary>
/// Open-network chains like Starbucks TW sit behind a Ruckus Cloud guest portal (asia.ruckus.cloud).
/// Its "connect" button is a React XHR, not a form, but the whole request can be rebuilt from the
/// captive redirect URL's query params — so we can accept the terms without a browser. Reverse
/// engineered from a real capture; see memory starbucks-portal-authorize.
/// </summary>
public static class RuckusPortal
{
    /// <summary>The accept request for <paramref name="portalUrl"/> (the captive redirect URL), or null if it isn't a Ruckus portal.</summary>
    public static PortalAuthorize? Authorize(string? portalUrl)
    {
        if (!Uri.TryCreate(portalUrl, UriKind.Absolute, out var uri)) return null;
        if (!uri.Host.Contains("ruckus", StringComparison.OrdinalIgnoreCase)) return null;

        var tenant = TenantFrom(uri.AbsolutePath);
        var q = Query(uri.Query);
        if (tenant is null || !q.TryGetValue("wlanName", out var networkId) || !q.ContainsKey("client_mac")) return null;

        var body = new
        {
            captchaField = "",
            captchaValue = "",
            nbiData = new
            {
                nbiIp = q.GetValueOrDefault("nbiIP", ""),
                clientMac = q.GetValueOrDefault("client_mac", ""),
                clientIp = q.GetValueOrDefault("uip", ""),
                clientProxy = q.GetValueOrDefault("proxy", "0"),
            },
            redirectUri = q.GetValueOrDefault("StartURL", ""),
            networkId,
            authenticationData = new { authServer = "Guest" },
        };
        var url = $"https://{uri.Host}/g/guest/api/tenant/{tenant}/portal/anonymous";
        return new PortalAuthorize(url, JsonSerializer.Serialize(body));
    }

    /// <summary>Did the portal accept us? The grant replies with info.ResponseCode "201".</summary>
    public static bool Succeeded(string? responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody ?? "");
            return doc.RootElement.TryGetProperty("info", out var info)
                && info.TryGetProperty("ResponseCode", out var code)
                && code.GetString() == "201";
        }
        catch (JsonException) { return false; }
    }

    private static string? TenantFrom(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var i = Array.IndexOf(parts, "tenant");
        return i >= 0 && i + 1 < parts.Length ? parts[i + 1] : null;
    }

    private static Dictionary<string, string> Query(string query)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0) continue;
            map[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..]);
        }
        return map;
    }
}
