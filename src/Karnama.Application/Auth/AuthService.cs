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
    IOtpDispatcher sms,
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
            .Where(c => c.Phone == phone && c.CreatedAt > windowStart && c.Purpose == OtpPurpose.Login)
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
        // The SMS goes out in the background: real and unknown phones get the same response time and status.
        var canLogin = await db.Users.AsNoTracking().AnyAsync(u => u.Phone == phone && u.IsActive && !u.IsDemo, ct);
        if (canLogin)
            sms.Enqueue(phone, code);
        else
            logger.LogInformation("OTP requested for unknown or inactive phone {Phone}", PhoneNumber.Mask(phone));

        return new RequestOtpResponse(challengeId, (int)OtpChallenge.Lifetime.TotalSeconds, options.ResendSeconds);
    }

    public async Task<Result<LoginResult>> VerifyOtpAsync(VerifyOtpRequest request, ClientKind client, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var challenge = await db.OtpChallenges.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ChallengeId && c.Purpose == OtpPurpose.Login, ct);
        if (challenge is null) return OtpChallenge.WrongCode;

        // Claim one attempt atomically: parallel guesses can never exceed MaxAttempts.
        var claimed = await db.OtpChallenges
            .Where(c => c.Id == challenge.Id && c.ConsumedAt == null && c.Attempts < OtpChallenge.MaxAttempts && c.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, c => c.Attempts + 1), ct);
        if (claimed == 0)
        {
            var fresh = await db.OtpChallenges.AsNoTracking().FirstAsync(c => c.Id == challenge.Id, ct);
            var reason = fresh.CheckUsable(now);
            return reason.IsFailure ? reason.Error! : OtpChallenge.WrongCode;
        }

        var code = Digits.ToLatin(request.Code.Trim());
        if (!otp.Matches(challenge.CodeHash, otp.Hash(challenge.Id, challenge.Phone, code)))
            return OtpChallenge.WrongCode;

        // Consume atomically: one code = one session, even if two correct requests race.
        var consumed = await db.OtpChallenges
            .Where(c => c.Id == challenge.Id && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), ct);
        if (consumed == 0)
            return Error.Validation("auth.otpUsed", "این کد قبلاً استفاده شده است. کد جدید بگیرید.");

        // OTP failures never lock the account (that would let anyone lock out the owner); the per-challenge
        // cap plus 3 sends per 10 minutes already bounds guessing.
        var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == challenge.Phone && !u.IsDemo, ct);
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
        var locked = Error.TooMany("auth.locked", "حساب به‌خاطر تلاش‌های ناموفق موقتاً قفل است. ۱۵ دقیقه‌ی دیگر تلاش کنید.");
        var userId = user.Id;

        // Claim one attempt atomically (max 10 per lockout window, even with parallel requests).
        var attempt = await db.Users
            .Where(u => u.Id == userId && (u.LockoutUntil == null || u.LockoutUntil <= now) && u.FailedLoginCount < User.MaxFailedLogins)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.FailedLoginCount, u => u.FailedLoginCount + 1), ct);
        if (attempt == 0)
        {
            var lockUntil = now.Add(User.LockoutDuration);
            await db.Users
                .Where(u => u.Id == userId && (u.LockoutUntil == null || u.LockoutUntil <= now))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LockoutUntil, lockUntil).SetProperty(u => u.FailedLoginCount, 0), ct);
            return locked;
        }

        var step = totp.Verify(protector.Unprotect(user.TwoFactorSecret), Digits.ToLatin(request.Code.Trim()), now);
        if (step is null)
        {
            audit.Add(AuditActions.LoginFailed, nameof(User), userId.ToString(), new { reason = "totp" }, actorOverride: userId);
            await db.SaveChangesAsync(ct);
            return Error.Validation("auth.totpWrong", "کد برنامه‌ی تأیید درست نیست.");
        }
        var accepted = user.AcceptTwoFactorStep(step.Value);
        if (accepted.IsFailure) return accepted.Error!;
        // Same check in SQL so two requests with one code can't both pass.
        var stepValue = step.Value;
        var stepClaimed = await db.Users
            .Where(u => u.Id == user.Id && (u.LastTwoFactorStep == null || u.LastTwoFactorStep < stepValue))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastTwoFactorStep, stepValue), ct);
        if (stepClaimed == 0)
            return Error.Validation("auth.totpReused", "این کد قبلاً استفاده شده است. منتظر کد بعدی بمانید.");

        if (!user.TwoFactorEnabled)
        {
            user.ConfirmTwoFactor();
            audit.Add(AuditActions.TwoFactorEnabled, nameof(User), user.Id.ToString(), actorOverride: user.Id);
        }

        await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.FailedLoginCount, 0).SetProperty(u => u.LockoutUntil, (DateTimeOffset?)null), ct);
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
        if (user is null || !user.IsActive) return invalid;
        if (user.IsDemo && !demoOptions.Value.Enabled) return invalid;

        // Claim + insert of the replacement in one transaction: a racing loser waits on the row lock,
        // then sees the claim and revokes the whole family including the new token.
        var userId = user.Id;
        var rotated = await db.InTransactionAsync<SessionTokens?>(async token =>
        {
            var replacementId = Guid.CreateVersion7();
            var claimed = await db.RefreshTokens
                .Where(t => t.Id == current.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.ReplacedById, replacementId), token);
            if (claimed == 0)
            {
                await RevokeFamilyAsync(current.FamilyId, now, token);
                return null;
            }
            var fresh = await db.Users.FirstAsync(u => u.Id == userId, token);
            var session = await IssueSessionAsync(fresh, current.Client == ClientKind.Android ? ClientKind.Android : client, current.FamilyId, token, replacementId);
            await db.SaveChangesAsync(token);
            return session;
        }, ct);
        return rotated is null ? invalid : rotated;
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
