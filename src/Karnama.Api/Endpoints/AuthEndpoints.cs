using Karnama.Api.Infrastructure;
using Karnama.Application.Auth;
using Karnama.Domain.Identity;

namespace Karnama.Api.Endpoints;

internal static class AuthEndpoints
{
    public const string RefreshCookie = "karnama_rt";
    public const string CookiePath = "/api/v1/auth";

    public static void MapAuth(this IEndpointRouteBuilder app, bool demoLoginEnabled)
    {
        var g = app.MapGroup("/api/v1/auth").WithTags("Auth").AddEndpointFilter<ValidationFilter>();

        g.MapPost("/otp/request", async (RequestOtpRequest body, AuthService auth, HttpContext http, CancellationToken ct) =>
            (await auth.RequestOtpAsync(body, ct)).ToHttp(http, r => Results.Accepted(value: r)))
            .RequireRateLimiting(RateLimits.Otp);

        g.MapPost("/otp/verify", async (VerifyOtpRequest body, AuthService auth, HttpContext http, CancellationToken ct) =>
            (await auth.VerifyOtpAsync(body, ClientOf(http), ct)).ToHttp(http, r => LoginResponse(http, r)))
            .RequireRateLimiting(RateLimits.Auth);

        g.MapPost("/2fa/setup", async (TwoFactorSetupRequest body, AuthService auth, HttpContext http, CancellationToken ct) =>
            (await auth.BeginTwoFactorSetupAsync(body, ct)).ToHttp(http))
            .RequireRateLimiting(RateLimits.Auth);

        g.MapPost("/2fa/verify", async (TwoFactorVerifyRequest body, AuthService auth, HttpContext http, CancellationToken ct) =>
            (await auth.VerifyTwoFactorAsync(body, ct)).ToHttp(http, r => LoginResponse(http, r)))
            .RequireRateLimiting(RateLimits.Auth);

        g.MapPost("/refresh", async (AuthService auth, HttpContext http, CancellationToken ct) =>
        {
            var client = ClientOf(http);
            var raw = client == ClientKind.Android ? http.Request.Headers["X-Refresh-Token"].ToString() : http.Request.Cookies[RefreshCookie];
            var result = await auth.RefreshAsync(raw, client, ct);
            // Clear only a cookie this request actually sent: a page that started with no session (and is answered 401)
            // must not wipe the cookie another tab has just received by signing in.
            if (result.IsFailure && !string.IsNullOrEmpty(raw)) ClearCookie(http);
            return result.ToHttp(http, s => Results.Ok(SessionBody(http, s)));
        }).RequireRateLimiting(RateLimits.Refresh);

        g.MapPost("/logout", async (AuthService auth, HttpContext http, CancellationToken ct) =>
        {
            var raw = ClientOf(http) == ClientKind.Android ? http.Request.Headers["X-Refresh-Token"].ToString() : http.Request.Cookies[RefreshCookie];
            await auth.LogoutAsync(raw, ct);
            ClearCookie(http);
            return Results.NoContent();
        });

        // Demo login exists only when the flag is on (otherwise 404, the route is never registered).
        if (demoLoginEnabled)
        {
            g.MapGet("/demo/options", async (AuthService auth, CancellationToken ct) => Results.Ok(await auth.GetDemoAccountsAsync(ct)));
            g.MapPost("/demo", async (DemoLoginRequest body, AuthService auth, HttpContext http, CancellationToken ct) =>
                (await auth.DemoLoginAsync(body, ClientOf(http), ct)).ToHttp(http, s => Results.Ok(SessionBody(http, s))))
                .RequireRateLimiting(RateLimits.Auth);
        }
    }

    public static ClientKind ClientOf(HttpContext http) =>
        string.Equals(http.Request.Headers["X-Client"], "android", StringComparison.OrdinalIgnoreCase) ? ClientKind.Android : ClientKind.Web;

    private static IResult LoginResponse(HttpContext http, LoginResult r) => r.Step switch
    {
        LoginStep.Completed => Results.Ok(new { step = "completed", session = SessionBody(http, r.Tokens!) }),
        LoginStep.TwoFactorRequired => Results.Ok(new { step = "twoFactorRequired", twoFactorToken = r.TwoFactorToken }),
        _ => Results.Ok(new { step = "twoFactorSetupRequired", twoFactorToken = r.TwoFactorToken }),
    };

    /// <summary>Web: refresh token only in an HttpOnly cookie. Android: in the body, to be kept in the Keystore.</summary>
    internal static object SessionBody(HttpContext http, SessionTokens s)
    {
        if (s.Client == ClientKind.Android)
            return new { accessToken = s.AccessToken, expiresAt = s.AccessTokenExpiresAt, refreshToken = s.RefreshToken, refreshExpiresAt = s.RefreshTokenExpiresAt };

        http.Response.Cookies.Append(RefreshCookie, s.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            Expires = s.RefreshTokenExpiresAt,
            IsEssential = true,
        });
        return new { accessToken = s.AccessToken, expiresAt = s.AccessTokenExpiresAt };
    }

    private static void ClearCookie(HttpContext http) =>
        http.Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = CookiePath, Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict });
}
