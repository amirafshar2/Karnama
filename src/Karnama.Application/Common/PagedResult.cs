namespace Karnama.Application.Common;

/// <summary>Keyset page: <see cref="NextCursor"/> is null on the last page.</summary>
public sealed record KeysetPage<T>(IReadOnlyList<T> Items, string? NextCursor);

public sealed record KeysetQuery(string? Cursor, int Limit = 20, string? Search = null, bool IncludeDeleted = false)
{
    public int SafeLimit => Math.Clamp(Limit, 1, 100);
}
