using Hangfire;
using Karnama.Application.Abstractions;
using Karnama.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Worker;

/// <summary>Housekeeping that must not slow the API. More jobs (absence detection, reminders, outbox) arrive in later milestones.</summary>
public sealed class MaintenanceJobs(IAppDbContext db, TimeProvider clock, ILogger<MaintenanceJobs> logger)
{
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
public sealed class AttendanceJobs(Karnama.Application.Attendance.AttendanceAdminService attendance, ILogger<AttendanceJobs> logger)
{
    [AutomaticRetry(Attempts = 3)]
    public async Task CloseForgottenAsync(CancellationToken ct)
    {
        var n = await attendance.CloseForgottenAsync(ct);
        if (n > 0) logger.LogInformation("Closed {Count} forgotten shifts for review", n);
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
        jobs.AddOrUpdate<AttendanceJobs>("close-forgotten-shifts", j => j.CloseForgottenAsync(CancellationToken.None), "*/30 * * * *");
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
