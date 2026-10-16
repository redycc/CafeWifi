using System.Globalization;
using CafeWiFi.Core;
using CafeWiFi.Platform;

namespace CafeWiFi.App;

/// <summary>
/// On an open brand's Wi-Fi (e.g. Starbucks) that doesn't reach the internet yet, a terms page
/// is waiting. Save a copy of it, at most once per brand every six hours, so automatic
/// acceptance can be written from real pages.
/// </summary>
public sealed class PortalWatcher
{
    private static readonly TimeSpan Every = TimeSpan.FromHours(6);

    private readonly IWifi wifi;
    private readonly IConnectivity net;
    private readonly BrandCatalog catalog;
    private readonly string root;
    private readonly Func<string, Task<PortalResult>> capture;
    private readonly Func<DateTimeOffset> clock;
    private readonly Action<string, string> notify;
    private readonly Dictionary<string, DateTimeOffset> last = new();
    private int running;

    /// <param name="root">Captures go to root\&lt;brand&gt;-&lt;time&gt;.</param>
    /// <param name="notify">Text and the capture's folder.</param>
    public PortalWatcher(IWifi wifi, IConnectivity net, BrandCatalog catalog, string root,
        Func<string, Task<PortalResult>> capture, Func<DateTimeOffset> clock, Action<string, string> notify)
    {
        (this.wifi, this.net, this.catalog, this.root, this.capture, this.clock, this.notify) =
            (wifi, net, catalog, root, capture, clock, notify);
    }

    public async Task CheckAsync()
    {
        if (catalog.BrandForSsid(wifi.CurrentLink()?.Ssid) is not { IsOpen: true } brand) return;
        if (last.TryGetValue(brand.Id, out var at) && clock() - at < Every) return;
        if (Interlocked.Exchange(ref running, 1) == 1) return;
        try
        {
            if (await net.IsOnlineAsync()) return;
            var now = clock();
            last[brand.Id] = now;
            var dir = Path.Combine(root, $"{brand.Id}-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}");
            var result = await capture(dir);
            if (result.IsPortal)
                notify($"連上{brand.Name} Wi-Fi 了，請在瀏覽器同意使用條款。條款頁已存一份，點這裡打開資料夾。", result.Dir);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            Log.Write($"portal capture failed: {ex.Message}");
        }
        finally { Volatile.Write(ref running, 0); }
    }
}
