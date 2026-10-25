using Karnama.Domain.Catalog;
using Karnama.Domain.Common;

namespace Karnama.Domain.Clients;

public enum ClientStatus { Active = 1, Paused = 2, Ended = 3 }

/// <summary>A business the agency serves. Only staff with clients.contact may see the mobile number.</summary>
public sealed class Client : AuditableEntity
{
    private readonly List<ClientAccount> _accounts = [];

    private Client() { BusinessName = string.Empty; Mobile = string.Empty; }

    public string BusinessName { get; private set; }
    public string? Industry { get; private set; }
    public string? ContactName { get; private set; }
    public string Mobile { get; private set; }
    public string? City { get; private set; }
    /// <summary>Never shown to the client.</summary>
    public string? InternalNote { get; private set; }
    public ClientStatus Status { get; private set; } = ClientStatus.Active;
    /// <summary>Portal login (a user of kind Client with the same mobile), once invited.</summary>
    public Guid? PortalUserId { get; private set; }
    public uint Version { get; private set; }

    public IReadOnlyCollection<ClientAccount> Accounts => _accounts;

    public static Result<Client> Create(ClientSpec spec)
    {
        var c = new Client();
        var r = c.Update(spec);
        return r.IsSuccess ? c : r.Error!;
    }

    public Result Update(ClientSpec s)
    {
        if (string.IsNullOrWhiteSpace(s.BusinessName) || s.BusinessName.Trim().Length > 120)
            return Error.Validation("clients.nameRequired", "نام کسب‌وکار الزامی است (حداکثر ۱۲۰ کاراکتر).");
        if (!PhoneNumber.TryNormalize(s.Mobile, out var mobile))
            return Error.Validation("clients.invalidMobile", "شماره‌ی موبایل معتبر نیست (مثل ۰۹۱۲۱۲۳۴۵۶۷).");
        if (s.InternalNote is { Length: > 2000 }) return Error.Validation("clients.noteTooLong", "یادداشت حداکثر ۲۰۰۰ کاراکتر است.");
        if (s.Accounts.Count > 20) return Error.Validation("clients.tooManyAccounts", "حداکثر ۲۰ حساب شبکه.");
        if (s.Accounts.Any(a => string.IsNullOrWhiteSpace(a.Handle) || a.Handle.Length > 100))
            return Error.Validation("clients.invalidHandle", "آیدی هر حساب الزامی است (حداکثر ۱۰۰ کاراکتر).");

        BusinessName = s.BusinessName.Trim();
        Industry = Clean(s.Industry);
        ContactName = Clean(s.ContactName);
        Mobile = mobile;
        City = Clean(s.City);
        InternalNote = Clean(s.InternalNote);
        Status = s.Status;

        _accounts.Clear();
        foreach (var a in s.Accounts)
            _accounts.Add(new ClientAccount(a.PlatformId, a.Handle.Trim().TrimStart('@'), a.Management));
        return Result.Success();
    }

    public void LinkPortalUser(Guid userId) => PortalUserId = userId;

    public void Delete(Guid? actor, DateTimeOffset now) => MarkDeleted(actor, now);
    public void Restore() => MarkRestored();

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public sealed record ClientSpec(
    string BusinessName, string? Industry, string? ContactName, string Mobile, string? City, string? InternalNote,
    ClientStatus Status, IReadOnlyList<ClientAccountSpec> Accounts);

public sealed record ClientAccountSpec(Guid PlatformId, string Handle, PageManagement Management);

/// <summary>Page/channel of the client on one platform (owned, stored as JSON). Passwords are NOT here (vault, N4).</summary>
public sealed class ClientAccount
{
    private ClientAccount() { Handle = string.Empty; }

    public ClientAccount(Guid platformId, string handle, PageManagement management)
    {
        PlatformId = platformId;
        Handle = handle;
        Management = management;
    }

    public Guid PlatformId { get; private set; }
    public string Handle { get; private set; }
    public PageManagement Management { get; private set; }
}

public enum ClientPackageStatus { Active = 1, Ended = 2, Cancelled = 3 }

/// <summary>
/// A package sold to a client. Quotas and price are frozen copies taken at assignment,
/// so later edits to the package never change an existing contract.
/// </summary>
public sealed class ClientPackage : AuditableEntity
{
    private ClientPackage() { PackageName = new LocalizedText(); QuotasJson = "[]"; }

    public Guid ClientId { get; private set; }
    public Guid PackageId { get; private set; }
    public LocalizedText PackageName { get; private set; }
    public long Price { get; private set; }
    public string QuotasJson { get; private set; }
    public PageAccess Access { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public ClientPackageStatus Status { get; private set; }
    public string? Note { get; private set; }
    public uint Version { get; private set; }

    public static Result<ClientPackage> Assign(Guid clientId, Package package, DateTimeOffset startsAt, string quotasJson, DateTimeOffset now, string? note)
    {
        if (package.IsDeleted || package.Status != PackageStatus.Active)
            return Error.Validation("clientPackages.inactive", "این پکیج فعال نیست.");
        if (!package.IsOnSale(startsAt) && !package.IsOnSale(now))
            return Error.Validation("clientPackages.notOnSale", "این پکیج در این تاریخ قابل فروش نیست.");
        return new ClientPackage
        {
            ClientId = clientId,
            PackageId = package.Id,
            PackageName = package.Name,
            Price = package.PriceAt(now),
            QuotasJson = quotasJson,
            Access = package.Access,
            StartsAt = startsAt,
            EndsAt = startsAt.AddDays(package.ValidityDays),
            Status = ClientPackageStatus.Active,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
    }

    /// <summary>Smallest "days left" threshold already announced (e.g. 7, 3, 1, 0 = the last day), so each reminder goes out once.</summary>
    public int? ExpiryNoticeSent { get; private set; }

    /// <summary>
    /// True when a new threshold was crossed and a reminder should go out now. <paramref name="daysLeft"/> counts calendar
    /// days in the agency's time zone (0 = today is the last day); thresholds come from the panel.
    /// Only the closest crossed threshold is announced: a job that missed days never sends a burst of old reminders.
    /// </summary>
    public bool NeedsExpiryNotice(int daysLeft, IReadOnlyCollection<int> thresholds, out int threshold)
    {
        threshold = -1;
        if (Status != ClientPackageStatus.Active || daysLeft < 0) return false;
        foreach (var t in thresholds.Where(t => t >= 0).Distinct().Order())
        {
            if (daysLeft > t) continue;
            if (ExpiryNoticeSent is not null && ExpiryNoticeSent <= t) return false;
            threshold = t;
            ExpiryNoticeSent = t;
            return true;
        }
        return false;
    }

    /// <summary>The contract time is over: the package stops counting as active.</summary>
    public bool EndIfOver(DateTimeOffset now)
    {
        if (Status != ClientPackageStatus.Active || EndsAt > now) return false;
        Status = ClientPackageStatus.Ended;
        return true;
    }

    public int DaysLeft(DateTimeOffset now) => Status != ClientPackageStatus.Active ? 0 : Math.Max(0, (int)Math.Ceiling((EndsAt - now).TotalDays));

    public Result Cancel()
    {
        if (Status != ClientPackageStatus.Active) return Error.Conflict("clientPackages.notActive", "این پکیج فعال نیست.");
        Status = ClientPackageStatus.Cancelled;
        return Result.Success();
    }
}
