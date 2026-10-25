using Karnama.Domain.Identity;

namespace Karnama.Application.Abstractions;

public interface ISmsSender
{
    /// <summary>Sends an OTP. Implementations must never log the code in Production.</summary>
    Task SendOtpAsync(string phone, string code, CancellationToken ct);
}

/// <summary>Queues an OTP SMS so the HTTP response never waits on (or reveals) the SMS provider.</summary>
public interface IOtpDispatcher
{
    void Enqueue(string phone, string code);
}

public interface IOtpCodeService
{
    string GenerateCode();
    string Hash(Guid challengeId, string phone, string code);
    bool Matches(string expectedHash, string actualHash);
}

public sealed record IssuedTokens(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

public sealed record TokenSubject(Guid UserId, UserKind Kind, bool IsDemo, string LanguageCode, IReadOnlyDictionary<string, PermissionScope> Permissions);

public interface ITokenService
{
    string CreateAccessToken(TokenSubject subject, Guid familyId, out DateTimeOffset expiresAt);
    string CreateRefreshTokenValue();
    string HashRefreshToken(string value);
    TimeSpan RefreshTokenLifetime { get; }

    /// <summary>Short-lived token proving the first factor passed; used for the TOTP step.</summary>
    string CreateTwoFactorToken(Guid userId, ClientKind client);
    (Guid UserId, ClientKind Client)? ReadTwoFactorToken(string token);
}

public interface ITotpService
{
    string GenerateSecret();
    string BuildUri(string secret, string accountName, string issuer);
    /// <summary>Returns the matched time step, or null.</summary>
    long? Verify(string secret, string code, DateTimeOffset now);
}

/// <summary>Encrypts sensitive fields at rest (Data Protection API).</summary>
public interface ISecretProtector
{
    string Protect(string plain);
    string Unprotect(string protectedValue);
}

public interface IAuditWriter
{
    /// <summary>Adds an audit row to the current unit of work (saved with the next SaveChanges).</summary>
    void Add(string action, string entityType, string? entityId, object? data = null, Guid? actorOverride = null, bool? isDemoOverride = null);
}

public interface IRealtimeNotifier
{
    Task PermissionsChangedAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct);
}
