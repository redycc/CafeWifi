// Manual Wi-Fi probe. Usage:
//   WifiSpike scan                       list interfaces, current link and every BSS
//   WifiSpike profile                    write and delete a throwaway all-user profile (no connecting)
//   WifiSpike join <ssid> <password>     write a profile and connect, timing the result (drops current Wi-Fi!)
using System.Diagnostics;
using System.Net.NetworkInformation;
using ManagedNativeWifi;

var cmd = args.FirstOrDefault() ?? "scan";
var iface = NativeWifi.EnumerateInterfaces().FirstOrDefault();
if (iface is null) { Console.WriteLine("no wifi interface"); return 1; }
Console.WriteLine($"interface: {iface.Description} {iface.Id} {iface.State}");

switch (cmd)
{
    case "scan":
    {
        var conn = NativeWifi.EnumerateInterfaceConnections().First(c => c.Id == iface.Id);
        Console.WriteLine($"radio on: {conn.IsRadioOn}, connected: {conn.IsConnected}, profile: {conn.ProfileName}");
        Console.WriteLine($"current: {CafeWiFi.Platform.WlanCurrentConnection.Query(iface.Id)}");
        var sw = Stopwatch.StartNew();
        var scanned = await NativeWifi.ScanNetworksAsync(TimeSpan.FromSeconds(4));
        Console.WriteLine($"scan: {scanned.Count()} interfaces in {sw.ElapsedMilliseconds} ms");
        foreach (var b in NativeWifi.EnumerateBssNetworks().OrderByDescending(b => b.SignalStrength))
            Console.WriteLine($"  {b.SignalStrength,4} dBm  {b.Bssid}  {b.Ssid}");
        break;
    }
    case "profile":
    {
        const string name = "CafeWiFi-Spike-Test";
        var xml = CafeWiFi.Platform.ProfileXml.Build(name, "12345678");
        var ok = NativeWifi.SetProfile(iface.Id, ProfileType.AllUser, xml, null, true);
        Console.WriteLine($"set all-user profile: {ok}");
        Console.WriteLine($"listed: {NativeWifi.EnumerateProfileNames().Contains(name)}");
        Console.WriteLine($"deleted: {NativeWifi.DeleteProfile(iface.Id, name)}");
        break;
    }
    case "join" when args.Length == 3:
    {
        var target = NativeWifi.EnumerateBssNetworks()
            .Where(b => b.Ssid.ToString() == args[1]).OrderByDescending(b => b.SignalStrength).FirstOrDefault();
        if (target is null) { Console.WriteLine("ssid not in range"); return 1; }
        NativeWifi.SetProfile(iface.Id, ProfileType.AllUser, CafeWiFi.Platform.ProfileXml.Build(args[1], args[2]), null, true);
        var sw = Stopwatch.StartNew();
        var bssid = new PhysicalAddress(target.Bssid.ToBytes());
        var ok = await NativeWifi.ConnectNetworkAsync(iface.Id, args[1], BssType.Infrastructure, bssid,
            TimeSpan.FromSeconds(20), CancellationToken.None);
        Console.WriteLine($"join {(ok ? "ok" : "failed")} in {sw.ElapsedMilliseconds} ms");
        break;
    }
    case "service":
    {
        var wifi = new CafeWiFi.Platform.NativeWifiService();
        Console.WriteLine($"HasInterface: {wifi.HasInterface}");
        Console.WriteLine($"CurrentLink: {wifi.CurrentLink()}");
        foreach (var b in (await wifi.ScanAsync()).OrderByDescending(b => b.Rssi)) Console.WriteLine($"  {b}");
        Console.WriteLine($"online: {await new CafeWiFi.Platform.Connectivity().IsOnlineAsync()}");
        break;
    }
    default:
        Console.WriteLine("usage: scan | profile | service | join <ssid> <password>");
        return 2;
}
return 0;
