using Karnama.Application.Abstractions;
using Karnama.Application.Common;
using Karnama.Domain.Auditing;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Karnama.Application.Auth;

public sealed class AuthService(
    IAppDbContext db,
    IOtpCodeService otp,
    ISmsSender sms,
    ITokenService tokens,
    ITotpService totp,
    ISecretProtector protector,
    IAuditWriter audit,
    TimeProvider clock,
    IOptions<OtpOptions> otpOptions,
    IOptions<DemoLoginOptions> demoOptions,
    ILogger<AuthService> logger)
{
    private const string TotpIssuer = "Karnama";

    public async Task<Result<RequestOtpResponse>> RequestOtpAsync(RequestOtpRequest request, CancellationToken ct)
    {
        if (!PhoneNumber.TryNormalize(request.Phone, out var phone))
            return Error.Validation("auth.invalidPhone", "شماره‌ی موبایل معتبر نیست.");

        var now = clock.GetUtcNow();
        var options = otpOptions.Value;
        var windowStart = now.AddMinutes(-10);

        var recent = await db.OtpChallenges.AsNoTracking()
            .Where(c => c.Phone == phone && c.CreatedAt > windowStart)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => c.CreatedAt)
            .ToListAsync(ct);

        if (recent.Count > 0 && (now - recent[0]).TotalSeconds < options.ResendSeconds)
        {
            var wait = options.ResendSeconds - (int)(now - recent[0]).TotalSeconds;
            return Error.TooMany("auth.otpWait", $"لطفاً {wait} ثانیه‌ی دیگر دوباره تلاش کنید.");
        }
        if (recent.Count >= options.MaxSendsPer10Minutes)
            return Error.TooMany("auth.otpLimit", "تعداد درخواست کد زیاد بود. ۱۰ دقیقه‌ی دیگر تلاش کنید.");

        var challengeId = Guid.CreateVersion7();
        var code = otp.GenerateCode();
        db.OtpChallenges.Add(OtpChallenge.Create(challengeId, phone, otp.Hash(challengeId, phone, code), now));
        await db.SaveChangesAsync(ct);

        // Same response whether or not the account exists (no user enumeration); SMS only goes to real, active accounts.
        var canLogin = await db.Users.AsNoTracking().AnyAsync(u => u.Phone == phone && u.IsActive && !u.IsDemo, ct);
        if (canLogin)
            await sms.SendOtpAsync(phone, code, ct);
        else
            logger.LogInformation("OTP requested for unknown or inactive phone {Phone}", PhoneNumber.Mask(phone));

        return new RequestOtpResponse(challengeId, (int)OtpChallenge.Lifetime.TotalSeconds, options.ResendSeconds);
    }

    public async Task<Result<LoginResult>> VerifyOtpAsync(VerifyOtpRequest request, ClientKind client, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var challenge = await db.OtpChallenges.FirstOrDefaultAsync(c => c.Id == request.ChallengeId, ct);
        if (challenge is null)
            return Error.Validation("auth.otpWrong", "کد واردشده درست نیست.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == challenge.Phone && !u.IsDemo, ct);
        if (user is not null && user.IsLockedOut(now))
            return Error.TooMany("auth.locked", "حساب به‌خاطر تلاش‌های ناموفق موقتاً قفل است. ۱۵ دقیقه‌ی دیگر تلاش کنید.");

        var code = Digits.ToLatin(request.Code.Trim());
        var matches = otp.Matches(challenge.CodeHash, otp.Hash(challenge.Id, challenge.Phone, code));
        var verified = challenge.Verify(matches, now);
        if (verified.IsFailure)
        {
            if (user is not null)
            {
                user.RegisterFailedLogin(now);
                audit.Add(AuditActions.LoginFailed, nameof(User), user.Id.ToString(), new { reason = verified.Error!.Code }, actorOverride: user.Id);
            }
            await db.SaveChangesAsync(ct);
            return verified.Error!;
        }

        if (user is null || !user.IsActive)
        {
            await db.SaveChangesAsync(ct);
            return Error.Unauthorized("auth.noAccount", "حسابی با این شماره فعال نیست. با مدیر آژانس تماس بگیرید.");
        }

        if (await PermissionLoader.IsOwnerAsync(db, user.Id, ct))
        {
            await db.SaveChangesAsync(ct);
            var step = user.TwoFactorEnabled ? LoginStep.TwoFactorRequired : LoginStep.TwoFactorSetupRequired;
            return new LoginResult(step, null, tokens.CreateTwoFactorToken(user.Id, client));
        }

        user.RegisterSuccessfulLogin();
        var session = await IssueSessionAsync(user, client, Guid.CreateVersion7(), ct);
        audit.Add(AuditActions.Login, nameof(User), user.Id.ToString(), new { client = client.ToString() }, actorOverride: user.Id);
        await db.SaveChangesAsync(ct);
        return new LoginResult(LoginStep.Completed, session, null);
    }

    public async Task<Result<TwoFactorSetupResponse>> BeginTwoFactorSetupAsync(TwoFactorSetupRequest request, CancellationToken ct)
    {
        var pending = tokens.ReadTwoFactorToken(request.TwoFactorToken);
        if (pending is null) return Error.Unauthorized("auth.twoFactorExpired", "مرحله‌ی ورود منقضی شده است. دوباره وارد شوید.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == pending.Value.UserId && u.IsActive, ct);
        if (user is null) return Error.Unauthorized("auth.twoFactorExpired", "مرحله‌ی ورود منقضی شده است. دوباره وارد شوید.");
        if (user.TwoFactorEnabled)
            return Error.Conflict("auth.twoFactorAlreadyEnabled", "ورود دومرحله‌ای قبلاً فعال شده است.");

        var secret = totp.GenerateSecret();
        user.BeginTwoFactorEnrollment(protector.Protect(secret));
        await db.SaveChangesAsync(ct);
        return new TwoFactorSetupResponse(secret, totp.BuildUri(secret, user.Phone, TotpIssuer));
    }

    public async Task<Result<LoginResult>> VerifyTwoFactorAsync(TwoFactorVerifyRequest request, CancellationToken ct)
    {
        var pending = tokens.ReadTwoFactorToken(request.TwoFactorToken);
        if (pending is null) return Error.Unauthorized("auth.twoFactorExpired", "مرحله‌ی ورود منقضی شده است. دوباره وارد شوید.");

        var now = clock.GetUtcNow();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == pending.Value.UserId && u.IsActive, ct);
        if (user is null || user.TwoFactorSecret is null)
            return Error.Unauthorized("auth.twoFactorExpired", "مرحله‌ی ورود منقضی شده است. دوباره وارد شوید.");
        if (user.IsLockedOut(now))
            return Error.TooMany("auth.locked", "حساب به‌خاطر تلاش‌های ناموفق موقتاً قفل است. ۱۵ دقیقه‌ی دیگر تلاش کنید.");

        var step = totp.Verify(protector.Unprotect(user.TwoFactorSecret), Digits.ToLatin(request.Code.Trim()), now);
        if (step is null)
        {
            user.RegisterFailedLogin(now);
            audit.Add(AuditActions.LoginFailed, nameof(User), user.Id.ToString(), new { reason = "totp" }, actorOverride: user.Id);
            await db.SaveChangesAsync(ct);
            return Error.Validation("auth.totpWrong", "کد برنامه‌ی تأیید درست نیست.");
        }
        var accepted = user.AcceptTwoFactorStep(step.Value);
        if (accepted.IsFailure) return accepted.Error!;

        if (!user.TwoFactorEnabled)
        {
            user.ConfirmTwoFactor();
            audit.Add(AuditActions.TwoFactorEnabled, nameof(User), user.Id.ToString(), actorOverride: user.Id);
        }

        user.RegisterSuccessfulLogin();
        var session = await IssueSessionAsync(user, pending.Value.Client, Guid.CreateVersion7(), ct);
        audit.Add(AuditActions.Login, nameof(User), user.Id.ToString(), new { client = pending.Value.Client.ToString(), twoFactor = true }, actorOverride: user.Id);
        await db.SaveChangesAsync(ct);
        return new LoginResult(LoginStep.Completed, session, null);
    }

    public async Task<Result<SessionTokens>> RefreshAsync(string? rawToken, ClientKind client, CancellationToken ct)
    {
        var invalid = Error.Unauthorized("auth.sessionExpired", "نشست شما تمام شده است. دوباره وارد شوید.");
        if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > 200) return invalid;

        var now = clock.GetUtcNow();
        var hash = tokens.HashRefreshToken(rawToken);
        var current = await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (current is null) return invalid;

        if (current.RevokedAt is not null)
        {
            if (current.ReplacedById is not null)
            {
                // A rotated token came back: it was stolen or replayed. Kill the whole login.
                await RevokeFamilyAsync(current.FamilyId, now, ct);
                audit.Add(AuditActions.RefreshReuse, nameof(User), current.UserId.ToString(), new { family = current.FamilyId }, actorOverride: current.UserId);
                await db.SaveChangesAsync(ct);
                logger.LogWarning("Refresh token reuse detected for user {UserId}; family revoked", current.UserId);
            }
            return invalid;
        }
        if (now >= current.ExpiresAt) return invalid;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == current.UserId, ct);
        if (user is null || !user.IsActive || user.IsLockedOut(now)) return invalid;
        if (user.IsDemo && !demoOptions.Value.Enabled) return invalid;

        var replacementId = Guid.CreateVersion7();
        // Atomic claim: only one concurrent refresh can rotate this token; the loser is treated as reuse.
        var claimed = await db.RefreshTokens
            .Where(t => t.Id == current.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.ReplacedById, replacementId), ct);
        if (claimed == 0)
        {
            await RevokeFamilyAsync(current.FamilyId, now, ct);
            await db.SaveChangesAsync(ct);
            return invalid;
        }

        var session = await IssueSessionAsync(user, current.Client == ClientKind.Android ? ClientKind.Android : client, current.FamilyId, ct, replacementId);
        await db.SaveChangesAsync(ct);
        return session;
    }

    public async Task LogoutAsync(string? rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > 200) return;
        var hash = tokens.HashRefreshToken(rawToken);
        var token = await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null) return;
        await RevokeFamilyAsync(token.FamilyId, clock.GetUtcNow(), ct);
    }

    public async Task<IReadOnlyList<DemoAccountDto>> GetDemoAccountsAsync(CancellationToken ct)
    {
        if (!demoOptions.Value.Enabled) return [];
        var phones = DemoAccounts.All.Select(a => a.Phone).ToArray();
        var existing = await db.Users.AsNoTracking()
            .Where(u => u.IsDemo && u.IsActive && phones.Contains(u.Phone))
            .Select(u => u.Phone).ToListAsync(ct);
        return DemoAccounts.All.Where(a => existing.Contains(a.Phone))
            .Select(a => new DemoAccountDto(a.Key, a.NameFa, a.DescriptionFa, a.Icon)).ToList();
    }

    public async Task<Result<SessionTokens>> DemoLoginAsync(DemoLoginRequest request, ClientKind client, CancellationToken ct)
    {
        if (!demoOptions.Value.Enabled) return Error.NotFound();
        var account = DemoAccounts.Find(request.Role);
        if (account is null) return Error.Validation("auth.demoInvalid", "نقش نمایشی معتبر نیست.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == account.Phone && u.IsDemo && u.IsActive, ct);
        if (user is null) return Error.NotFound("auth.demoMissing", "حساب نمایشی در این نصب وجود ندارد.");

        var session = await IssueSessionAsync(user, client, Guid.CreateVersion7(), ct);
        audit.Add(AuditActions.DemoLogin, nameof(User), user.Id.ToString(), new { role = account.Key }, actorOverride: user.Id, isDemoOverride: true);
        await db.SaveChangesAsync(ct);
        return session;
    }

    private async Task<SessionTokens> IssueSessionAsync(User user, ClientKind client, Guid familyId, CancellationToken ct, Guid? refreshId = null)
    {
        var now = clock.GetUtcNow();
        var permissions = await PermissionLoader.LoadAsync(db, user.Id, ct);
        var subject = new TokenSubject(user.Id, user.Kind, user.IsDemo, user.LanguageCode, permissions);
        var access = tokens.CreateAccessToken(subject, familyId, out var accessExpires);

        var raw = tokens.CreateRefreshTokenValue();
        var refresh = RefreshToken.Issue(refreshId ?? Guid.CreateVersion7(), user.Id, familyId, tokens.HashRefreshToken(raw), client, user.IsDemo, now, tokens.RefreshTokenLifetime);
        db.RefreshTokens.Add(refresh);
        return new SessionTokens(access, accessExpires, raw, refresh.ExpiresAt, client);
    }

    private Task<int> RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken ct) =>
        db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
}
