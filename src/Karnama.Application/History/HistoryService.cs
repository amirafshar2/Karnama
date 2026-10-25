using Karnama.Application.Abstractions;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.History;

public sealed record HistoryEntryDto(DateTimeOffset At, string Action, string? ActorName, bool IsDemo, string? DataJson);

/// <summary>Change history of one record, from the append-only audit log. Each entity type needs its own manage permission.</summary>
public sealed class HistoryService(IAppDbContext db, ICurrentUser me)
{
    private static readonly Dictionary<string, string> Required = new(StringComparer.Ordinal)
    {
        ["Role"] = Permissions.RolesManage,
        ["User"] = Permissions.UsersManage,
        ["Client"] = Permissions.ClientsManage,
        ["Package"] = Permissions.PackagesManage,
        ["ClientPackage"] = Permissions.PackagesAssign,
        ["ServiceType"] = Permissions.ServicesManage,
        ["Platform"] = Permissions.SettingsManage,
        ["SiteSetting"] = Permissions.SettingsManage,
        ["Order"] = Permissions.OrdersManage,
        ["WorkItem"] = Permissions.WorkReview,
    };

    public async Task<Result<IReadOnlyList<HistoryEntryDto>>> GetAsync(string entityType, Guid id, CancellationToken ct)
    {
        if (!Required.TryGetValue(entityType, out var perm)) return Error.NotFound();
        if (!me.Has(perm, PermissionScope.All)) return Error.Forbidden();

        var key = id.ToString();
        var rows = await (from a in db.AuditLogs.AsNoTracking()
                          where a.EntityType == entityType && a.EntityId == key
                          orderby a.Id descending
                          select new { a.OccurredAt, a.Action, a.ActorId, a.IsDemo, a.DataJson })
            .Take(200).ToListAsync(ct);
        var actorIds = rows.Where(r => r.ActorId != null).Select(r => r.ActorId!.Value).Distinct().ToList();
        var names = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return rows.Select(r => new HistoryEntryDto(r.OccurredAt, r.Action, r.ActorId is { } a && names.TryGetValue(a, out var n) ? n : null, r.IsDemo,
            Redact(r.DataJson))).ToList();
    }

    private static readonly System.Text.RegularExpressions.Regex Sensitive =
        new("phone|mobile|secret|password|token|apikey|api_key|key$|otp|code$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Never echo credentials or phone numbers back through history, whatever a future audit entry contains.</summary>
    public static string? Redact(string? json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(json);
            Walk(node);
            return node?.ToJsonString();
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        static void Walk(System.Text.Json.Nodes.JsonNode? n)
        {
            switch (n)
            {
                case System.Text.Json.Nodes.JsonObject o:
                    foreach (var k in o.Select(p => p.Key).ToList())
                        if (Sensitive.IsMatch(k)) o[k] = "•••";
                        else Walk(o[k]);
                    break;
                case System.Text.Json.Nodes.JsonArray a:
                    foreach (var item in a) Walk(item);
                    break;
            }
        }
    }
}
