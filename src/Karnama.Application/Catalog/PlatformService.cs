using Karnama.Application.Abstractions;
using Karnama.Application.Common;
using Karnama.Domain.Catalog;
using Karnama.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Catalog;

public sealed record PlatformDto(Guid Id, string Key, string Name, IReadOnlyDictionary<string, string> NameValues, string? UrlTemplate, int SortOrder, bool IsActive, bool IsDeleted, uint Version);
public sealed record UpsertPlatformRequest(string Key, Dictionary<string, string> Name, string? UrlTemplate, int SortOrder, bool IsActive, uint? Version);

public sealed class PlatformService(IAppDbContext db, ICurrentUser me, IAuditWriter audit, TimeProvider clock)
{
    public async Task<IReadOnlyList<PlatformDto>> ListAsync(bool includeDeleted, CancellationToken ct)
    {
        var rows = await (includeDeleted ? db.Platforms.IgnoreQueryFilters() : db.Platforms).AsNoTracking()
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Key).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<Result<PlatformDto>> CreateAsync(UpsertPlatformRequest r, CancellationToken ct)
    {
        if (await db.Platforms.AnyAsync(p => p.Key == r.Key.Trim().ToLowerInvariant(), ct))
            return Error.Conflict("platforms.duplicate", "شبکه‌ای با این شناسه وجود دارد.");
        var created = Platform.Create(r.Key, new LocalizedText(r.Name), r.UrlTemplate, r.SortOrder);
        if (created.IsFailure) return created.Error!;
        db.Platforms.Add(created.Value);
        audit.Add("platforms.created", nameof(Platform), created.Value.Id.ToString(), r);
        await db.SaveChangesAsync(ct);
        return ToDto(created.Value);
    }

    public async Task<Result<PlatformDto>> UpdateAsync(Guid id, UpsertPlatformRequest r, CancellationToken ct)
    {
        var p = await db.Platforms.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Error.NotFound("platforms.notFound", "شبکه پیدا نشد.");
        if (r.Version is { } v) db.SetOriginalVersion(p, v);
        var key = r.Key.Trim().ToLowerInvariant();
        if (key != p.Key && await db.Platforms.AnyAsync(x => x.Key == key && x.Id != id, ct))
            return Error.Conflict("platforms.duplicate", "شبکه‌ای با این شناسه وجود دارد.");
        var u = p.Update(r.Key, new LocalizedText(r.Name), r.UrlTemplate, r.SortOrder, r.IsActive);
        if (u.IsFailure) return u.Error!;
        audit.Add("platforms.updated", nameof(Platform), id.ToString(), r);
        await db.SaveChangesAsync(ct);
        return ToDto(p);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Platforms.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Error.NotFound("platforms.notFound", "شبکه پیدا نشد.");
        p.Delete(me.UserId, clock.GetUtcNow());
        audit.Add("platforms.deleted", nameof(Platform), id.ToString());
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<PlatformDto>> RestoreAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Platforms.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Error.NotFound("platforms.notFound", "شبکه پیدا نشد.");
        if (await db.Platforms.AnyAsync(x => x.Key == p.Key, ct)) return Error.Conflict("platforms.duplicate", "شبکه‌ای با این شناسه وجود دارد.");
        p.Restore();
        audit.Add("platforms.restored", nameof(Platform), id.ToString());
        await db.SaveChangesAsync(ct);
        return ToDto(p);
    }

    private PlatformDto ToDto(Platform p) => new(p.Id, p.Key, p.Name.Get(me.LanguageCode), p.Name.Values, p.UrlTemplate, p.SortOrder, p.IsActive, p.IsDeleted, p.Version);
}
