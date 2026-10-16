using System.Text;
using System.Xml.Linq;

namespace CafeWiFi.Platform;

/// <summary>
/// Builds WLAN profiles for WPA2-Personal and open networks. A profile is named after
/// the SSID and overwritten for every password we try. connectionMode is manual so
/// Windows never retries a stale password on its own; this app decides when to join.
/// </summary>
public static class ProfileXml
{
    private static readonly XNamespace Ns = "http://www.microsoft.com/networking/WLAN/profile/v1";

    /// <summary>A profile for an open network (no password), e.g. Starbucks.</summary>
    public static string BuildOpen(string ssid) => Serialize(Profile(ssid,
        new XElement(Ns + "authentication", "open"),
        new XElement(Ns + "encryption", "none"),
        new XElement(Ns + "useOneX", "false")));

    private static XElement Profile(string ssid, params XElement[] auth) =>
        new(Ns + "WLANProfile",
            new XElement(Ns + "name", ssid),
            new XElement(Ns + "SSIDConfig",
                new XElement(Ns + "SSID",
                    new XElement(Ns + "hex", Convert.ToHexString(Encoding.UTF8.GetBytes(ssid))),
                    new XElement(Ns + "name", ssid))),
            new XElement(Ns + "connectionType", "ESS"),
            new XElement(Ns + "connectionMode", "manual"),
            new XElement(Ns + "MSM", new XElement(Ns + "security", new XElement(Ns + "authEncryption", auth))));

    private static string Serialize(XElement root) =>
        new XDeclaration("1.0", null, null) + Environment.NewLine + root;

    public static string Build(string ssid, string password)
    {
        // 64 hex digits is a raw PSK rather than a passphrase.
        bool rawKey = password.Length == 64 && password.All(Uri.IsHexDigit);
        var root = Profile(ssid,
            new XElement(Ns + "authentication", "WPA2PSK"),
            new XElement(Ns + "encryption", "AES"),
            new XElement(Ns + "useOneX", "false"));
        root.Descendants(Ns + "security").Single().Add(
            new XElement(Ns + "sharedKey",
                new XElement(Ns + "keyType", rawKey ? "networkKey" : "passPhrase"),
                new XElement(Ns + "protected", "false"),
                new XElement(Ns + "keyMaterial", password)));
        return Serialize(root);
    }
}
