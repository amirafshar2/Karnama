using System.Text;

namespace Karnama.Domain.Common;

/// <summary>German mobile numbers normalised to <c>01xxxxxxxxx</c> (accepts +49, 0049, 49, spaces and Persian/Arabic digits).</summary>
public static class PhoneNumber
{
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (ch is >= '0' and <= '9') sb.Append(ch);
            else if (ch is >= '\u06F0' and <= '\u06F9') sb.Append((char)('0' + (ch - '\u06F0')));
            else if (ch is >= '\u0660' and <= '\u0669') sb.Append((char)('0' + (ch - '\u0660')));
            else if (ch is ' ' or '-' or '(' or ')' or '+' or '/') continue;
            else return false;
        }

        var digits = sb.ToString();
        if (digits.StartsWith("0049", StringComparison.Ordinal)) digits = "0" + digits[4..];
        else if (digits.StartsWith("49", StringComparison.Ordinal) && digits.Length is 12 or 13) digits = "0" + digits[2..];
        else if (digits.StartsWith('1') && digits.Length is 10 or 11) digits = "0" + digits;

        // German mobile: 015x, 016x, 017x + 7 to 8 more digits (11 or 12 digits in total).
        if (digits.Length is not (11 or 12) || !digits.StartsWith("01", StringComparison.Ordinal) || digits[2] is not ('5' or '6' or '7')) return false;
        normalized = digits;
        return true;
    }

    /// <summary>Masks a phone for logs and non-privileged views: 0151***4567.</summary>
    public static string Mask(string phone) =>
        phone.Length >= 11 ? string.Concat(phone.AsSpan(0, 4), "***", phone.AsSpan(phone.Length - 4)) : "***";
}
