using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Karnama.Application.Attendance;
using Karnama.Domain.Attendance;
using Karnama.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Karnama.IntegrationTests;

/// <summary>N2: fingerprint/face devices — ADMS push, PIN mapping, file import (PROMPT2 §6, §15).</summary>
public sealed class DeviceTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
{
    private const string Serial = "DEMO0001";
    private readonly KarnamaFactory _f = app.Factory;
    private async Task<HttpClient> As(string role) => _f.CreateClient().WithToken(await _f.DemoTokenAsync(role));

    private async Task<T> Db<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = _f.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>A device call from a given address (the demo office network is 127.0.0.1).</summary>
    private async Task<(int Status, string Body)> Device(string method, string pathAndQuery, string? body = null, string ip = "127.0.0.1")
    {
        var ctx = await _f.Server.SendAsync(c =>
        {
            c.Request.Method = method;
            var q = pathAndQuery.IndexOf('?');
            c.Request.Path = q < 0 ? pathAndQuery : pathAndQuery[..q];
            c.Request.QueryString = q < 0 ? QueryString.Empty : new QueryString(pathAndQuery[q..]);
            c.Connection.RemoteIpAddress = IPAddress.Parse(ip);
            if (body is not null)
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                c.Request.Body = new MemoryStream(bytes);
                c.Request.ContentLength = bytes.Length;
                c.Request.ContentType = "text/plain";
            }
        });
        using var reader = new StreamReader(ctx.Response.Body);
        return (ctx.Response.StatusCode, await reader.ReadToEndAsync());
    }

    private static string Local(DateTimeOffset utc) => Iran.Local(utc).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>A demo person with no shift today, so the device decides in/out from scratch.</summary>
    private async Task<Guid> FreshPersonAsync(string pin)
    {
        var userId = await Db(db => db.DevicePins.Where(p => p.Pin == pin).Select(p => p.UserId).FirstAsync());
        await Db(async db =>
        {
            await db.WorkTimers.Where(t => t.UserId == userId).ExecuteDeleteAsync();
            await db.AttendanceEvents.Where(e => e.UserId == userId).ExecuteDeleteAsync();
            return await db.Shifts.Where(s => s.UserId == userId).ExecuteDeleteAsync();
        });
        return userId;
    }

    [Fact]
    public async Task Only_a_registered_device_from_the_office_is_answered_and_it_is_told_to_send_attendance_only()
    {
        (await Device("GET", "/iclock/cdata?SN=UNKNOWN99&options=all")).Status.Should().Be(403);
        (await Device("GET", $"/iclock/cdata?SN={Serial}&options=all", ip: "5.6.7.8")).Status.Should().Be(403);
        var hello = await Device("GET", $"/iclock/cdata?SN={Serial}&options=all");
        hello.Status.Should().Be(200);
        hello.Body.Should().Contain("ATTLOGStamp=").And.Contain("TransFlag=TransData AttLog").And.NotContain("FingerTmp");
        (await Device("GET", $"/iclock/getrequest?SN={Serial}")).Body.Should().Be("OK");
        (await Db(db => db.BiometricDevices.Where(d => d.Serial == Serial).Select(d => d.LastSeenAt).FirstAsync())).Should().NotBeNull();

        // Fingerprint templates / user lists are acknowledged and dropped; an oversized body is refused.
        var before = await Db(db => db.DevicePunches.CountAsync());
        (await Device("POST", $"/iclock/cdata?SN={Serial}&table=FINGERTMP", "PIN=1\tFID=0\tSize=1024\tValid=1\tTMP=AAAA")).Body.Should().Be("OK");
        (await Db(db => db.DevicePunches.CountAsync())).Should().Be(before);
        (await Device("POST", $"/iclock/cdata?SN={Serial}&table=ATTLOG", new string('x', DeviceService.MaxBodyBytes + 10))).Status.Should().Be(413);
    }

    [Fact]
    public async Task Punches_open_and_close_the_shift_once_even_when_the_device_resends()
    {
        var userId = await FreshPersonAsync("5");
        var now = _f.Clock.GetUtcNow();
        var inAt = now.AddMinutes(-40);
        var record = $"5\t{Local(inAt)}\t0\t1\t0\t0\n";
        (await Device("POST", $"/iclock/cdata?SN={Serial}&table=ATTLOG&Stamp=100", record)).Body.Should().Be("OK: 1");
        (await Device("POST", $"/iclock/cdata?SN={Serial}&table=ATTLOG&Stamp=100", record)).Body.Should().Be("OK: 1");   // re-sent
        var shift = await Db(db => db.Shifts.AsNoTracking().SingleAsync(s => s.UserId == userId));
        shift.Status.Should().Be(ShiftStatus.Open);
        shift.ClockInAt.Should().BeCloseTo(inAt, TimeSpan.FromSeconds(1));
        shift.Methods.Should().ContainSingle(m => m.Method == "BiometricDevice" && m.Passed);

        // A double tap a few seconds later is ignored; the next real punch closes the shift (toggle mode).
        await Device("POST", $"/iclock/cdata?SN={Serial}&table=ATTLOG&Stamp=101", $"5\t{Local(inAt.AddSeconds(20))}\t0\t1\n");
        await Device("POST", $"/iclock/cdata?SN={Serial}&table=ATTLOG&Stamp=102", $"5\t{Local(now.AddMinutes(-5))}\t0\t1\n");
        shift = await Db(db => db.Shifts.AsNoTracking().SingleAsync(s => s.UserId == userId));
        shift.Status.Should().Be(ShiftStatus.Closed);
        shift.ClockOutAt.Should().BeCloseTo(now.AddMinutes(-5), TimeSpan.FromSeconds(1));
        (await Db(db => db.DevicePunches.CountAsync(p => p.Pin == "5" && p.At == inAt.AddTicks(-(inAt.Ticks % TimeSpan.TicksPerSecond))))).Should().Be(1);
        (await Device("GET", $"/iclock/cdata?SN={Serial}&options=all")).Body.Should().Contain("ATTLOGStamp=102");
    }

    [Fact]
    public async Task An_unknown_pin_waits_until_it_is_mapped_and_a_wrong_device_clock_is_reported()
    {
        var admin = await As("admin");
        var now = _f.Clock.GetUtcNow();
        // 7 minutes ahead, arriving as it happens → clock skew measured and announced.
        await Device("POST", $"/iclock/cdata?SN={Serial}&table=ATTLOG", $"77\t{Local(now.AddMinutes(7))}\t0\t1\n");
        (await Db(db => db.BiometricDevices.Where(d => d.Serial == Serial).Select(d => d.ClockSkewSeconds).FirstAsync())).Should().BeInRange(400, 440);
        (await Db(db => db.Notifications.CountAsync(n => n.Kind == "devices.skew"))).Should().BeGreaterThan(0);
        var unknown = await (await admin.GetAsync("/api/v1/devices/unknown-pins")).JsonAsync();
        unknown.EnumerateArray().Should().Contain(u => u.GetProperty("pin").GetString() == "77");

        // A brand-new colleague gets PIN 77: the waiting record becomes their clock-in.
        var created = await (await admin.PostAsJsonAsync("/api/v1/users", new { displayName = "همکار دستگاه", phone = "09127770077", roleIds = Array.Empty<Guid>(), isActive = true })).JsonAsync();
        var newUser = created.GetProperty("id").GetGuid();
        (await admin.PostAsJsonAsync("/api/v1/devices/pins", new { pin = "0077", userId = newUser })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Db(db => db.Shifts.CountAsync(s => s.UserId == newUser && s.Status == ShiftStatus.Open))).Should().Be(1);
        (await admin.PostAsJsonAsync("/api/v1/devices/pins", new { pin = "77", userId = newUser })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var id = await Db(db => db.BiometricDevices.Where(d => d.Serial == Serial).Select(d => d.Id).FirstAsync());
        await (await admin.PostAsync($"/api/v1/devices/{id}/sync-time", null)).JsonAsync();
        (await Device("GET", $"/iclock/getrequest?SN={Serial}")).Body.Should().MatchRegex("C:\\d+:SET OPTION DateTime=\\d+").And.Contain("CHECK");
        (await Device("GET", $"/iclock/getrequest?SN={Serial}")).Body.Should().Be("OK");
        (await (await As("editor")).GetAsync("/api/v1/devices")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_file_from_the_attendance_software_becomes_shifts_for_past_days_once()
    {
        var admin = await As("admin");
        var userId = await FreshPersonAsync("4");
        var fileDevice = await Db(db => db.BiometricDevices.Where(d => d.Kind == DeviceKind.FileImport).Select(d => d.Id).FirstAsync());
        var pc = new PersianCalendar();
        var day = Iran.Local(_f.Clock.GetUtcNow()).DateTime.Date.AddDays(-3);
        var jalali = $"{pc.GetYear(day)}/{pc.GetMonth(day):00}/{pc.GetDayOfMonth(day):00}";
        var csv = $"کد پرسنلی,تاریخ,ساعت\n۴,{jalali},08:05\n4,{jalali},12:30\n4,{jalali},17:10\n99,{jalali},08:00\nxx,bad,\n";
        MultipartFormDataContent Form()
        {
            var f = new MultipartFormDataContent();
            var c = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
            c.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
            f.Add(c, "file", "export.csv");
            return f;
        }
        var preview = await (await admin.PostAsync("/api/v1/devices/import/preview", Form())).JsonAsync();
        preview.GetProperty("totalRows").GetInt32().Should().Be(5);
        var m = preview.GetProperty("suggested");
        m.GetProperty("pin").GetInt32().Should().Be(0);
        m.GetProperty("date").GetInt32().Should().Be(1);
        m.GetProperty("time").GetInt32().Should().Be(2);

        var r = await (await admin.PostAsync($"/api/v1/devices/{fileDevice}/import?pin=0&date=1&time=2&header=true", Form())).JsonAsync();
        r.GetProperty("added").GetInt32().Should().Be(4);
        r.GetProperty("invalid").GetInt32().Should().Be(1);
        r.GetProperty("unknownPins").GetInt32().Should().Be(1);
        r.GetProperty("shiftsCreated").GetInt32().Should().Be(1);
        var shift = await Db(db => db.Shifts.AsNoTracking().SingleAsync(s => s.UserId == userId));
        Iran.Local(shift.ClockInAt).ToString("HH:mm", CultureInfo.InvariantCulture).Should().Be("08:05");
        Iran.Local(shift.ClockOutAt!.Value).ToString("HH:mm", CultureInfo.InvariantCulture).Should().Be("17:10");

        var again = await (await admin.PostAsync($"/api/v1/devices/{fileDevice}/import?pin=0&date=1&time=2&header=true", Form())).JsonAsync();
        again.GetProperty("added").GetInt32().Should().Be(0);
        again.GetProperty("duplicates").GetInt32().Should().Be(4);
        (await Db(db => db.Shifts.CountAsync(s => s.UserId == userId))).Should().Be(1);
    }

    [Fact]
    public async Task A_record_far_from_now_is_dropped_so_it_cannot_lock_anyone_out()
    {
        var userId = await FreshPersonAsync("3");
        var now = _f.Clock.GetUtcNow();
        (await Device("POST", $"/iclock/cdata?SN={Serial}&table=ATTLOG", $"3\t{Local(now.AddYears(3))}\t0\t1\n3\t{Local(now.AddDays(-30))}\t0\t1\n")).Status.Should().Be(200);
        (await Db(db => db.DevicePunches.CountAsync(p => p.Pin == "3" && (p.At > now.AddHours(1) || p.At < now.AddDays(-8))))).Should().Be(0);
        (await Db(db => db.Shifts.CountAsync(s => s.UserId == userId))).Should().Be(0);
    }

    [Fact]
    public void Persian_and_gregorian_dates_and_zk_time()
    {
        DeviceService.ParseDate("۱۴۰۵/۰۸/۰۳ ۰۸:۰۱").Should().Be(Iran.At(new DateOnly(2026, 10, 25), new TimeOnly(8, 1)));
        DeviceService.ParseDate("2026-10-25 08:01:00").Should().Be(Iran.At(new DateOnly(2026, 10, 25), new TimeOnly(8, 1)));
        DeviceService.ParseDate("25/10/2026").Should().BeNull();
        DeviceService.ZkTime(new DateTime(2000, 1, 1, 0, 0, 1)).Should().Be(1);
        DeviceService.ZkTime(new DateTime(2000, 2, 1)).Should().Be(31 * 86400);
    }
}
