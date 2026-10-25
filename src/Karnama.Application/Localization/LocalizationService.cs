using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Karnama.Application.Abstractions;
using Karnama.Domain.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Karnama.Application.Localization;

public sealed record LanguageDto(string Code, string Name, TextDirection Direction, CalendarKind Calendar, DigitStyle Digits, bool IsDefault);

/// <summary>Overrides for UI texts. The frontend ships Persian defaults; this returns admin edits merged over the default language.</summary>
public sealed record TranslationBundle(string Language, IReadOnlyDictionary<string, string> Texts, string ETag);

public sealed class LocalizationService(IAppDbContext db, HybridCache cache)
{
    public const string CacheTag = "i18n";
    private static readonly HybridCacheEntryOptions CacheOptions = new() { Expiration = TimeSpan.FromMinutes(30), LocalCacheExpiration = TimeSpan.FromMinutes(5) };

    public Task<IReadOnlyList<LanguageDto>> GetLanguagesAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync<IReadOnlyList<LanguageDto>>("i18n:languages", async token =>
            await db.Languages.AsNoTracking().Where(l => l.IsActive)
                .OrderBy(l => l.SortOrder).ThenBy(l => l.Code)
                .Select(l => new LanguageDto(l.Code, l.Name, l.Direction, l.Calendar, l.Digits, l.IsDefault))
                .ToListAsync(token),
            CacheOptions, [CacheTag], ct).AsTask();

    public async Task<TranslationBundle?> GetBundleAsync(string code, CancellationToken ct)
    {
        code = code.Trim().ToLowerInvariant();
        var languages = await GetLanguagesAsync(ct);
        if (languages.All(l => l.Code != code)) return null;
        var defaultCode = languages.FirstOrDefault(l => l.IsDefault)?.Code ?? "fa";

        return await cache.GetOrCreateAsync($"i18n:bundle:{code}", async token =>
        {
            var rows = await db.Translations.AsNoTracking()
                .Where(t => t.LanguageCode == code || t.LanguageCode == defaultCode)
                .Select(t => new { t.LanguageCode, t.Key, t.Value })
                .ToListAsync(token);

            var texts = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in rows.Where(r => r.LanguageCode == defaultCode)) texts[r.Key] = r.Value;
            foreach (var r in rows.Where(r => r.LanguageCode == code)) texts[r.Key] = r.Value;

            var json = JsonSerializer.Serialize(texts);
            var etag = "\"" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code + json)))[..16] + "\"";
            return new TranslationBundle(code, texts, etag);
        }, CacheOptions, [CacheTag], ct);
    }
}
