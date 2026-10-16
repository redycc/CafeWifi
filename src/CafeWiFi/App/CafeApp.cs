using System.Net.NetworkInformation;
using CafeWiFi.Core;
using CafeWiFi.Platform;

namespace CafeWiFi.App;

/// <summary>Wires the services together and owns the tray icon for the life of the app.</summary>
public sealed class CafeApp : ApplicationContext
{
    private readonly Settings settings = Settings.Load();
    private readonly BrandCatalog catalog = BrandCatalog.LoadDefault(Paths.UserBrands);
    private readonly UsageStore usage = new(Paths.Usage);
    private readonly WindowsLocator locator;
    private readonly Joiner joiner;
    private readonly Tracker tracker;
    private readonly Updater updater;
    private readonly TrayController tray;
    private readonly SynchronizationContext ui;
    private readonly System.Windows.Forms.Timer autoTimer = new() { Interval = 15_000 };
    private StatsForm? stats;

    public CafeApp(Command first)
    {
        var wifi = new NativeWifiService();
        locator = new WindowsLocator(settings);
        joiner = new Joiner(wifi, locator, new Connectivity(), catalog, new LearnedStore(Paths.Learned),
            () => settings.AutoConnect, () => DateTimeOffset.Now);
        tracker = new Tracker(joiner.CurrentStoreKey, usage);
        updater = new Updater(settings, text => Notifier.Show(text), () => ui.Post(_ => Quit(), null));
        tray = new TrayController(joiner, catalog, locator, settings, updater, ShowStats, Quit);
        ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        joiner.StatusChanged += (status, notify) =>
        {
            WriteStatus(status);
            ui.Post(_ => tray.Update(status), null);
            if (notify) Notifier.Show(status.Text);
        };
        Notifier.Activated += action => ui.Post(_ => OnToast(action), null);
        Notifier.Start();
        SingleInstance.Listen(args => ui.Post(_ => Handle(CommandLine.Parse(args)), null));

        locator.Changed += Auto;
        NetworkChange.NetworkAvailabilityChanged += (_, _) => Auto();
        NetworkChange.NetworkAddressChanged += (_, _) => Auto();
        autoTimer.Tick += (_, _) => Auto();
        autoTimer.Start();
        tracker.Start();
        updater.Start();
        _ = StartLocationAsync();

        Log.Write($"started {Updater.Current}: {string.Join(", ", catalog.Brands.Select(b => $"{b.Id} {b.Stores.Count}"))} stores");
        Handle(first);
        Auto();
    }

    private void Auto() => TrayController.Run(joiner.AutoConnectAsync);

    private async Task StartLocationAsync()
    {
        await locator.StartAsync();
        if (locator.Authorized || settings.LocationHintShown) return;
        // Without location Windows hides SSIDs and BSSIDs too, so say so once.
        settings.LocationHintShown = true;
        settings.Save();
        Notifier.Show("請允許「位置」權限，CafeWiFi 才能讀取 Wi-Fi 並找到最近的門市。點這裡開啟設定。", "location");
    }

    private void OnToast(string action)
    {
        if (action == "location") TrayController.OpenLocationSettings();
    }

    /// <summary>A command from this launch or a later one (forwarded over the pipe).</summary>
    public void Handle(Command cmd)
    {
        switch (cmd.Kind)
        {
            case "show":
                tray.ShowIcon();
                break;
            case "connect":
                var matches = catalog.Resolve(cmd.Store!, cmd.Brand);
                if (matches.Count == 1)
                    TrayController.Run(() => joiner.ConnectAsync(matches[0].Brand, matches[0].Store));
                else
                    Notifier.Show(matches.Count == 0
                        ? $"找不到門市「{cmd.Store}」"
                        : $"有 {matches.Count} 家門市符合「{cmd.Store}」，請輸入更完整的名稱");
                break;
            case "nearest":
                TrayController.Run(joiner.ConnectNearestAsync);
                break;
            case "stats":
                ShowStats();
                break;
            case "update":
                TrayController.Run(() => updater.Available is null ? updater.CheckAsync(manual: true) : updater.InstallAsync());
                break;
        }
    }

    private void ShowStats()
    {
        if (stats is null || stats.IsDisposed) stats = new StatsForm(usage, catalog);
        stats.Show();
        if (stats.WindowState == FormWindowState.Minimized) stats.WindowState = FormWindowState.Normal;
        stats.Activate();
    }

    /// <summary>status.json, for scripts that want to know what happened after "--connect".</summary>
    private static void WriteStatus(Status status)
    {
        var state = status switch
        {
            Status.Connected => "connected",
            Status.Connecting => "connecting",
            Status.Failed => "failed",
            _ => "idle",
        };
        try { JsonFile.Save(Paths.Status, new { state, message = status.Text, time = DateTimeOffset.Now.ToUnixTimeSeconds() }); }
        catch (IOException) { }
    }

    private void Quit()
    {
        autoTimer.Stop();
        stats?.Close();
        tray.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            autoTimer.Dispose();
            tracker.Dispose();
            tray.Dispose();
        }
        base.Dispose(disposing);
    }
}
