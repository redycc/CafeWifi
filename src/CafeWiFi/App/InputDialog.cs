namespace CafeWiFi.App;

/// <summary>A one-line prompt: "輸入密碼…" for a store's posted password, or a cafe's name.</summary>
public sealed class InputDialog : Form
{
    private readonly TextBox field = new() { Width = 280, Margin = new Padding(0, 8, 0, 12) };

    private InputDialog(string title, string hint, string? placeholder, string? initial)
    {
        Text = "CafeWiFi";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = SystemFonts.MessageBoxFont;
        Padding = new Padding(16);
        field.PlaceholderText = placeholder ?? "";
        field.Text = initial ?? "";

        var ok = new Button { Text = "連線", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        AcceptButton = ok;
        CancelButton = cancel;

        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        layout.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        layout.Controls.Add(new Label
        {
            Text = hint,
            AutoSize = true, MaximumSize = new Size(300, 0), Margin = new Padding(0, 6, 0, 0),
        });
        layout.Controls.Add(field);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Width = 280 };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        Shown += (_, _) => { Activate(); field.Focus(); field.SelectAll(); };
    }

    /// <summary>The trimmed text, or null if cancelled or empty.</summary>
    public static string? Ask(string title, string hint, string? placeholder = null, string? initial = null)
    {
        using var dialog = new InputDialog(title, hint, placeholder, initial);
        if (dialog.ShowDialog() != DialogResult.OK) return null;
        var text = dialog.field.Text.Trim();
        return text.Length > 0 ? text : null;
    }
}
