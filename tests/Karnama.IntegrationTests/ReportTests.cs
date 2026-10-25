using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Karnama.Domain.Work;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Karnama.IntegrationTests;

/// <summary>N8: period work report and scores (PROMPT2 §9, §11, §15).</summary>
public sealed class ReportTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;
    private async Task<HttpClient> As(string role) => _f.CreateClient().WithToken(await _f.DemoTokenAsync(role));
    private static readonly string Range = $"from={DateTime.UtcNow.AddDays(-40):yyyy-MM-dd}&to={DateTime.UtcNow:yyyy-MM-dd}";

    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
        return r.StatusCode == HttpStatusCode.NoContent || r.Content.Headers.ContentLength == 0 ? default : await r.JsonAsync();
    }

    private async Task<T> Db<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = _f.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task Workers_see_only_themselves_managers_see_the_team()
    {
        var editorId = await Db(db => db.Users.Where(u => u.Phone == "09000000002").Select(u => u.Id).FirstAsync());
        var adminTeam = await Ok(await (await As("admin")).GetAsync($"/api/v1/reports/work?{Range}"));
        adminTeam.GetProperty("rows").GetArrayLength().Should().BeGreaterThan(3);
        var ali = adminTeam.GetProperty("rows").EnumerateArray().First(r => r.GetProperty("userId").GetGuid() == editorId);
        ali.GetProperty("doneCount").GetInt32().Should().BeGreaterThan(0);
        ali.GetProperty("presenceMinutes").GetInt32().Should().BeGreaterThan(0);
        ali.GetProperty("usefulPercent").GetDouble().Should().BeGreaterThan(0);
        adminTeam.GetProperty("days").GetArrayLength().Should().BeGreaterThan(30);

        var writer = await As("writer");
        var own = await Ok(await writer.GetAsync($"/api/v1/reports/work?{Range}&user={editorId}"));
        own.GetProperty("rows").EnumerateArray().Should().ContainSingle().Which.GetProperty("userId").GetGuid().Should().NotBe(editorId);
        (await writer.GetAsync($"/api/v1/reports/work/{editorId}?{Range}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await writer.GetAsync($"/api/v1/reports/work/export?{Range}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var xlsx = await (await As("admin")).GetAsync($"/api/v1/reports/work/export?{Range}");
        xlsx.StatusCode.Should().Be(HttpStatusCode.OK);
        (await xlsx.Content.ReadAsByteArrayAsync()).Take(2).Should().Equal((byte)'P', (byte)'K');
    }

    [Fact]
    public async Task On_time_counts_the_workers_first_delivery_not_the_clients_wait()
    {
        var editorId = await Db(db => db.Users.Where(u => u.Phone == "09000000002").Select(u => u.Id).FirstAsync());
        // A finished item: delivered at 10:00, the client approved at 14:00, the deadline was 12:00 → on time for the worker.
        var item = await Db(db => db.WorkItems.Where(w => w.AssigneeId == editorId && w.Status == WorkStatus.Done && w.FirstSubmittedAt != null).OrderBy(w => w.DoneAt).FirstAsync());
        var deadline = item.FirstSubmittedAt!.Value.AddMinutes(30);
        await Db(db => db.WorkItems.Where(w => w.Id == item.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.Deadline, deadline).SetProperty(w => w.DoneAt, deadline.AddHours(3))));
        var details = await Ok(await (await As("admin")).GetAsync($"/api/v1/reports/work/{editorId}?{Range}"));
        var line = details.GetProperty("done").EnumerateArray().First(d => d.GetProperty("id").GetGuid() == item.Id);
        line.GetProperty("onTime").GetBoolean().Should().BeTrue();
        details.GetProperty("shifts").GetArrayLength().Should().BeGreaterThan(0);
        details.GetProperty("timers").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Scores_explain_themselves_need_enough_samples_and_ranking_follows_the_setting()
    {
        var admin = await As("admin");
        var all = await Ok(await admin.GetAsync($"/api/v1/reports/scores?{Range}"));
        var people = all.GetProperty("people").EnumerateArray().ToList();
        people.Should().Contain(p => p.GetProperty("enough").GetBoolean() && p.GetProperty("total").ValueKind == JsonValueKind.Number);
        var scored = people.First(p => p.GetProperty("enough").GetBoolean());
        scored.GetProperty("indicators").EnumerateArray().Select(i => i.GetProperty("key").GetString()).Should().Contain(["quality", "speed", "onTime", "volume", "discipline"]);
        scored.GetProperty("indicators").EnumerateArray().Should().OnlyContain(i => i.GetProperty("hint").GetString()!.Length > 0);
        people.Where(p => !p.GetProperty("enough").GetBoolean()).Should().OnlyContain(p => p.GetProperty("total").ValueKind == JsonValueKind.Null);

        // Workers: only themselves until the ranking is opened to everyone (then others without the details).
        var editor = await As("editor");
        (await Ok(await editor.GetAsync($"/api/v1/reports/scores?{Range}"))).GetProperty("people").GetArrayLength().Should().Be(1);
        (await editor.PutAsJsonAsync("/api/v1/reports/scores/settings", new { weights = new { quality = 1 }, minSamples = 5, rankingForAll = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PutAsJsonAsync("/api/v1/reports/scores/settings", new { weights = new { quality = 0, speed = 0, onTime = 0, volume = 0, discipline = 0, manager = 0, client = 0 }, minSamples = 5, rankingForAll = true }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await Ok(await admin.PutAsJsonAsync("/api/v1/reports/scores/settings", new { weights = new { quality = 30, speed = 10, onTime = 30, volume = 10, discipline = 10, manager = 5, client = 5 }, minSamples = 3, rankingForAll = true }));
        (await Db(db => db.AuditLogs.CountAsync(a => a.Action == "scores.settingsChanged"))).Should().BeGreaterThan(0);
        var ranking = await Ok(await editor.GetAsync($"/api/v1/reports/scores?{Range}"));
        var rows = ranking.GetProperty("people").EnumerateArray().ToList();
        rows.Count.Should().BeGreaterThan(1);
        var editorId = await Db(db => db.Users.Where(u => u.Phone == "09000000002").Select(u => u.Id).FirstAsync());
        rows.Where(r => r.GetProperty("userId").GetGuid() != editorId).Should().OnlyContain(r => r.GetProperty("indicators").GetArrayLength() == 0);

        (await Ok(await editor.GetAsync($"/api/v1/reports/scores/{editorId}/trend"))).GetArrayLength().Should().Be(6);
        var adminId = await Db(db => db.Users.Where(u => u.Phone == "09000000001").Select(u => u.Id).FirstAsync());
        (await editor.GetAsync($"/api/v1/reports/scores/{adminId}/trend")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Stars_only_from_reviewers_and_only_on_finished_work()
    {
        var done = await Db(db => db.WorkItems.Where(w => w.Status == WorkStatus.Done).Select(w => w.Id).FirstAsync());
        var open = await Db(db => db.WorkItems.Where(w => w.Status == WorkStatus.InProgress).Select(w => w.Id).FirstAsync());
        (await (await As("editor")).PostAsJsonAsync($"/api/v1/work/{done}/rating", new { stars = 5 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var admin = await As("admin");
        (await admin.PostAsJsonAsync($"/api/v1/work/{open}/rating", new { stars = 4 })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsJsonAsync($"/api/v1/work/{done}/rating", new { stars = 9 })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await Ok(await admin.PostAsJsonAsync($"/api/v1/work/{done}/rating", new { stars = 4 }));
        // Nobody rates their own work (the stars feed their score).
        var adminId = await Db(db => db.Users.Where(u => u.Phone == "09000000001").Select(u => u.Id).FirstAsync());
        await Db(db => db.WorkItems.Where(w => w.Id == done).ExecuteUpdateAsync(x => x.SetProperty(w => w.AssigneeId, adminId)));
        (await admin.PostAsJsonAsync($"/api/v1/work/{done}/rating", new { stars = 5 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Db(db => db.WorkItems.Where(w => w.Id == done).Select(w => w.ManagerStars).FirstAsync())).Should().Be(4);
    }
}
