using System.Text;
using System.Xml.Linq;

namespace CafeWiFi.Platform;

/// <summary>
/// Builds a WLAN profile for a WPA2-Personal network. The profile is named after
/// the SSID and overwritten for every password we try. connectionMode is manual so
/// Windows never retries a stale password on its own; this app decides when to join.
/// </summary>
public static class ProfileXml
{
    private static readonly XNamespace Ns = "http://www.microsoft.com/networking/WLAN/profile/v1";

    public static string Build(string ssid, string password)
    {
        // 64 hex digits is a raw PSK rather than a passphrase.
        bool rawKey = password.Length == 64 && password.All(Uri.IsHexDigit);
        var doc = new XDocument(new XDeclaration("1.0", null, null),
            new XElement(Ns + "WLANProfile",
                new XElement(Ns + "name", ssid),
                new XElement(Ns + "SSIDConfig",
                    new XElement(Ns + "SSID",
                        new XElement(Ns + "hex", Convert.ToHexString(Encoding.UTF8.GetBytes(ssid))),
                        new XElement(Ns + "name", ssid))),
                new XElement(Ns + "connectionType", "ESS"),
                new XElement(Ns + "connectionMode", "manual"),
                new XElement(Ns + "MSM",
                    new XElement(Ns + "security",
                        new XElement(Ns + "authEncryption",
                            new XElement(Ns + "authentication", "WPA2PSK"),
                            new XElement(Ns + "encryption", "AES"),
                            new XElement(Ns + "useOneX", "false")),
                        new XElement(Ns + "sharedKey",
                            new XElement(Ns + "keyType", rawKey ? "networkKey" : "passPhrase"),
                            new XElement(Ns + "protected", "false"),
                            new XElement(Ns + "keyMaterial", password))))));
        return doc.Declaration + Environment.NewLine + doc.Root;
    }
}
