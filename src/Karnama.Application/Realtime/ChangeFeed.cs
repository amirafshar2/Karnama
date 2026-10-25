using Microsoft.EntityFrameworkCore;
using Karnama.Application.Abstractions;
using Karnama.Domain.Realtime;

namespace Karnama.Application.Realtime;

internal sealed class ChangeFeed(IAppDbContext db, IRealtimeNotifier notifier, TimeProvider clock) : IChangeFeed
{
    private readonly List<ChangeEvent> _pending = [];

    public void Record(string kind, Guid id, Guid? clientId, Guid? assigneeId, Guid? previousAssigneeId, params string[] fields)
    {
        var e = new ChangeEvent(kind, id, clientId, assigneeId, previousAssigneeId == assigneeId ? null : previousAssigneeId,
            string.Join(',', fields.Distinct()), clock.GetUtcNow());
        _pending.Add(e);
        db.ChangeEvents.Add(e);
    }

    public async Task PublishAsync(CancellationToken ct)
    {
        if (_pending.Count == 0) return;
        var deltas = _pending.Where(e => e.Seq > 0)
            .Select(e => new ChangeDelta(e.Seq, e.Kind, e.EntityId, e.Fields.Split(',', StringSplitOptions.RemoveEmptyEntries), e.ClientId, e.AssigneeId, e.PreviousAssigneeId))
            .ToList();
        _pending.Clear();
        // Live push is best effort: a client that misses it catches up from the cursor on reconnect.
        try { await notifier.ChangesAsync(deltas, ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { }
    }
}

public sealed record ChangeEventDto(long Seq, string Kind, Guid Id, IReadOnlyList<string> Fields);
public sealed record ChangesPage(long Cursor, bool Reset, IReadOnlyList<ChangeEventDto> Events);

/// <summary>"What changed since cursor N" for a client that reconnects (only what this user may see).</summary>
public sealed class ChangesService(IAppDbContext db, ICurrentUser me, Work.WorkAccess access)
{
    public const int MaxEvents = 300;

    /// <summary><paramref name="since"/> null = "just tell me the current cursor" (first load of a page).</summary>
    public async Task<ChangesPage> SinceAsync(long? sinceOrNull, CancellationToken ct)
    {
        var latest = await db.ChangeEvents.MaxAsync(e => (long?)e.Seq, ct) ?? 0;
        if (sinceOrNull is not { } since || since < 0) return new ChangesPage(latest, false, []);
        if (since > latest) return new ChangesPage(latest, true, []);
        var oldest = await db.ChangeEvents.MinAsync(e => (long?)e.Seq, ct) ?? 0;
        if (oldest > 0 && since < oldest - 1) return new ChangesPage(latest, true, []);

        var q = db.ChangeEvents.AsNoTracking().Where(e => e.Seq > since);
        if (await access.PortalClientIdAsync(ct) is { } clientId) q = q.Where(e => e.ClientId == clientId);
        else
        {
            var uid = me.UserId ?? Guid.Empty;
            var seesPresence = me.Has(Domain.Identity.Permissions.AttendanceView, Domain.Identity.PermissionScope.All);
            if (!access.SeesAllWork) q = q.Where(e => e.AssigneeId == uid || e.PreviousAssigneeId == uid || (seesPresence && e.Kind == "presence"));
            else if (!seesPresence) q = q.Where(e => e.Kind != "presence" || e.AssigneeId == uid);
        }
        var rows = await q.OrderBy(e => e.Seq).Take(MaxEvents + 1).ToListAsync(ct);
        if (rows.Count > MaxEvents) return new ChangesPage(latest, true, []);
        return new ChangesPage(latest, false, rows.Select(e => new ChangeEventDto(e.Seq, e.Kind, e.EntityId, e.Fields.Split(',', StringSplitOptions.RemoveEmptyEntries))).ToList());
    }
}
