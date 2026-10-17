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

    /// <param name="action">Raised when the toast itself is clicked.</param>
    /// <param name="button">An extra button and the action it raises.</param>
    public static void Show(string text, string? action = null, (string Text, string Action)? button = null)
    {
        try
        {
            var toast = new ToastContentBuilder().AddText("CafeWiFi").AddText(text);
            if (action is not null) toast.AddArgument("action", action);
            if (button is { } b) toast.AddButton(new ToastButton().SetContent(b.Text).AddArgument("action", b.Action));
            // One status toast at a time: a newer one replaces the older.
            toast.Show(t => t.Tag = Tag);
        }
        catch (Exception ex)
        {
            Log.Write($"toast failed: {ex.Message}");
        }
    }
}
