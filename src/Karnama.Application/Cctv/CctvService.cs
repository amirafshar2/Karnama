using System.Collections.Concurrent;
using System.Text;
using Karnama.Application.Abstractions;
using Karnama.Application.Attendance;
using Karnama.Application.Notify;
using Karnama.Domain.Cctv;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Cctv;

public sealed record CameraDto(Guid Id, string Name, string? Location, int SortOrder, bool IsActive, bool Online, DateTimeOffset? LastOnlineAt, bool HasSubstream,
    string? MainMasked, string? SubMasked, uint Version);
public sealed record CctvStatusDto(bool Configured, IReadOnlyList<CameraDto> Cameras);
public sealed record SaveCameraRequest(string Name, string? Location, int SortOrder, bool IsActive, string? MainUrl, string? SubUrl, bool WorkAreaConfirmed, uint? Version = null);
public sealed record LiveDto(string WhepUrl, string Token, DateTimeOffset ExpiresAt, Guid SessionId, int IdleSeconds);
public sealed record LiveStopRequest(Guid SessionId);
/// <summary>What MediaMTX sends to its "http" authentication hook (one call per read/publish/api request).</summary>
public sealed record MediaAuthRequest(string? User, string? Password, string? Token, string? Ip, string? Action, string? Path, string? Protocol, string? Id, string? Query);

/// <summary>Short-lived memory of frames, so ten people looking at the grid cost the office one frame per camera per 10 s.</summary>
public sealed class SnapshotCache
{
    private readonly ConcurrentDictionary<Guid, (DateTimeOffset At, byte[]? Jpeg)> _frames = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    /// <summary>A frame, or a recent failure (null Jpeg) so a dead camera isn't asked again for 10 s.</summary>
    public bool TryGet(Guid camera, DateTimeOffset now, out byte[]? jpeg)
    {
        jpeg = null;
        if (!_frames.TryGetValue(camera, out var f) || now - f.At >= TimeSpan.FromSeconds(10)) return false;
        jpeg = f.Jpeg;
        return true;
    }
    public void Put(Guid camera, DateTimeOffset now, byte[]? jpeg) => _frames[camera] = (now, jpeg);
    /// <summary>One grab per camera at a time; everyone else waits for it.</summary>
    public SemaphoreSlim Lock(Guid camera) => _locks.GetOrAdd(camera, _ => new SemaphoreSlim(1, 1));
}

