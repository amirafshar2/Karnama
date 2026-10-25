using System.Text.Json;
using Karnama.Application.Abstractions;
using Karnama.Domain.Auditing;
using Karnama.Infrastructure.Persistence;

namespace Karnama.Infrastructure.Auditing;

internal sealed class AuditWriter(AppDbContext db, ICurrentUser me, TimeProvider clock) : IAuditWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Add(string action, string entityType, string? entityId, object? data = null, Guid? actorOverride = null, bool? isDemoOverride = null)
    {
        var json = data is null ? null : JsonSerializer.Serialize(data, Json);
        db.AuditLogs.Add(AuditLog.Record(clock.GetUtcNow(), actorOverride ?? me.UserId, isDemoOverride ?? me.IsDemo,
            action, entityType, entityId, json, me.TraceId));
    }
}
