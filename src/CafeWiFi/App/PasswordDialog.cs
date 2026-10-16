namespace CafeWiFi.App;

/// <summary>"輸入密碼…": type the password posted in the store.</summary>
public sealed class PasswordDialog : Form
{
    private readonly TextBox field = new() { Width = 280, Margin = new Padding(0, 8, 0, 12) };

    private PasswordDialog(string store, string? placeholder)
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

        var ok = new Button { Text = "連線", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        AcceptButton = ok;
        CancelButton = cancel;

        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        layout.Controls.Add(new Label { Text = $"輸入{store}的 Wi-Fi 密碼", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        layout.Controls.Add(new Label
        {
            Text = "照店內張貼的密碼輸入。連上之後會記住，下次自動使用。",
            AutoSize = true, MaximumSize = new Size(300, 0), Margin = new Padding(0, 6, 0, 0),
        });
        layout.Controls.Add(field);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Width = 280 };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        Shown += (_, _) => { Activate(); field.Focus(); };
    }

    /// <summary>The trimmed password, or null if cancelled or empty.</summary>
    public static string? Ask(string store, string? placeholder)
    {
        using var dialog = new PasswordDialog(store, placeholder);
        if (dialog.ShowDialog() != DialogResult.OK) return null;
        var password = dialog.field.Text.Trim();
        return password.Length > 0 ? password : null;
    }
}