/// <summary>
/// CCTV (PROMPT2 §5): cameras stay on the office network; a media box there pulls them and is reached only through
/// a WireGuard tunnel. Viewers get a snapshot every 10 s by default (low-quality substream) and live video only on
/// demand with a 5-minute token bound to one camera; every live view is audited (who, which camera, when, how long).
/// Camera addresses (they contain passwords) are sealed with the vault and never sent back.
/// </summary>
public sealed class CctvService(IAppDbContext db, ICurrentUser me, IVault vault, IMediaServer media, ILinkSigner signer, SnapshotCache cache,
    Notifier notifier, IAuditWriter audit, TimeProvider clock)
{
    public const int IdleSeconds = 120;
    private static readonly TimeSpan TokenLife = TimeSpan.FromMinutes(5);
    private static Error NotFound => Error.NotFound("cctv.notFound", "دوربین پیدا نشد.");
    private bool CanView => me.Kind == UserKind.Staff && me.Has(Permissions.CctvView, PermissionScope.All);
    private bool CanManage => me.Kind == UserKind.Staff && me.Has(Permissions.CctvManage, PermissionScope.All);
    private static string Low(string path) => path + "-low";

    public async Task<Result<CctvStatusDto>> ListAsync(CancellationToken ct)
    {
        if (!CanView && !CanManage) return Error.Forbidden();
        var cams = await db.Cameras.AsNoTracking().Where(c => CanManage || c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        return new CctvStatusDto(media.IsConfigured, cams.Select(Dto).ToList());
    }

    private CameraDto Dto(Camera c) => new(c.Id, c.Name, c.Location, c.SortOrder, c.IsActive, c.Online, c.LastOnlineAt, c.SubCipher is not null,
        CanManage ? c.MainMasked : null, CanManage ? c.SubMasked : null, c.Version);

    public async Task<Result<CameraDto>> SaveAsync(Guid? id, SaveCameraRequest r, CancellationToken ct)
    {
        if (!CanManage) return Error.Forbidden();
        if (!vault.IsConfigured) return Error.Conflict("cctv.vault", "کلید رمزنگاری سرور تنظیم نشده؛ نشانی دوربین بدون رمزنگاری ذخیره نمی‌شود.");
        Camera c;
        if (id is { } given)
        {
            var found = await db.Cameras.FirstOrDefaultAsync(x => x.Id == given, ct);
            if (found is null) return NotFound;
            if (r.Version is { } v) db.SetOriginalVersion(found, v);
            var u = found.Update(r.Name, r.Location, r.SortOrder, r.IsActive);
            if (u.IsFailure) return u.Error!;
            c = found;
        }
        else
        {
            // PROMPT2 §5: only work areas, never toilets or private rooms, with a visible sign.
            if (!r.WorkAreaConfirmed) return Error.Validation("cctv.ethics", "تأیید کنید دوربین فقط در فضای کار است و تابلوی «این محیط دوربین دارد» نصب شده.");
            if (string.IsNullOrWhiteSpace(r.MainUrl)) return Error.Validation("cctv.url", "نشانی RTSP دوربین را وارد کنید.");
            var created = Camera.Create(r.Name, r.Location, r.SortOrder);
            if (created.IsFailure) return created.Error!;
            c = created.Value!;
            if (!r.IsActive) c.Update(r.Name, r.Location, r.SortOrder, isActive: false);   // honour "inactive" on create too
            db.Cameras.Add(c);
        }

        // Addresses: empty = keep the stored one.
        Uri? main = null, sub = null;
        if (!string.IsNullOrWhiteSpace(r.MainUrl))
        {
            var m = Camera.ValidateSource(r.MainUrl);
            if (m.IsFailure) return m.Error!;
            main = m.Value;
        }
        if (!string.IsNullOrWhiteSpace(r.SubUrl))
        {
            var s = Camera.ValidateSource(r.SubUrl);
            if (s.IsFailure) return s.Error!;
            sub = s.Value;
        }
        if (main is not null || sub is not null)
        {
            var mainUrl = main?.ToString() ?? Open(c, sub: false)!;
            var subUrl = sub?.ToString() ?? (c.SubCipher is null ? null : Open(c, sub: true));
            var sm = vault.Seal(Encoding.UTF8.GetBytes(mainUrl), $"camera:{c.Id}:main");
            var ss = subUrl is null ? null : vault.Seal(Encoding.UTF8.GetBytes(subUrl), $"camera:{c.Id}:sub");
            c.SetSources(sm.Cipher, sm.WrappedKey, main is not null ? Camera.Mask(main) : c.MainMasked, ss?.Cipher, ss?.WrappedKey,
                sub is not null ? Camera.Mask(sub) : c.SubMasked, sm.KeyVersion);
        }
        // A configured box that can't be reached: say so (after the input checks) instead of saving a camera nobody can watch.
        if (media.IsConfigured && c.IsActive && await media.ReadyPathsAsync(ct) is null) return Unreachable;
        audit.Add(id is null ? "cctv.cameraCreated" : "cctv.cameraUpdated", nameof(Camera), c.Id.ToString(),
            new { c.Name, c.Location, c.IsActive, mainChanged = main is not null, subChanged = sub is not null });
        await db.SaveChangesAsync(ct);
        if (!await PushAsync(c, ct))
            return Error.Conflict("cctv.unreachable", "دوربین ذخیره شد ولی جعبه‌ی تصویر دفتر جواب نداد؛ وقتی وصل شد «همگام‌سازی» را بزنید.");
        return Dto(c);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        if (!CanManage) return Error.Forbidden();
        var c = await db.Cameras.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound;
        db.Cameras.Remove(c);
        audit.Add("cctv.cameraDeleted", nameof(Camera), c.Id.ToString(), new { c.Name });
        await db.SaveChangesAsync(ct);
        if (media.IsConfigured)
        {
            await media.RemovePathAsync(c.Path, ct);
            await media.RemovePathAsync(Low(c.Path), ct);
        }
        return Result.Success();
    }

    private string? Open(Camera c, bool sub)
    {
        var bytes = sub
            ? (c.SubCipher is null ? null : vault.Open(new SealedSecret(c.SubCipher, c.SubKey!, c.KeyVersion), $"camera:{c.Id}:sub"))
            : vault.Open(new SealedSecret(c.MainCipher, c.MainKey, c.KeyVersion), $"camera:{c.Id}:main");
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Main stream only while someone watches live; the substream always (it's cheap on the office network and tells us the camera is up).</summary>
    /// <returns>False when the box refused or couldn't be reached (removing a stream that isn't there counts as done).</returns>
    private async Task<bool> PushAsync(Camera c, CancellationToken ct)
    {
        if (!media.IsConfigured) return true;
        if (!c.IsActive)
        {
            await media.RemovePathAsync(c.Path, ct);
            await media.RemovePathAsync(Low(c.Path), ct);
            return true;
        }
        var ok = true;
        if (Open(c, sub: false) is { } main) ok &= await media.UpsertPathAsync(c.Path, main, onDemand: true, ct);
        ok &= await media.UpsertPathAsync(Low(c.Path), Open(c, sub: true) ?? Open(c, sub: false) ?? "", onDemand: false, ct);
        return ok;
    }

    private static Error Unreachable => Error.Conflict("cctv.unreachable", "جعبه‌ی تصویر دفتر جواب نمی‌دهد؛ برق و اینترنت آن را بررسی کنید.");

    /// <summary>Re-sends every camera to the box (after it was replaced or restarted).</summary>
    public async Task<Result<int>> ResyncAsync(CancellationToken ct)
    {
        if (!CanManage) return Error.Forbidden();
        if (!media.IsConfigured) return Error.Conflict("cctv.notConfigured", "جعبه‌ی تصویر دفتر هنوز راه‌اندازی نشده است.");
        if (await media.ReadyPathsAsync(ct) is null) return Unreachable;
        var cams = await db.Cameras.ToListAsync(ct);
        var failed = 0;
        foreach (var c in cams)
            if (!await PushAsync(c, ct)) failed++;
        if (failed > 0) return Error.Conflict("cctv.unreachable", $"{failed} دوربین از {cams.Count} به جعبه‌ی تصویر نرسید؛ دوباره امتحان کنید.");
        return cams.Count;
    }

    // ---------------- viewing ----------------

    public async Task<Result<byte[]>> SnapshotAsync(Guid id, CancellationToken ct)
    {
        if (!CanView) return Error.Forbidden();
        var c = await db.Cameras.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        if (c is null) return NotFound;
        if (!media.IsConfigured) return Error.Conflict("cctv.notConfigured", "جعبه‌ی تصویر دفتر هنوز راه‌اندازی نشده است.");
        var gate = cache.Lock(c.Id);
        await gate.WaitAsync(ct);
        try
        {
            var now = clock.GetUtcNow();
            if (!cache.TryGet(c.Id, now, out var frame))
            {
                frame = await media.SnapshotAsync(Low(c.Path), ct);
                cache.Put(c.Id, now, frame);
            }
            return frame is null ? Error.Conflict("cctv.noFrame", "تصویری از این دوربین نرسید؛ شاید خاموش یا قطع است.") : frame;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Live video: a 5-minute token for this camera only. The page stops after 2 minutes without activity.</summary>
    public async Task<Result<LiveDto>> StartLiveAsync(Guid id, bool high, CancellationToken ct)
    {
        if (!CanView) return Error.Forbidden();
        var c = await db.Cameras.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        if (c is null) return NotFound;
        if (!media.IsConfigured) return Error.Conflict("cctv.notConfigured", "جعبه‌ی تصویر دفتر هنوز راه‌اندازی نشده است.");
        if (await media.ReadyPathsAsync(ct) is null) return Unreachable;   // no token for a box that is down
        var path = high ? c.Path : Low(c.Path);
        var session = Guid.CreateVersion7();
        var token = signer.Sign($"cctv|{path}|{me.UserId}|{session:N}", TokenLife);
        audit.Add("cctv.liveStarted", nameof(Camera), c.Id.ToString(), new { c.Name, session, quality = high ? "main" : "sub" });
        await db.SaveChangesAsync(ct);
        return new LiveDto(media.WhepUrl(path), token, clock.GetUtcNow() + TokenLife, session, IdleSeconds);
    }

    public async Task<Result> StopLiveAsync(Guid id, LiveStopRequest r, CancellationToken ct)
    {
        if (!CanView) return Error.Forbidden();
        if (!await db.Cameras.AnyAsync(x => x.Id == id, ct)) return NotFound;   // no audit rows for cameras that don't exist
        var started = await db.AuditLogs.AsNoTracking().Where(a => a.Action == "cctv.liveStarted" && a.EntityId == id.ToString() && a.ActorId == me.UserId)
            .OrderByDescending(a => a.OccurredAt).Take(20).ToListAsync(ct);
        var mine = started.FirstOrDefault(a => a.DataJson?.Contains(r.SessionId.ToString(), StringComparison.Ordinal) == true);
        var seconds = mine is null ? (int?)null : (int)Math.Min((clock.GetUtcNow() - mine.OccurredAt).TotalSeconds, 86_400);
        audit.Add("cctv.liveStopped", nameof(Camera), id.ToString(), new { session = r.SessionId, seconds });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// MediaMTX's authentication hook. Allowed: our own server (snapshots, API) with the internal account, and a
    /// viewer reading exactly the stream their token was made for, before it expires. Everything else — publishing,
    /// other streams, recordings, playback — is refused.
    /// </summary>
    public bool AuthorizeMedia(MediaAuthRequest r)
    {
        if (media.IsInternalAccount(r.User, r.Password)) return true;
        if (!string.Equals(r.Action, "read", StringComparison.Ordinal)) return false;
        var token = r.Token;
        if (string.IsNullOrEmpty(token) && r.Query is { } q)
            token = q.Split('&').Select(p => p.Split('=', 2)).FirstOrDefault(p => p.Length == 2 && p[0] == "token")?[1] is { } t ? Uri.UnescapeDataString(t) : null;
        if (string.IsNullOrEmpty(token) || signer.Verify(token) is not { } payload) return false;
        var parts = payload.Split('|');
        return parts.Length == 4 && parts[0] == "cctv" && parts[1] == r.Path;
    }

    // ---------------- health ----------------

    /// <summary>
    /// Every minute: which cameras are receiving (a camera that drops warns the managers once per outage), and live
    /// sessions are cut after 30 minutes — the token is only checked at the start, so this is the hard limit.
    /// </summary>
    public async Task<int> HealthAsync(CancellationToken ct)
    {
        if (!media.IsConfigured) return 0;
        await media.KickOldSessionsAsync(TimeSpan.FromMinutes(30), ct);
        var ready = await media.ReadyPathsAsync(ct);
        var now = clock.GetUtcNow();
        var cams = await db.Cameras.Where(c => c.IsActive).ToListAsync(ct);
        var down = 0;
        foreach (var c in cams)
        {
            var online = ready is not null && ready.TryGetValue(Low(c.Path), out var r) && r;
            if (ready is null) online = false;
            if (c.Seen(online, now) || (!online && c.OfflineWarnedAt is null && c.LastOnlineAt is not null && now - c.LastOnlineAt > TimeSpan.FromMinutes(2)))
            {
                if (c.OfflineWarnedAt is not null) continue;
                await notifier.NotifyAsync(await notifier.StaffWithAsync(Permissions.CctvManage, PermissionScope.All, ct), "cctv.offline",
                    $"دوربین «{c.Name}» قطع شد", ready is null ? "جعبه‌ی تصویر دفتر جواب نمی‌دهد (برق یا اینترنت دفتر)." : "برق و کابل دوربین را بررسی کنید.",
                    "/cctv", true, $"cctv-off:{c.Id:N}:{(c.LastOnlineAt ?? now).ToUnixTimeSeconds()}", ct);
                c.OfflineWarned(now);
                down++;
            }
        }
        await db.SaveChangesAsync(ct);
        return down;
    }
}
