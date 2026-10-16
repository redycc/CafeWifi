using System.Diagnostics;
using System.Reflection;
using CafeWiFi.Core;
using CafeWiFi.Platform;

namespace CafeWiFi.App;

/// <summary>The ☕ in the notification area and its menu, rebuilt every time it opens.</summary>
public sealed class TrayController : IDisposable
{
    private readonly NotifyIcon icon = new();
    private readonly ContextMenuStrip menu = new() { ShowItemToolTips = true };
    private readonly Icon idleIcon = LoadIcon("cup"), connectedIcon = LoadIcon("cup-filled"), connectingIcon = LoadIcon("connecting");

    private readonly Joiner joiner;
    private readonly BrandCatalog catalog;
    private readonly ILocator locator;
    private readonly Settings settings;
    private readonly Updater updater;
    private readonly Action showStats;
    private readonly Action exit;
    private readonly System.Windows.Forms.Timer leftClick = new() { Interval = SystemInformation.DoubleClickTime };
    private Status status = new Status.Idle();

    public TrayController(Joiner joiner, BrandCatalog catalog, ILocator locator, Settings settings, Updater updater,
        Action showStats, Action exit)
    {
        (this.joiner, this.catalog, this.locator, this.settings, this.updater, this.showStats, this.exit) =
            (joiner, catalog, locator, settings, updater, showStats, exit);
        menu.Opening += (_, _) => Rebuild();
        icon.ContextMenuStrip = menu;
        // Left click opens the same menu, like the Mac menu bar; double click opens stats.
        // The first click of a double click also arrives as a click, so wait to see which it is.
        leftClick.Tick += (_, _) =>
        {
            leftClick.Stop();
            typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(icon, null);
        };
        icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) leftClick.Start(); };
        icon.DoubleClick += (_, _) =>
        {
            leftClick.Stop();
            showStats();
        };
        Update(joiner.CurrentStatus());
        icon.Visible = !settings.HideTrayIcon;
    }

    public void ShowIcon()
    {
        settings.HideTrayIcon = false;
        settings.Save();
        icon.Visible = true;
    }

    public void Update(Status s)
    {
        status = s;
        icon.Icon = s switch
        {
            Status.Connected => connectedIcon,
            Status.Connecting => connectingIcon,
            _ => idleIcon,
        };
        var tip = $"CafeWiFi：{s.Text}";
        icon.Text = tip.Length > 127 ? tip[..127] : tip;
    }

    // MARK: menu

    private void Rebuild()
    {
        // Keep showing a failure until we're actually connected somewhere.
        if (status is not Status.Connecting)
        {
            var now = joiner.CurrentStatus();
            Update(now is Status.Idle && status is Status.Failed ? status : now);
        }

        var items = menu.Items;
        items.Clear();
        if (updater.Available is { } update)
        {
            var u = Action($"更新到 {update.Version}", () => Run(updater.InstallAsync));
            u.Font = new Font(u.Font, FontStyle.Bold);
            items.Add(u);
            items.Add(new ToolStripSeparator());
        }
        items.Add(Disabled(status.Text));
        if (PasswordMenu() is { } passwords) items.Add(passwords);
        items.Add(new ToolStripSeparator());

        bool onlyKnown = settings.OnlyKnown;
        var known = joiner.KnownStores();
        bool Shown(Brand b, Store s) => !onlyKnown || known.Contains(new StoreKey(b.Id, s.Name).ToString());
        bool any = catalog.Brands.Any(b => b.Stores.Any(s => Shown(b, s)));

        if (onlyKnown && !any)
        {
            items.Add(Disabled("還沒有連過任何門市"));
        }
        else if (locator.Location is { } here)
        {
            items.Add(Disabled(onlyKnown ? "連過的門市" : "附近門市"));
            foreach (var (brand, store, meters) in catalog.Nearest(here, 6, Shown))
                items.Add(StoreItem(store, $"{Label(brand, store)}  ·  {Geo.Format(meters)}", () => Connect(brand, store)));
        }
        else
        {
            items.Add(Action("連線到最近的門市", () => Run(joiner.ConnectNearestAsync)));
        }

        var all = new ToolStripMenuItem(onlyKnown ? "所有連過的門市" : "所有門市") { Enabled = any };
        Lazy(all, () => catalog.Brands.Count == 1
            ? CityItems(catalog.Brands[0], Shown)
            : catalog.Brands.Where(b => b.Stores.Any(s => Shown(b, s))).Select(b =>
            {
                var brandItem = new ToolStripMenuItem(b.Name);
                Lazy(brandItem, () => CityItems(b, Shown));
                return (ToolStripItem)brandItem;
            }));
        items.Add(all);
        items.Add(new ToolStripSeparator());

        items.Add(Action("連線統計…", showStats));
        items.Add(new ToolStripSeparator());

        if (!locator.Authorized) items.Add(Action("開啟定位權限…", OpenLocationSettings));
        items.Add(Toggle("自動連線", settings.AutoConnect, on =>
        {
            settings.AutoConnect = on;
            settings.Save();
            if (on) Run(joiner.AutoConnectAsync);
        }));
        items.Add(Toggle("只顯示連過的門市", onlyKnown, on => { settings.OnlyKnown = on; settings.Save(); }));
        items.Add(Toggle("開機時啟動", Startup.Enabled, Startup.Set));
        items.Add(Action("不在系統匣顯示…", HideIcon));
        items.Add(new ToolStripSeparator());
        if (Updater.Enabled) items.Add(Action($"檢查更新（目前 {Updater.Current}）", () => Run(() => updater.CheckAsync(manual: true))));
        items.Add(Action("結束", exit));
    }

    /// <summary>For when the password is posted on the wall: the nearest store's candidates, or type one.</summary>
    private ToolStripMenuItem? PasswordMenu()
    {
        // Open networks (e.g. Starbucks) have no passwords to pick from.
        if (locator.Location is not { } here || catalog.Nearest(here, 1, (b, _) => !b.IsOpen) is not [var (brand, store, _)]) return null;
        var item = new ToolStripMenuItem("用密碼連線");
        item.DropDownItems.Add(Disabled(Label(brand, store)));
        foreach (var p in PasswordItems(brand, store)) item.DropDownItems.Add(p);
        return item;
    }

    private IEnumerable<ToolStripItem> CityItems(Brand brand, Func<Brand, Store, bool> shown) =>
        BrandCatalog.ByCity(brand.Stores.Where(s => shown(brand, s))).Select(group =>
        {
            var city = new ToolStripMenuItem($"{group.City}（{group.Stores.Count}）");
            Lazy(city, () => group.Stores.Select(store =>
            {
                // No passwords to choose between: clicking the store connects.
                if (brand.IsOpen) return (ToolStripItem)StoreItem(store, store.Name, () => Connect(brand, store));
                var item = StoreItem(store, store.Name, null);
                Lazy(item, () =>
                    new ToolStripItem[] { Action("自動嘗試", () => Connect(brand, store)), new ToolStripSeparator() }
                        .Concat(PasswordItems(brand, store)));
                return (ToolStripItem)item;
            }));
            return (ToolStripItem)city;
        });

    /// <summary>The store's candidate passwords, then 輸入密碼… for anything else.</summary>
    private IEnumerable<ToolStripItem> PasswordItems(Brand brand, Store store) =>
        store.Passwords.Select(p => (ToolStripItem)Action(p, () => Connect(brand, store, p)))
            .Append(Action("輸入密碼…", () =>
            {
                if (PasswordDialog.Ask(Label(brand, store), store.Passwords.FirstOrDefault()) is { } typed)
                    Connect(brand, store, typed);
            }));

    private static ToolStripMenuItem StoreItem(Store store, string text, Action? onClick)
    {
        var item = onClick is null ? new ToolStripMenuItem(text) : Action(text, onClick);
        item.ToolTipText = $"{store.Address}\n{store.Phone}";
        return item;
    }

    private string Label(Brand brand, Store store) => catalog.Label(new StoreKey(brand.Id, store.Name));

    private void Connect(Brand brand, Store store, string? password = null) => Run(() => joiner.ConnectAsync(brand, store, password));

    // MARK: actions

    private void HideIcon()
    {
        var answer = MessageBox.Show(
            "CafeWiFi 會繼續在背景自動連線、記錄時間。\n\n想再顯示時，再打開一次 CafeWiFi 就會出現。",
            "不在系統匣顯示 ☕？", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;
        settings.HideTrayIcon = true;
        settings.Save();
        icon.Visible = false;
    }

    public static void OpenLocationSettings() =>
        Process.Start(new ProcessStartInfo("ms-settings:privacy-location") { UseShellExecute = true });

    /// <summary>Runs Wi-Fi work off the UI thread; failures go to the log.</summary>
    public static void Run(Func<Task> work) => Task.Run(async () =>
    {
        try { await work(); }
        catch (Exception ex) { Log.Write($"error: {ex}"); }
    });

    // MARK: item helpers

    private static ToolStripMenuItem Disabled(string text) => new(text) { Enabled = false };

    private static ToolStripMenuItem Action(string text, Action onClick)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => onClick();
        return item;
    }

    private static ToolStripMenuItem Toggle(string text, bool on, Action<bool> set)
    {
        var item = new ToolStripMenuItem(text) { Checked = on };
        item.Click += (_, _) => set(!item.Checked);
        return item;
    }

    /// <summary>Fills a submenu the first time it opens; hundreds of stores would make the menu slow to build.</summary>
    private static void Lazy(ToolStripMenuItem item, Func<IEnumerable<ToolStripItem>> fill)
    {
        item.DropDownItems.Add(new ToolStripMenuItem("…") { Enabled = false });
        bool filled = false;
        item.DropDownOpening += (_, _) =>
        {
            if (filled) return;
            filled = true;
            item.DropDownItems.Clear();
            item.DropDownItems.AddRange(fill().ToArray());
        };
    }

    private static Icon LoadIcon(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"icons.{name}.ico")!;
        return new Icon(s, SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        leftClick.Dispose();
        icon.Visible = false;
        icon.Dispose();
        menu.Dispose();
    }
}
