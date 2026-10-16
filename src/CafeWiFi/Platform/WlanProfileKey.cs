using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace CafeWiFi.Platform;

/// <summary>
/// Reads a saved profile's password, like "netsh wlan show profile key=clear". Windows hands
/// the plain key to the signed-in user without elevation.
/// </summary>
public static class WlanProfileKey
{
    private const uint PlaintextKey = 4; // WLAN_PROFILE_GET_PLAINTEXT_KEY

    public static string? Read(Guid interfaceId, string profileName)
    {
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out var client) != 0) return null;
            try
            {
                var id = interfaceId;
                uint flags = PlaintextKey;
                if (WlanGetProfile(client, ref id, profileName, IntPtr.Zero, out var xml, ref flags, out _) != 0 || xml == IntPtr.Zero)
                    return null;
                try
                {
                    var doc = XDocument.Parse(Marshal.PtrToStringUni(xml)!);
                    var key = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "sharedKey");
                    var isProtected = key?.Elements().FirstOrDefault(e => e.Name.LocalName == "protected")?.Value;
                    var material = key?.Elements().FirstOrDefault(e => e.Name.LocalName == "keyMaterial")?.Value;
                    // Still encrypted means Windows wouldn't hand it over.
                    return isProtected == "false" && !string.IsNullOrEmpty(material) ? material : null;
                }
                finally { WlanFreeMemory(xml); }
            }
            finally { WlanCloseHandle(client, IntPtr.Zero); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or System.Xml.XmlException)
        {
            Log.Write($"cannot read profile key: {ex.Message}");
            return null;
        }
    }

    [DllImport("wlanapi.dll")]
    private static extern int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

    [DllImport("wlanapi.dll")]
    private static extern int WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern int WlanGetProfile(IntPtr clientHandle, ref Guid interfaceGuid, string profileName, IntPtr reserved,
        out IntPtr profileXml, ref uint flags, out uint grantedAccess);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);
}
