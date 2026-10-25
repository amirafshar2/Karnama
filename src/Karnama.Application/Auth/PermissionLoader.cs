using Karnama.Application.Abstractions;
using Karnama.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Auth;

public static class PermissionLoader
{
    /// <summary>Union of all grants of the user's (non-deleted) roles, keeping the widest scope per permission.</summary>
    public static async Task<Dictionary<string, PermissionScope>> LoadAsync(IAppDbContext db, Guid userId, CancellationToken ct)
    {
        var grants = await (
            from ur in db.UserRoles.AsNoTracking()
            join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            from p in r.Permissions
            where ur.UserId == userId
            select new { p.Code, p.Scope }).ToListAsync(ct);

        var result = new Dictionary<string, PermissionScope>(StringComparer.Ordinal);
        foreach (var g in grants)
        {
            if (!result.TryGetValue(g.Code, out var existing) || g.Scope > existing) result[g.Code] = g.Scope;
        }
        return result;
    }

    public static Task<bool> IsOwnerAsync(IAppDbContext db, Guid userId, CancellationToken ct) =>
        (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
         where ur.UserId == userId && r.SystemKey == SystemRoles.Owner select ur).AnyAsync(ct);
}
