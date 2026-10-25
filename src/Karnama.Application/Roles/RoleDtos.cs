using Karnama.Domain.Identity;

namespace Karnama.Application.Roles;

public sealed record GrantDto(string Code, PermissionScope Scope);

public sealed record RoleListItemDto(
    Guid Id, string Name, IReadOnlyDictionary<string, string> NameValues, string? Description,
    bool IsSystem, string? SystemKey, int PermissionCount, int UserCount, bool IsDeleted, uint Version);

public sealed record RoleDetailDto(
    Guid Id, IReadOnlyDictionary<string, string> NameValues, string? Description,
    bool IsSystem, string? SystemKey, IReadOnlyList<GrantDto> Permissions, bool IsDeleted, uint Version,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

public sealed record UpsertRoleRequest(Dictionary<string, string> Name, string? Description, List<GrantDto> Permissions, uint? Version);

public sealed record PermissionDefinitionDto(string Code, string Group, IReadOnlyList<PermissionScope> AllowedScopes);
