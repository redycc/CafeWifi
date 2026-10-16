using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using CafeWiFi.Core;

namespace CafeWiFi.App;

/// <summary>
/// Checks GitHub Releases for a newer CafeWiFi-win-x64.zip and swaps the exe in place.
/// The repo comes from build metadata (CI sets it); without it updates are off.
/// </summary>
public sealed class Updater
{
    public const string Asset = "CafeWiFi-win-x64.zip";

    private static readonly HttpClient Http = CreateClient();
    private readonly Settings settings;
    private readonly Action<string> notify;
    private System.Threading.Timer? timer;

    public Updater(Settings settings, Action<string> notify)
    {
        this.settings = settings;
        this.notify = notify;
    }

    public static string Current { get; } = VersionCompare.Strip(
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0");

    public static string? Repo { get; } = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "GitHubRepo")?.Value is { Length: > 0 } r ? r : null;

    public static bool Enabled => Repo is not null;

    /// <summary>A newer release, if any.</summary>
    public (string Version, Uri Zip)? Available { get; private set; }

    public void Start()
    {
        if (!Enabled) return;
        timer = new System.Threading.Timer(_ => _ = CheckAsync(manual: false), null, TimeSpan.FromSeconds(10), TimeSpan.FromHours(6));
    }

    public async Task CheckAsync(bool manual)
    {
        if (!Enabled) return;
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"));
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var zip = doc.RootElement.GetProperty("assets").EnumerateArray()
                .FirstOrDefault(a => a.GetProperty("name").GetString() == Asset);
            if (zip.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{Asset} missing from {tag}");

            var version = VersionCompare.Strip(tag);
            if (VersionCompare.Compare(version, Current) <= 0)
            {
                if (manual) notify($"目前已是最新版本（{Current}）");
                return;
            }
            if (manual || settings.NotifiedVersion != version)
            {
                notify($"有新版本 {version}，請從 ☕ 選單選擇「更新到 {version}」");
                settings.NotifiedVersion = version;
                settings.Save();
            }
            Available = (version, new Uri(zip.GetProperty("browser_download_url").GetString()!));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or KeyNotFoundException or InvalidDataException or InvalidOperationException)
        {
            Log.Write($"update check failed: {ex.Message}");
            if (manual) notify("檢查更新失敗，請稍後再試");
        }
    }

    /// <summary>Download, unzip, and let a script swap the exe after we exit, then relaunch.</summary>
    public async Task InstallAsync()
    {
        if (Available is not { } update) return;
        var exe = Environment.ProcessPath!;
        if (!CanWrite(Path.GetDirectoryName(exe)!))
        {
            OpenReleasesPage();
            return;
        }
        notify($"正在下載 {update.Version}…");
        try
        {
            var dir = Directory.CreateTempSubdirectory($"CafeWiFi-{update.Version}-").FullName;
            var archive = Path.Combine(dir, Asset);
            await using (var file = File.Create(archive))
            await using (var body = await Http.GetStreamAsync(update.Zip))
                await body.CopyToAsync(file);
            ZipFile.ExtractToDirectory(archive, Path.Combine(dir, "x"));
            var fresh = Directory.GetFiles(Path.Combine(dir, "x"), "CafeWiFi.exe", SearchOption.AllDirectories).FirstOrDefault()
                        ?? throw new FileNotFoundException("CafeWiFi.exe not in update");

            var script = Path.Combine(dir, "update.cmd");
            // cmd mis-parses labels in LF-only batch files.
            await File.WriteAllTextAsync(script, SwapScript.ReplaceLineEndings("\r\n"));
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"\"{script}\" {Environment.ProcessId} \"{exe}\" \"{fresh}\"\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            Log.Write($"updating to {update.Version}");
            System.Windows.Forms.Application.Exit();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException
                                       or System.ComponentModel.Win32Exception)
        {
            Log.Write($"update failed: {ex.Message}");
            notify("更新失敗，請到 GitHub 下載新版");
        }
    }

    public static void OpenReleasesPage()
    {
        if (Repo is null) return;
        Process.Start(new ProcessStartInfo($"https://github.com/{Repo}/releases/latest") { UseShellExecute = true });
    }

    private static bool CanWrite(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".cafewifi-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CafeWiFi", Current));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    // Args: pid of the running app, its exe, the new exe. Waits for us to exit, swaps, relaunches.
    // On failure it puts the old exe back and starts that.
    private const string SwapScript = """
        @echo off
        setlocal
        set "PID=%~1"
        set "EXE=%~2"
        set "NEW=%~3"
        :wait
        tasklist /FI "PID eq %PID%" /NH 2>nul | find "%PID%" >nul && (timeout /t 1 /nobreak >nul & goto wait)
        move /Y "%EXE%" "%EXE%.old" >nul || goto fail
        move /Y "%NEW%" "%EXE%" >nul || (move /Y "%EXE%.old" "%EXE%" >nul & goto fail)
        start "" "%EXE%"
        del "%EXE%.old" >nul 2>&1
        exit /b 0
        :fail
        start "" "%EXE%"
        exit /b 1
        """;
}
