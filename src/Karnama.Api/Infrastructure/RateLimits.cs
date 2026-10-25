using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Karnama.Api.Infrastructure;

internal static class RateLimits
{
    public const string Otp = "otp";
    public const string Auth = "auth";

    public static IServiceCollection AddKarnamaRateLimits(this IServiceCollection services, IConfiguration config) => services.AddRateLimiter(o =>
    {
        var otpPerIp = config.GetValue("RateLimits:OtpPerIpPer10Minutes", 10);
        var authPerIp = config.GetValue("RateLimits:AuthPerIpPerMinute", 30);
        var userTokens = config.GetValue("RateLimits:UserBurst", 60);

        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.OnRejected = async (ctx, ct) =>
        {
            if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            ctx.HttpContext.Response.ContentType = "application/problem+json";
            await ctx.HttpContext.Response.WriteAsJsonAsync(new
            {
                status = 429,
                title = "درخواست‌ها زیاد بود. کمی صبر کنید و دوباره تلاش کنید.",
                code = "errors.rateLimited",
            }, ct);
        };

        // Per IP: OTP sends (per-phone limits are enforced in the database by AuthService).
        o.AddPolicy(Otp, http => RateLimitPartition.GetFixedWindowLimiter(IpOf(http),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = otpPerIp, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));

        o.AddPolicy(Auth, http => RateLimitPartition.GetFixedWindowLimiter(IpOf(http),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerIp, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

        // Everyone: token bucket per user (or per IP when anonymous) so one broken client can't take the server down.
        o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        {
            var key = http.User.Identity?.IsAuthenticated == true ? "u:" + http.User.FindFirst("sub")?.Value : "ip:" + IpOf(http);
            return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = userTokens,
                TokensPerPeriod = Math.Max(1, userTokens / 2),
                ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
        });
    });

    private static string IpOf(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
