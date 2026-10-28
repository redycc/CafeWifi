using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Text;
using ManagedNativeWifi;
using CafeWiFi.Core;

namespace CafeWiFi.Platform;

/// <summary>
/// The Windows Native Wifi API via ManagedNativeWifi. Uses the first wireless interface.
/// Every call tolerates a stopped WLAN service or missing adapter by reporting "nothing".
/// </summary>
public sealed class NativeWifiService : IWifi
{
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(20);

    private static InterfaceConnectionInfo? Interface()
    {
        try { return NativeWifi.EnumerateInterfaceConnections().FirstOrDefault(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Write($"wlan unavailable: {ex.Message}");
            return null;
        }
    }

    public bool HasInterface => Interface()?.IsRadioOn == true;

    public WifiLink? CurrentLink()
    {
        var i = Interface();
        if (i is null || !i.IsConnected) return null;
        var link = WlanCurrentConnection.Query(i.Id);
        return link is { } l ? new WifiLink(l.Ssid, Bssid.Normalize(l.Bssid)) : new WifiLink(i.ProfileName, null);
    }

    public string? CurrentPassword()
    {
        var i = Interface();
        if (i is null || !i.IsConnected || string.IsNullOrEmpty(i.ProfileName)) return null;
        return WlanProfileKey.Read(i.Id, i.ProfileName);
    }

    public async Task<IReadOnlyList<Bss>> ScanAsync(CancellationToken ct = default)
    {
        var i = Interface();
        if (i is null || !i.IsRadioOn) return Array.Empty<Bss>();
        try
        {
            await NativeWifi.ScanNetworksAsync(ScanTimeout, ct);
            return NativeWifi.EnumerateBssNetworks()
                .Where(b => b.Interface.Id == i.Id)
                // NetworkIdentifier.ToString() garbles non-ASCII SSIDs; decode the bytes ourselves.
                .Select(b => new Bss(Encoding.UTF8.GetString(b.Ssid.ToBytes()), Bssid.Normalize(b.Bssid.ToString()), b.SignalStrength))
                .Where(b => b.Ssid.Length > 0)
                .ToList();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Log.Write($"scan failed: {ex.Message}");
            return Array.Empty<Bss>();
        }
    }

    public IReadOnlySet<string> OpenSsids()
    {
        var i = Interface();
        if (i is null) return new HashSet<string>(StringComparer.Ordinal);
        try
        {
            return NativeWifi.EnumerateAvailableNetworks()
                .Where(n => n.Interface.Id == i.Id && !n.IsSecurityEnabled)
                .Select(n => Encoding.UTF8.GetString(n.Ssid.ToBytes()))
                .Where(s => s.Length > 0)
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Log.Write($"available networks failed: {ex.Message}");
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    public void Disconnect()
    {
        if (Interface() is not { } i) return;
        try { NativeWifi.DisconnectNetwork(i.Id); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { Log.Write($"disconnect failed: {ex.Message}"); }
    }

    public async Task<bool> TryJoinAsync(Bss target, string password, CancellationToken ct = default)
    {
        var i = Interface();
        if (i is null) return false;
        try
        {
            var profile = password.Length == 0 ? ProfileXml.BuildOpen(target.Ssid) : ProfileXml.Build(target.Ssid, password);
            if (!NativeWifi.SetProfile(i.Id, ProfileType.AllUser, profile, null, true))
            {
                Log.Write($"SetProfile refused for {target.Ssid}");
                return false;
            }
            var bssid = PhysicalAddress.Parse(target.Bssid.Replace(":", "-").ToUpperInvariant());
            var started = DateTime.UtcNow;
            bool ok = await NativeWifi.ConnectNetworkAsync(i.Id, target.Ssid, BssType.Infrastructure, bssid, JoinTimeout, ct);
            Log.Write($"join {target.Bssid} {(ok ? "ok" : "failed")} in {(DateTime.UtcNow - started).TotalSeconds:0.0}s");
            return ok;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
        {
            Log.Write($"join error: {ex.Message}");
            return false;
        }
    }
}
