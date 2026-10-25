using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Karnama.IntegrationTests;

/// <summary>M4 + N1: clock in/out rules, idempotency, offline time, QR single use, one timer, corrections, append-only evidence.</summary>
public sealed class AttendanceTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;
    private async Task<HttpClient> As(string role) => _f.CreateClient().WithToken(await _f.DemoTokenAsync(role));
    private static string Key() => Guid.NewGuid().ToString("N");
    // The demo office (sample place) — 10 m away.
    private static object Office => new { latitude = 29.61009, longitude = 52.53, accuracy = 15.0, mock = false };

    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
        return await r.JsonAsync();
    }

    [Fact]
    public async Task Clock_in_verifies_is_idempotent_and_one_shift_at_a_time()
    {
        var writer = await As("writer");
        var key = Key();
        var today = await Ok(await writer.PostAsJsonAsync("/api/v1/attendance/clock-in", new { idempotencyKey = key, evidence = Office }));
        var shift = today.GetProperty("shift");
        shift.GetProperty("trust").GetString().Should().Be("green");
        shift.GetProperty("needsReview").GetBoolean().Should().BeFalse();
        shift.GetProperty("methods").EnumerateArray().Should().Contain(m => m.GetProperty("method").GetString() == "Geofence" && m.GetProperty("passed").GetBoolean());

        // Same request again (a retry after a lost response) → same shift, no error.
        var again = await Ok(await writer.PostAsJsonAsync("/api/v1/attendance/clock-in", new { idempotencyKey = key, evidence = Office }));
        again.GetProperty("shift").GetProperty("id").GetGuid().Should().Be(shift.GetProperty("id").GetGuid());
        // A new tap while already in → conflict.
        (await writer.PostAsJsonAsync("/api/v1/attendance/clock-in", new { idempotencyKey = Key(), evidence = Office })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        await Ok(await writer.PostAsJsonAsync("/api/v1/attendance/break/start", new { idempotencyKey = Key() }));
        (await Ok(await writer.GetAsync("/api/v1/attendance/me"))).GetProperty("shift").GetProperty("onBreak").GetBoolean().Should().BeTrue();
        await Ok(await writer.PostAsJsonAsync("/api/v1/attendance/break/end", new { idempotencyKey = Key() }));
        var outResult = await Ok(await writer.PostAsJsonAsync("/api/v1/attendance/clock-out", new { idempotencyKey = Key(), evidence = Office, note = "دکتر" }));
        outResult.GetProperty("shift").ValueKind.Should().Be(JsonValueKind.Null);
        outResult.GetProperty("recentShifts").EnumerateArray().First(s => s.GetProperty("id").GetGuid() == shift.GetProperty("id").GetGuid())
            .GetProperty("clockOutAt").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Failed_checks_do_not_block_they_flag_for_review_and_fake_location_is_red()
    {
        var editor = await As("editor");
        var far = await Ok(await editor.PostAsJsonAsync("/api/v1/attendance/clock-in", new { idempotencyKey = Key(), evidence = new { latitude = 35.7, longitude = 51.4, accuracy = 10.0 } }));
        far.GetProperty("shift").GetProperty("trust").GetString().Should().Be("yellow");
        far.GetProperty("shift").GetProperty("needsReview").GetBoolean().Should().BeTrue();
        await Ok(await editor.PostAsJsonAsync("/api/v1/attendance/clock-out", new { idempotencyKey = Key(), evidence = new { latitude = 29.61, longitude = 52.53, accuracy = 10.0, mock = true } }));
        var me = await Ok(await editor.GetAsync("/api/v1/attendance/me"));
        var farId = far.GetProperty("shift").GetProperty("id").GetGuid();
        me.GetProperty("recentShifts").EnumerateArray().First(s => s.GetProperty("id").GetGuid() == farId).GetProperty("trust").GetString().Should().Be("red");

        // The manager sees it in the review list.
        var admin = await As("admin");
        var day = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var review = await Ok(await admin.GetAsync($"/api/v1/attendance/shifts?from={DateTime.UtcNow.AddDays(-1):yyyy-MM-dd}&to={DateTime.UtcNow.AddDays(1):yyyy-MM-dd}&needsReview=true"));
        review.EnumerateArray().Should().Contain(s => s.GetProperty("userName").GetString()!.StartsWith("علی"));
        _ = day;
    }

    [Fact]
    public async Task Offline_events_use_elapsed_time_and_a_wrong_phone_clock_is_flagged()
    {
        var writer = _f.CreateClient().WithToken(await _f.DemoTokenAsync("admin"));
        var before = DateTimeOffset.UtcNow;
        var r = await Ok(await writer.PostAsJsonAsync("/api/v1/attendance/clock-in",
            new { idempotencyKey = Key(), elapsedMs = 30 * 60 * 1000, deviceAt = before.AddHours(-3), evidence = Office }));
        var shift = r.GetProperty("shift");
        shift.GetProperty("offline").GetBoolean().Should().BeTrue();
        shift.GetProperty("clockInAt").GetDateTimeOffset().Should().BeCloseTo(before.AddMinutes(-30), TimeSpan.FromMinutes(1));
        shift.GetProperty("needsReview").GetBoolean().Should().BeTrue();
        shift.GetProperty("reviewReason").GetString().Should().Contain("ساعت گوشی");
        await Ok(await writer.PostAsJsonAsync("/api/v1/attendance/clock-out", new { idempotencyKey = Key(), evidence = Office }));
    }

    [Fact]
    public async Task Office_qr_is_single_use_and_forged_codes_are_red()
    {
        var admin = await As("admin");
        (await admin.PutAsJsonAsync("/api/v1/attendance/setup/settings", new
        {
            officeNetworks = Array.Empty<string>(), autoClockInOnTimer = false, offlineToleranceMinutes = 10, forgottenAfterHours = 4,
            selfieRetentionDays = 30, locationRetentionDays = 90, askEarlyLeaveReason = true,
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var code = (await Ok(await admin.GetAsync("/api/v1/attendance/kiosk/code"))).GetProperty("code").GetString()!;
        (await (await As("editor")).GetAsync("/api/v1/attendance/kiosk/code")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var writer = await As("writer");
        var ok = await Ok(await writer.PostAsJsonAsync("/api/v1/attendance/clock-in", new { idempotencyKey = Key(), evidence = new { qr = code } }));
        ok.GetProperty("shift").GetProperty("trust").GetString().Should().Be("green");
        await Ok(await writer.PostAsJsonAsync("/api/v1/attendance/clock-out", new { idempotencyKey = Key(), evidence = new { qr = code } }));
        // Used once → the same code again is "duplicate" (red); the kiosk already shows a new one.
        var me = await Ok(await writer.GetAsync("/api/v1/attendance/me"));
        var shiftId = ok.GetProperty("shift").GetProperty("id").GetGuid();
        me.GetProperty("recentShifts").EnumerateArray().First(s => s.GetProperty("id").GetGuid() == shiftId).GetProperty("trust").GetString().Should().Be("red");
        (await Ok(await admin.GetAsync("/api/v1/attendance/kiosk/code"))).GetProperty("code").GetString().Should().NotBe(code);

        var parts = code.Split('.');
        var forged = $"k1.{parts[1]}.{int.Parse(parts[2]) + 5}.0000000000000000";
        var editor = await As("editor");
        var bad = await Ok(await editor.PostAsJsonAsync("/api/v1/attendance/clock-in", new { idempotencyKey = Key(), evidence = new { qr = forged } }));
        bad.GetProperty("shift").GetProperty("trust").GetString().Should().Be("red");
        await Ok(await editor.PostAsJsonAsync("/api/v1/attendance/clock-out", new { idempotencyKey = Key() }));
    }

    [Fact]
    public async Task One_timer_inside_a_shift_and_clock_out_stops_it()
    {
        var admin = await As("admin");
        var writer = await As("writer");
        var board = await Ok(await admin.GetAsync("/api/v1/work/board"));
        var users = await Ok(await admin.GetAsync("/api/v1/users?limit=100"));
        var maryam = users.GetProperty("items").EnumerateArray().First(u => u.GetProperty("displayName").GetString()!.StartsWith("مریم")).GetProperty("id").GetGuid();
        var mine = board.EnumerateArray().Where(c => c.TryGetProperty("assigneeId", out var a) && a.ValueKind == JsonValueKind.String && a.GetGuid() == maryam
                                                     && c.GetProperty("status").GetString() != "done").Take(2).ToList();
        mine.Should().HaveCount(2);
        var (a1, a2) = (mine[0].GetProperty("id").GetGuid(), mine[1].GetProperty("id").GetGuid());

        (await writer.PostAsync($"/api/v1/attendance/timer/{a1}", null)).StatusCode.Should().Be(HttpStatusCode.Conflict); // not clocked in
        await Ok(await writer.PostAsJsonAsync("/api/v1/attendance/clock-in", new { idempotencyKey = Key(), evidence = Office }));
        (await Ok(await writer.PostAsync($"/api/v1/attendance/timer/{a1}", null))).GetProperty("timer").GetProperty("workItemId").GetGuid().Should().Be(a1);
        (await Ok(await writer.PostAsync($"/api/v1/attendance/timer/{a2}", null))).GetProperty("timer").GetProperty("workItemId").GetGuid().Should().Be(a2);
        var notMine = board.EnumerateArray().First(c => !(c.TryGetProperty("assigneeId", out var a) && a.ValueKind == JsonValueKind.String && a.GetGuid() == maryam)).GetProperty("id").GetGuid();
        (await writer.PostAsync($"/api/v1/attendance/timer/{notMine}", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using (var scope = _f.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.WorkTimers.CountAsync(t => t.UserId == maryam && t.StoppedAt == null)).Should().Be(1);
        }
        var after = await Ok(await writer.PostAsJsonAsync("/api/v1/attendance/clock-out", new { idempotencyKey = Key(), evidence = Office }));
        after.GetProperty("timer").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Corrections_need_another_manager_and_evidence_is_append_only()
    {
        var admin = await As("admin");
        var editor = await As("editor");
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));
        var at = new DateTimeOffset(day.ToDateTime(new TimeOnly(5, 30)), TimeSpan.Zero);
        var c = await Ok(await editor.PostAsJsonAsync("/api/v1/attendance/corrections", new { day, clockIn = at, clockOut = at.AddHours(8), reason = "اینترنت قطع بود، ساعت ۹ آمدم" }));
        var id = c.GetProperty("id").GetGuid();
        (await editor.PostAsJsonAsync($"/api/v1/attendance/corrections/{id}/decide", new { approve = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var decided = await Ok(await admin.PostAsJsonAsync($"/api/v1/attendance/corrections/{id}/decide", new { approve = true, note = "تأیید" }));
        decided.GetProperty("status").GetString().Should().Be("approved");
        (await admin.PostAsJsonAsync($"/api/v1/attendance/corrections/{id}/decide", new { approve = false })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var shifts = await Ok(await editor.GetAsync($"/api/v1/attendance/shifts?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}"));
        shifts.EnumerateArray().Should().Contain(s => s.GetProperty("workedMinutes").GetInt32() == 480);

        await using var scope = _f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var act = () => db.Database.ExecuteSqlRawAsync("UPDATE attendance_events SET \"Note\" = 'x'");
        await act.Should().ThrowAsync<Exception>().WithMessage("*append-only*");
    }
}
