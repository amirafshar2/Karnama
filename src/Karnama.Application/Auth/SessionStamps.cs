using Karnama.Application.Abstractions;
using Karnama.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Karnama.Application.Auth;

/// <summary>What an access token is checked against on every request: the account is usable and its stamp.</summary>
public sealed record SessionState(bool Active, int Stamp);

/// <summary>
/// Makes removed access take effect at once instead of when the 15-minute access token expires.
/// Tokens carry <c>User.SecurityStamp</c>; each request compares it with the current value, cached for at most
/// 30 seconds (other API nodes) and evicted here right after a change (this node and the shared L2 cache).
/// </summary>
public sealed class SessionStamps(IAppDbContext db, DbScope scope, HybridCache cache)
{
    public const string ClaimType = "sst";
    private static readonly HybridCacheEntryOptions Options = new() { Expiration = TimeSpan.FromSeconds(30), LocalCacheExpiration = TimeSpan.FromSeconds(30) };

    private static string Key(Guid userId) => $"session-stamp:{userId:N}";

    /// <summary>Null when the user doesn't exist (any more).</summary>
    public async Task<SessionState?> GetAsync(Guid userId, CancellationToken ct)
    {
        // The factory reads through its own scope: parallel requests of one user share this lookup (see DbScope).
        var state = await cache.GetOrCreateAsync(Key(userId), async token =>
            await scope.ReadAsync((db, t) => db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => u.Id == userId)
                .Select(u => new SessionState(u.IsActive && !u.IsDeleted, u.SecurityStamp))
                .FirstOrDefaultAsync(t), token) ?? new SessionState(false, -1), Options, cancellationToken: ct);
        return state.Stamp < 0 ? null : state;
    }

    /// <summary>Call after the change is saved.</summary>
    public async Task ForgetAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        foreach (var id in userIds.Distinct()) await cache.RemoveAsync(Key(id), ct);
    }

    /// <summary>A role's grants changed (or it was deleted/restored): every member must take a fresh token.</summary>
    public async Task BumpRoleMembersAsync(Guid roleId, CancellationToken ct)
    {
        var members = await db.UserRoles.AsNoTracking().Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId).ToListAsync(ct);
        if (members.Count == 0) return;
        await db.Users.IgnoreQueryFilters().Where(u => members.Contains(u.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.SecurityStamp, u => u.SecurityStamp + 1), ct);
        await ForgetAsync(members, ct);
    }
}
