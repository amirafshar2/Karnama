using Karnama.Domain.Common;

namespace Karnama.Domain.Identity;

public enum ClientKind
{
    Web = 1,
    Android = 2,
}

/// <summary>
/// Rotating refresh token. Only a SHA-256 hash is stored. Every token belongs to a family (one login);
/// presenting an already-rotated token revokes the whole family (reuse detection).
/// </summary>
public sealed class RefreshToken : Entity
{
    private RefreshToken() { TokenHash = string.Empty; }

    public Guid UserId { get; private set; }
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; }
    public ClientKind Client { get; private set; }
    public bool IsDemo { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? ReplacedById { get; private set; }

    public static RefreshToken Issue(Guid id, Guid userId, Guid familyId, string tokenHash, ClientKind client, bool isDemo, DateTimeOffset now, TimeSpan lifetime) => new()
    {
        Id = id,
        UserId = userId,
        FamilyId = familyId,
        TokenHash = tokenHash,
        Client = client,
        IsDemo = isDemo,
        CreatedAt = now,
        ExpiresAt = now.Add(lifetime),
    };

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public void MarkRotated(Guid replacementId, DateTimeOffset now)
    {
        RevokedAt = now;
        ReplacedById = replacementId;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
