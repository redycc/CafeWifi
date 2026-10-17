using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;
using CafeWiFi.Core;

namespace CafeWiFi.App;

/// <summary>
/// "連線統計": stats.html in WebView2. The page posts {view, range, store, day}; we answer
/// {kind, data} with StatsModel's or FootprintModel's view, and push a fresh one whenever
/// usage or the user's own cafes change.
/// </summary>
public sealed class StatsForm : Form
{
    private readonly UsageStore usage;
    private readonly BrandCatalog catalog;
    private readonly PlaceStore places;
    private readonly WebView2 web = new() { Dock = DockStyle.Fill };
    private (string? View, string? Range, string? Store, string? Day) request;
    private bool ready;
    private string? pendingView;

    /// <summary>Switches the page to "stats" or "footprint", now or once it has loaded.</summary>
    public void ShowView(string view)
    {
        if (ready && web.CoreWebView2 is not null) _ = web.CoreWebView2.ExecuteScriptAsync($"showView({JsonSerializer.Serialize(view)})");
        else pendingView = view;
    }

    public StatsForm(UsageStore usage, BrandCatalog catalog, PlaceStore places)
    {
        this.usage = usage;
        this.catalog = catalog;
        this.places = places;
        Text = "連線統計";
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(LogicalToDeviceUnits(960), LogicalToDeviceUnits(700));
        MinimumSize = new Size(LogicalToDeviceUnits(700), LogicalToDeviceUnits(500));
        bool dark = IsDarkMode();
        BackColor = dark ? Color.FromArgb(0x20, 0x20, 0x20) : Color.White;
        web.DefaultBackgroundColor = BackColor;
        Controls.Add(web);

        HandleCreated += (_, _) => { if (dark) UseDarkTitleBar(Handle); };
        Load += async (_, _) => await InitAsync();
        usage.Changed += OnUsageChanged;
        places.Changed += OnUsageChanged;
        FormClosed += (_, _) =>
        {
            usage.Changed -= OnUsageChanged;
            places.Changed -= OnUsageChanged;
        };
    }

    private async Task InitAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Paths.LocalDir, "WebView2"));
            await web.EnsureCoreWebView2Async(env);
            var settings = web.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = Debugger.IsAttached;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            web.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                string? Get(string name) => doc.RootElement.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                request = (Get("view"), Get("range"), Get("store"), Get("day"));
                bool first = !ready;
                ready = true;
                if (first && pendingView is { } v && v != request.View)
                {
                    pendingView = null;
                    ShowView(v); // asks again with the right view
                    return;
                }
                Send();
            };
            // Served from a made-up https origin rather than NavigateToString: map tile servers
            // refuse requests that carry no Referer. ".example" is reserved, and WebView2 answers
            // it from the folder below without touching the network.
            var dir = Path.Combine(Paths.LocalDir, "web");
            Directory.CreateDirectory(dir);
            await using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("stats.html")!)
            await using (var file = File.Create(Path.Combine(dir, "stats.html")))
                await stream.CopyToAsync(file);
            web.CoreWebView2.SetVirtualHostNameToFolderMapping("cafewifi.example", dir, CoreWebView2HostResourceAccessKind.DenyCors);
            web.CoreWebView2.Navigate("https://cafewifi.example/stats.html");
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or InvalidOperationException)
        {
            Log.Write($"webview2 unavailable: {ex.Message}");
            ShowRuntimeMissing();
        }
    }

    private void Send()
    {
        if (!ready || IsDisposed || web.CoreWebView2 is null) return;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var range = StatsRange.Parse(request.Range);
        object message = request.View == "footprint"
            ? new { kind = "footprint", data = FootprintModel.Build(usage.Snapshot(), range, today, Locate) }
            : new
            {
                kind = "stats",
                data = StatsModel.Build(usage.Snapshot(), range, request.Store, today, key => catalog.Label(StoreKey.Parse(key)),
                    DateOnly.TryParseExact(request.Day, "yyyy-MM-dd", out var d) ? d : null),
            };
        web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, JsonFile.Options));
    }

    /// <summary>Where a store or own cafe is on the map; null if we don't know.</summary>
    private FootprintPlace? Locate(StoreKey key)
    {
        if (key.BrandId == Place.BrandId)
            return places.Find(key.StoreName) is { } p ? new FootprintPlace(p.Name, p.Lat, p.Lng, p.City, "own") : null;
        return catalog.Find(key) is { HasLocation: true } s ? new FootprintPlace(catalog.Label(key), s.Lat, s.Lng, s.City, "chain") : null;
    }

    private void OnUsageChanged()
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(Send);
    }

    private void ShowRuntimeMissing()
    {
        Controls.Clear();
        var link = new LinkLabel
        {
            Text = "顯示連線統計需要 Microsoft Edge WebView2 Runtime。\n點這裡下載並安裝，完成後再打開一次「連線統計」。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = SystemFonts.MessageBoxFont,
        };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://go.microsoft.com/fwlink/p/?LinkId=2124703") { UseShellExecute = true });
        Controls.Add(link);
    }

    private static bool IsDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    private static void UseDarkTitleBar(IntPtr hwnd)
    {
        int on = 1;
        DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
