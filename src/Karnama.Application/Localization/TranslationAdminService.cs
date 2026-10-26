using System.Text.RegularExpressions;
using FluentValidation;
using Karnama.Application.Abstractions;
using Karnama.Domain.Common;
using Karnama.Domain.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Karnama.Application.Localization;

public sealed record TranslationOverrideDto(string Key, string Value);
public sealed record SetTranslationRequest(string Value);

public sealed partial class SetTranslationValidator : AbstractValidator<SetTranslationRequest>
{
    public SetTranslationValidator()
    {
        RuleFor(x => x.Value).NotNull().MaximumLength(Translation.MaxValueLength).WithMessage("متن حداکثر ۲۰۰۰ کاراکتر است.");
    }
}

/// <summary>Admin edits of UI texts. The frontend owns the keys and the built-in defaults; this stores overrides.</summary>
public sealed partial class TranslationAdminService(IAppDbContext db, IAuditWriter audit, HybridCache cache)
{
    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9_.-]{0,149}$")]
    private static partial Regex KeyPattern();

    public async Task<Result<IReadOnlyList<TranslationOverrideDto>>> ListAsync(string lang, CancellationToken ct)
    {
        if (!await db.Languages.AnyAsync(l => l.Code == lang, ct)) return Error.NotFound("languages.notFound", "زبان پیدا نشد.");
        return await db.Translations.AsNoTracking().Where(t => t.LanguageCode == lang)
            .OrderBy(t => t.Key).Select(t => new TranslationOverrideDto(t.Key, t.Value)).ToListAsync(ct);
    }

    public async Task<Result> SetAsync(string lang, string key, string value, CancellationToken ct)
    {
        if (!KeyPattern().IsMatch(key)) return Error.Validation("translations.invalidKey", "کلید متن معتبر نیست.");
        if (!await db.Languages.AnyAsync(l => l.Code == lang, ct)) return Error.NotFound("languages.notFound", "زبان پیدا نشد.");

        var row = await db.Translations.FirstOrDefaultAsync(t => t.LanguageCode == lang && t.Key == key, ct);
        var before = row?.Value;
        if (row is null)
        {
            var created = Translation.Create(lang, key, value);
            if (created.IsFailure) return created.Error!;
            db.Translations.Add(created.Value);
        }
        else
        {
            var r = row.SetValue(value);
            if (r.IsFailure) return r;
        }
        audit.Add("translations.updated", nameof(Translation), $"{lang}:{key}", new { before, after = value });
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(LocalizationService.CacheTag, ct);
        return Result.Success();
    }

    public async Task<Result> ResetAsync(string lang, string key, CancellationToken ct)
    {
        var row = await db.Translations.FirstOrDefaultAsync(t => t.LanguageCode == lang && t.Key == key, ct);
        if (row is null) return Result.Success();
        db.Translations.Remove(row);
        audit.Add("translations.reset", nameof(Translation), $"{lang}:{key}", new { before = row.Value });
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(LocalizationService.CacheTag, ct);
        return Result.Success();
    }
}

public sealed record UpsertLanguageRequest(string Code, string Name, Karnama.Domain.Localization.TextDirection Direction,
    Karnama.Domain.Localization.CalendarKind Calendar, Karnama.Domain.Localization.DigitStyle Digits, bool IsActive, int SortOrder);
public sealed record LanguageAdminDto(string Code, string Name, Karnama.Domain.Localization.TextDirection Direction, Karnama.Domain.Localization.CalendarKind Calendar,
    Karnama.Domain.Localization.DigitStyle Digits, bool IsDefault, bool IsActive, int SortOrder);

/// <summary>Languages of the interface (PROMPT R13): add one from the panel, change its direction/calendar/digits, switch it on or off.</summary>
public sealed class LanguageAdminService(IAppDbContext db, IAuditWriter audit, HybridCache cache)
{
    public async Task<IReadOnlyList<LanguageAdminDto>> ListAsync(CancellationToken ct) =>
        await db.Languages.AsNoTracking().OrderBy(l => l.SortOrder).ThenBy(l => l.Code)
            .Select(l => new LanguageAdminDto(l.Code, l.Name, l.Direction, l.Calendar, l.Digits, l.IsDefault, l.IsActive, l.SortOrder)).ToListAsync(ct);

    public async Task<Result<LanguageAdminDto>> CreateAsync(UpsertLanguageRequest r, CancellationToken ct)
    {
        var code = (r.Code ?? string.Empty).Trim().ToLowerInvariant();
        if (await db.Languages.AnyAsync(l => l.Code == code, ct)) return Error.Conflict("languages.exists", "این زبان از قبل هست.");
        var created = Karnama.Domain.Localization.Language.Create(code, r.Name, r.Direction, r.Calendar, r.Digits, false, r.SortOrder);
        if (created.IsFailure) return created.Error!;
        var lang = created.Value!;
        if (!r.IsActive) lang.Update(lang.Name, lang.Direction, lang.Calendar, lang.Digits, false, lang.SortOrder);
        db.Languages.Add(lang);
        audit.Add("languages.created", "Language", code, new { r.Direction });
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(LocalizationService.CacheTag, ct);
        return Dto(lang);
    }

    public async Task<Result<LanguageAdminDto>> UpdateAsync(string code, UpsertLanguageRequest r, CancellationToken ct)
    {
        var lang = await db.Languages.FirstOrDefaultAsync(l => l.Code == code, ct);
        if (lang is null) return Error.NotFound("languages.notFound", "زبان پیدا نشد.");
        var updated = lang.Update(r.Name, r.Direction, r.Calendar, r.Digits, r.IsActive, r.SortOrder);
        if (updated.IsFailure) return updated.Error!;
        audit.Add("languages.updated", "Language", code, new { r.Direction, r.IsActive });
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(LocalizationService.CacheTag, ct);
        return Dto(lang);
    }

    private static LanguageAdminDto Dto(Karnama.Domain.Localization.Language l) => new(l.Code, l.Name, l.Direction, l.Calendar, l.Digits, l.IsDefault, l.IsActive, l.SortOrder);
}
