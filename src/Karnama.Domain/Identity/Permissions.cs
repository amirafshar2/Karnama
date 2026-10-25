namespace Karnama.Domain.Identity;

/// <summary>How far a permission reaches. Higher includes lower.</summary>
public enum PermissionScope
{
    Own = 1,
    Assigned = 2,
    All = 3,
}

public sealed record PermissionDefinition(string Code, string Group, IReadOnlyList<PermissionScope> AllowedScopes)
{
    public PermissionScope MaxScope => AllowedScopes.Max();
}

/// <summary>
/// The fixed permission catalog. Permissions are code; roles are data built from them in the admin panel.
/// Add new permissions here only (and a translation key <c>perm.{code}</c>).
/// </summary>
public static class Permissions
{
    public const string WorkView = "work.view";
    public const string WorkCreate = "work.create";
    public const string WorkAssign = "work.assign";
    public const string WorkRun = "work.run";
    public const string WorkReview = "work.review";
    public const string WorkDelete = "work.delete";

    public const string OrdersView = "orders.view";
    public const string OrdersCreate = "orders.create";
    public const string OrdersManage = "orders.manage";

    public const string ClientsView = "clients.view";
    public const string ClientsManage = "clients.manage";
    public const string ClientsContact = "clients.contact";

    public const string PackagesView = "packages.view";
    public const string PackagesManage = "packages.manage";
    public const string PackagesAssign = "packages.assign";

    public const string AttendanceSelf = "attendance.self";
    public const string AttendanceView = "attendance.view";
    public const string AttendancePoliciesManage = "attendance.policies.manage";
    public const string AttendanceKiosk = "attendance.kiosk";
    public const string AttendanceApprove = "attendance.approve";
    public const string AttendanceCorrect = "attendance.correct";

    public const string ServicesManage = "services.manage";

    public const string ReportsView = "reports.view";
    public const string ReportsExport = "reports.export";

    public const string SettingsManage = "settings.manage";
    public const string LanguagesManage = "languages.manage";
    public const string ContentManage = "content.manage";

    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";

    public const string CampaignsManage = "campaigns.manage";
    public const string InfluencersManage = "influencers.manage";

    private static readonly PermissionScope[] AllOnly = [PermissionScope.All];
    private static readonly PermissionScope[] Scoped = [PermissionScope.Own, PermissionScope.Assigned, PermissionScope.All];
    private static readonly PermissionScope[] OwnOrAll = [PermissionScope.Own, PermissionScope.All];

    public static IReadOnlyList<PermissionDefinition> Catalog { get; } =
    [
        new(WorkView, "work", Scoped),
        new(WorkCreate, "work", AllOnly),
        new(WorkAssign, "work", AllOnly),
        new(WorkRun, "work", [PermissionScope.Assigned, PermissionScope.All]),
        new(WorkReview, "work", AllOnly),
        new(WorkDelete, "work", AllOnly),

        new(OrdersView, "orders", Scoped),
        new(OrdersCreate, "orders", AllOnly),
        new(OrdersManage, "orders", AllOnly),

        new(ClientsView, "clients", [PermissionScope.Assigned, PermissionScope.All]),
        new(ClientsManage, "clients", AllOnly),
        new(ClientsContact, "clients", AllOnly),

        new(PackagesView, "packages", AllOnly),
        new(PackagesManage, "packages", AllOnly),
        new(PackagesAssign, "packages", AllOnly),

        new(AttendanceSelf, "attendance", [PermissionScope.Own]),
        new(AttendanceView, "attendance", OwnOrAll),
        new(AttendancePoliciesManage, "attendance", AllOnly),
        new(AttendanceKiosk, "attendance", AllOnly),
        new(AttendanceApprove, "attendance", AllOnly),
        new(AttendanceCorrect, "attendance", AllOnly),

        new(ServicesManage, "services", AllOnly),

        new(ReportsView, "reports", OwnOrAll),
        new(ReportsExport, "reports", AllOnly),

        new(SettingsManage, "settings", AllOnly),
        new(LanguagesManage, "settings", AllOnly),
        new(ContentManage, "settings", AllOnly),

        new(UsersManage, "users", AllOnly),
        new(RolesManage, "users", AllOnly),

        new(CampaignsManage, "campaigns", AllOnly),
        new(InfluencersManage, "campaigns", AllOnly),
    ];

    private static readonly Dictionary<string, PermissionDefinition> ByCode =
        Catalog.ToDictionary(p => p.Code, StringComparer.Ordinal);

    public static PermissionDefinition? Find(string code) => ByCode.GetValueOrDefault(code);

    public static IReadOnlyList<string> Groups { get; } = Catalog.Select(p => p.Group).Distinct().ToArray();
}
