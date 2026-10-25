using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Npgsql;

namespace Karnama.IntegrationTests;

public sealed class RolesTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;
    private readonly PostgresFixture pg = app.Pg;

    [Fact]
    public async Task Requires_roles_manage_permission()
    {
        (await _f.CreateClient().GetAsync("/api/v1/roles")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var editor = _f.CreateClient().WithToken(await _f.DemoTokenAsync("editor"));
        (await editor.GetAsync("/api/v1/roles")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await editor.GetAsync("/api/v1/permissions")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Full_crud_with_soft_delete_restore_and_concurrency()
    {
        var admin = _f.CreateClient().WithToken(await _f.DemoTokenAsync("admin"));

        var created = await admin.PostAsJsonAsync("/api/v1/roles", new
        {
            name = new Dictionary<string, string> { ["fa"] = "گرافیست", ["en"] = "Designer" },
            description = "طراحی پست",
            permissions = new[] { new { code = "work.view", scope = "assigned" }, new { code = "work.run", scope = "assigned" } },
        }, Http.Json);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var role = await created.JsonAsync();
        var id = role.GetProperty("id").GetGuid();
        var version = role.GetProperty("version").GetUInt32();

        var updated = await admin.PutAsJsonAsync($"/api/v1/roles/{id}", new
        {
            name = new Dictionary<string, string> { ["fa"] = "گرافیست ارشد" },
            permissions = new[] { new { code = "work.view", scope = "all" } },
            version,
        }, Http.Json);
        updated.StatusCode.Should().Be(HttpStatusCode.OK);

        var stale = await admin.PutAsJsonAsync($"/api/v1/roles/{id}", new
        {
            name = new Dictionary<string, string> { ["fa"] = "قدیمی" },
            permissions = Array.Empty<object>(),
            version,
        }, Http.Json);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await admin.DeleteAsync($"/api/v1/roles/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var list = await (await admin.GetAsync("/api/v1/roles?limit=100")).JsonAsync();
        list.GetProperty("items").EnumerateArray().Should().NotContain(r => r.GetProperty("id").GetGuid() == id);

        var restored = await admin.PostAsync($"/api/v1/roles/{id}/restore", null);
        restored.StatusCode.Should().Be(HttpStatusCode.OK);
        (await restored.JsonAsync()).GetProperty("isDeleted").GetBoolean().Should().BeFalse();

        var audit = await CountAuditAsync(id.ToString());
        audit.Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task Rejects_invalid_scope_unknown_permission_and_empty_name_with_persian_messages()
    {
        var admin = _f.CreateClient().WithToken(await _f.DemoTokenAsync("admin"));
        var bad = await admin.PostAsJsonAsync("/api/v1/roles", new
        {
            name = new Dictionary<string, string> { ["fa"] = "x" },
            permissions = new[] { new { code = "settings.manage", scope = "own" } },
        }, Http.Json);
        bad.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await bad.JsonAsync()).GetProperty("title").GetString().Should().Contain("مجاز نیست");

        var empty = await admin.PostAsJsonAsync("/api/v1/roles", new { name = new Dictionary<string, string>(), permissions = Array.Empty<object>() }, Http.Json);
        empty.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await empty.JsonAsync()).GetProperty("title").GetString().Should().Be("نام نقش الزامی است.");
    }

    [Fact]
    public async Task System_roles_cannot_be_deleted()
    {
        var admin = _f.CreateClient().WithToken(await _f.DemoTokenAsync("admin"));
        var list = await (await admin.GetAsync("/api/v1/roles?limit=100")).JsonAsync();
        var owner = list.GetProperty("items").EnumerateArray().First(r => r.GetProperty("systemKey").GetString() == "owner");
        (await admin.DeleteAsync($"/api/v1/roles/{owner.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Keyset_pagination_walks_all_roles_once()
    {
        var admin = _f.CreateClient().WithToken(await _f.DemoTokenAsync("admin"));
        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await (await admin.GetAsync($"/api/v1/roles?limit=2&cursor={cursor}")).JsonAsync();
            seen.AddRange(page.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()));
            cursor = page.GetProperty("nextCursor").ValueKind == System.Text.Json.JsonValueKind.Null ? null : page.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        seen.Should().OnlyHaveUniqueItems().And.HaveCountGreaterThanOrEqualTo(6);
    }

    [Fact]
    public async Task Audit_log_is_append_only()
    {
        await using var conn = new NpgsqlConnection(pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM audit_logs", conn);
        var act = () => cmd.ExecuteNonQueryAsync();
        await act.Should().ThrowAsync<PostgresException>().Where(e => e.MessageText.Contains("append-only"));
    }

    private async Task<long> CountAuditAsync(string entityId)
    {
        await using var conn = new NpgsqlConnection(pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM audit_logs WHERE \"EntityId\" = @id", conn);
        cmd.Parameters.AddWithValue("id", entityId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
