namespace Karnama.Application.Abstractions;

/// <summary>The movable QA clock (only registered in QA mode).</summary>
public interface IQaClock
{
    TimeSpan Offset { get; }
    DateTimeOffset GetUtcNow();
    void Advance(TimeSpan by);
    void Set(DateTimeOffset utc);
    void Reset();
}

public sealed record QaPerson(string Phone, string Name, string Group);
public sealed record QaCrowd(IReadOnlyList<QaPerson> People);

/// <summary>Seeds the 83-person QA crowd (only used in QA mode).</summary>
public interface IQaSeeder
{
    Task<QaCrowd> SeedAsync(CancellationToken ct);
}
