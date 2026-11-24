using System.Text.Json;
using Karnama.Application.Abstractions;
using Karnama.Application.Attendance;
using Karnama.Application.Catalog;
using Karnama.Application.Clients;
using Karnama.Domain.Clients;
using Karnama.Domain.Identity;
using Karnama.Domain.Work;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Notify;

/// <summary>
/// Daily package job (PROMPT2 §10.3 + §10.2), to the client AND the managers through the outbox:
/// "N days left" at the configured days and on the last day, "package ended", "a quota is used up",
/// and when a contract really ends: managers are reminded to delete the stored passwords, the client to change them.
/// Running it twice a day sends nothing new (threshold memory on the package + unique event keys).
/// </summary>
public sealed class PackageExpiryJob(IAppDbContext db, Notifier notifier, TimeProvider clock, PackageNoticeSettingsStore settingsStore)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <returns>The number of notifications actually created (0 when a run finds nothing new).</returns>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = Iran.Today(now);
        var settings = await settingsStore.GetAsync(ct);
        var thresholds = settings.Thresholds();
        var horizon = now.AddDays((thresholds.Count == 0 ? 0 : thresholds.Max()) + 2);

        var packages = await db.ClientPackages.Where(p => p.Status == ClientPackageStatus.Active && p.EndsAt < horizon).ToListAsync(ct);
        var quotaPackages = settings.QuotaUsedUp
            ? await db.ClientPackages.AsNoTracking().Where(p => p.Status == ClientPackageStatus.Active && p.EndsAt > now && p.StartsAt <= now).ToListAsync(ct)
            : [];
        if (packages.Count == 0 && quotaPackages.Count == 0) return 0;

        var managers = await notifier.StaffWithAsync(Permissions.ClientsManage, PermissionScope.All, ct);
        var ids = packages.Concat(quotaPackages).Select(p => p.ClientId).Distinct().ToList();
        var clients = await db.Clients.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var sent = 0;

        async Task Both(Client c, string kind, string title, string? clientBody, bool important, string key)
        {
            if (c.PortalUserId is { } portal)
                sent += await notifier.NotifyAsync([portal], kind, title, clientBody, "/profile", important, key, ct);
            sent += await notifier.NotifyAsync(managers, kind, $"{c.BusinessName}: {title}", null, "/clients", important, key, ct);
        }

        var ended = new List<ClientPackage>();
        foreach (var p in packages)
        {
            if (!clients.TryGetValue(p.ClientId, out var c)) continue;
            var name = p.PackageName.Get("de");
            if (p.EndIfOver(now))
            {
                ended.Add(p);
                await Both(c, "package.ended", $"Das Paket „{name}“ ist abgelaufen", "Um weiterzuarbeiten, fragen Sie im Profil eine Verlängerung an.", true, $"pkg:{p.Id:N}:ended");
                continue;
            }
            var daysLeft = Iran.Today(p.EndsAt).DayNumber - today.DayNumber;
            if (!p.NeedsExpiryNotice(daysLeft, thresholds, out var t)) continue;
            var title = t == 0 ? $"Heute ist der letzte Tag des Pakets „{name}“" : $"Das Paket „{name}“ endet in {t} Tagen";
            await Both(c, "package.expiring", title, "Zum Verlängern tippen Sie im Profil auf „Verlängerung anfragen“.", t <= 3, $"pkg:{p.Id:N}:{t}");
        }

        if (settings.ContractEndPasswords && ended.Count > 0) sent += await ContractEndAsync(ended, clients, now, ct);

        foreach (var p in quotaPackages)
        {
            if (!clients.TryGetValue(p.ClientId, out var c)) continue;
            var quotas = JsonSerializer.Deserialize<List<QuotaDto>>(p.QuotasJson, Json) ?? [];
            if (quotas.Count == 0) continue;
            var used = await db.Orders.AsNoTracking()
                .Where(o => o.ClientPackageId == p.Id && o.Status != OrderStatus.Rejected && o.Status != OrderStatus.Cancelled)
                .GroupBy(o => o.ServiceTypeId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
            foreach (var q in quotas.Where(q => q.Count > 0 && used.GetValueOrDefault(q.ServiceTypeId) >= q.Count))
                await Both(c, "package.quotaUsedUp", $"Kontingent „{q.ServiceName ?? "Leistung"}“ im Paket „{p.PackageName.Get("de")}“ ist abgelaufen",
                    "Für weitere Aufträge upgraden oder verlängern Sie das Paket.", false, $"pkg:{p.Id:N}:quota:{q.ServiceTypeId:N}");
        }

        await db.SaveChangesAsync(ct);
        return sent;
    }

    /// <summary>Only when the client has no other running package and the vault still holds a password for them.</summary>
    private async Task<int> ContractEndAsync(List<ClientPackage> ended, Dictionary<Guid, Client> clients, DateTimeOffset now, CancellationToken ct)
    {
        var sent = 0;
        var clientIds = ended.Select(p => p.ClientId).Distinct().ToList();
        var stillActive = await db.ClientPackages.AsNoTracking()
            .Where(p => clientIds.Contains(p.ClientId) && p.Status == ClientPackageStatus.Active && p.EndsAt > now).Select(p => p.ClientId).ToListAsync(ct);
        var withSecrets = await db.ClientCredentials.AsNoTracking()
            .Where(c => clientIds.Contains(c.ClientId) && c.Cipher != null).Select(c => c.ClientId).Distinct().ToListAsync(ct);
        var keepers = await notifier.StaffWithAsync(Permissions.CredentialsManage, PermissionScope.All, ct);
        foreach (var id in clientIds.Except(stillActive).Intersect(withSecrets))
        {
            if (!clients.TryGetValue(id, out var c)) continue;
            sent += await notifier.NotifyAsync(keepers, "credentials.contractEnded", $"Der Vertrag mit {c.BusinessName} ist beendet: gespeicherte Passwörter löschen",
                "Falls nicht verlängert wird, löschen Sie die Passwörter auf der Kundenseite.", "/clients", true, $"contract-end:{id:N}:{Iran.Today(now):yyyyMMdd}", ct);
            if (c.PortalUserId is { } portal)
                sent += await notifier.NotifyAsync([portal], "credentials.changePassword", "Der Vertrag ist beendet; ändern Sie zur Sicherheit die Passwörter Ihrer Konten",
                    "Ändern Sie die Passwörter, die Sie der Agentur gegeben haben, direkt in Instagram, und entziehen Sie der Agentur den Zugriff, falls Sie sie in der Business Suite hinzugefügt hatten.",
                    "/profile", true, $"contract-end:{id:N}:{Iran.Today(now):yyyyMMdd}", ct);
        }
        return sent;
    }
}
