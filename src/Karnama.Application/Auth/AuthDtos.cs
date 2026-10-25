using Karnama.Domain.Identity;

namespace Karnama.Application.Auth;

public sealed record RequestOtpRequest(string Phone);
public sealed record RequestOtpResponse(Guid ChallengeId, int ExpiresInSeconds, int ResendAfterSeconds);

public sealed record VerifyOtpRequest(Guid ChallengeId, string Code);

public sealed record TwoFactorVerifyRequest(string TwoFactorToken, string Code);
public sealed record TwoFactorSetupRequest(string TwoFactorToken);
public sealed record TwoFactorSetupResponse(string Secret, string OtpAuthUri);

public sealed record DemoLoginRequest(string Role);
public sealed record DemoAccountDto(string Key, string Name, string Description, string Icon);

public enum LoginStep
{
    Completed = 1,
    TwoFactorRequired = 2,
    TwoFactorSetupRequired = 3,
}

/// <summary>Result of a login step. Tokens are set only when <see cref="Step"/> is Completed.</summary>
public sealed record LoginResult(LoginStep Step, SessionTokens? Tokens, string? TwoFactorToken);

public sealed record SessionTokens(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt, ClientKind Client);
