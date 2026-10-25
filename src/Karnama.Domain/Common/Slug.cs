using System.Globalization;
using System.Text;

namespace Karnama.Domain.Common;

/// <summary>
/// Readable Persian/English URL slugs: "مدیریت پیج نقره‌ای" → "مدیریت-پیج-نقره-ای".
/// Arabic ي/ك become Persian ی/ک, Persian/Arabic digits become ASCII, spaces/ZWNJ/punctuation become single dashes.
/// </summary>
public static class Slug
{
    public static string From(string? text, int maxLength = 80)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        var dash = false;
        foreach (var raw in text.Trim().Normalize(NormalizationForm.FormKC))
        {
            var ch = raw switch
            {
                'ي' or 'ى' => 'ی',
                'ك' => 'ک',
                >= '۰' and <= '۹' => (char)('0' + (raw - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (raw - '٠')),
                _ => char.ToLowerInvariant(raw),
            };
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            var keep = char.IsLetterOrDigit(ch) && cat != UnicodeCategory.Format;
            if (keep)
            {
                sb.Append(ch);
                dash = false;
            }
            else if (!dash && sb.Length > 0)
            {
                sb.Append('-');
                dash = true;
            }
            if (sb.Length >= maxLength) break;
        }
        return sb.ToString().Trim('-');
    }
}
