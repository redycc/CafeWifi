namespace CafeWiFi.Core;

/// <summary>
/// What a launch asks for. Kinds: show, connect, nearest, stats, update, capture-portal
/// (save the terms page of the open network we're on), startup (launched at sign-in: run
/// quietly without bringing back a hidden icon).
/// </summary>
public sealed record Command(string Kind, string? Store = null, string? Brand = null);

/// <summary>
/// CafeWiFi.exe [--connect &lt;store words…&gt; [--brand &lt;id&gt;]] [--nearest] [--stats] [--show] [--update].
/// Anything unrecognized means "show".
/// </summary>
public static class CommandLine
{
    public static Command Parse(string[] args)
    {
        string? kind = null, brand = null;
        var store = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--connect": kind = "connect"; break;
                case "--brand" when i + 1 < args.Length: brand = args[++i]; break;
                case "--nearest": kind ??= "nearest"; break;
                case "--stats": kind ??= "stats"; break;
                case "--footprint": kind ??= "footprint"; break;
                case "--update": kind ??= "update"; break;
                case "--startup": kind ??= "startup"; break;
                case "--capture-portal": kind ??= "capture-portal"; break;
                case "--show": kind ??= "show"; break;
                default:
                    if (kind == "connect" && !args[i].StartsWith("--", StringComparison.Ordinal)) store.Add(args[i]);
                    else return new Command("show");
                    break;
            }
        }
        if (kind == "connect")
            return store.Count > 0 ? new Command("connect", string.Join(" ", store), brand) : new Command("show");
        return new Command(kind ?? "show");
    }
}

public static class VersionCompare
{
    /// <summary>"v1.2.3+sha" → "1.2.3".</summary>
    public static string Strip(string version)
    {
        var v = version.Trim();
        if (v.StartsWith('v') || v.StartsWith('V')) v = v[1..];
        int plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }

    /// <summary>Numeric, part by part; missing parts count as 0 and pre-release tags are ignored.</summary>
    public static int Compare(string a, string b)
    {
        int[] Parts(string s) => Strip(s).Split('-')[0].Split('.')
            .Select(p => int.TryParse(p, out int n) ? n : 0).ToArray();
        var (x, y) = (Parts(a), Parts(b));
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            int c = (i < x.Length ? x[i] : 0).CompareTo(i < y.Length ? y[i] : 0);
            if (c != 0) return c;
        }
        return 0;
    }
}
