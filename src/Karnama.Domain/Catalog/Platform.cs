using System.Text.RegularExpressions;
using Karnama.Domain.Common;

namespace Karnama.Domain.Catalog;

/// <summary>A social network / messenger the agency works on (Instagram, Telegram, Eitaa, Bale, Rubika, …). Dynamic list.</summary>
public sealed partial class Platform : AuditableEntity
{
    private Platform() { Key = string.Empty; Name = new LocalizedText(); }

    public string Key { get; private set; }
    public LocalizedText Name { get; private set; }
    /// <summary>Profile link with a {handle} placeholder, e.g. https://instagram.com/{handle}.</summary>
    public string? UrlTemplate { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    public uint Version { get; private set; }

    [GeneratedRegex("^[a-z][a-z0-9-]{1,29}$")]
    private static partial Regex KeyPattern();

    public static Result<Platform> Create(string key, LocalizedText name, string? urlTemplate, int sortOrder)
    {
        var p = new Platform();
        var r = p.Update(key, name, urlTemplate, sortOrder, true);
        return r.IsSuccess ? p : r.Error!;
    }

    public Result Update(string key, LocalizedText name, string? urlTemplate, int sortOrder, bool isActive)
    {
        key = (key ?? string.Empty).Trim().ToLowerInvariant();
        if (!KeyPattern().IsMatch(key)) return Error.Validation("platforms.invalidKey", "شناسه باید انگلیسی کوچک باشد (مثل instagram).");
        if (name.IsEmpty) return Error.Validation("platforms.nameRequired", "نام شبکه الزامی است.");
        if (urlTemplate is { Length: > 0 } && (!urlTemplate.StartsWith("https://", StringComparison.Ordinal) || !urlTemplate.Contains("{handle}", StringComparison.Ordinal) || urlTemplate.Length > 200))
            return Error.Validation("platforms.invalidUrl", "الگوی لینک باید با https:// شروع شود و {handle} داشته باشد.");
        Key = key;
        Name = name;
        UrlTemplate = string.IsNullOrWhiteSpace(urlTemplate) ? null : urlTemplate;
        SortOrder = sortOrder;
        IsActive = isActive;
        return Result.Success();
    }

    public void Delete(Guid? actor, DateTimeOffset now) => MarkDeleted(actor, now);
    public void Restore() => MarkRestored();
}
