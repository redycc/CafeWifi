using System.Text.RegularExpressions;

namespace CafeWiFi.Core;

/// <summary>
/// Likely Wi-Fi passwords derived from a phone number, best first. Many Taiwanese cafes set the
/// Wi-Fi password to the shop phone, but the exact form varies: usually without the leading 0,
/// in Taipei (02) often without the whole area code, sometimes the full number. This mirrors the
/// Louisa scraper's rule (scripts/brands/louisa.py) so independent cafes can reuse it.
/// </summary>
public static partial class PhonePasswords
{
    public static IReadOnlyList<string> Candidates(string? phone)
    {
        if (phone is null) return Array.Empty<string>();
        // First number only; drop extensions like #12, 分機, ext.
        var first = Extension().Split(phone, 2)[0];
        var d = Digits().Replace(first, "");
        if (d.Length == 0) return Array.Empty<string>();

        // A wrong password costs ~15 s, so keep this list short.
        var forms = d.StartsWith("02", StringComparison.Ordinal)
            ? new[] { d[2..], d, d[1..] }
            : new[] { d[1..], d, d.Length >= 8 ? d[^8..] : d };

        var seen = new HashSet<string>();
        return forms.Where(p => p.Length is >= 8 and <= 63 && seen.Add(p)).ToList();
    }

    [GeneratedRegex(@"[#轉,、/]|分機|ext", RegexOptions.IgnoreCase)]
    private static partial Regex Extension();

    [GeneratedRegex(@"\D")]
    private static partial Regex Digits();
}
