using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Karnama.Api.Infrastructure;

internal static class RateLimits
{
    public const string Otp = "otp";
    public const string Auth = "auth";
    public const string Refresh = "refresh";
    public const string PublicForm = "public-form";
    public const string Heavy = "heavy";
    public const string Device = "device";

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

        // Refresh tokens are 48 random bytes (not guessable); this cap only stops a broken client looping.
        // It is high on purpose: a whole office behind one NAT address reloads at 9 am.
        var refreshPerIp = config.GetValue("RateLimits:RefreshPerIpPerMinute", 300);
        o.AddPolicy(Refresh, http => RateLimitPartition.GetFixedWindowLimiter(IpOf(http),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = refreshPerIp, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

        // Website forms (consultation): a person sends one; a script gets stopped quickly.
        var formsPerIp = config.GetValue("RateLimits:FormsPerIpPer10Minutes", 5);
        o.AddPolicy(PublicForm, http => RateLimitPartition.GetFixedWindowLimiter(IpOf(http),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = formsPerIp, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));

        // Reports that read a whole team's month (scores, trend, Excel): plenty for a person, too few for a script.
        var heavyPerUser = config.GetValue("RateLimits:HeavyPerUserPerMinute", 20);
        o.AddPolicy(Heavy, http => RateLimitPartition.GetFixedWindowLimiter(
            http.User.Identity?.IsAuthenticated == true ? "u:" + http.User.FindFirst("sub")?.Value : "ip:" + IpOf(http),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = heavyPerUser, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

        // Attendance devices (/iclock): a device polls every few seconds; a scanner hammering the path gets cut off.
        var devicePerIp = config.GetValue("RateLimits:DevicePerIpPerMinute", 120);
        o.AddPolicy(Device, http => RateLimitPartition.GetFixedWindowLimiter(IpOf(http),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = devicePerIp, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

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

    /// <summary>IPv4 per address; IPv6 per /64 (one customer usually owns a whole /64).</summary>
    private static string IpOf(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress;
        if (ip is null) return "unknown";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return ip.ToString();
        var bytes = ip.GetAddressBytes();
        return Convert.ToHexString(bytes, 0, 8) + "::/64";
    }
}
