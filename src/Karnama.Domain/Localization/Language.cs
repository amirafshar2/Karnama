using Karnama.Domain.Common;

namespace Karnama.Domain.Localization;

public enum TextDirection { Rtl = 1, Ltr = 2 }
public enum CalendarKind { Persian = 1, Gregorian = 2, Hijri = 3 }
public enum DigitStyle { Persian = 1, Latin = 2 }

public sealed class Language
{
    private Language() { Code = string.Empty; Name = string.Empty; }

    public string Code { get; private set; }
    public string Name { get; private set; }
    public TextDirection Direction { get; private set; }
    public CalendarKind Calendar { get; private set; }
    public DigitStyle Digits { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }

    public static Result<Language> Create(string code, string name, TextDirection direction, CalendarKind calendar, DigitStyle digits, bool isDefault = false, int sortOrder = 0)
    {
        code = (code ?? string.Empty).Trim().ToLowerInvariant();
        if (code.Length is < 2 or > 10 || !code.All(c => char.IsAsciiLetterLower(c) || c == '-'))
            return Error.Validation("languages.invalidCode", "کد زبان معتبر نیست (مثل fa یا en).");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 50)
            return Error.Validation("languages.invalidName", "نام زبان معتبر نیست.");
        return new Language
        {
            Code = code, Name = name.Trim(), Direction = direction, Calendar = calendar, Digits = digits,
            IsDefault = isDefault, IsActive = true, SortOrder = sortOrder,
        };
    }

    /// <summary>Edits a language; the default language can't be switched off (the app would have nothing to fall back to).</summary>
    public Result Update(string name, TextDirection direction, CalendarKind calendar, DigitStyle digits, bool isActive, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 50) return Error.Validation("languages.invalidName", "نام زبان معتبر نیست.");
        if (IsDefault && !isActive) return Error.Conflict("languages.defaultOff", "زبان پیش‌فرض را نمی‌شود غیرفعال کرد.");
        Name = name.Trim(); Direction = direction; Calendar = calendar; Digits = digits; IsActive = isActive; SortOrder = sortOrder;
        return Result.Success();
    }
}

/// <summary>An admin-provided override for a UI text key in one language.</summary>
public sealed class Translation : Entity
{
    public const int MaxKeyLength = 150;
    public const int MaxValueLength = 2000;

    private Translation() { LanguageCode = string.Empty; Key = string.Empty; Value = string.Empty; }

    public string LanguageCode { get; private set; }
    public string Key { get; private set; }
    public string Value { get; private set; }

    public static Result<Translation> Create(string languageCode, string key, string value)
    {
        var t = new Translation { LanguageCode = languageCode, Key = key };
        var r = t.SetValue(value);
        if (r.IsFailure) return r.Error!;
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength)
            return Error.Validation("translations.invalidKey", "کلید ترجمه معتبر نیست.");
        return t;
    }

    public Result SetValue(string value)
    {
        if (value is null || value.Length > MaxValueLength)
            return Error.Validation("translations.invalidValue", "متن ترجمه خیلی طولانی است.");
        Value = value;
        return Result.Success();
    }
}
