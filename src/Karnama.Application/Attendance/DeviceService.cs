using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Karnama.Application.Abstractions;
using Karnama.Application.Notify;
using Karnama.Domain.Attendance;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Attendance;

public sealed record DeviceDto(Guid Id, string Name, string Serial, DeviceKind Kind, PunchMode Mode, string? Location, IReadOnlyList<string> AllowedIps, bool IsActive,
    DateTimeOffset? LastSeenAt, bool Online, string? LastIp, int? ClockSkewSeconds, int PunchesToday, int PendingCommands, uint Version);
public sealed record SaveDeviceRequest(string Name, string? Serial, DeviceKind Kind, PunchMode Mode, string? Location, List<string>? AllowedIps, bool IsActive, uint? Version = null);
public sealed record PinDto(Guid Id, string Pin, Guid UserId, string UserName);
public sealed record SavePinRequest(string Pin, Guid UserId);
public sealed record UnknownPinDto(string Pin, int Count, DateTimeOffset LastAt, string DeviceName);
public sealed record PunchDto(long Id, string DeviceName, string Pin, string? UserName, DateTimeOffset At, int Status, PunchOutcome Outcome, string? Note);
public sealed record ImportPreviewDto(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, int TotalRows, ImportMapping Suggested);
/// <summary>Zero-based column numbers. <see cref="Time"/> null = the date column holds date and time together.</summary>
public sealed record ImportMapping(int Pin, int Date, int? Time, int? Status, bool HasHeader);
public sealed record ImportResultDto(int Added, int Duplicates, int UnknownPins, int Invalid, int ShiftsCreated, IReadOnlyList<string> Problems);

