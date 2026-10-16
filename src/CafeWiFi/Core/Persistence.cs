using System.Globalization;
using System.Text.Json;

namespace CafeWiFi.Core;

/// <summary>
/// Small JSON files that survive crashes: each save is flushed to disk under a temp name
/// and swapped in, keeping the previous version as .bak. A file that won't parse is set
/// aside as .bad and the .bak is used instead, or a fresh start if there is none.
/// </summary>
public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // Keep store names readable instead of \uXXXX escapes; these files never land in HTML.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static T Load<T>(string path) where T : new()
    {
        if (TryRead<T>(path, out var value)) return value;
        if (File.Exists(path))
        {
            Log.Write($"corrupt {path}, keeping it as .bad");
            try { File.Move(path, path + ".bad", overwrite: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        if (TryRead<T>(path + ".bak", out var previous))
        {
            Log.Write($"restored {path} from .bak");
            return previous;
        }
        return new T();
    }

    private static bool TryRead<T>(string path, out T value) where T : new()
    {
        value = new T();
        if (!File.Exists(path)) return false;
        try
        {
            if (JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) is not { } v) return false;
            value = v;
            return true;
        }
        catch (JsonException) { return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"cannot read {path}: {ex.Message}");
            return false;
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> Gates =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Safe to call from several threads: writes to one path are serialized (Windows refuses concurrent replaces).</summary>
    public static void Save<T>(string path, T value)
    {
        var full = Path.GetFullPath(path);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        lock (Gates.GetOrAdd(full, _ => new object()))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            var tmp = full + ".tmp";
            using (var file = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true); // a power cut must not leave a torn file behind
            }
            if (File.Exists(full)) File.Replace(tmp, full, full + ".bak", ignoreMetadataErrors: true);
            else File.Move(tmp, full);
        }
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Write($"cannot save learned: {ex.Message}"); }
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Write($"cannot save usage: {ex.Message}"); }
        }
        Changed?.Invoke();
    }
}
