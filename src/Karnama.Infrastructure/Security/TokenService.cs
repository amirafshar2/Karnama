using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Karnama.Application.Abstractions;
using Karnama.Application.Common;
using Karnama.Domain.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Karnama.Infrastructure.Security;

public static class KarnamaClaims
{
    public const string Permission = "perm";      // "roles.manage:3"
    public const string Kind = "kind";
    public const string Demo = "demo";
    public const string Language = "lang";
    public const string Family = "fam";
    public const string SecurityStamp = Application.Auth.SessionStamps.ClaimType;
    public const string Purpose = "purpose";
    public const string Client = "client";
    public const string TwoFactorPurpose = "2fa";
    public const string TwoFactorAudience = "karnama-2fa";
}

internal sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    private readonly JwtOptions _o = options.Value;
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    private SigningCredentials Credentials => new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.SigningKey)), SecurityAlgorithms.HmacSha256);

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_o.RefreshTokenDays);

    public string CreateAccessToken(TokenSubject s, Guid familyId, out DateTimeOffset expiresAt)
    {
        var now = clock.GetUtcNow();
        expiresAt = now.AddMinutes(_o.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, s.UserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(KarnamaClaims.Kind, ((int)s.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(KarnamaClaims.Language, s.LanguageCode),
            new(KarnamaClaims.Family, familyId.ToString("N")),
            new(KarnamaClaims.SecurityStamp, s.SecurityStamp.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };
        if (s.IsDemo) claims.Add(new Claim(KarnamaClaims.Demo, "1"));
        claims.AddRange(s.Permissions.Select(p => new Claim(KarnamaClaims.Permission, $"{p.Key}:{(int)p.Value}")));

        var token = new JwtSecurityToken(_o.Issuer, _o.Audience, claims, now.UtcDateTime, expiresAt.UtcDateTime, Credentials);
        return _handler.WriteToken(token);
    }

    public string CreateRefreshTokenValue() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));

    public string HashRefreshToken(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public string CreateTwoFactorToken(Guid userId, ClientKind client)
    {
        var now = clock.GetUtcNow();
        var token = new JwtSecurityToken(_o.Issuer, KarnamaClaims.TwoFactorAudience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(KarnamaClaims.Purpose, KarnamaClaims.TwoFactorPurpose),
                new Claim(KarnamaClaims.Client, ((int)client).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ],
            now.UtcDateTime, now.AddMinutes(5).UtcDateTime, Credentials);
        return _handler.WriteToken(token);
    }

    public (Guid UserId, ClientKind Client)? ReadTwoFactorToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2000) return null;
        try
        {
            var principal = _handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidIssuer = _o.Issuer,
                ValidAudience = KarnamaClaims.TwoFactorAudience,
                IssuerSigningKey = Credentials.Key,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ClockSkew = TimeSpan.FromSeconds(10),
                LifetimeValidator = (nb, exp, _, _) => exp is { } e && e > clock.GetUtcNow().UtcDateTime,
            }, out _);
            if (principal.FindFirst(KarnamaClaims.Purpose)?.Value != KarnamaClaims.TwoFactorPurpose) return null;
            if (!Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)) return null;
            var client = int.TryParse(principal.FindFirst(KarnamaClaims.Client)?.Value, out var c) && Enum.IsDefined((ClientKind)c) ? (ClientKind)c : ClientKind.Web;
            return (id, client);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
