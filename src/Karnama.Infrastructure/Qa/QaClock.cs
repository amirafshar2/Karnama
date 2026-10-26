namespace Karnama.Infrastructure.Qa;

/// <summary>
/// QA clock (docs/PROMPT3.md Q0): real time plus a movable offset, so a whole day — or seven days of package reminders —
/// can be played in minutes. Registered as the app's TimeProvider only when Qa:Enabled (Development only, StartupGuard).
/// </summary>
public sealed class QaClock : TimeProvider, Karnama.Application.Abstractions.IQaClock
{
    private long _offsetTicks;

    public TimeSpan Offset => TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));
    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Offset;
    public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);
    public void Set(DateTimeOffset utc) => Interlocked.Exchange(ref _offsetTicks, (utc - base.GetUtcNow()).Ticks);
    public void Reset() => Interlocked.Exchange(ref _offsetTicks, 0);
}
