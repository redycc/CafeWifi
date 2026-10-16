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
/// "連線統計": stats.html in WebView2. The page posts {range, store, day}; we answer with
/// StatsModel's view as JSON, and push a fresh one whenever usage changes.
/// </summary>
public sealed class StatsForm : Form
{
    private readonly UsageStore usage;
    private readonly BrandCatalog catalog;
    private readonly WebView2 web = new() { Dock = DockStyle.Fill };
    private (string? Range, string? Store, string? Day) request;
    private bool ready;

    public StatsForm(UsageStore usage, BrandCatalog catalog)
    {
        this.usage = usage;
        this.catalog = catalog;
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
        FormClosed += (_, _) => usage.Changed -= OnUsageChanged;
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
                request = (Get("range"), Get("store"), Get("day"));
                ready = true;
                Send();
            };
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("stats.html")!;
            using var reader = new StreamReader(stream);
            web.NavigateToString(await reader.ReadToEndAsync());
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
        var day = DateOnly.TryParseExact(request.Day, "yyyy-MM-dd", out var d) ? d : (DateOnly?)null;
        var view = StatsModel.Build(usage.Snapshot(), StatsRange.Parse(request.Range), request.Store,
            DateOnly.FromDateTime(DateTime.Now), key => catalog.Label(StoreKey.Parse(key)), day);
        web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(view, JsonFile.Options));
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
