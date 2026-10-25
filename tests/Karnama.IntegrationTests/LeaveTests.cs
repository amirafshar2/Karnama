using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Karnama.Application.Attendance;
using Microsoft.Extensions.DependencyInjection;

namespace Karnama.IntegrationTests;

/// <summary>M5: leave requests and approvals, absence detection → reason → decision, late conversion, balance, calendar, report.</summary>
public sealed class LeaveTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;
    private async Task<HttpClient> As(string role) => _f.CreateClient().WithToken(await _f.DemoTokenAsync(role));

    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
        return await r.JsonAsync();
    }

    private async Task<(int, int)> Detect()
    {
        await using var scope = _f.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LeaveService>().DetectAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Leave_request_approval_balance_and_absence_flow()
    {
        var local = Iran.Local(_f.Clock.GetUtcNow());
        if (local.Hour is < 2 or > 19) return;   // the test builds a schedule around "now"; skip near midnight in Tehran
        var admin = await As("admin");
        var writer = await As("writer");
        var editor = await As("editor");

        // Today is a working day that started 40 minutes ago (grace 10).
        var day = WorkScheduleDay(local);
        (await admin.PutAsJsonAsync("/api/v1/attendance/setup/schedules", new
        {
            userId = (Guid?)null, days = new[] { new { day, start = local.AddMinutes(-40).ToString("HH:mm:00"), end = local.AddHours(3).ToString("HH:mm:00") } },
            breakMinutes = 30, graceMinutes = 10, flexible = false, weeklyHours = 40,
        })).IsSuccessStatusCode.Should().BeTrue();

        var types = await Ok(await writer.GetAsync("/api/v1/leave/types"));
        var annual = types.EnumerateArray().First(t => t.GetProperty("kind").GetString() == "annual").GetProperty("id").GetGuid();
        var sick = types.EnumerateArray().First(t => t.GetProperty("kind").GetString() == "sick").GetProperty("id").GetGuid();

        // Writer asks for today off (hourly, covering now). Sick leave needs a document.
        var now = _f.Clock.GetUtcNow();
        (await writer.PostAsJsonAsync("/api/v1/leave/requests", new { leaveTypeId = sick, hourly = true, from = now.AddDays(5), to = now.AddDays(5).AddHours(2), reason = "د" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);   // reason too short
        var req = await Ok(await writer.PostAsJsonAsync("/api/v1/leave/requests", new { leaveTypeId = annual, hourly = true, from = now.AddHours(-1), to = now.AddHours(2), reason = "کار بانکی" }));
        req.GetProperty("status").GetString().Should().Be("pending");
        (await writer.PostAsJsonAsync("/api/v1/leave/requests", new { leaveTypeId = annual, hourly = true, from = now, to = now.AddHours(1), reason = "تکراری" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await writer.PostAsJsonAsync($"/api/v1/leave/requests/{req.GetProperty("id").GetGuid()}/decide", new { approve = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var decided = await Ok(await admin.PostAsJsonAsync($"/api/v1/leave/requests/{req.GetProperty("id").GetGuid()}/decide", new { approve = true }));
        decided.GetProperty("leave").GetProperty("status").GetString().Should().Be("approved");

        // Detection: the editor (no shift, no leave) is absent; the writer (on approved leave) is not. Running again creates nothing new.
        var (first, _) = await Detect();
        first.Should().BeGreaterThan(0);
        (await Detect()).Item1.Should().Be(0);
        var mine = await Ok(await writer.GetAsync("/api/v1/leave/absences"));
        mine.GetArrayLength().Should().Be(0);
        var editorAbs = (await Ok(await editor.GetAsync("/api/v1/leave/absences?open=true"))).EnumerateArray().Single();
        editorAbs.GetProperty("status").GetString().Should().Be("detected");

        // The editor writes the reason; can't decide it himself; the manager excuses it.
        var absId = editorAbs.GetProperty("id").GetGuid();
        (await Ok(await editor.PostAsJsonAsync($"/api/v1/leave/absences/{absId}/reason", new { reason = "ترافیک و تصادف در مسیر" })))
            .GetProperty("status").GetString().Should().Be("reasonSubmitted");
        (await editor.PostAsJsonAsync($"/api/v1/leave/absences/{absId}/decide", new { approve = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The editor shows up after all → the absence becomes lateness with the real minutes.
        await Ok(await editor.PostAsJsonAsync("/api/v1/attendance/clock-in", new { idempotencyKey = Guid.NewGuid().ToString("N") }));
        var late = (await Ok(await editor.GetAsync("/api/v1/leave/absences"))).EnumerateArray().Single();
        late.GetProperty("status").GetString().Should().Be("becameLate");
        late.GetProperty("lateMinutes").GetInt32().Should().BeInRange(39, 41);
        (await admin.PostAsJsonAsync($"/api/v1/leave/absences/{absId}/decide", new { approve = true })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await Ok(await editor.PostAsJsonAsync("/api/v1/attendance/clock-out", new { idempotencyKey = Guid.NewGuid().ToString("N") }));

        // Balance (computed, not stored), team calendar and payroll report.
        var bal = await Ok(await writer.GetAsync("/api/v1/leave/balance"));
        var annualBal = bal.EnumerateArray().First(b => b.GetProperty("leaveTypeId").GetGuid() == annual);
        annualBal.GetProperty("usedDays").GetDecimal().Should().BeGreaterThan(0);
        annualBal.GetProperty("leftDays").GetDecimal().Should().BeLessThan(26);
        var today = Iran.Today(now);
        var cal = await Ok(await admin.GetAsync($"/api/v1/leave/calendar?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}"));
        cal.EnumerateArray().Should().Contain(r => r.GetProperty("days")[0].GetProperty("state").GetString() == "hourlyLeave");
        var report = await Ok(await admin.GetAsync($"/api/v1/leave/report?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}"));
        report.EnumerateArray().Should().Contain(r => r.GetProperty("paidLeaveMinutes").GetInt32() == 180);
        (await admin.GetAsync($"/api/v1/leave/report/export?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}")).Content.Headers.ContentType!.MediaType.Should().Contain("spreadsheet");
        (await writer.GetAsync($"/api/v1/leave/report?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Nobody gave a reason in time (48 h) → unexcused automatically.
        // (Last step: the fake clock can't go back, and this class has its own app and database.)
        _f.Clock.Advance(TimeSpan.FromHours(49));
        var (_, expired) = await Detect();
        expired.Should().BeGreaterThan(0);
    }

    private static int WorkScheduleDay(DateTimeOffset local) => Karnama.Domain.Attendance.WorkSchedule.DayIndex(DateOnly.FromDateTime(local.DateTime));
}
