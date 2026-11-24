using System.Text.Json;
using FluentValidation;
using Karnama.Application.Abstractions;
using Karnama.Application.Auth;
using Karnama.Application.Common;
using Karnama.Application.Work;
using Karnama.Domain.Clients;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Karnama.Application.Clients;

/// <summary>The secret part of a page login. Never printed: records would otherwise dump it into any log line.</summary>
public sealed record SecretInput(string? Password, string? RecoveryEmail, string? RecoveryPhone, string? TwoFactorNote)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Password) && string.IsNullOrWhiteSpace(RecoveryEmail)
                           && string.IsNullOrWhiteSpace(RecoveryPhone) && string.IsNullOrWhiteSpace(TwoFactorNote);
    public override string ToString() => "SecretInput { *** }";
}

/// <summary>
/// On update each secret field works on its own: null keeps the stored value, "" removes it, text replaces it
/// (so adding a recovery email never drops the password). <paramref name="RemoveSecret"/> wipes the whole secret.
/// </summary>
public sealed record UpsertCredentialRequest(Guid PlatformId, string Handle, bool BusinessSuiteAccess, SecretInput? Secret, bool RemoveSecret = false);

public sealed record CredentialDto(Guid Id, Guid ClientId, Guid PlatformId, string PlatformName, string Handle, bool HasSecret, bool BusinessSuiteAccess,
    CredentialSource Source, DateTimeOffset UpdatedAt, DateTimeOffset? LastRevealedAt);

public sealed record RevealStartDto(Guid ChallengeId, int ExpiresInSeconds, string MaskedPhone, string? DemoCode);
public sealed record RevealRequest(Guid ChallengeId, string Code);

public sealed record RevealedSecretDto(string? Password, string? RecoveryEmail, string? RecoveryPhone, string? TwoFactorNote, int VisibleSeconds)
{
    public override string ToString() => "RevealedSecretDto { *** }";
}

public sealed class UpsertCredentialValidator : AbstractValidator<UpsertCredentialRequest>
{
    public UpsertCredentialValidator()
    {
        RuleFor(x => x.PlatformId).NotEmpty();
        RuleFor(x => x.Handle).NotEmpty().MaximumLength(100);
        When(x => x.Secret is not null, () =>
        {
            RuleFor(x => x.Secret!.Password).MaximumLength(200);
            RuleFor(x => x.Secret!.RecoveryEmail).MaximumLength(200);
            RuleFor(x => x.Secret!.RecoveryPhone).MaximumLength(30);
            RuleFor(x => x.Secret!.TwoFactorNote).MaximumLength(500);
        });
    }
}

