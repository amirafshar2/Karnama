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
