using Karnama.Domain.Common;

namespace Karnama.Domain.Clients;

public enum CredentialSource { Staff = 1, Client = 2 }

/// <summary>
/// Login details of one client page (PROMPT2 §10.2, the credential vault). The secret part (password, recovery
/// email/phone, 2FA note) is stored only as envelope-encrypted bytes: a random per-record data key (AES-256-GCM)
/// wrapped by the master key, which lives outside the database. Nothing here is ever plain text.
/// Deleting is a hard delete: a removed password must really be gone.
/// </summary>
public sealed class ClientCredential : Entity
{
    private ClientCredential() { Handle = string.Empty; }

    public Guid ClientId { get; private set; }
    public Guid PlatformId { get; private set; }
    public string Handle { get; private set; }
    /// <summary>The agency was added as a collaborator in Meta Business Suite: no password needed (the safer way).</summary>
    public bool BusinessSuiteAccess { get; private set; }

    public byte[]? Cipher { get; private set; }
    public byte[]? WrappedKey { get; private set; }
    public int KeyVersion { get; private set; }
    public bool HasSecret => Cipher is not null;

    public CredentialSource Source { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset? LastRevealedAt { get; private set; }
    public Guid? LastRevealedBy { get; private set; }
    public uint Version { get; private set; }

    public static Result<ClientCredential> Create(Guid clientId, Guid platformId, string handle, bool businessSuite, CredentialSource source, Guid? actor, DateTimeOffset now)
    {
        var c = new ClientCredential { ClientId = clientId, PlatformId = platformId };
        var r = c.Update(handle, businessSuite, source, actor, now);
        return r.IsSuccess ? c : r.Error!;
    }

    public Result Update(string handle, bool businessSuite, CredentialSource source, Guid? actor, DateTimeOffset now)
    {
        var h = (handle ?? "").Trim().TrimStart('@');
        if (h.Length is 0 or > 100) return Error.Validation("credentials.handle", "نام کاربری الزامی است (حداکثر ۱۰۰ کاراکتر).");
        Handle = h;
        BusinessSuiteAccess = businessSuite;
        Touch(source, actor, now);
        return Result.Success();
    }

    public void SetSecret(byte[] cipher, byte[] wrappedKey, int keyVersion, CredentialSource source, Guid? actor, DateTimeOffset now)
    {
        Cipher = cipher;
        WrappedKey = wrappedKey;
        KeyVersion = keyVersion;
        Touch(source, actor, now);
    }

    public void ClearSecret(CredentialSource source, Guid? actor, DateTimeOffset now)
    {
        Cipher = null;
        WrappedKey = null;
        KeyVersion = 0;
        Touch(source, actor, now);
    }

    public void MarkRevealed(Guid actor, DateTimeOffset now)
    {
        LastRevealedAt = now;
        LastRevealedBy = actor;
    }

    private void Touch(CredentialSource source, Guid? actor, DateTimeOffset now)
    {
        Source = source;
        UpdatedBy = actor;
        UpdatedAt = now;
    }
}
