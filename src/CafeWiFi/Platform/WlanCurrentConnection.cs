using System.Runtime.InteropServices;
using System.Text;

namespace CafeWiFi.Platform;

/// <summary>
/// SSID and BSSID of the network an interface is associated with. ManagedNativeWifi
/// exposes the profile name but not the BSSID, so this asks wlanapi directly.
/// </summary>
public static class WlanCurrentConnection
{
    public static (string Ssid, string Bssid)? Query(Guid interfaceId)
    {
        if (WlanOpenHandle(2, IntPtr.Zero, out _, out var client) != 0) return null;
        try
        {
            var id = interfaceId;
            int rc = WlanQueryInterface(client, ref id, WlanIntfOpcode.CurrentConnection, IntPtr.Zero,
                out _, out var data, out _);
            if (rc != 0 || data == IntPtr.Zero) return null; // ERROR_INVALID_STATE when not connected
            try
            {
                var attrs = Marshal.PtrToStructure<WlanConnectionAttributes>(data);
                if (attrs.State != 1 /* wlan_interface_state_connected */) return null;
                var a = attrs.Association;
                var ssid = Encoding.UTF8.GetString(a.Ssid.Bytes, 0, (int)Math.Min(a.Ssid.Length, 32u));
                var bssid = string.Join(":", a.Bssid.Select(b => b.ToString("x2")));
                return (ssid, bssid);
            }
            finally { WlanFreeMemory(data); }
        }
        finally { WlanCloseHandle(client, IntPtr.Zero); }
    }

    private enum WlanIntfOpcode { CurrentConnection = 7 }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanConnectionAttributes
    {
        public int State;
        public int ConnectionMode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ProfileName;
        public WlanAssociationAttributes Association;
        // WLAN_SECURITY_ATTRIBUTES follows; not needed.
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dot11Ssid
    {
        public uint Length;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Bytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanAssociationAttributes
    {
        public Dot11Ssid Ssid;
        public int BssType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid;
        public int PhyType;
        public uint PhyIndex;
        public uint SignalQuality;
        public uint RxRate;
        public uint TxRate;
    }

    [DllImport("wlanapi.dll")]
    private static extern int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

    [DllImport("wlanapi.dll")]
    private static extern int WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern int WlanQueryInterface(IntPtr clientHandle, ref Guid interfaceGuid, WlanIntfOpcode opCode,
        IntPtr reserved, out int dataSize, out IntPtr data, out int opcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);
}
