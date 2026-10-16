using System.Runtime.InteropServices;
using CafeWiFi.App;
using CafeWiFi.Core;
using CafeWiFi.Platform;

namespace CafeWiFi;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var cmd = CommandLine.Parse(args);

        if (!SingleInstance.TryAcquire())
        {
            // Already running: answer "--connect" ambiguities here, where the caller can see them,
            // then hand the arguments over.
            if (cmd.Kind == "connect" && Ambiguous(cmd) is int code) return code;
            // Launched at sign-in twice, or similar: nothing to forward.
            if (cmd.Kind == "startup") return 0;
            return SingleInstance.Send(args) ? 0 : 1;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Write($"unhandled: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"fatal: {e.ExceptionObject}");
        ApplicationConfiguration.Initialize();
        Application.Run(new CafeApp(cmd));
        return 0;
    }

    /// <summary>Prints the matches and returns 2 when "--connect" doesn't name exactly one store (3 for none).</summary>
    private static int? Ambiguous(Command cmd)
    {
        var matches = BrandCatalog.LoadDefault(Paths.UserBrands).Resolve(cmd.Store!, cmd.Brand);
        if (matches.Count == 1) return null;
        AttachConsole(-1); // ATTACH_PARENT_PROCESS: we're a GUI exe, so borrow the caller's console
        if (matches.Count == 0)
        {
            Console.WriteLine($"找不到門市「{cmd.Store}」");
            return 3;
        }
        Console.WriteLine($"有 {matches.Count} 家門市符合「{cmd.Store}」：");
        foreach (var (brand, store) in matches) Console.WriteLine($"  {store.Name}（{brand.Name}）  {store.Address}");
        return 2;
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
