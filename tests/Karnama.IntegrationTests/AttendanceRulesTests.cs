using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Karnama.Application.Abstractions;
using Karnama.Application.Attendance;
using Karnama.Domain.Attendance;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Karnama.IntegrationTests;

/// <summary>
/// QA findings A (B1–B7): lateness counted once, absence and forgotten-shift notices for the approvers, leave resolving
/// a detected absence, races → replay / 409 (never 500), corrections recompute lateness and keep hours backed by events,
/// and only people who clock in can be absent.
/// </summary>
public sealed class AttendanceRulesTests(FakeClockDemoAppFixture app) : IClassFixture<FakeClockDemoAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;

    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
        return await r.JsonAsync();
    }

    private async Task<T> Db<T>(Func<AppDbContext, Task<T>> work)
    {
        await using var scope = _f.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<T> Svc<T, TService>(Func<TService, Task<T>> work) where TService : notnull
    {
        await using var scope = _f.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private Task<(int, int)> Detect() => Svc<(int, int), LeaveService>(s => s.DetectAsync(CancellationToken.None));
    private Task<int> CloseForgotten() => Svc<int, AttendanceAdminService>(s => s.CloseForgottenAsync(CancellationToken.None));

    private static readonly Dictionary<string, PermissionScope> SelfOnly = new() { [Permissions.AttendanceSelf] = PermissionScope.Own };

    /// <summary>A staff member with a role of the given grants (a real role, so detection and notices see it).</summary>
    private async Task<(Guid Id, string Name)> UserAsync(string phone, string name, params PermissionGrant[] grants)
    {
        var id = await Db(async db =>
        {
            var role = Role.Create(new LocalizedText(new Dictionary<string, string> { ["fa"] = $"نقش {name}", ["en"] = $"Role {phone}" }), null, grants).Value!;
            db.Roles.Add(role);
            var user = User.Create(phone, name, UserKind.Staff).Value!;
            user.AssignRole(role.Id);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return user.Id;
        });
        return (id, name);
    }

    /// <summary>Tokens are checked against the fake clock: mint a fresh one after every clock move.</summary>
    private HttpClient As(Guid userId, Dictionary<string, PermissionScope>? perms = null)
    {
        var tokens = _f.Services.GetRequiredService<ITokenService>();
        return _f.CreateClient().WithToken(tokens.CreateAccessToken(new TokenSubject(userId, UserKind.Staff, false, "fa", perms ?? SelfOnly), Guid.NewGuid(), out _));
    }

    private async Task<HttpClient> Admin() => _f.CreateClient().WithToken(await _f.DemoTokenAsync("admin"));

    private void SetTehran(DateOnly day, int hour, int minute) => _f.Clock.SetUtcNow(Iran.At(day, new TimeOnly(hour, minute)));

    private static object ClockBody(string? key = null) => new { idempotencyKey = key ?? Guid.NewGuid().ToString("N") };

    private async Task<JsonElement> ReportRow(HttpClient admin, DateOnly day, Guid userId) =>
        (await Ok(await admin.GetAsync($"/api/v1/leave/report?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}"))).EnumerateArray()
            .Single(r => r.GetProperty("userId").GetGuid() == userId);

    private async Task<List<JsonElement>> Inbox(HttpClient c) =>
        (await Ok(await c.GetAsync("/api/v1/notifications"))).GetProperty("items").EnumerateArray().ToList();

    [Fact]
    public async Task A_working_day_lateness_absence_leave_corrections_and_forgotten_shifts()
    {
        // A day after "now" that is not a holiday; every weekday is a 09:00–17:00 workday with 10 minutes' grace.
        var day = Iran.Today(_f.Clock.GetUtcNow()).AddDays(1);
        var holidays = await Db(db => db.Holidays.Select(h => h.Date).ToListAsync());
        while (holidays.Contains(day)) day = day.AddDays(1);
        SetTehran(day, 8, 0);
        var admin = await Admin();
        (await admin.PutAsJsonAsync("/api/v1/attendance/setup/schedules", new
        {
            userId = (Guid?)null, days = Enumerable.Range(0, 7).Select(d => new { day = d, start = "09:00:00", end = "17:00:00" }).ToArray(),
            breakMinutes = 60, graceMinutes = 10, flexible = false, weeklyHours = 44,
        })).IsSuccessStatusCode.Should().BeTrue();

        var self = new PermissionGrant(Permissions.AttendanceSelf, PermissionScope.Own);
        var late = await UserAsync("09351000001", "دیرکرد آزمون", self);
        var phoned = await UserAsync("09351000002", "تلفنی آزمون", self);
        var corrected = await UserAsync("09351000003", "اصلاح آزمون", self);
        var forgot = await UserAsync("09351000004", "فراموشکار آزمون", self);
        var silent = await UserAsync("09351000005", "بی‌خبر آزمون", self);
        var kiosk = await UserAsync("09351000006", "کیوسک آزمون", new PermissionGrant(Permissions.AttendanceKiosk, PermissionScope.All));

        SetTehran(day, 8, 50);
        await Ok(await As(forgot.Id).PostAsJsonAsync("/api/v1/attendance/clock-in", ClockBody()));

        // B6 + B2: detection flags only people who clock in; the approvers get a notice naming them.
        SetTehran(day, 9, 20);
        (await Detect()).Item1.Should().BeGreaterThanOrEqualTo(4);
        var absences = await Db(db => db.Absences.AsNoTracking().Where(a => a.Day == day).ToListAsync());
        absences.Select(a => a.UserId).Should().Contain([late.Id, phoned.Id, corrected.Id, silent.Id]);
        absences.Should().NotContain(a => a.UserId == kiosk.Id, "the kiosk tablet never clocks in, so it is never absent");
        absences.Should().NotContain(a => a.UserId == forgot.Id);
        admin = await Admin();
        var adminInbox = await Inbox(admin);
        adminInbox.Should().Contain(n => n.GetProperty("kind").GetString() == "absence.team"
                                         && (n.GetProperty("title").GetString() + n.GetProperty("body").GetString()).Contains(late.Name));
        (await Inbox(As(late.Id))).Should().Contain(n => n.GetProperty("kind").GetString() == "absence.detected");

        // B1: the absence turns into lateness; the report counts the 40 minutes once.
        SetTehran(day, 9, 25);
        (await Ok(await As(corrected.Id).PostAsJsonAsync("/api/v1/attendance/clock-in", ClockBody()))).GetProperty("shift").GetProperty("lateMinutes").GetInt32().Should().Be(25);
        SetTehran(day, 9, 40);
        (await Ok(await As(late.Id).PostAsJsonAsync("/api/v1/attendance/clock-in", ClockBody()))).GetProperty("shift").GetProperty("lateMinutes").GetInt32().Should().Be(40);
        admin = await Admin();
        var row = await ReportRow(admin, day, late.Id);
        row.GetProperty("lateMinutes").GetInt32().Should().Be(40);
        row.GetProperty("lateCount").GetInt32().Should().Be(1);

        // B5: an approved clock-in correction to 08:58 clears the lateness (shift and report).
        SetTehran(day, 10, 0);
        var corr = await Ok(await As(corrected.Id).PostAsJsonAsync("/api/v1/attendance/corrections",
            new { day, clockIn = Iran.At(day, new TimeOnly(8, 58)), reason = "کیوسک خراب بود، ساعت ۸:۵۸ رسیدم" }));
        admin = await Admin();
        await Ok(await admin.PostAsJsonAsync($"/api/v1/attendance/corrections/{corr.GetProperty("id").GetGuid()}/decide", new { approve = true }));
        var shift = await Db(db => db.Shifts.AsNoTracking().SingleAsync(s => s.UserId == corrected.Id && s.Day == day));
        shift.LateMinutes.Should().Be(0);
        (await ReportRow(admin, day, corrected.Id)).GetProperty("lateMinutes").GetInt32().Should().Be(0);

        // B4: a clock-in-only correction for a past day while clocked in would open a second shift → a clear 409.
        var open = await Ok(await As(late.Id).PostAsJsonAsync("/api/v1/attendance/corrections",
            new { day = day.AddDays(-2), clockIn = Iran.At(day.AddDays(-2), new TimeOnly(9, 0)), reason = "ورود آن روز ثبت نشد" }));
        var conflict = await admin.PostAsJsonAsync($"/api/v1/attendance/corrections/{open.GetProperty("id").GetGuid()}/decide", new { approve = true });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await conflict.JsonAsync()).GetProperty("code").GetString().Should().Be("corrections.openShift");
        (await Db(db => db.AttendanceCorrections.AsNoTracking().SingleAsync(c => c.Id == open.GetProperty("id").GetGuid()))).Status.Should().Be(CorrectionStatus.Pending);
        (await Db(db => db.Shifts.CountAsync(s => s.UserId == late.Id && s.Status == ShiftStatus.Open))).Should().Be(1);

        // B7: a whole missed day added by a correction gets clock-in and clock-out events (actor = approver).
        var missed = await Ok(await As(late.Id).PostAsJsonAsync("/api/v1/attendance/corrections",
            new { day = day.AddDays(-1), clockIn = Iran.At(day.AddDays(-1), new TimeOnly(9, 0)), clockOut = Iran.At(day.AddDays(-1), new TimeOnly(17, 0)), reason = "کل روز را ثبت نکردم" }));
        await Ok(await admin.PostAsJsonAsync($"/api/v1/attendance/corrections/{missed.GetProperty("id").GetGuid()}/decide", new { approve = true }));
        var missedShift = await Db(db => db.Shifts.AsNoTracking().SingleAsync(s => s.UserId == late.Id && s.Day == day.AddDays(-1)));
        missedShift.WorkedMinutes(_f.Clock.GetUtcNow()).Should().Be(480);
        var missedEvents = await Db(db => db.AttendanceEvents.AsNoTracking().Where(e => e.ShiftId == missedShift.Id).ToListAsync());
        missedEvents.Select(e => e.Kind).Should().Contain([AttendanceEventKind.ClockIn, AttendanceEventKind.ClockOut, AttendanceEventKind.Correction]);
        missedEvents.Should().OnlyContain(e => e.ActorId != null && e.ActorId != late.Id);

        // B3: the manager registers sick leave after the absence was detected → the absence is resolved at once.
        var types = await Ok(await admin.GetAsync("/api/v1/leave/types"));
        var sick = types.EnumerateArray().First(t => t.GetProperty("kind").GetString() == "sick").GetProperty("id").GetGuid();
        var leave = await Ok(await admin.PostAsJsonAsync("/api/v1/leave/requests", new
        {
            leaveTypeId = sick, hourly = false, from = Iran.At(day, TimeOnly.MinValue), to = Iran.At(day.AddDays(1), TimeOnly.MinValue), reason = "تلفنی خبر داد: بیمار است", forUserId = phoned.Id,
        }));
        leave.GetProperty("status").GetString().Should().Be("approved");
        (await Db(db => db.Absences.AsNoTracking().SingleAsync(a => a.UserId == phoned.Id && a.Day == day))).Status.Should().Be(AbsenceStatus.Excused);

        // B3 (expiry): an approved leave that covers the day (stored without going through the decision) still stops "unexcused".
        await Db(async db =>
        {
            var type = await db.LeaveTypes.FirstAsync(t => t.Id == sick);
            var adminId = await db.Users.Where(u => u.Phone == "09000000001").Select(u => u.Id).FirstAsync();
            db.LeaveRequests.Add(LeaveRequest.Create(silent.Id, type, false, Iran.At(day, TimeOnly.MinValue), Iran.At(day.AddDays(1), TimeOnly.MinValue), "ثبت قدیمی",
                null, adminId, true, _f.Clock.GetUtcNow()).Value!);
            return await db.SaveChangesAsync();
        });

        // B2 + B7: a forgotten clock-out is closed at 17:00 with a system clock-out event, and the worker and the approvers are warned once.
        SetTehran(day, 20, 0);
        await CloseForgotten();
        (await Db(db => db.Shifts.AsNoTracking().SingleAsync(s => s.UserId == forgot.Id && s.Day == day))).Status.Should().Be(ShiftStatus.Open);
        SetTehran(day, 21, 30);
        (await CloseForgotten()).Should().BeGreaterThanOrEqualTo(1);
        var closed = await Db(db => db.Shifts.AsNoTracking().SingleAsync(s => s.UserId == forgot.Id && s.Day == day));
        closed.Status.Should().Be(ShiftStatus.Closed);
        closed.ClockOutAt.Should().Be(Iran.At(day, new TimeOnly(17, 0)));
        closed.NeedsReview.Should().BeTrue();
        var outEvent = await Db(db => db.AttendanceEvents.AsNoTracking().SingleAsync(e => e.ShiftId == closed.Id && e.Kind == AttendanceEventKind.ClockOut));
        outEvent.ActorId.Should().BeNull();
        outEvent.At.Should().Be(closed.ClockOutAt!.Value);
        (await Inbox(As(forgot.Id))).Should().Contain(n => n.GetProperty("kind").GetString() == "attendance.forgotten");
        admin = await Admin();
        (await Inbox(admin)).Count(n => n.GetProperty("kind").GetString() == "attendance.forgotten" && n.GetProperty("title").GetString()!.Contains(forgot.Name)).Should().Be(1);
        await CloseForgotten();   // nothing left to close, nothing sent twice
        (await Inbox(await Admin())).Count(n => n.GetProperty("kind").GetString() == "attendance.forgotten" && n.GetProperty("title").GetString()!.Contains(forgot.Name)).Should().Be(1);

        // Invariant "hours match events": every closed shift has its clock-in and clock-out events.
        var missing = await Db(db => db.Shifts.AsNoTracking().Where(s => s.Status == ShiftStatus.Closed)
            .CountAsync(s => !db.AttendanceEvents.Any(e => e.ShiftId == s.Id && e.Kind == AttendanceEventKind.ClockIn)
                             || !db.AttendanceEvents.Any(e => e.ShiftId == s.Id && e.Kind == AttendanceEventKind.ClockOut)));
        missing.Should().Be(0);

        // 49 h later: the covered absence is excused, the other open ones become unexcused; the kiosk never appears.
        _f.Clock.Advance(TimeSpan.FromHours(49));
        await Detect();
        (await Db(db => db.Absences.AsNoTracking().SingleAsync(a => a.UserId == silent.Id && a.Day == day))).Status.Should().Be(AbsenceStatus.Excused);
        (await Db(db => db.Absences.AsNoTracking().SingleAsync(a => a.UserId == phoned.Id && a.Day == day))).Status.Should().Be(AbsenceStatus.Excused);
        (await Db(db => db.Absences.AnyAsync(a => a.UserId == kiosk.Id))).Should().BeFalse();
        var phonedRow = await ReportRow(await Admin(), day, phoned.Id);
        phonedRow.GetProperty("unexcusedAbsences").GetInt32().Should().Be(0);
        phonedRow.GetProperty("excusedAbsences").GetInt32().Should().Be(1);
        phonedRow.GetProperty("paidLeaveMinutes").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Racing_clock_ins_replay_or_conflict_and_never_fail_with_500()
    {
        var self = new PermissionGrant(Permissions.AttendanceSelf, PermissionScope.Own);
        var tapper = await UserAsync("09351000011", "دوبار‌زن آزمون", self);
        var retrier = await UserAsync("09351000012", "صف آفلاین آزمون", self);

        // Five taps with different keys at once: one wins, the rest are told "already in".
        var c = As(tapper.Id);
        var statuses = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ => (await c.PostAsJsonAsync("/api/v1/attendance/clock-in", ClockBody())).StatusCode));
        statuses.Count(s => s == HttpStatusCode.OK).Should().Be(1, string.Join(",", statuses));
        statuses.Should().OnlyContain(s => s == HttpStatusCode.OK || s == HttpStatusCode.Conflict, string.Join(",", statuses));

        // The offline queue sends the same event four times at once: all get the first result, one shift.
        var key = Guid.NewGuid().ToString("N");
        var r = As(retrier.Id);
        var replies = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => r.PostAsJsonAsync("/api/v1/attendance/clock-in", ClockBody(key))));
        replies.Select(x => x.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.OK, string.Join(",", replies.Select(x => (int)x.StatusCode)));
        var ids = new List<Guid>();
        foreach (var reply in replies) ids.Add((await reply.JsonAsync()).GetProperty("shift").GetProperty("id").GetGuid());
        ids.Distinct().Should().HaveCount(1);
        (await Db(db => db.Shifts.CountAsync(s => s.UserId == retrier.Id))).Should().Be(1);

        // Same for clock-out: one closes the shift, the retries replay it.
        var outKey = Guid.NewGuid().ToString("N");
        var outs = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => r.PostAsJsonAsync("/api/v1/attendance/clock-out", ClockBody(outKey))));
        outs.Select(x => x.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.OK, string.Join(",", outs.Select(x => (int)x.StatusCode)));
        (await Db(db => db.AttendanceEvents.CountAsync(e => e.UserId == retrier.Id && e.Kind == AttendanceEventKind.ClockOut))).Should().Be(1);
    }

    [Fact]
    public async Task A_unique_index_race_elsewhere_is_a_409_problem_not_a_500()
    {
        // Two managers add the same holiday at once: the check passes for both, the unique index refuses one.
        var admin = await Admin();
        var date = new DateOnly(2031, 3, 21);
        var replies = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => admin.PostAsJsonAsync("/api/v1/attendance/setup/holidays", new { date, name = "نوروز" })));
        var statuses = replies.Select(x => x.StatusCode).ToList();
        statuses.Count(s => s is HttpStatusCode.OK or HttpStatusCode.Created).Should().Be(1, string.Join(",", statuses));
        statuses.Should().OnlyContain(s => s == HttpStatusCode.OK || s == HttpStatusCode.Created || s == HttpStatusCode.Conflict, string.Join(",", statuses));
        foreach (var conflict in replies.Where(x => x.StatusCode == HttpStatusCode.Conflict))
        {
            conflict.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
            var body = await conflict.JsonAsync();
            body.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        }
    }
}
