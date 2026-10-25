using Karnama.Domain.Common;

namespace Karnama.Domain.Identity;

public static class SystemRoles
{
    public const string Owner = "owner";
    public const string Client = "client";
}

/// <summary>A role is data: a name plus a set of (permission, scope) grants.</summary>
public sealed class Role : AuditableEntity
{
    public const int MaxDescriptionLength = 500;
    private readonly List<RolePermission> _permissions = [];

    private Role() { Name = new LocalizedText(); }

    public LocalizedText Name { get; private set; }
    public string? Description { get; private set; }

    /// <summary>System roles (owner, client) cannot be deleted, renamed away or have their grants edited.</summary>
    public string? SystemKey { get; private set; }
    public bool IsSystem => SystemKey is not null;

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public IReadOnlyCollection<RolePermission> Permissions => _permissions;

    public static Result<Role> Create(LocalizedText name, string? description, IEnumerable<PermissionGrant> grants)
    {
        var role = new Role();
        var r = role.Update(name, description, grants);
        return r.IsSuccess ? role : r.Error!;
    }

    public static Role CreateSystem(string systemKey, LocalizedText name, IEnumerable<PermissionGrant> grants)
    {
        var role = new Role { SystemKey = systemKey, Name = name };
        role.ReplaceGrants(grants);
        return role;
    }

    public Result Update(LocalizedText name, string? description, IEnumerable<PermissionGrant> grants)
    {
        if (name.IsEmpty)
            return Error.Validation("roles.nameRequired", "نام نقش الزامی است.");
        if (name.Values.Values.Any(v => v.Length > LocalizedText.MaxLength))
            return Error.Validation("roles.nameTooLong", "نام نقش خیلی طولانی است.");
        if (description is { Length: > MaxDescriptionLength })
            return Error.Validation("roles.descriptionTooLong", "توضیح نقش خیلی طولانی است.");

        var list = grants.ToList();
        if (IsSystem)
        {
            // Grants of system roles are fixed in code; only the display name may change.
            Name = name;
            Description = description?.Trim();
            return Result.Success();
        }

        foreach (var g in list)
        {
            var def = Identity.Permissions.Find(g.Code);
            if (def is null)
                return Error.Validation("roles.unknownPermission", $"مجوز «{g.Code}» وجود ندارد.");
            if (!def.AllowedScopes.Contains(g.Scope))
                return Error.Validation("roles.invalidScope", $"دامنه‌ی انتخاب‌شده برای «{g.Code}» مجاز نیست.");
        }
        if (list.GroupBy(g => g.Code).Any(g => g.Count() > 1))
            return Error.Validation("roles.duplicatePermission", "یک مجوز دو بار انتخاب شده است.");

        Name = name;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ReplaceGrants(list);
        return Result.Success();
    }

    public Result Delete(Guid? actorId, DateTimeOffset now)
    {
        if (IsSystem) return Error.Conflict("roles.systemCannotDelete", "نقش‌های سیستمی حذف‌شدنی نیستند.");
        if (IsDeleted) return Result.Success();
        MarkDeleted(actorId, now);
        return Result.Success();
    }

    public void Restore() => MarkRestored();

    /// <summary>Keeps a system role's grants in line with the code catalog (used by the seeder only).</summary>
    public void SyncSystemGrants(IEnumerable<PermissionGrant> grants)
    {
        if (!IsSystem) throw new InvalidOperationException("Only system roles are synced from code.");
        ReplaceGrants(grants);
        if (IsDeleted) MarkRestored();
    }

    // Diff instead of clear+add so EF Core tracks updates instead of delete/insert of the same key.
    private void ReplaceGrants(IEnumerable<PermissionGrant> grants)
    {
        var wanted = grants.ToDictionary(g => g.Code, g => g.Scope, StringComparer.Ordinal);
        _permissions.RemoveAll(p => !wanted.ContainsKey(p.Code));
        foreach (var (code, scope) in wanted)
        {
            var existing = _permissions.Find(p => p.Code == code);
            if (existing is null) _permissions.Add(new RolePermission(Id, code, scope));
            else existing.Scope = scope;
        }
    }
}

public readonly record struct PermissionGrant(string Code, PermissionScope Scope);

public sealed class RolePermission
{
    private RolePermission() { Code = string.Empty; }

    internal RolePermission(Guid roleId, string code, PermissionScope scope)
    {
        RoleId = roleId;
        Code = code;
        Scope = scope;
    }

    public Guid RoleId { get; private set; }
    public string Code { get; private set; }
    public PermissionScope Scope { get; internal set; }
}
