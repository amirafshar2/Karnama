using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Karnama.Application.Abstractions;
using Karnama.Domain.Identity;
using Karnama.Infrastructure.Security;

namespace Karnama.Api.Infrastructure;

internal sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private Dictionary<string, PermissionScope>? _permissions;

    private System.Security.Claims.ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => IsAuthenticated && Guid.TryParse(Principal!.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;

    public bool IsDemo => Principal?.FindFirst(KarnamaClaims.Demo)?.Value == "1";

    public UserKind? Kind => int.TryParse(Principal?.FindFirst(KarnamaClaims.Kind)?.Value, out var k) ? (UserKind)k : null;

    public string LanguageCode
    {
        get
        {
            var fromToken = Principal?.FindFirst(KarnamaClaims.Language)?.Value;
            if (!string.IsNullOrEmpty(fromToken)) return fromToken;
            var header = accessor.HttpContext?.Request.Headers.AcceptLanguage.ToString();
            return string.IsNullOrEmpty(header) ? "fa" : header.Split(',')[0].Split('-')[0].Trim().ToLowerInvariant();
        }
    }

    public string? TraceId => System.Diagnostics.Activity.Current?.TraceId.ToString() ?? accessor.HttpContext?.TraceIdentifier;

    public PermissionScope? ScopeOf(string permission)
    {
        _permissions ??= Parse();
        return _permissions.TryGetValue(permission, out var s) ? s : null;
    }

    private Dictionary<string, PermissionScope> Parse()
    {
        var result = new Dictionary<string, PermissionScope>(StringComparer.Ordinal);
        if (Principal is null) return result;
        foreach (var claim in Principal.FindAll(KarnamaClaims.Permission))
        {
            var idx = claim.Value.LastIndexOf(':');
            if (idx <= 0) continue;
            if (int.TryParse(claim.Value.AsSpan(idx + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var scope))
                result[claim.Value[..idx]] = (PermissionScope)scope;
        }
        return result;
    }
}

internal sealed class HttpRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    /// <summary>Client IP after UseForwardedHeaders (only trusted proxies may set X-Forwarded-For).</summary>
    public System.Net.IPAddress? RemoteIp => accessor.HttpContext?.Connection.RemoteIpAddress;
}
