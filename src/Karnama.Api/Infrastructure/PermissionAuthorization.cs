using Karnama.Application.Abstractions;
using Karnama.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Karnama.Api.Infrastructure;

/// <summary>Requires a permission with at least the given scope. Data-level scope filtering happens in the query.</summary>
internal sealed record PermissionRequirement(string Permission, PermissionScope MinimumScope) : IAuthorizationRequirement;

internal sealed class PermissionHandler(ICurrentUser me) : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (me.Has(requirement.Permission, requirement.MinimumScope)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>Builds policies named <c>perm:{code}:{scope}</c> on demand.</summary>
internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "perm:";

    public static string Name(string permission, PermissionScope scope = PermissionScope.Own) => $"{Prefix}{permission}:{(int)scope}";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal)) return await base.GetPolicyAsync(policyName);
        var rest = policyName[Prefix.Length..];
        var idx = rest.LastIndexOf(':');
        if (idx <= 0 || !int.TryParse(rest[(idx + 1)..], out var scope)) return null;
        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(rest[..idx], (PermissionScope)scope))
            .Build();
    }
}

internal static class PermissionEndpointExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission, PermissionScope scope = PermissionScope.Own)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(PermissionPolicyProvider.Name(permission, scope));
}