/// <summary>
/// Fingerprint / face devices (PROMPT2 §6): the ZKTeco ADMS "push" protocol (the device posts its records to /iclock),
/// file import for devices that can't push, PIN ↔ user mapping, and turning records into shifts.
/// Only a PIN and a time are ever accepted — fingerprint templates, face photos and user lists are refused unread.
/// </summary>
public sealed class DeviceService(IAppDbContext db, ICurrentUser me, AttendanceSettingsStore settings, ISpreadsheetReader sheets, Notifier notifier,
    IAuditWriter audit, TimeProvider clock, IChangeFeed changes)
{
    public const int MaxBodyBytes = 256 * 1024;
    private const int MaxImportRows = 20_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static Error NotFound => Error.NotFound("devices.notFound", "دستگاه پیدا نشد.");
    private bool CanManage => me.Kind == UserKind.Staff && me.Has(Permissions.DevicesManage, PermissionScope.All);

    // ===================== ADMS protocol (anonymous; the serial number + allowed address are the gate) =====================

    /// <summary>Unknown or switched-off serial, or a call from outside the allowed addresses → refused before reading anything.</summary>
    private async Task<BiometricDevice?> GateAsync(string? sn, IPAddress? ip, CancellationToken ct)
    {
        var serial = (sn ?? "").Trim().ToUpperInvariant();
        if (serial.Length is < 4 or > 40) return null;
        var d = await db.BiometricDevices.FirstOrDefaultAsync(x => x.Serial == serial && x.Kind == DeviceKind.Adms && x.IsActive, ct);
        if (d is null) return null;
        var allowed = d.AllowedIps.Count > 0 ? d.AllowedIps : (await settings.GetAsync(ct)).OfficeNetworks;
        if (allowed.Count > 0 && !AttendanceSettingsStore.InNetworks(ip, allowed)) return null;
        d.Seen(clock.GetUtcNow(), ip?.ToString());
        return d;
    }

    /// <summary>GET /iclock/cdata?SN=…&amp;options=all — tells the device what to send: attendance records only.</summary>
    public async Task<string?> HandshakeAsync(string? sn, IPAddress? ip, CancellationToken ct)
    {
        var d = await GateAsync(sn, ip, ct);
        if (d is null) return null;
        await db.SaveChangesAsync(ct);
        return string.Join("\n",
            $"GET OPTION FROM: {d.Serial}",
            $"ATTLOGStamp={d.AttStamp}",
            "OPERLOGStamp=9999",
            "ATTPHOTOStamp=None",
            "ErrorDelay=30",
            "Delay=10",
            "TransTimes=00:00;14:05",
            "TransInterval=1",
            "TransFlag=TransData AttLog",
            "TimeZone=3.5",
            "Realtime=1",
            "Encrypt=None") + "\n";
    }

    /// <summary>POST /iclock/cdata?SN=…&amp;table=ATTLOG — tab-separated records: PIN, time, status, verify, …</summary>
    public async Task<string?> ReceiveAsync(string? sn, IPAddress? ip, string? table, long? stamp, string body, CancellationToken ct)
    {
        var d = await GateAsync(sn, ip, ct);
        if (d is null) return null;
        // Operation logs, user lists, fingerprint templates and photos: acknowledged and dropped, never stored.
        if (!string.Equals(table, "ATTLOG", StringComparison.OrdinalIgnoreCase))
        {
            await db.SaveChangesAsync(ct);
            return "OK";
        }
        var now = clock.GetUtcNow();
        var lines = body.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Take(5000).ToList();
        var parsed = new List<DevicePunch>();
        foreach (var line in lines)
        {
            var f = line.Split('\t');
            if (f.Length < 2) continue;
            var pin = DevicePin.Normalize(f[0]);
            if (pin is null || !DateTime.TryParseExact(f[1].Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) continue;
            var status = f.Length > 2 && int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var st) ? st : -1;
            parsed.Add(DevicePunch.Create(d.Id, pin, Iran.At(DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local)), status, now));
        }
        // A single record arriving as it happens tells us how far the device clock is from ours.
        if (parsed.Count == 1 && Math.Abs((parsed[0].At - now).TotalMinutes) < 30)
        {
            var skew = (int)(parsed[0].At - now).TotalSeconds;
            d.MeasuredSkew(skew);
            if (Math.Abs(skew) > 120 && (d.SkewWarnedAt is null || now - d.SkewWarnedAt > TimeSpan.FromDays(1)))
            {
                d.SkewWarned(now);
                await notifier.NotifyAsync(await notifier.StaffWithAsync(Permissions.DevicesManage, PermissionScope.All, ct), "devices.skew",
                    $"ساعت دستگاه «{d.Name}» {Math.Abs(skew) / 60} دقیقه {(skew > 0 ? "جلو" : "عقب")} است", "در صفحه‌ی دستگاه‌ها «همگام‌سازی ساعت» را بزنید.",
                    "/attendance/devices", true, $"device-skew:{d.Id:N}:{Iran.Today(now):yyyyMMdd}", ct);
            }
        }
        if (stamp is { } s) d.Stamp(s);
        var added = await AddNewAsync(d, parsed, ct);
        await ApplyAsync(added, d, fromFile: false, ct);
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return $"OK: {lines.Count}";
    }

    /// <summary>GET /iclock/getrequest — the device asks for commands (time sync, info).</summary>
    public async Task<string?> PollAsync(string? sn, IPAddress? ip, CancellationToken ct)
    {
        var d = await GateAsync(sn, ip, ct);
        if (d is null) return null;
        var cmds = d.TakeCommands();
        await db.SaveChangesAsync(ct);
        return cmds.Count == 0 ? "OK" : string.Join("\n", cmds) + "\n";
    }

    /// <summary>POST /iclock/devicecmd — results of commands; logged only.</summary>
    public async Task<string?> CommandResultAsync(string? sn, IPAddress? ip, CancellationToken ct)
    {
        var d = await GateAsync(sn, ip, ct);
        if (d is null) return null;
        await db.SaveChangesAsync(ct);
        return "OK";
    }

    // ===================== punches → shifts =====================

    /// <summary>Keeps only records not seen before ((device, PIN, time) is unique).</summary>
    private async Task<List<DevicePunch>> AddNewAsync(BiometricDevice d, List<DevicePunch> punches, CancellationToken ct)
    {
        if (punches.Count == 0) return [];
        var from = punches.Min(p => p.At);
        var to = punches.Max(p => p.At);
        var existing = (await db.DevicePunches.AsNoTracking().Where(p => p.DeviceId == d.Id && p.At >= from && p.At <= to).Select(p => new { p.Pin, p.At }).ToListAsync(ct))
            .Select(p => (p.Pin, p.At)).ToHashSet();
        var fresh = new List<DevicePunch>();
        foreach (var p in punches.OrderBy(p => p.At))
        {
            if (!existing.Add((p.Pin, p.At))) continue;
            db.DevicePunches.Add(p);
            fresh.Add(p);
        }
        return fresh;
    }

    private async Task ApplyAsync(List<DevicePunch> punches, BiometricDevice d, bool fromFile, CancellationToken ct)
    {
        if (punches.Count == 0) return;
        var pins = punches.Select(p => p.Pin).Distinct().ToList();
        var map = await db.DevicePins.AsNoTracking().Where(p => pins.Contains(p.Pin)).ToDictionaryAsync(p => p.Pin, p => p.UserId, ct);
        var activeIds = await db.Users.AsNoTracking().Where(u => map.Values.Contains(u.Id) && u.IsActive && u.Kind == UserKind.Staff).Select(u => u.Id).ToListAsync(ct);
        foreach (var group in punches.GroupBy(p => p.Pin))
        {
            if (!map.TryGetValue(group.Key, out var userId) || !activeIds.Contains(userId))
            {
                foreach (var p in group) p.Applied(null, PunchOutcome.UnknownPin, "این شماره به هیچ همکاری وصل نیست");
                continue;
            }
            if (fromFile) await ApplyFileDaysAsync(userId, group.ToList(), d, ct);
            else foreach (var p in group.OrderBy(p => p.At)) await ApplyOneAsync(userId, p, d, ct);
        }
    }

    /// <summary>A record as it happens: in, out or a break, by the device's key or by flipping.</summary>
    private async Task ApplyOneAsync(Guid userId, DevicePunch p, BiometricDevice d, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var last = await db.AttendanceEvents.Where(e => e.UserId == userId).MaxAsync(e => (DateTimeOffset?)e.At, ct);
        if (last is { } l && p.At < l)
        {
            p.Applied(userId, PunchOutcome.OutOfOrder, "قدیمی‌تر از آخرین ثبت این همکار — مدیر بررسی کند");
            return;
        }
        if (last is { } l2 && p.At - l2 < TimeSpan.FromMinutes(2))
        {
            p.Applied(userId, PunchOutcome.Ignored, "دو بار پشت‌سرهم زده شد");
            return;
        }
        var open = await db.Shifts.FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open, ct)
                   ?? db.Shifts.Local.FirstOrDefault(s => s.UserId == userId && s.Status == ShiftStatus.Open);
        var action = d.Mode == PunchMode.StatusKey
            ? p.Status switch { 0 or 4 => PunchOutcome.ClockIn, 1 or 5 => PunchOutcome.ClockOut, 2 => PunchOutcome.BreakStart, 3 => PunchOutcome.BreakEnd, _ => Toggle() }
            : Toggle();
        PunchOutcome Toggle() => open is null ? PunchOutcome.ClockIn : open.OnBreak ? PunchOutcome.BreakEnd : PunchOutcome.ClockOut;

        var methods = new List<MethodResult> { new("BiometricDevice", true, $"دستگاه «{d.Name}»") };
        var key = $"dev:{d.Id:N}:{p.Pin}:{p.At.ToUnixTimeSeconds()}";
        switch (action)
        {
            case PunchOutcome.ClockIn:
                if (open is not null) { p.Applied(userId, PunchOutcome.Ignored, "ورود از قبل ثبت شده بود"); return; }
                var (trust, reason) = await TrustAsync(userId, methods, ct);
                var day = Iran.Today(p.At);
                var shift = Shift.Open(userId, day, p.At, methods, trust, reason is not null, reason, false, await LateAsync(userId, day, p.At, ct));
                db.Shifts.Add(shift);
                foreach (var absence in await db.Absences.Where(a => a.UserId == userId && a.Day == day).ToListAsync(ct)) absence.BecomeLate(p.At);
                db.AttendanceEvents.Add(new AttendanceEvent(userId, shift.Id, AttendanceEventKind.ClockIn, p.At, now, p.At, key, JsonSerializer.Serialize(methods, Json), trust, null, null));
                changes.Record("presence", userId, null, userId, null, "clockIn");
                break;
            case PunchOutcome.ClockOut:
                if (open is null) { p.Applied(userId, PunchOutcome.Ignored, "خروج بدون ورود"); return; }
                var (t2, r2) = await TrustAsync(userId, methods, ct);
                var closed = open.Close(p.At, methods, t2, r2 is not null, r2, false, await EarlyAsync(userId, open.Day, p.At, ct), null);
                if (closed.IsFailure) { p.Applied(userId, PunchOutcome.Ignored, closed.Error!.Message); return; }
                foreach (var timer in await db.WorkTimers.Where(t => t.UserId == userId && t.StoppedAt == null).ToListAsync(ct)) timer.Stop(p.At);   // leaving stops the timer
                db.AttendanceEvents.Add(new AttendanceEvent(userId, open.Id, AttendanceEventKind.ClockOut, p.At, now, p.At, key, JsonSerializer.Serialize(methods, Json), t2, null, null));
                changes.Record("presence", userId, null, userId, null, "clockOut");
                break;
            case PunchOutcome.BreakStart or PunchOutcome.BreakEnd:
                if (open is null) { p.Applied(userId, PunchOutcome.Ignored, "استراحت بدون ورود"); return; }
                var b = action == PunchOutcome.BreakStart ? open.StartBreak(p.At) : open.EndBreak(p.At);
                if (b.IsFailure) { p.Applied(userId, PunchOutcome.Ignored, b.Error!.Message); return; }
                db.AttendanceEvents.Add(new AttendanceEvent(userId, open.Id, action == PunchOutcome.BreakStart ? AttendanceEventKind.BreakStart : AttendanceEventKind.BreakEnd,
                    p.At, now, p.At, key, "[]", Trust.Green, null, null));
                changes.Record("presence", userId, null, userId, null, "break");
                break;
        }
        p.Applied(userId, action);
    }

    /// <summary>
    /// An exported file covers past days: for each day with no shift yet, the first record is the arrival and the last
    /// one the departure. Days that already have a shift are left alone (the app's own record wins).
    /// </summary>
    private async Task<int> ApplyFileDaysAsync(Guid userId, List<DevicePunch> punches, BiometricDevice d, CancellationToken ct)
    {
        var created = 0;
        var now = clock.GetUtcNow();
        var methods = new List<MethodResult> { new("BiometricDevice", true, $"فایل دستگاه «{d.Name}»") };
        foreach (var day in punches.GroupBy(p => Iran.Today(p.At)))
        {
            var list = day.OrderBy(p => p.At).ToList();
            if (await db.Shifts.AnyAsync(s => s.UserId == userId && s.Day == day.Key, ct) || db.Shifts.Local.Any(s => s.UserId == userId && s.Day == day.Key))
            {
                foreach (var p in list) p.Applied(userId, PunchOutcome.Ignored, "برای این روز شیفت ثبت شده بود");
                continue;
            }
            var first = list[0];
            var lastPunch = list[^1];
            var (trust, reason) = await TrustAsync(userId, methods, ct);
            var single = list.Count == 1 || lastPunch.At - first.At < TimeSpan.FromMinutes(5);
            var shift = Shift.Open(userId, day.Key, first.At, methods, trust, reason is not null || single, single ? "فقط یک ثبت در فایل بود — خروج را مدیر وارد کند" : reason, false,
                await LateAsync(userId, day.Key, first.At, ct));
            db.Shifts.Add(shift);
            db.AttendanceEvents.Add(new AttendanceEvent(userId, shift.Id, AttendanceEventKind.ClockIn, first.At, now, first.At,
                $"file:{d.Id:N}:{first.Pin}:{first.At.ToUnixTimeSeconds()}", JsonSerializer.Serialize(methods, Json), trust, null, me.UserId));
            first.Applied(userId, PunchOutcome.ClockIn);
            if (!single)
            {
                shift.Close(lastPunch.At, methods, trust, reason is not null, reason, false, await EarlyAsync(userId, day.Key, lastPunch.At, ct), null);
                db.AttendanceEvents.Add(new AttendanceEvent(userId, shift.Id, AttendanceEventKind.ClockOut, lastPunch.At, now, lastPunch.At,
                    $"file:{d.Id:N}:{lastPunch.Pin}:{lastPunch.At.ToUnixTimeSeconds()}", JsonSerializer.Serialize(methods, Json), trust, null, me.UserId));
                lastPunch.Applied(userId, PunchOutcome.ClockOut);
            }
            foreach (var p in list.Where(p => p.Outcome == PunchOutcome.Pending)) p.Applied(userId, PunchOutcome.Ignored, "ثبت میانی روز");
            created++;
        }
        return created;
    }

    /// <summary>The person's own clock-in rule, with "BiometricDevice" passed (the device is in the office).</summary>
    private async Task<(Trust, string?)> TrustAsync(Guid userId, List<MethodResult> methods, CancellationToken ct)
    {
        var roleIds = await db.UserRoles.Where(r => r.UserId == userId).Select(r => r.RoleId).ToListAsync(ct);
        var policies = await db.AttendancePolicies.AsNoTracking().ToListAsync(ct);
        var p = policies.FirstOrDefault(x => x.UserIds.Contains(userId)) ?? policies.FirstOrDefault(x => x.RoleIds.Any(roleIds.Contains)) ?? policies.FirstOrDefault(x => x.IsDefault);
        var text = p?.Rule ?? "BiometricDevice";
        var rule = PolicyRule.Parse(text);
        if (rule.IsFailure) return (Trust.Yellow, "قانون ورود معتبر نیست");
        var passed = rule.Value!.Evaluate(name => methods.Any(m => m.Method == name && m.Passed));
        return passed ? (Trust.Green, null) : (Trust.Yellow, $"قانون ورود ({text}) فقط با دستگاه برقرار نشد");
    }

    private async Task<WorkSchedule?> ScheduleAsync(Guid userId, CancellationToken ct) =>
        await db.WorkSchedules.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct)
        ?? await db.WorkSchedules.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == null, ct);

    private async Task<int> LateAsync(Guid userId, DateOnly day, DateTimeOffset at, CancellationToken ct)
    {
        var sched = await ScheduleAsync(userId, ct);
        if (sched?.For(day) is not { } d || sched.Flexible) return 0;
        var minutes = (int)(at - Iran.At(day, d.Start)).TotalMinutes;
        return minutes > sched.GraceMinutes ? minutes : 0;
    }

    private async Task<int> EarlyAsync(Guid userId, DateOnly day, DateTimeOffset at, CancellationToken ct)
    {
        var sched = await ScheduleAsync(userId, ct);
        if (sched?.For(day) is not { } d || sched.Flexible) return 0;
        var end = Iran.At(day, d.End);
        return at < end ? (int)(end - at).TotalMinutes : 0;
    }

    // ===================== admin =====================

    public async Task<Result<List<DeviceDto>>> ListAsync(CancellationToken ct)
    {
        if (!CanManage) return Error.Forbidden();
        var now = clock.GetUtcNow();
        var start = Iran.At(Iran.Today(now), TimeOnly.MinValue);
        var counts = await db.DevicePunches.AsNoTracking().Where(p => p.At >= start).GroupBy(p => p.DeviceId).Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return (await db.BiometricDevices.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct)).Select(d => Dto(d, now, counts.GetValueOrDefault(d.Id))).ToList();
    }

    private static DeviceDto Dto(BiometricDevice d, DateTimeOffset now, int today) => new(d.Id, d.Name, d.Serial, d.Kind, d.Mode, d.Location, d.AllowedIps, d.IsActive,
        d.LastSeenAt, d.Kind == DeviceKind.Adms && d.LastSeenAt is { } s && now - s < TimeSpan.FromMinutes(5), d.LastIp, d.ClockSkewSeconds, today, d.PendingCommands.Count, d.Version);

    public async Task<Result<DeviceDto>> SaveAsync(Guid? id, SaveDeviceRequest r, CancellationToken ct)
    {
        if (!CanManage) return Error.Forbidden();
        BiometricDevice d;
        if (id is { } given)
        {
            var found = await db.BiometricDevices.FirstOrDefaultAsync(x => x.Id == given, ct);
            if (found is null) return NotFound;
            if (r.Version is { } v) db.SetOriginalVersion(found, v);
            var u = found.Update(r.Name, r.Mode, r.Location, r.AllowedIps, r.IsActive);
            if (u.IsFailure) return u.Error!;
            d = found;
        }
        else
        {
            var c = BiometricDevice.Create(r.Name, r.Serial ?? "", r.Kind, r.Mode, r.Location, r.AllowedIps);
            if (c.IsFailure) return c.Error!;
            d = c.Value!;
            if (await db.BiometricDevices.AnyAsync(x => x.Serial == d.Serial, ct)) return Error.Conflict("devices.serialExists", "دستگاهی با این شماره‌ی سریال ثبت شده است.");
            db.BiometricDevices.Add(d);
        }
        audit.Add(id is null ? "devices.created" : "devices.updated", nameof(BiometricDevice), d.Id.ToString(), new { d.Name, d.Serial, d.Kind, d.Mode, d.AllowedIps, d.IsActive });
        await db.SaveChangesAsync(ct);
        return Dto(d, clock.GetUtcNow(), 0);
    }

    /// <summary>
    /// Queues a time sync for the device's next poll. ZK firmware takes the time from our reply's Date header on that
    /// handshake; the explicit command is for firmware that supports it.
    /// </summary>
    public async Task<Result<DeviceDto>> SyncTimeAsync(Guid id, CancellationToken ct)
    {
        if (!CanManage) return Error.Forbidden();
        var d = await db.BiometricDevices.FirstOrDefaultAsync(x => x.Id == id && x.Kind == DeviceKind.Adms, ct);
        if (d is null) return NotFound;
        var local = Iran.Local(clock.GetUtcNow());
        d.Queue($"SET OPTION DateTime={ZkTime(local.DateTime)}");
        d.Queue("CHECK");
        audit.Add("devices.syncTime", nameof(BiometricDevice), d.Id.ToString());
        await db.SaveChangesAsync(ct);
        return Dto(d, clock.GetUtcNow(), 0);
    }

    /// <summary>ZK's packed time: seconds since 2000-01-01 counted with 31-day months and 12-month years.</summary>
    public static long ZkTime(DateTime t) =>
        ((((long)(t.Year - 2000) * 12 + (t.Month - 1)) * 31 + (t.Day - 1)) * 24 + t.Hour) * 3600 + t.Minute * 60 + t.Second;

    public async Task<Result<List<PinDto>>> PinsAsync(CancellationToken ct)
    {
        if (!CanManage) return Error.Forbidden();
        var pins = await db.DevicePins.AsNoTracking().ToListAsync(ct);
        var ids = pins.Select(p => p.UserId).ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return pins.OrderBy(p => p.Pin.Length).ThenBy(p => p.Pin, StringComparer.Ordinal).Select(p => new PinDto(p.Id, p.Pin, p.UserId, names.GetValueOrDefault(p.UserId, "—"))).ToList();
    }

    /// <summary>Maps a PIN to a person; records that were waiting for this PIN are processed now (newest first wins nothing — in order).</summary>
    public async Task<Result<PinDto>> SavePinAsync(SavePinRequest r, CancellationToken ct)
    {
        if (!CanManage) return Error.Forbidden();
        var created = DevicePin.Create(r.Pin, r.UserId);
        if (created.IsFailure) return created.Error!;
        var pin = created.Value!;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == r.UserId && u.Kind == UserKind.Staff, ct);
        if (user is null) return Error.Validation("devices.user", "همکار را انتخاب کنید.");
        if (await db.DevicePins.AnyAsync(p => p.Pin == pin.Pin, ct)) return Error.Conflict("devices.pinTaken", "این شماره قبلاً به همکار دیگری وصل شده است.");
        if (await db.DevicePins.AnyAsync(p => p.UserId == r.UserId, ct)) return Error.Conflict("devices.userHasPin", "این همکار شماره‌ی دستگاه دارد؛ اول آن را حذف کنید.");
        db.DevicePins.Add(pin);
        audit.Add("devices.pinMapped", nameof(DevicePin), pin.Id.ToString(), new { pin.Pin, r.UserId });
        await db.SaveChangesAsync(ct);

        // Records from the last 2 days that were waiting for this PIN.
        var since = clock.GetUtcNow().AddDays(-2);
        var waiting = await db.DevicePunches.Where(p => p.Pin == pin.Pin && p.Outcome == PunchOutcome.UnknownPin && p.At >= since).OrderBy(p => p.At).ToListAsync(ct);
        foreach (var byDevice in waiting.GroupBy(p => p.DeviceId))
        {
            var d = await db.BiometricDevices.AsNoTracking().FirstAsync(x => x.Id == byDevice.Key, ct);
            foreach (var p in byDevice) p.Applied(null, PunchOutcome.Pending);
            await ApplyAsync(byDevice.ToList(), d, d.Kind == DeviceKind.FileImport, ct);
        }
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return new PinDto(pin.Id, pin.Pin, pin.UserId, user.DisplayName);
    }

    public async Task<Result> DeletePinAsync(Guid id, CancellationToken ct)
    {
        if (!CanManage) return Error.Forbidden();
        var p = await db.DevicePins.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Error.NotFound("devices.pinNotFound", "این نگاشت پیدا نشد.");
        db.DevicePins.Remove(p);
        audit.Add("devices.pinRemoved", nameof(DevicePin), p.Id.ToString(), new { p.Pin, p.UserId });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<List<UnknownPinDto>>> UnknownPinsAsync(CancellationToken ct)
    {
        if (!CanManage) return Error.Forbidden();
        var since = clock.GetUtcNow().AddDays(-30);
        var rows = await db.DevicePunches.AsNoTracking().Where(p => p.Outcome == PunchOutcome.UnknownPin && p.At >= since)
            .GroupBy(p => new { p.Pin, p.DeviceId }).Select(g => new { g.Key.Pin, g.Key.DeviceId, N = g.Count(), Last = g.Max(p => p.At) }).ToListAsync(ct);
        var names = await db.BiometricDevices.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        return rows.OrderByDescending(r => r.Last).Select(r => new UnknownPinDto(r.Pin, r.N, r.Last, names.GetValueOrDefault(r.DeviceId, "—"))).ToList();
    }

    public async Task<Result<List<PunchDto>>> PunchesAsync(Guid? deviceId, CancellationToken ct)
    {
        if (!CanManage) return Error.Forbidden();
        var q = db.DevicePunches.AsNoTracking();
        if (deviceId is { } id) q = q.Where(p => p.DeviceId == id);
        var list = await q.OrderByDescending(p => p.At).Take(100).ToListAsync(ct);
        var devices = await db.BiometricDevices.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var userIds = list.Where(p => p.UserId != null).Select(p => p.UserId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return list.Select(p => new PunchDto(p.Id, devices.GetValueOrDefault(p.DeviceId, "—"), p.Pin, p.UserId is { } u ? users.GetValueOrDefault(u) : null, p.At, p.Status, p.Outcome, p.Note)).ToList();
    }

    // ===================== file import =====================

    public Result<ImportPreviewDto> Preview(byte[] content, string fileName)
    {
        if (!CanManage) return Error.Forbidden();
        var rows = sheets.Read(content, fileName, MaxImportRows + 1, 20);
        if (rows is null || rows.Count == 0) return Error.Validation("devices.file", "فایل خوانده نشد. خروجی Excel (xlsx) یا CSV نرم‌افزار دستگاه را بفرستید.");
        var hasHeader = rows[0].Any(c => c.Length > 0 && !c.Any(char.IsDigit));
        var headers = hasHeader ? rows[0] : rows[0].Select((_, i) => $"ستون {i + 1}").ToList();
        var data = rows.Skip(hasHeader ? 1 : 0).ToList();
        return new ImportPreviewDto(headers, data.Take(10).ToList(), data.Count, Suggest(headers, data, hasHeader));
    }

    /// <summary>Guesses columns from header words (Persian/English) and from what the first rows look like.</summary>
    private static ImportMapping Suggest(IReadOnlyList<string> headers, List<IReadOnlyList<string>> data, bool hasHeader)
    {
        int Find(Func<string, bool> byHeader, Func<string, bool> byValue)
        {
            for (var i = 0; i < headers.Count; i++) if (byHeader(headers[i].ToLowerInvariant())) return i;
            var sample = data.Take(5).ToList();
            for (var i = 0; i < headers.Count; i++) if (sample.Count > 0 && sample.All(r => i < r.Count && byValue(r[i]))) return i;
            return -1;
        }
        var date = Find(h => h.Contains("تاریخ") || h.Contains("date") || h.Contains("زمان"), v => ParseDate(v) is not null);
        var time = Find(h => h.Contains("ساعت") || h == "time" || h.Contains("time"), v => TimeOnly.TryParse(Digits(v), CultureInfo.InvariantCulture, out _) && !v.Contains('/') && !v.Contains('-'));
        var pin = Find(h => h.Contains("شماره") || h.Contains("کد") || h.Contains("pin") || h.Contains("id") || h.Contains("پرسنلی"), v => DevicePin.Normalize(Digits(v)) is not null);
        if (time == date) time = -1;
        return new ImportMapping(Math.Max(pin, 0), Math.Max(date, 0), time < 0 ? null : time, null, hasHeader);
    }

    public async Task<Result<ImportResultDto>> ImportAsync(Guid deviceId, byte[] content, string fileName, ImportMapping m, CancellationToken ct)
    {
        if (!CanManage) return Error.Forbidden();
        var d = await db.BiometricDevices.FirstOrDefaultAsync(x => x.Id == deviceId && x.Kind == DeviceKind.FileImport, ct);
        if (d is null) return Error.Validation("devices.importDevice", "اول یک دستگاه از نوع «وارد کردن فایل» بسازید.");
        var rows = sheets.Read(content, fileName, MaxImportRows + 1, 20);
        if (rows is null || rows.Count == 0) return Error.Validation("devices.file", "فایل خوانده نشد.");
        var now = clock.GetUtcNow();
        var problems = new List<string>();
        var punches = new List<DevicePunch>();
        var invalid = 0;
        var line = m.HasHeader ? 1 : 0;
        foreach (var r in rows.Skip(m.HasHeader ? 1 : 0))
        {
            line++;
            string Cell(int i) => i >= 0 && i < r.Count ? r[i] : "";
            var pin = DevicePin.Normalize(Digits(Cell(m.Pin)));
            var dateText = Cell(m.Date);
            var at = m.Time is { } t ? ParseDate($"{Digits(dateText).Split(' ')[0]} {Digits(Cell(t))}") : ParseDate(dateText);
            if (pin is null || at is null)
            {
                invalid++;
                if (problems.Count < 10) problems.Add($"ردیف {line}: شماره یا تاریخ خوانده نشد");
                continue;
            }
            if (at > now.AddMinutes(10) || at < now.AddYears(-1))
            {
                invalid++;
                if (problems.Count < 10) problems.Add($"ردیف {line}: تاریخ خارج از یک سال گذشته است");
                continue;
            }
            var status = m.Status is { } s && int.TryParse(Digits(Cell(s)), out var st) ? st : -1;
            punches.Add(DevicePunch.Create(d.Id, pin, at.Value, status, now));
        }
        var before = punches.Count;
        var fresh = await AddNewAsync(d, punches, ct);
        var shiftsBefore = db.Shifts.Local.Count;
        await ApplyAsync(fresh, d, fromFile: true, ct);
        var shiftsCreated = db.Shifts.Local.Count - shiftsBefore;
        d.Seen(now, null);
        audit.Add("devices.imported", nameof(BiometricDevice), d.Id.ToString(), new { fileName, rows = before, added = fresh.Count, invalid });
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return new ImportResultDto(fresh.Count, before - fresh.Count, fresh.Count(p => p.Outcome == PunchOutcome.UnknownPin), invalid, shiftsCreated, problems);
    }

    /// <summary>Persian/Arabic digits → ASCII.</summary>
    private static string Digits(string s) => string.Concat((s ?? "").Trim().Select(c => c switch
    {
        >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
        >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
        _ => c,
    }));

    /// <summary>"2026-10-25 08:01[:00]", "2026/10/25 08:01", or Persian "1405/08/03 08:01" (year below 1500 = Jalali), as Iran time.</summary>
    public static DateTimeOffset? ParseDate(string text)
    {
        var t = Digits(text).Replace('-', '/').Replace('T', ' ');
        var parts = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 2) return null;
        var ymd = parts[0].Split('/');
        if (ymd.Length != 3 || !int.TryParse(ymd[0], out var y) || !int.TryParse(ymd[1], out var mo) || !int.TryParse(ymd[2], out var dd)) return null;
        var time = TimeOnly.MinValue;
        if (parts.Length == 2 && !TimeOnly.TryParse(parts[1], CultureInfo.InvariantCulture, out time)) return null;
        try
        {
            DateOnly day;
            if (y is > 1300 and < 1500)
            {
                var g = new PersianCalendar().ToDateTime(y, mo, dd, 0, 0, 0, 0);
                day = DateOnly.FromDateTime(g);
            }
            else if (y is > 2000 and < 2100) day = new DateOnly(y, mo, dd);
            else return null;
            return Iran.At(day, time);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    // ===================== health =====================

    /// <summary>Every 10 minutes: a pushing device silent for 15+ minutes in working hours → one warning per outage.</summary>
    public async Task<int> HealthAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var local = Iran.Local(now);
        if (local.Hour is < 8 or >= 20) return 0;
        var silent = await db.BiometricDevices.Where(d => d.Kind == DeviceKind.Adms && d.IsActive && d.LastSeenAt != null && d.LastSeenAt < now.AddMinutes(-15) && d.OfflineWarnedAt == null)
            .ToListAsync(ct);
        if (silent.Count == 0) return 0;
        var to = await notifier.StaffWithAsync(Permissions.DevicesManage, PermissionScope.All, ct);
        foreach (var d in silent)
        {
            await notifier.NotifyAsync(to, "devices.offline", $"دستگاه «{d.Name}» وصل نیست", "برق و اینترنت دستگاه را بررسی کنید؛ همکارها فعلاً از اپ ورود بزنند.",
                "/attendance/devices", true, $"device-offline:{d.Id:N}:{d.LastSeenAt!.Value.ToUnixTimeSeconds()}", ct);
            d.OfflineWarned(now);
        }
        await db.SaveChangesAsync(ct);
        return silent.Count;
    }
}
