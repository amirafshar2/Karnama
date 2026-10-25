using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Karnama.IntegrationTests;

/// <summary>Client portal: own data only, approvals, ordering, and nothing internal leaks.</summary>
public sealed class PortalTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;
    private async Task<HttpClient> As(string role) => _f.CreateClient().WithToken(await _f.DemoTokenAsync(role));

    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
        return await r.JsonAsync();
    }

    [Fact]
    public async Task Client_sees_only_own_orders_and_no_staff_endpoints()
    {
        var client = await As("client");   // the demo café
        var home = await Ok(await client.GetAsync("/api/v1/portal/home"));
        home.GetProperty("clientName").GetString().Should().Be("کافه نمونه");
        home.GetProperty("package").GetProperty("quotas").GetArrayLength().Should().BeGreaterThan(0);
        var orders = home.GetProperty("orders").EnumerateArray().ToList();
        orders.Should().NotBeEmpty();

        var all = await Ok(await client.GetAsync("/api/v1/orders?limit=100"));
        all.GetProperty("items").EnumerateArray().Should().OnlyContain(o => o.GetProperty("clientName").GetString() == "کافه نمونه");

        // Another client's order: not found. Staff endpoints: forbidden.
        var admin = await As("admin");
        var other = (await Ok(await admin.GetAsync("/api/v1/orders?limit=100"))).GetProperty("items").EnumerateArray()
            .First(o => o.GetProperty("clientName").GetString() != "کافه نمونه").GetProperty("id").GetGuid();
        (await client.GetAsync($"/api/v1/orders/{other}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/portal/orders/{other}/messages")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/v1/work/board")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/v1/clients")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/v1/attendance/live")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Order detail hides who works on it and the price.
        var detail = await Ok(await client.GetAsync($"/api/v1/orders/{orders[0].GetProperty("id").GetGuid()}"));
        detail.GetProperty("stages").EnumerateArray().Should().OnlyContain(s => s.GetProperty("assigneeName").ValueKind == JsonValueKind.Null);
        detail.GetProperty("price").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Client_approves_or_asks_for_changes_and_only_sees_shared_files_and_client_messages()
    {
        var client = await As("client");
        var admin = await As("admin");
        var home = await Ok(await client.GetAsync("/api/v1/portal/home"));
        var approval = home.GetProperty("approvals").EnumerateArray().First();   // demo: the café's weekend post
        var itemId = approval.GetProperty("workItemId").GetGuid();
        var orderId = approval.GetProperty("orderId").GetGuid();

        // Staff: an internal note and a client message.
        await Ok(await admin.PostAsJsonAsync($"/api/v1/work/{itemId}/comments", new { body = "یادداشت داخلی", visibility = "internal" }));
        await Ok(await admin.PostAsJsonAsync($"/api/v1/work/{itemId}/comments", new { body = "طرح آماده‌ی تأیید شماست", visibility = "client" }));
        var msgs = await Ok(await client.GetAsync($"/api/v1/portal/orders/{orderId}/messages"));
        msgs.EnumerateArray().Select(m => m.GetProperty("body").GetString()).Should().Contain("طرح آماده‌ی تأیید شماست").And.NotContain("یادداشت داخلی");
        msgs.EnumerateArray().Should().OnlyContain(m => m.GetProperty("author").GetString() == "آژانس" || m.GetProperty("fromMe").GetBoolean());

        (await client.PostAsJsonAsync($"/api/v1/portal/approvals/{itemId}", new { approve = false })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await client.PostAsJsonAsync($"/api/v1/portal/approvals/{itemId}", new { approve = false, note = "رنگ قرمز کمتر" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var card = (await Ok(await admin.GetAsync($"/api/v1/work/{itemId}"))).GetProperty("card");
        card.GetProperty("status").GetString().Should().Be("changesRequested");
        (await client.PostAsJsonAsync($"/api/v1/portal/approvals/{itemId}", new { approve = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden); // not in review anymore

        // Back to review → approve → the order is complete.
        await Ok(await admin.PostAsJsonAsync($"/api/v1/work/{itemId}/move", new { status = "clientReview", version = card.GetProperty("version").GetUInt32() }));
        (await client.PostAsJsonAsync($"/api/v1/portal/approvals/{itemId}", new { approve = true })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Ok(await client.GetAsync($"/api/v1/orders/{orderId}"))).GetProperty("status").GetString().Should().Be("completed");
    }

    [Fact]
    public async Task Client_orders_a_service_for_itself_only()
    {
        var client = await As("client");
        var admin = await As("admin");
        var services = await Ok(await client.GetAsync("/api/v1/services/options"));
        var post = services.EnumerateArray().First(s => s.GetProperty("name").GetString() == "پست تکی").GetProperty("id").GetGuid();
        var other = (await Ok(await admin.GetAsync("/api/v1/clients?limit=10"))).GetProperty("items").EnumerateArray()
            .First(c => c.GetProperty("businessName").GetString() != "کافه نمونه").GetProperty("id").GetGuid();
        var o = await Ok(await client.PostAsJsonAsync("/api/v1/portal/orders", new
        {
            clientId = other, serviceTypeId = post, title = "پست تخفیف", answers = new Dictionary<string, string> { ["topic"] = "تخفیف یلدا" },
            priority = "urgent", autoAccept = true,
        }));
        o.GetProperty("clientName").GetString().Should().Be("کافه نمونه");   // the body's clientId is ignored
        o.GetProperty("status").GetString().Should().Be("submitted");          // clients can't auto-accept
        o.GetProperty("priority").GetString().Should().Be("normal");
        (await client.PostAsJsonAsync("/api/v1/orders", new { clientId = other, serviceTypeId = post, answers = new { topic = "x" } })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Staff_files_reach_the_client_only_when_shared()
    {
        var client = await As("client");
        var admin = await As("admin");
        var orderId = (await Ok(await client.GetAsync("/api/v1/portal/home"))).GetProperty("orders")[0].GetProperty("id").GetGuid();
        var png = new byte[200];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        var start = await Ok(await admin.PostAsJsonAsync("/api/v1/files", new { orderId, purpose = "brief", fileName = "brief.png", size = png.Length }));
        var id = start.GetProperty("id").GetGuid();
        var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/files/{id}/upload") { Content = new ByteArrayContent(png) };
        req.Headers.Add("Upload-Offset", "0");
        await Ok(await admin.SendAsync(req));

        (await Ok(await client.GetAsync($"/api/v1/orders/{orderId}"))).GetProperty("files").EnumerateArray().Should().NotContain(f => f.GetProperty("id").GetGuid() == id);
        (await client.GetAsync($"/api/v1/files/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await Ok(await admin.PostAsync($"/api/v1/files/{id}/share?visible=true", null));
        (await Ok(await client.GetAsync($"/api/v1/orders/{orderId}"))).GetProperty("files").EnumerateArray().Should().Contain(f => f.GetProperty("id").GetGuid() == id);
        (await client.GetAsync($"/api/v1/files/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
