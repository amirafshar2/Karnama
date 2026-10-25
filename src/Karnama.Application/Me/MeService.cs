using Karnama.Application.Abstractions;
using Karnama.Application.Auth;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Me;

public sealed record MeDto(
    Guid Id, string DisplayName, string PhoneMasked, UserKind Kind, bool IsDemo, string LanguageCode,
    bool TwoFactorEnabled, IReadOnlyList<string> Roles, IReadOnlyDictionary<string, PermissionScope> Permissions);

public sealed record UpdateMeRequest(string? DisplayName, string? LanguageCode);

public sealed class MeService(IAppDbContext db, ICurrentUser me)
{
    public async Task<Result<MeDto>> GetAsync(CancellationToken ct)
    {
        if (me.UserId is not { } id) return Error.Unauthorized("auth.required", "اول وارد شوید.");
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && u.IsActive, ct);
        if (user is null) return Error.Unauthorized("auth.required", "اول وارد شوید.");

        var roleNames = await (from ur in db.UserRoles.AsNoTracking()
                               join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                               where ur.UserId == id
                               select r.Name).ToListAsync(ct);
        var permissions = await PermissionLoader.LoadAsync(db, id, ct);

        return new MeDto(user.Id, user.DisplayName, PhoneNumber.Mask(user.Phone), user.Kind, user.IsDemo, user.LanguageCode,
            user.TwoFactorEnabled, roleNames.Select(n => n.Get(user.LanguageCode)).ToList(), permissions);
    }

    public async Task<Result<MeDto>> UpdateAsync(UpdateMeRequest request, CancellationToken ct)
    {
        if (me.UserId is not { } id) return Error.Unauthorized("auth.required", "اول وارد شوید.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.IsActive, ct);
        if (user is null) return Error.Unauthorized("auth.required", "اول وارد شوید.");

        if (request.LanguageCode is { } lang)
        {
            var exists = await db.Languages.AnyAsync(l => l.Code == lang && l.IsActive, ct);
            if (!exists) return Error.Validation("users.invalidLanguage", "این زبان فعال نیست.");
            var r = user.ChangeLanguage(lang);
            if (r.IsFailure) return r.Error!;
        }
        if (request.DisplayName is { } name && !user.IsDemo)
        {
            var r = user.Rename(name);
            if (r.IsFailure) return r.Error!;
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }
}
