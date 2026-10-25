namespace Karnama.Api.Infrastructure;

internal static class SecurityHeaders
{
    /// <summary>The API only serves JSON, so the CSP can deny everything.</summary>
    public static IApplicationBuilder UseKarnamaSecurityHeaders(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        var h = http.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
        await next();
    });
}
