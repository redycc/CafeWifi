using System.Globalization;
using System.Text.Json;

namespace CafeWiFi.Core;

/// <summary>
/// Small JSON files that survive crashes: writes go to .tmp first, and a file that won't
/// parse is set aside as .bad so the app starts fresh instead of failing.
/// </summary>
public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static T Load<T>(string path) where T : new()
    {
        if (!File.Exists(path)) return new T();
        try
        {
            var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
            if (value is not null) return value;
        }
        catch (JsonException) { }
        catch (IOException ex) { Log.Write($"cannot read {path}: {ex.Message}"); return new T(); }
        Log.Write($"corrupt {path}, keeping it as .bad");
        try { File.Move(path, path + ".bad", overwrite: true); } catch (IOException) { }
        return new T();
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>What worked at a router: the store it belongs to (a StoreKey string) and its password.</summary>
public sealed record Learned(string Store, string Password);

/// <summary>BSSID → store and password that worked there. learned.json.</summary>
public sealed class LearnedStore
{
    private readonly string path;
    private readonly object gate = new();
    private readonly Dictionary<string, Learned> map;

    public LearnedStore(string path)
    {
        this.path = path;
        map = JsonFile.Load<Dictionary<string, Learned>>(path);
    }

    public Learned? Lookup(string bssid)
    {
        lock (gate) return map.GetValueOrDefault(Bssid.Normalize(bssid));
    }

    public void Remember(IEnumerable<string> bssids, StoreKey store, string password)
    {
        lock (gate)
        {
            foreach (var b in bssids) map[Bssid.Normalize(b)] = new Learned(store.ToString(), password);
            try { JsonFile.Save(path, map); }
            catch (IOException ex) { Log.Write($"cannot save learned: {ex.Message}"); }
        }
    }

    /// <summary>StoreKey strings of every store we've joined.</summary>
    public IReadOnlySet<string> KnownStores()
    {
        lock (gate) return map.Values.Select(l => l.Store).ToHashSet();
    }
}

/// <summary>Seconds on cafe Wi-Fi per local day and store. usage.json: "yyyy-MM-dd" → StoreKey → seconds.</summary>
public sealed class UsageStore
{
    private readonly string path;
    private readonly object gate = new();
    private readonly Dictionary<string, Dictionary<string, double>> usage;

    public event Action? Changed;

    public UsageStore(string path)
    {
        this.path = path;
        usage = JsonFile.Load<Dictionary<string, Dictionary<string, double>>>(path);
    }

    public static string DayKey(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Snapshot()
    {
        lock (gate)
            return usage.ToDictionary(d => d.Key, d => (IReadOnlyDictionary<string, double>)new Dictionary<string, double>(d.Value));
    }

    public void Add(DateOnly day, StoreKey store, double seconds)
    {
        lock (gate)
        {
            var key = DayKey(day);
            if (!usage.TryGetValue(key, out var stores)) usage[key] = stores = new();
            stores[store.ToString()] = stores.GetValueOrDefault(store.ToString()) + seconds;
            try { JsonFile.Save(path, usage); }
            catch (IOException ex) { Log.Write($"cannot save usage: {ex.Message}"); }
        }
        Changed?.Invoke();
    }
}
