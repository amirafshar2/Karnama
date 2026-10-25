using Hangfire;
using Karnama.Application.Abstractions;
using Karnama.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Worker;

/// <summary>Housekeeping that must not slow the API. More jobs (absence detection, reminders, outbox) arrive in later milestones.</summary>
public sealed class MaintenanceJobs(IAppDbContext db, TimeProvider clock, ILogger<MaintenanceJobs> logger, IFileStore files,
    Microsoft.Extensions.Options.IOptions<Karnama.Application.Files.FileOptions> fileOptions)
{
    [AutomaticRetry(Attempts = 3)]
    public async Task PurgeStaleUploadsAsync(CancellationToken ct)
    {
        var n = await Karnama.Application.Files.FileService.PurgeStaleAsync(db, files, fileOptions.Value, clock.GetUtcNow(), ct);
        if (n > 0) logger.LogInformation("Deleted {Count} abandoned uploads", n);
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task PurgeExpiredAuthDataAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var otps = await db.OtpChallenges.Where(c => c.ExpiresAt < now.AddDays(-1)).ExecuteDeleteAsync(ct);
        var tokens = await db.RefreshTokens.Where(t => t.ExpiresAt < now.AddDays(-7)).ExecuteDeleteAsync(ct);
        logger.LogInformation("Purged {Otps} OTP challenges and {Tokens} refresh tokens", otps, tokens);
    }
}

/// <summary>Attendance housekeeping (docs/PROMPT.md §6.6): forgotten clock-outs are closed and flagged, never guessed.</summary>
public sealed class AttendanceJobs(Karnama.Application.Attendance.AttendanceAdminService attendance, Karnama.Application.Attendance.LeaveService leave, ILogger<AttendanceJobs> logger)
{
    /// <summary>Every 5 minutes (docs/PROMPT.md §6.7 scenario 2).</summary>
    [AutomaticRetry(Attempts = 1)]
    public async Task DetectAbsencesAsync(CancellationToken ct)
    {
        var (detected, expired) = await leave.DetectAsync(ct);
        if (detected + expired > 0) logger.LogInformation("Absences: {Detected} detected, {Expired} became unexcused", detected, expired);
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task CloseForgottenAsync(CancellationToken ct)
    {
        var n = await attendance.CloseForgottenAsync(ct);
        if (n > 0) logger.LogInformation("Closed {Count} forgotten shifts for review", n);
    }
}

/// <summary>Publishing reminders (M6) and package expiry notices (7/3/1 days).</summary>
public sealed class NotifyJobs(Karnama.Application.Publishing.PublishService publish, Karnama.Application.Notify.PackageExpiryJob expiry,
    Karnama.Application.Blog.BlogService blog, ILogger<NotifyJobs> logger)
{
    [AutomaticRetry(Attempts = 1)]
    public async Task BlogScheduleAsync(CancellationToken ct)
    {
        var n = await blog.PublishDueAsync(ct);
        if (n > 0) logger.LogInformation("Published {Count} scheduled posts", n);
    }

    [AutomaticRetry(Attempts = 1)]
    public async Task PublishTickAsync(CancellationToken ct)
    {
        var (due, overdue) = await publish.TickAsync(ct);
        if (due + overdue > 0) logger.LogInformation("Publish jobs: {Due} due, {Overdue} overdue", due, overdue);
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task PackageExpiryAsync(CancellationToken ct)
    {
        var n = await expiry.RunAsync(ct);
        if (n > 0) logger.LogInformation("Sent {Count} package expiry notices", n);
    }
}

internal sealed class NoRequestContext : IRequestContext
{
    public System.Net.IPAddress? RemoteIp => null;
}

internal sealed class JobRegistrar(IRecurringJobManager jobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        jobs.AddOrUpdate<MaintenanceJobs>("purge-auth", j => j.PurgeExpiredAuthDataAsync(CancellationToken.None), Cron.Hourly());
        jobs.AddOrUpdate<MaintenanceJobs>("purge-stale-uploads", j => j.PurgeStaleUploadsAsync(CancellationToken.None), Cron.Hourly());
        jobs.AddOrUpdate<AttendanceJobs>("detect-absences", j => j.DetectAbsencesAsync(CancellationToken.None), "*/5 * * * *");
        jobs.AddOrUpdate<AttendanceJobs>("close-forgotten-shifts", j => j.CloseForgottenAsync(CancellationToken.None), "*/30 * * * *");
        jobs.AddOrUpdate<NotifyJobs>("blog-schedule", j => j.BlogScheduleAsync(CancellationToken.None), Cron.Minutely());
        jobs.AddOrUpdate<NotifyJobs>("publish-tick", j => j.PublishTickAsync(CancellationToken.None), Cron.Minutely());
        jobs.AddOrUpdate<NotifyJobs>("package-expiry", j => j.PackageExpiryAsync(CancellationToken.None), "30 5 * * *", new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran") });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class SystemUser : ICurrentUser
{
    public bool IsAuthenticated => false;
    public Guid? UserId => null;
    public bool IsDemo => false;
    public UserKind? Kind => null;
    public string LanguageCode => "fa";
    public string? TraceId => System.Diagnostics.Activity.Current?.TraceId.ToString();
    public PermissionScope? ScopeOf(string permission) => null;
}

/// <summary>The output cache lives in the API process; it expires on its own (30s) for changes made here.</summary>
internal sealed class NoopPublicCache : IPublicCache
{
    public Task EvictAsync(CancellationToken ct) => Task.CompletedTask;
}

internal sealed class NoopRealtimeNotifier : IRealtimeNotifier
{
    public Task PermissionsChangedAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct) => Task.CompletedTask;
    public Task ChangesAsync(IReadOnlyList<ChangeDelta> changes, CancellationToken ct) => Task.CompletedTask;
}

internal static class DevDotEnv
{
    public static void Load(IHostEnvironment env)
    {
        if (!env.IsDevelopment()) return;
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, ".env"))) dir = dir.Parent;
        if (dir is null) return;
        foreach (var raw in File.ReadAllLines(Path.Combine(dir.FullName, ".env")))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (line.StartsWith('#') || eq <= 0) continue;
            var value = line[(eq + 1)..];
            var c = value.IndexOf(" #", StringComparison.Ordinal);
            if (c >= 0) value = value[..c];
            value = value.Trim().Trim('"');
            var key = line[..eq].Trim();
            if (value.Length > 0 && Environment.GetEnvironmentVariable(key) is null) Environment.SetEnvironmentVariable(key, value);
        }
    }
}
