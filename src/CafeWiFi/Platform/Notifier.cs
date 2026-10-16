using Microsoft.Toolkit.Uwp.Notifications;

namespace CafeWiFi.Platform;

/// <summary>
/// Windows toast notifications. Unlike tray balloons these still show when the tray icon is
/// hidden. A toast may carry an action id; clicking it raises <see cref="Activated"/>.
/// </summary>
public static class Notifier
{
    private const string Tag = "status";

    /// <summary>Raised on a background thread with the toast's action id.</summary>
    public static event Action<string>? Activated;

    public static void Start()
    {
        ToastNotificationManagerCompat.OnActivated += e =>
        {
            var args = ToastArguments.Parse(e.Argument);
            if (args.TryGetValue("action", out var action)) Activated?.Invoke(action);
        };
    }

    public static void Show(string text, string? action = null)
    {
        try
        {
            var toast = new ToastContentBuilder().AddText("CafeWiFi").AddText(text);
            if (action is not null) toast.AddArgument("action", action);
            // One status toast at a time: a newer one replaces the older.
            toast.Show(t => t.Tag = Tag);
        }
        catch (Exception ex)
        {
            Log.Write($"toast failed: {ex.Message}");
        }
    }
}