/// <summary>
/// Credential vault (PROMPT2 §10.2). Staff with credentials.view see which pages have a stored login, never the secret;
/// credentials.reveal shows it only after a fresh SMS code, for 30 seconds, and every reveal is audited and reported to
/// the owner. Clients manage their own logins from the portal (write-only: they never read a password back either).
/// A client asking for another client's record gets 404.
/// </summary>
public sealed class CredentialService(
    IAppDbContext db, ICurrentUser me, WorkAccess access, IVault vault, IAuditWriter audit, TimeProvider clock,
    IOtpCodeService otp, IOtpDispatcher sms, IOptions<OtpOptions> otpOptions, IOptions<DemoLoginOptions> demo, Notify.Notifier notifier)
{
    public const int VisibleSeconds = 30;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static Error NotFound => Error.NotFound("credentials.notFound", "Konto nicht gefunden.");
    private static Error NotConfigured => Error.Conflict("credentials.vaultOff", "Der Passwort-Tresor ist auf dem Server nicht eingerichtet (Hauptschlüssel). Wenden Sie sich an den technischen Support.");

    // ---------- staff ----------

    public async Task<Result<IReadOnlyList<CredentialDto>>> ListAsync(Guid clientId, CancellationToken ct)
    {
        if (!me.Has(Permissions.CredentialsView, PermissionScope.All)) return Error.Forbidden();
        if (!await db.Clients.AnyAsync(c => c.Id == clientId, ct)) return Error.NotFound("clients.notFound", "Kunde nicht gefunden.");
        return Result.Success(await ListForAsync(clientId, ct));
    }

    public async Task<Result<CredentialDto>> CreateAsync(Guid clientId, UpsertCredentialRequest r, CancellationToken ct)
    {
        if (!me.Has(Permissions.CredentialsManage, PermissionScope.All)) return Error.Forbidden();
        if (!await db.Clients.AnyAsync(c => c.Id == clientId, ct)) return Error.NotFound("clients.notFound", "Kunde nicht gefunden.");
        return await CreateForAsync(clientId, r, CredentialSource.Staff, ct);
    }

    public async Task<Result<CredentialDto>> UpdateAsync(Guid id, UpsertCredentialRequest r, CancellationToken ct)
    {
        if (!me.Has(Permissions.CredentialsManage, PermissionScope.All)) return Error.Forbidden();
        var c = await db.ClientCredentials.FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? NotFound : await UpdateForAsync(c, r, CredentialSource.Staff, ct);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        if (!me.Has(Permissions.CredentialsManage, PermissionScope.All)) return Error.Forbidden();
        var c = await db.ClientCredentials.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound;
        db.ClientCredentials.Remove(c);
        audit.Add("credentials.deleted", nameof(ClientCredential), c.Id.ToString(), new { c.ClientId, c.Handle });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Step 1 of seeing a password: a one-time code to the staff member's own phone (not the client's).</summary>
    public async Task<Result<RevealStartDto>> RevealStartAsync(Guid id, CancellationToken ct)
    {
        if (!me.Has(Permissions.CredentialsReveal, PermissionScope.All) || me.UserId is not { } uid) return Error.Forbidden();
        var cred = await db.ClientCredentials.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (cred is null) return NotFound;
        if (!cred.HasSecret) return Error.Validation("credentials.noSecret", "Für dieses Konto ist kein Passwort hinterlegt.");

        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == uid, ct);
        var now = clock.GetUtcNow();
        var windowStart = now.AddMinutes(-10);
        var recent = await db.OtpChallenges.AsNoTracking()
            .Where(c => c.Phone == user.Phone && c.Purpose == OtpPurpose.Reveal && c.CreatedAt > windowStart)
            .Select(c => c.CreatedAt).ToListAsync(ct);
        var options = otpOptions.Value;
        // No resend timer here (several pages may be opened one after another), only a cap per 10 minutes.
        if (recent.Count >= options.MaxSendsPer10Minutes * 3)
            return Error.TooMany("auth.otpLimit", "Zu viele Code-Anfragen. Versuchen Sie es in 10 Minuten erneut.");

        var challengeId = Guid.CreateVersion7();
        var code = otp.GenerateCode();
        db.OtpChallenges.Add(OtpChallenge.Create(challengeId, user.Phone, otp.Hash(challengeId, user.Phone, code), now, OtpPurpose.Reveal));
        await db.SaveChangesAsync(ct);

        // Demo accounts have made-up phone numbers: the demo shows the code on screen instead of an SMS.
        var showCode = demo.Value.Enabled && user.IsDemo;
        if (!showCode) sms.Enqueue(user.Phone, code);
        return new RevealStartDto(challengeId, (int)OtpChallenge.Lifetime.TotalSeconds, PhoneNumber.Mask(user.Phone), showCode ? code : null);
    }

    /// <summary>Step 2: the right code opens the secret once; audited and reported to the owner.</summary>
    public async Task<Result<RevealedSecretDto>> RevealAsync(Guid id, RevealRequest r, CancellationToken ct)
    {
        if (!me.Has(Permissions.CredentialsReveal, PermissionScope.All) || me.UserId is not { } uid) return Error.Forbidden();
        var cred = await db.ClientCredentials.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (cred is null) return NotFound;
        var phone = await db.Users.AsNoTracking().Where(u => u.Id == uid).Select(u => u.Phone).FirstAsync(ct);

        var now = clock.GetUtcNow();
        var challenge = await db.OtpChallenges.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == r.ChallengeId && c.Purpose == OtpPurpose.Reveal && c.Phone == phone, ct);
        if (challenge is null) return OtpChallenge.WrongCode;
        var claimed = await db.OtpChallenges
            .Where(c => c.Id == challenge.Id && c.ConsumedAt == null && c.Attempts < OtpChallenge.MaxAttempts && c.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, c => c.Attempts + 1), ct);
        if (claimed == 0)
        {
            var fresh = await db.OtpChallenges.AsNoTracking().FirstAsync(c => c.Id == challenge.Id, ct);
            var reason = fresh.CheckUsable(now);
            return reason.IsFailure ? reason.Error! : OtpChallenge.WrongCode;
        }
        if (!otp.Matches(challenge.CodeHash, otp.Hash(challenge.Id, phone, Digits.ToLatin((r.Code ?? "").Trim()))))
        {
            audit.Add("credentials.revealFailed", nameof(ClientCredential), cred.Id.ToString(), new { cred.ClientId });
            await db.SaveChangesAsync(ct);
            return OtpChallenge.WrongCode;
        }
        var consumed = await db.OtpChallenges.Where(c => c.Id == challenge.Id && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), ct);
        if (consumed == 0) return Error.Validation("auth.otpUsed", "Dieser Code wurde bereits verwendet. Fordern Sie einen neuen Code an.");

        if (!cred.HasSecret) return Error.Validation("credentials.noSecret", "Für dieses Konto ist kein Passwort hinterlegt.");
        var secret = OpenSecret(cred);
        if (secret is null) return Error.Conflict("credentials.unreadable", "Dieses Passwort lässt sich mit dem aktuellen Serverschlüssel nicht entschlüsseln. Holen Sie sich vom Kunden ein neues Passwort.");

        cred.MarkRevealed(uid, now);
        audit.Add("credentials.revealed", nameof(ClientCredential), cred.Id.ToString(), new { cred.ClientId, cred.Handle });
        var who = await db.Users.AsNoTracking().Where(u => u.Id == uid).Select(u => u.DisplayName).FirstAsync(ct);
        var clientName = await db.Clients.AsNoTracking().Where(c => c.Id == cred.ClientId).Select(c => c.BusinessName).FirstAsync(ct);
        var owners = await OwnersAsync(ct);
        await notifier.NotifyAsync(owners.Where(o => o != uid), "credentials.revealed", $"{who} hat das Passwort von @{cred.Handle} ({clientName}) angesehen",
            null, "/clients", true, $"reveal:{cred.Id:N}:{now.ToUnixTimeSeconds()}:{uid:N}", ct);
        await db.SaveChangesAsync(ct);
        return new RevealedSecretDto(secret.Password, secret.RecoveryEmail, secret.RecoveryPhone, secret.TwoFactorNote, VisibleSeconds);
    }

    // ---------- client portal ----------

    public async Task<Result<IReadOnlyList<CredentialDto>>> PortalListAsync(CancellationToken ct) =>
        await PortalClientAsync(ct) is { } clientId ? Result.Success(await ListForAsync(clientId, ct)) : Error.Forbidden();

    public async Task<Result<CredentialDto>> PortalCreateAsync(UpsertCredentialRequest r, CancellationToken ct)
    {
        if (await PortalClientAsync(ct) is not { } clientId) return Error.Forbidden();
        if (await db.ClientCredentials.CountAsync(x => x.ClientId == clientId, ct) >= 20) return Error.Validation("credentials.tooMany", "Höchstens 20 Konten.");
        var result = await CreateForAsync(clientId, r, CredentialSource.Client, ct);
        if (result.IsSuccess) await TellManagersAsync(clientId, result.Value!.Handle, ct);
        return result;
    }

    public async Task<Result<CredentialDto>> PortalUpdateAsync(Guid id, UpsertCredentialRequest r, CancellationToken ct)
    {
        if (await PortalClientAsync(ct) is not { } clientId) return Error.Forbidden();
        var c = await db.ClientCredentials.FirstOrDefaultAsync(x => x.Id == id && x.ClientId == clientId, ct);
        if (c is null) return NotFound;
        var result = await UpdateForAsync(c, r, CredentialSource.Client, ct);
        if (result.IsSuccess) await TellManagersAsync(clientId, c.Handle, ct);
        return result;
    }

    public async Task<Result> PortalDeleteAsync(Guid id, CancellationToken ct)
    {
        if (await PortalClientAsync(ct) is not { } clientId) return Error.Forbidden();
        var c = await db.ClientCredentials.FirstOrDefaultAsync(x => x.Id == id && x.ClientId == clientId, ct);
        if (c is null) return NotFound;
        db.ClientCredentials.Remove(c);
        audit.Add("credentials.deleted", nameof(ClientCredential), c.Id.ToString(), new { c.ClientId, c.Handle, byClient = true });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---------- shared ----------

    private async Task<Guid?> PortalClientAsync(CancellationToken ct) =>
        await access.PortalClientIdAsync(ct) is { } id && id != Guid.Empty ? id : null;

    private async Task<IReadOnlyList<CredentialDto>> ListForAsync(Guid clientId, CancellationToken ct)
    {
        var rows = await db.ClientCredentials.AsNoTracking().Where(c => c.ClientId == clientId).OrderBy(c => c.Handle).ToListAsync(ct);
        var names = await PlatformNamesAsync(ct);
        return rows.Select(c => ToDto(c, names)).ToList();
    }

    private async Task<Result<CredentialDto>> CreateForAsync(Guid clientId, UpsertCredentialRequest r, CredentialSource source, CancellationToken ct)
    {
        if (await CheckPlatformAsync(r.PlatformId, ct) is { } bad) return bad;
        var handle = (r.Handle ?? "").Trim().TrimStart('@');
        if (await db.ClientCredentials.AnyAsync(x => x.ClientId == clientId && x.PlatformId == r.PlatformId && x.Handle == handle, ct))
            return Error.Conflict("credentials.duplicate", "Dieses Konto ist bereits erfasst; bearbeiten Sie das vorhandene.");
        if (r.Secret is { IsEmpty: false } && !vault.IsConfigured) return NotConfigured;

        var now = clock.GetUtcNow();
        var created = ClientCredential.Create(clientId, r.PlatformId, handle, r.BusinessSuiteAccess, source, me.UserId, now);
        if (created.IsFailure) return created.Error!;
        var c = created.Value!;
        if (r.Secret is { IsEmpty: false } s) Seal(c, s, source, now);
        db.ClientCredentials.Add(c);
        audit.Add("credentials.created", nameof(ClientCredential), c.Id.ToString(), new { clientId, c.Handle, withSecret = c.HasSecret, source = source.ToString() });
        await db.SaveChangesAsync(ct);
        return ToDto(c, await PlatformNamesAsync(ct));
    }

    private async Task<Result<CredentialDto>> UpdateForAsync(ClientCredential c, UpsertCredentialRequest r, CredentialSource source, CancellationToken ct)
    {
        if (await CheckPlatformAsync(r.PlatformId, ct) is { } bad) return bad;
        if (r.PlatformId != c.PlatformId) return Error.Validation("credentials.platformFixed", "Das Netzwerk eines Kontos lässt sich nicht ändern; legen Sie ein neues Konto an.");
        var touchesSecret = !r.RemoveSecret && r.Secret is { } input
                            && (input.Password ?? input.RecoveryEmail ?? input.RecoveryPhone ?? input.TwoFactorNote) is not null;
        if (touchesSecret && !vault.IsConfigured) return NotConfigured;
        var now = clock.GetUtcNow();
        var updated = c.Update(r.Handle, r.BusinessSuiteAccess, source, me.UserId, now);
        if (updated.IsFailure) return updated.Error!;
        string secretChange = "kept";
        if (r.RemoveSecret)
        {
            c.ClearSecret(source, me.UserId, now);
            secretChange = "removed";
        }
        else if (touchesSecret)
        {
            var old = c.HasSecret ? OpenSecret(c) : null;
            var n = r.Secret!;
            static string? Pick(string? input, string? stored) => input is null ? stored : input.Length == 0 ? null : input;
            var merged = new SecretInput(Pick(n.Password, old?.Password), Pick(n.RecoveryEmail, old?.RecoveryEmail),
                Pick(n.RecoveryPhone, old?.RecoveryPhone), Pick(n.TwoFactorNote, old?.TwoFactorNote));
            if (merged.IsEmpty) { c.ClearSecret(source, me.UserId, now); secretChange = "removed"; }
            else { Seal(c, merged, source, now); secretChange = "changed"; }
        }
        audit.Add("credentials.updated", nameof(ClientCredential), c.Id.ToString(), new { c.ClientId, c.Handle, secret = secretChange, source = source.ToString() });
        await db.SaveChangesAsync(ct);
        return ToDto(c, await PlatformNamesAsync(ct));
    }

    private sealed record SecretPayload(string? Password, string? RecoveryEmail, string? RecoveryPhone, string? TwoFactorNote);

    private void Seal(ClientCredential c, SecretInput s, CredentialSource source, DateTimeOffset now)
    {
        static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        // The password keeps its exact characters (spaces may be part of it); the other fields are trimmed.
        var payload = new SecretPayload(string.IsNullOrEmpty(s.Password) ? null : s.Password, Clean(s.RecoveryEmail), Clean(s.RecoveryPhone), Clean(s.TwoFactorNote));
        var plain = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        try
        {
            var sealedSecret = vault.Seal(plain, c.Id.ToString("N"));
            c.SetSecret(sealedSecret.Cipher, sealedSecret.WrappedKey, sealedSecret.KeyVersion, source, me.UserId, now);
        }
        finally { Array.Clear(plain); }
    }

    private SecretPayload? OpenSecret(ClientCredential c)
    {
        var plain = vault.Open(new SealedSecret(c.Cipher!, c.WrappedKey!, c.KeyVersion), c.Id.ToString("N"));
        if (plain is null) return null;
        try { return JsonSerializer.Deserialize<SecretPayload>(plain, Json); }
        finally { Array.Clear(plain); }
    }

    private async Task<Error?> CheckPlatformAsync(Guid platformId, CancellationToken ct) =>
        await db.Platforms.AnyAsync(p => p.Id == platformId, ct) ? null : Error.Validation("credentials.platform", "Wählen Sie das soziale Netzwerk.");

    private async Task<Dictionary<Guid, string>> PlatformNamesAsync(CancellationToken ct) =>
        (await db.Platforms.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct)).ToDictionary(p => p.Id, p => p.Name.Get(me.LanguageCode));

    private static CredentialDto ToDto(ClientCredential c, Dictionary<Guid, string> names) =>
        new(c.Id, c.ClientId, c.PlatformId, names.GetValueOrDefault(c.PlatformId, "—"), c.Handle, c.HasSecret, c.BusinessSuiteAccess, c.Source, c.UpdatedAt, c.LastRevealedAt);

    private async Task<List<Guid>> OwnersAsync(CancellationToken ct) =>
        await (from ur in db.UserRoles
               join r in db.Roles on ur.RoleId equals r.Id
               join u in db.Users on ur.UserId equals u.Id
               where r.SystemKey == SystemRoles.Owner && u.IsActive
               select u.Id).Distinct().ToListAsync(ct);

    private async Task TellManagersAsync(Guid clientId, string handle, CancellationToken ct)
    {
        var managers = await notifier.StaffWithAsync(Permissions.CredentialsManage, PermissionScope.All, ct);
        var name = await db.Clients.AsNoTracking().Where(c => c.Id == clientId).Select(c => c.BusinessName).FirstAsync(ct);
        var now = clock.GetUtcNow();
        await notifier.NotifyAsync(managers, "credentials.clientChanged", $"{name} hat die Zugangsdaten von @{handle} aktualisiert", null, "/clients", false,
            $"cred-client:{clientId:N}:{handle}:{now.ToUnixTimeSeconds() / 600}", ct);
        await db.SaveChangesAsync(ct);
    }
}
