using CafeWiFi.Core;

namespace CafeWiFi.App;

/// <summary>
/// Counts minutes on cafe Wi-Fi per day and store. The timer doesn't run while the PC
/// sleeps, so a closed lid isn't counted.
/// </summary>
public sealed class Tracker : IDisposable
{
    public const int IntervalSeconds = 60;

    private readonly Func<StoreKey?> currentStore;
    private readonly UsageStore usage;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = IntervalSeconds * 1000 };

    public Tracker(Func<StoreKey?> currentStore, UsageStore usage)
    {
        this.currentStore = currentStore;
        this.usage = usage;
        timer.Tick += (_, _) => Tick(DateOnly.FromDateTime(DateTime.Now));
    }

    public void Start() => timer.Start();

    public void Tick(DateOnly today)
    {
        if (currentStore() is { } store) usage.Add(today, store, IntervalSeconds);
    }

    public void Dispose() => timer.Dispose();
}
