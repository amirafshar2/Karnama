using System.Text;

namespace Karnama.Domain.Common;

/// <summary>Iranian mobile numbers normalised to <c>09xxxxxxxxx</c> (accepts +98, 0098, 98 and Persian/Arabic digits).</summary>
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
            else if (ch is >= '۰' and <= '۹') sb.Append((char)('0' + (ch - '۰')));
            else if (ch is >= '٠' and <= '٩') sb.Append((char)('0' + (ch - '٠')));
            else if (ch is ' ' or '-' or '(' or ')' or '+' ) continue;
            else return false;
        }

        var digits = sb.ToString();
        if (digits.StartsWith("0098", StringComparison.Ordinal)) digits = "0" + digits[4..];
        else if (digits.StartsWith("98", StringComparison.Ordinal) && digits.Length == 12) digits = "0" + digits[2..];
        else if (digits.StartsWith('9') && digits.Length == 10) digits = "0" + digits;

        if (digits.Length != 11 || !digits.StartsWith("09", StringComparison.Ordinal)) return false;
        normalized = digits;
        return true;
    }

    /// <summary>Masks a phone for logs and non-privileged views: 0912***4567.</summary>
    public static string Mask(string phone) =>
        phone.Length == 11 ? string.Concat(phone.AsSpan(0, 4), "***", phone.AsSpan(7)) : "***";
}
