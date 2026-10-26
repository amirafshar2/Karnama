using Karnama.Api.Infrastructure;
using Karnama.Application.Auth;
using Karnama.Application.Qa;

namespace Karnama.Api.Endpoints;

public sealed record QaLoginRequest(string Phone);

/// <summary>
/// QA tools (docs/PROMPT3.md Q0). Mapped only when Qa:Enabled — which StartupGuard allows in Development only — and only
/// from the loopback address, so even a mis-set test server can't expose them to the network.
/// </summary>
internal static class QaEndpoints
{
    public static void MapQa(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/qa").WithTags("QA").AllowAnonymous().DisableRateLimiting()
            .AddEndpointFilter(async (ctx, next) =>
                System.Net.IPAddress.IsLoopback(ctx.HttpContext.Connection.RemoteIpAddress ?? System.Net.IPAddress.None) ? await next(ctx) : Results.NotFound());
        g.MapGet("/clock", (QaService qa) => Results.Ok(qa.Clock()));
        g.MapPost("/clock", (QaClockRequest body, QaService qa) => Results.Ok(qa.MoveClock(body)));
        g.MapPost("/login-as", async (QaLoginRequest body, AuthService auth, HttpContext http, CancellationToken ct) =>
            (await auth.QaLoginAsync(body.Phone, AuthEndpoints.ClientOf(http), ct)).ToHttp(http, s => Results.Ok(AuthEndpoints.SessionBody(http, s))));
        g.MapGet("/jobs", () => Results.Ok(QaService.Jobs));
        g.MapPost("/jobs/{job}", async (string job, QaService qa, CancellationToken ct) => await qa.RunJobAsync(job, ct) is { } r ? Results.Ok(r) : Results.NotFound());
        g.MapPost("/seed-crowd", async (Karnama.Application.Abstractions.IQaSeeder seeder, CancellationToken ct) => Results.Ok(await seeder.SeedAsync(ct)));
        g.MapGet("/invariants", async (QaService qa, CancellationToken ct) => Results.Ok(await qa.InvariantsAsync(ct)));
    }
}
