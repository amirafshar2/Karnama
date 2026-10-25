using Karnama.Application.Abstractions;
using Karnama.Domain.Clients;
using Karnama.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Notify;

/// <summary>PROMPT2 §10 (N4 part): "package ends in 7 / 3 / 1 days" to the client and the managers — each threshold once.</summary>
public sealed class PackageExpiryJob(IAppDbContext db, Notifier notifier, TimeProvider clock)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var soon = now.AddDays(8);
        var packages = await db.ClientPackages.Where(p => p.Status == ClientPackageStatus.Active && p.EndsAt > now && p.EndsAt < soon).ToListAsync(ct);
        if (packages.Count == 0) return 0;
        var managers = await notifier.StaffWithAsync(Permissions.ClientsManage, PermissionScope.All, ct);
        var ids = packages.Select(p => p.ClientId).Distinct().ToList();
        var clients = await db.Clients.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var sent = 0;
        foreach (var p in packages)
        {
            if (!p.NeedsExpiryNotice(now, out var days) || !clients.TryGetValue(p.ClientId, out var c)) continue;
            var title = $"پکیج «{p.PackageName.Get("fa")}» {days} روز دیگر تمام می‌شود";
            if (c.PortalUserId is { } portal)
                await notifier.NotifyAsync([portal], "package.expiring", title, "برای تمدید با آژانس هماهنگ کنید.", "/dashboard", true, $"pkg:{p.Id:N}:{days}", ct);
            await notifier.NotifyAsync(managers, "package.expiring", $"{c.BusinessName}: {title}", null, "/clients", days <= 3, $"pkg:{p.Id:N}:{days}", ct);
            sent++;
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }
}
