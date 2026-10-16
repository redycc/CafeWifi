using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace CafeWiFi.Platform;

/// <summary>
/// One running copy per user. A second launch hands its arguments to the first over a
/// named pipe and exits; that's how "--connect 延平" or reopening the app reaches it.
/// </summary>
public static class SingleInstance
{
    private static readonly string Id = $"CafeWiFi-{System.Diagnostics.Process.GetCurrentProcess().SessionId}-{Environment.UserName}";
    private static Mutex? mutex;

    public static bool TryAcquire()
    {
        mutex = new Mutex(initiallyOwned: true, @"Local\" + Id, out bool created);
        return created;
    }

    public static bool Send(string[] args)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", Id, PipeDirection.Out);
            pipe.Connect(3000);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(args));
            pipe.Write(bytes);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            Log.Write($"cannot reach running instance: {ex.Message}");
            return false;
        }
    }

    /// <summary>Calls onArgs (on a background thread) for every later launch.</summary>
    public static void Listen(Action<string[]> onArgs)
    {
        new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(Id, PipeDirection.In, 1, PipeTransmissionMode.Byte);
                    pipe.WaitForConnection();
                    using var reader = new StreamReader(pipe, Encoding.UTF8);
                    var args = JsonSerializer.Deserialize<string[]>(reader.ReadToEnd()) ?? Array.Empty<string>();
                    onArgs(args);
                }
                catch (Exception ex)
                {
                    Log.Write($"pipe: {ex.Message}");
                    Thread.Sleep(1000);
                }
            }
        }) { IsBackground = true, Name = "CafeWiFi pipe" }.Start();
    }
}
