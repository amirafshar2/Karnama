using Karnama.Application.Abstractions;
using Karnama.Domain.Identity;
using Karnama.Domain.Work;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Work;

/// <summary>
/// Who sees which orders, work items and files. Decided on the server only:
/// managers (work.view:All) see everything; workers see the items assigned to them; a portal client sees only its own
/// orders (and never workers, internal comments or internal files).
/// </summary>
public sealed class WorkAccess(IAppDbContext db, ICurrentUser me)
{
    private Guid? _portalClient;
    private bool _portalResolved;

    public Guid Me => me.UserId ?? Guid.Empty;
    public bool SeesAllWork => me.Kind == UserKind.Staff && me.Has(Permissions.WorkView, PermissionScope.All);
    public bool SeesAssignedWork => me.Kind == UserKind.Staff && me.Has(Permissions.WorkView, PermissionScope.Assigned);
    /// <summary>Reviews, overrides and moves any card.</summary>
    public bool IsManager => me.Kind == UserKind.Staff && (me.Has(Permissions.WorkReview, PermissionScope.All) || me.Has(Permissions.WorkAssign, PermissionScope.All));
    public bool CanAssign => me.Kind == UserKind.Staff && me.Has(Permissions.WorkAssign, PermissionScope.All);
    public bool CanRun => me.Kind == UserKind.Staff && me.Has(Permissions.WorkRun, PermissionScope.Assigned);
    /// <summary>Only people who talk to clients may write comments the client sees.</summary>
    public bool CanTalkToClient => me.Kind == UserKind.Staff && (me.Has(Permissions.OrdersManage, PermissionScope.All) || me.Has(Permissions.ClientsContact, PermissionScope.All));
    public bool SeesAllOrders => me.Kind == UserKind.Staff && me.Has(Permissions.OrdersView, PermissionScope.All);

    public async Task<Guid?> PortalClientIdAsync(CancellationToken ct)
    {
        if (me.Kind != UserKind.Client) return null;
        if (!_portalResolved)
        {
            var uid = Me;
            _portalClient = await db.Clients.Where(c => c.PortalUserId == uid).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
            _portalResolved = true;
        }
        // A client login that isn't linked to a client sees nothing (never "everything").
        return _portalClient ?? Guid.Empty;
    }

    public IQueryable<WorkItem> VisibleItems()
    {
        if (SeesAllWork) return db.WorkItems;
        if (SeesAssignedWork) { var uid = Me; return db.WorkItems.Where(w => w.AssigneeId == uid); }
        return db.WorkItems.Where(_ => false);
    }

    public async Task<IQueryable<Order>> VisibleOrdersAsync(CancellationToken ct)
    {
        if (await PortalClientIdAsync(ct) is { } clientId) return db.Orders.Where(o => o.ClientId == clientId);
        if (SeesAllOrders || SeesAllWork) return db.Orders;
        if (me.Has(Permissions.OrdersView, PermissionScope.Assigned) || SeesAssignedWork)
        {
            var uid = Me;
            return db.Orders.Where(o => db.WorkItems.Any(w => w.OrderId == o.Id && w.AssigneeId == uid));
        }
        return db.Orders.Where(_ => false);
    }

    public bool CanSeeItem(WorkItem w) => SeesAllWork || (SeesAssignedWork && w.AssigneeId == Me);
}
