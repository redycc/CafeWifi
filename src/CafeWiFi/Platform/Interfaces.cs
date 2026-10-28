using CafeWiFi.Core;

namespace CafeWiFi.Platform;

/// <summary>A router in range. Bssid is normalized; Rssi is in dBm.</summary>
public sealed record Bss(string Ssid, string Bssid, int Rssi);

/// <summary>The network Wi-Fi is associated with. Bssid may be null if Windows won't say.</summary>
public sealed record WifiLink(string? Ssid, string? Bssid);

public interface IWifi
{
    /// <summary>A wireless interface exists and its radio is on.</summary>
    bool HasInterface { get; }

    /// <summary>Null when Wi-Fi isn't associated with anything.</summary>
    WifiLink? CurrentLink();

    /// <summary>The password Windows saved for the network we're on, or null if it can't be read.</summary>
    string? CurrentPassword();

    /// <summary>Asks for a fresh scan (waiting up to 4 s), then returns every router in range.</summary>
    Task<IReadOnlyList<Bss>> ScanAsync(CancellationToken ct = default);

    /// <summary>SSIDs of open (passwordless) networks in range, so a password guess can skip them.</summary>
    IReadOnlySet<string> OpenSsids();

    /// <summary>Writes a profile for target.Ssid with this password ("" for an open network) and joins that exact router.</summary>
    Task<bool> TryJoinAsync(Bss target, string password, CancellationToken ct = default);

    /// <summary>Leaves the current network.</summary>
    void Disconnect();
}

public interface IConnectivity
{
    Task<bool> IsOnlineAsync();
}

public interface ILocator
{
    /// <summary>The last known location, however old: inside a cafe there's often no fresh fix.</summary>
    GeoPoint? Location { get; }
    bool Authorized { get; }
    event Action? Changed;

    /// <summary>A fresh fix (we're online, so it's accurate), or null if there's none from the last few minutes.</summary>
    Task<GeoPoint?> RefreshAsync();
}
