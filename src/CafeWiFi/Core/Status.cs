namespace CafeWiFi.Core;

/// <summary>What the tray shows and notifications say.</summary>
public abstract record Status
{
    public abstract string Text { get; }

    public sealed record Idle : Status
    {
        public override string Text => "附近沒有咖啡廳 Wi-Fi";
    }

    public sealed record Connecting(string What) : Status
    {
        public override string Text => $"連線中：{What}…";
    }

    /// <summary>Store is null when we're on a cafe network but haven't learned which store it is.</summary>
    public sealed record Connected(string? Store, string? Ssid) : Status
    {
        public override string Text => Store is not null ? $"已連上：{Store}" : $"已連上 {Ssid}";
    }

    public sealed record Failed(string Why) : Status
    {
        public override string Text => Why;
    }
}
