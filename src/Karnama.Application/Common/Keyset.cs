using Karnama.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Common;

/// <summary>
/// Keyset pagination on time-ordered Guid v7 ids (newest first). No OFFSET: page 500 costs the same as page 1.
/// </summary>
public static class Keyset
{
    public static async Task<KeysetPage<TOut>> PageAsync<TEntity, TOut>(
        IQueryable<TEntity> query, KeysetQuery q, Func<IQueryable<TEntity>, IQueryable<TOut>> project, Func<TOut, Guid> idOf, CancellationToken ct)
        where TEntity : Entity
    {
        if (Decode(q.Cursor) is { } after) query = query.Where(e => e.Id.CompareTo(after) < 0);
        var rows = await project(query.OrderByDescending(e => e.Id).Take(q.SafeLimit + 1)).ToListAsync(ct);
        var more = rows.Count > q.SafeLimit;
        if (more) rows.RemoveAt(rows.Count - 1);
        return new KeysetPage<TOut>(rows, more ? Encode(idOf(rows[^1])) : null);
    }

    public static string Encode(Guid id) => Convert.ToBase64String(id.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static Guid? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > 32) return null;
        try
        {
            var s = cursor.Replace('-', '+').Replace('_', '/');
            s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            var bytes = Convert.FromBase64String(s);
            return bytes.Length == 16 ? new Guid(bytes) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Escapes LIKE wildcards in user search text.</summary>
    public static string Like(string search) => "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}
