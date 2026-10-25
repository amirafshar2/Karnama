using Karnama.Domain.Identity;

namespace Karnama.Application.Abstractions;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    bool IsDemo { get; }
    UserKind? Kind { get; }
    string LanguageCode { get; }
    string? TraceId { get; }

    /// <summary>Highest scope the user holds for <paramref name="permission"/>, or null.</summary>
    PermissionScope? ScopeOf(string permission);

    bool Has(string permission, PermissionScope minimum = PermissionScope.Own) =>
        ScopeOf(permission) is { } s && s >= minimum;
}
