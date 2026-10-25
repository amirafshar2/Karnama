using System.Text.Json;
using FluentValidation;
using Karnama.Application.Abstractions;
using Karnama.Application.Attendance;
using Karnama.Application.Common;
using Karnama.Application.Notify;
using Karnama.Domain.Clients;
using Karnama.Domain.Common;
using Karnama.Domain.Crm;
using Karnama.Domain.Identity;
using Karnama.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Crm;

public sealed record StageDto(Guid Id, string Name, StageKind Kind, int SortOrder, bool IsActive);
public sealed record SourceDto(Guid Id, string Name, int SortOrder, bool IsActive);
public sealed record SaveStageRequest(string Name, StageKind Kind, int SortOrder, bool IsActive);
public sealed record SaveSourceRequest(string Name, int SortOrder, bool IsActive);

public sealed record LeadItemDto(Guid Id, string Name, string? Business, string? Phone, string? City, string? Instagram, Guid? SourceId, string? SourceName,
    Guid StageId, Guid? OwnerId, string? OwnerName, IReadOnlyList<string> Tags, long? Budget, DateTimeOffset? NextFollowUpAt, DateTimeOffset LastActivityAt,
    DateTimeOffset CreatedAt, Guid? ClientId, uint Version);
public sealed record ActivityDto(long Id, ActivityKind Kind, string? ActorName, CallDirection? Direction, int? DurationSeconds, CallOutcome? Outcome,
    MessageChannel? Channel, string? Note, string? From, string? To, DateTimeOffset At);
public sealed record LeadDetailDto(LeadItemDto Lead, string? Industry, string? Message, string? Origin, string? Note, string? LostReason, IReadOnlyList<ActivityDto> Activities);

public sealed record SaveLeadRequest(string Name, string? Phone, string? Business, string? Industry, string? City, string? Instagram, Guid? SourceId,
    List<string>? Tags, long? Budget, string? Note, Guid? StageId, Guid? OwnerId, DateTimeOffset? NextFollowUpAt, bool Force = false, uint? Version = null);
public sealed record MoveLeadRequest(Guid StageId, string? LostReason);
public sealed record AssignLeadRequest(Guid? OwnerId);
public sealed record RoundRobinRequest(List<Guid> LeadIds);
public sealed record CallRequest(CallDirection Direction, int? DurationSeconds, CallOutcome Outcome, string? Note, DateTimeOffset? NextFollowUpAt);
public sealed record ActivityRequest(ActivityKind Kind, MessageChannel? Channel, string? Note, DateTimeOffset? NextFollowUpAt);
public sealed record FollowUpRequest(DateTimeOffset? At);
public sealed record ConvertedDto(Guid ClientId, bool Existing);
public sealed record MarketerDto(Guid Id, string Name, int OpenLeads);

public sealed record CrmSettingsDto(bool AutoAssign, bool CommissionEnabled, decimal CommissionPercent, Guid? LastAssignedId = null)
{
    public static CrmSettingsDto Default => new(false, false, 0);
}

public sealed record MarketerReportDto(Guid Id, string Name, int Leads, int Calls, int CallMinutes, int Won, int Lost, long Sales, long? Commission);
public sealed record StageReportDto(Guid StageId, string Name, int Reached, double Percent);
public sealed record SourceReportDto(Guid? SourceId, string Name, int Leads, int Won, double Rate);
public sealed record CrmReportDto(DateOnly From, DateOnly To, int TotalLeads, IReadOnlyList<MarketerReportDto> Marketers, IReadOnlyList<StageReportDto> Stages, IReadOnlyList<SourceReportDto> Sources);

public sealed class SaveLeadValidator : AbstractValidator<SaveLeadRequest>
{
    public SaveLeadValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Note).MaximumLength(2000);
        RuleFor(x => x.Tags).Must(t => t is null || t.Count <= 10).WithMessage("حداکثر ۱۰ برچسب.");
        RuleFor(x => x.Budget).GreaterThanOrEqualTo(0).When(x => x.Budget is not null);
    }
}

/// <summary>
/// CRM (PROMPT2 §4): dynamic pipeline and sources, leads with duplicate detection by phone, manual or round-robin
/// hand-out, calls/messages/meetings with follow-ups, one-click conversion to a client, and reports.
/// A marketer (crm.leads:Own) sees only their own leads — anything else is 404.
/// </summary>
public sealed class CrmService(IAppDbContext db, ICurrentUser me, TimeProvider clock, Notifier notifier, IAuditWriter audit, IChangeFeed changes)
{
    public const string SettingsKey = "crm";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static Error NotFound => Error.NotFound("crm.notFound", "سرنخ پیدا نشد.");

    private Guid Me => me.UserId ?? Guid.Empty;
    private bool IsStaff => me.Kind == UserKind.Staff;
    private bool CanUse => IsStaff && me.Has(Permissions.CrmLeads, PermissionScope.Own);
    private bool SeesAll => IsStaff && me.Has(Permissions.CrmLeads, PermissionScope.All);
    private bool CanAssign => IsStaff && me.Has(Permissions.CrmAssign, PermissionScope.All);
    private bool CanPipeline => IsStaff && me.Has(Permissions.CrmPipelineManage, PermissionScope.All);

    private IQueryable<Lead> Visible() => SeesAll ? db.Leads : db.Leads.Where(l => l.OwnerId == Me);

    // ================= pipeline & sources =================

    public async Task<IReadOnlyList<StageDto>> StagesAsync(CancellationToken ct) =>
        await db.LeadStages.AsNoTracking().OrderBy(s => s.SortOrder).Select(s => new StageDto(s.Id, s.Name, s.Kind, s.SortOrder, s.IsActive)).ToListAsync(ct);

    public async Task<IReadOnlyList<SourceDto>> SourcesAsync(CancellationToken ct) =>
        await db.LeadSources.AsNoTracking().OrderBy(s => s.SortOrder).Select(s => new SourceDto(s.Id, s.Name, s.SortOrder, s.IsActive)).ToListAsync(ct);

    public async Task<Result<StageDto>> SaveStageAsync(Guid? id, SaveStageRequest r, CancellationToken ct)
    {
        if (!CanPipeline) return Error.Forbidden();
        LeadStage s;
        if (id is { } sid)
        {
            var found = await db.LeadStages.FirstOrDefaultAsync(x => x.Id == sid, ct);
            if (found is null) return Error.NotFound("crm.stageNotFound", "مرحله پیدا نشد.");
            if (!r.IsActive && found.Kind != StageKind.Open && !await db.LeadStages.AnyAsync(x => x.Id != sid && x.Kind == found.Kind && x.IsActive, ct))
                return Error.Conflict("crm.lastEnding", "حداقل یک مرحله‌ی «برد» و یک «از دست رفت» باید فعال بماند.");
            var u = found.Update(r.Name, r.SortOrder, r.IsActive);
            if (u.IsFailure) return u.Error!;
            s = found;
        }
        else
        {
            var c = LeadStage.Create(r.Name, r.Kind, r.SortOrder);
            if (c.IsFailure) return c.Error!;
            s = c.Value!;
            db.LeadStages.Add(s);
        }
        audit.Add("crm.stageSaved", nameof(LeadStage), s.Id.ToString(), r);
        await db.SaveChangesAsync(ct);
        return new StageDto(s.Id, s.Name, s.Kind, s.SortOrder, s.IsActive);
    }

    public async Task<Result<SourceDto>> SaveSourceAsync(Guid? id, SaveSourceRequest r, CancellationToken ct)
    {
        if (!CanPipeline) return Error.Forbidden();
        LeadSource s;
        if (id is { } sid)
        {
            var found = await db.LeadSources.FirstOrDefaultAsync(x => x.Id == sid, ct);
            if (found is null) return Error.NotFound("crm.sourceNotFound", "منبع پیدا نشد.");
            var u = found.Update(r.Name, r.SortOrder, r.IsActive);
            if (u.IsFailure) return u.Error!;
            s = found;
        }
        else
        {
            var c = LeadSource.Create(r.Name, r.SortOrder);
            if (c.IsFailure) return c.Error!;
            s = c.Value!;
            db.LeadSources.Add(s);
        }
        await db.SaveChangesAsync(ct);
        return new SourceDto(s.Id, s.Name, s.SortOrder, s.IsActive);
    }

    // ================= settings =================

    public async Task<CrmSettingsDto> SettingsAsync(CancellationToken ct)
    {
        // Unsaved changes of this request first (the round-robin pointer moves several times before one save).
        var json = db.SiteSettings.Local.FirstOrDefault(s => s.Key == SettingsKey)?.Json
                   ?? await db.SiteSettings.AsNoTracking().Where(s => s.Key == SettingsKey).Select(s => s.Json).FirstOrDefaultAsync(ct);
        if (json is null) return CrmSettingsDto.Default;
        try { return JsonSerializer.Deserialize<CrmSettingsDto>(json, Json) ?? CrmSettingsDto.Default; }
        catch (JsonException) { return CrmSettingsDto.Default; }
    }

    public async Task<Result<CrmSettingsDto>> SaveSettingsAsync(CrmSettingsDto s, CancellationToken ct)
    {
        if (!CanPipeline) return Error.Forbidden();
        if (s.CommissionPercent is < 0 or > 50) return Error.Validation("crm.commission", "درصد پورسانت باید بین ۰ تا ۵۰ باشد.");
        var current = await SettingsAsync(ct);
        await WriteSettingsAsync(s with { LastAssignedId = current.LastAssignedId }, ct);
        audit.Add("crm.settingsUpdated", nameof(SiteSetting), SettingsKey, s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    private async Task WriteSettingsAsync(CrmSettingsDto s, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(s, Json);
        var row = db.SiteSettings.Local.FirstOrDefault(x => x.Key == SettingsKey) ?? await db.SiteSettings.FirstOrDefaultAsync(x => x.Key == SettingsKey, ct);
        if (row is null) db.SiteSettings.Add(new SiteSetting(SettingsKey, json));
        else row.Replace(json, me.UserId, clock.GetUtcNow());
    }

    // ================= reading =================

    public async Task<Result<IReadOnlyList<MarketerDto>>> MarketersAsync(CancellationToken ct)
    {
        if (!CanUse) return Error.Forbidden();
        var ids = await notifier.StaffWithAsync(Permissions.CrmLeads, PermissionScope.Own, ct);
        var openStages = await db.LeadStages.AsNoTracking().Where(s => s.Kind == StageKind.Open).Select(s => s.Id).ToListAsync(ct);
        var counts = await db.Leads.AsNoTracking().Where(l => l.OwnerId != null && openStages.Contains(l.StageId)).GroupBy(l => l.OwnerId!.Value)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).OrderBy(u => u.DisplayName).Select(u => new { u.Id, u.DisplayName }).ToListAsync(ct);
        return users.Select(u => new MarketerDto(u.Id, u.DisplayName, counts.GetValueOrDefault(u.Id))).ToList();
    }

    /// <summary>Kanban: every open lead plus those closed in the last 30 days (the board never grows without bound).</summary>
    public async Task<Result<IReadOnlyList<LeadItemDto>>> BoardAsync(Guid? ownerId, string? q, CancellationToken ct)
    {
        if (!CanUse) return Error.Forbidden();
        var since = clock.GetUtcNow().AddDays(-30);
        var query = Filter(Visible().AsNoTracking(), ownerId, null, null, q).Where(l => l.ClosedAt == null || l.ClosedAt > since);
        var rows = await query.OrderByDescending(l => l.LastActivityAt).Take(500).ToListAsync(ct);
        return await ItemsAsync(rows, ct);
    }

    public async Task<Result<KeysetPage<LeadItemDto>>> ListAsync(KeysetQuery q, Guid? ownerId, Guid? stageId, Guid? sourceId, CancellationToken ct)
    {
        if (!CanUse) return Error.Forbidden();
        var query = Filter(Visible().AsNoTracking(), ownerId, stageId, sourceId, q.Search);
        var page = await Keyset.PageAsync(query, q with { Search = null }, x => x, x => x.Id, ct);
        return new KeysetPage<LeadItemDto>(await ItemsAsync(page.Items, ct), page.NextCursor);
    }

    /// <summary>"Who do I call today": follow-ups due by the end of today (Iran time), overdue first.</summary>
    public async Task<Result<IReadOnlyList<LeadItemDto>>> TodayAsync(CancellationToken ct)
    {
        if (!CanUse) return Error.Forbidden();
        var end = Iran.At(Iran.Today(clock.GetUtcNow()).AddDays(1), TimeOnly.MinValue);
        // Marketers: their own; sales managers: the whole team's (who is behind).
        var rows = await Visible().AsNoTracking().Where(l => l.NextFollowUpAt != null && l.NextFollowUpAt < end).OrderBy(l => l.NextFollowUpAt).Take(200).ToListAsync(ct);
        return await ItemsAsync(rows, ct);
    }

    /// <summary>Follow-up calendar between two dates.</summary>
    public async Task<Result<IReadOnlyList<LeadItemDto>>> CalendarAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (!CanUse) return Error.Forbidden();
        if (to <= from || to - from > TimeSpan.FromDays(62)) return Error.Validation("crm.range", "بازه حداکثر دو ماه است.");
        var rows = await Visible().AsNoTracking().Where(l => l.NextFollowUpAt >= from && l.NextFollowUpAt < to).OrderBy(l => l.NextFollowUpAt).Take(500).ToListAsync(ct);
        return await ItemsAsync(rows, ct);
    }

    public async Task<Result<LeadDetailDto>> GetAsync(Guid id, CancellationToken ct)
    {
        if (!CanUse) return Error.Forbidden();
        var l = await Visible().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return l is null ? NotFound : await DetailAsync(l, ct);
    }

    private static IQueryable<Lead> Filter(IQueryable<Lead> q, Guid? ownerId, Guid? stageId, Guid? sourceId, string? search)
    {
        if (ownerId is { } o) q = o == Guid.Empty ? q.Where(l => l.OwnerId == null) : q.Where(l => l.OwnerId == o);
        if (stageId is { } s) q = q.Where(l => l.StageId == s);
        if (sourceId is { } src) q = q.Where(l => l.SourceId == src);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = Keyset.Like(search);
            var digits = new string(search.Where(char.IsAsciiDigit).ToArray());
            q = q.Where(l => EF.Functions.ILike(l.Name, like) || EF.Functions.ILike(l.Business ?? "", like) || EF.Functions.ILike(l.Instagram ?? "", like)
                             || EF.Functions.ILike(l.City ?? "", like) || (digits.Length >= 4 && l.Phone != null && l.Phone.Contains(digits)));
        }
        return q;
    }

    // ================= writing =================

    public async Task<Result<LeadDetailDto>> CreateAsync(SaveLeadRequest r, CancellationToken ct)
    {
        if (!CanUse) return Error.Forbidden();
        var now = clock.GetUtcNow();
        var spec = Spec(r);
        if (await DuplicateAsync(spec.Phone, null, ct) is { } dup && !(r.Force && SeesAll)) return dup;
        var stage = await StageForAsync(r.StageId, ct);
        if (stage is null) return Error.Validation("crm.stage", "مرحله را انتخاب کنید.");
        if (await CheckSourceAsync(r.SourceId, ct) is { } badSource) return badSource;
        var created = Lead.Create(spec, stage.Id, now);
        if (created.IsFailure) return created.Error!;
        var l = created.Value!;
        // A marketer's own lead is theirs; managers choose (or leave it for the round-robin).
        var owner = CanAssign ? r.OwnerId : Me;
        if (owner is { } o && !await IsMarketerAsync(o, ct)) return Error.Validation("crm.owner", "این شخص بازاریاب نیست.");
        l.Assign(owner, now);
        if (r.NextFollowUpAt is { } f) l.FollowUp(f, now);
        db.Leads.Add(l);
        db.LeadActivities.Add(LeadActivity.Log(l.Id, Me, ActivityKind.Created, now));
        if (owner is { } assigned && assigned != Me) await TellOwnerAsync(l, assigned, ct);
        audit.Add("crm.leadCreated", nameof(Lead), l.Id.ToString());
        await SaveAndPingAsync(l, ct);
        return await DetailAsync(l, ct);
    }

    public async Task<Result<LeadDetailDto>> UpdateAsync(Guid id, SaveLeadRequest r, CancellationToken ct)
    {
        if (!CanUse) return Error.Forbidden();
        var l = await Visible().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return NotFound;
        if (r.Version is { } v) db.SetOriginalVersion(l, v);
        var spec = Spec(r);
        if (await DuplicateAsync(spec.Phone, id, ct) is { } dup && !(r.Force && SeesAll)) return dup;
        if (await CheckSourceAsync(r.SourceId, ct) is { } badSource) return badSource;
        var now = clock.GetUtcNow();
        var u = l.Update(spec, now);
        if (u.IsFailure) return u.Error!;
        if (r.NextFollowUpAt != l.NextFollowUpAt) l.FollowUp(r.NextFollowUpAt, now);
        await SaveAndPingAsync(l, ct);
        return await DetailAsync(l, ct);
    }

    public async Task<Result<LeadDetailDto>> MoveAsync(Guid id, MoveLeadRequest r, CancellationToken ct)
    {
        if (!CanUse) return Error.Forbidden();
        var l = await Visible().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return NotFound;
        var stage = await db.LeadStages.AsNoTracking().FirstOrDefaultAsync(s => s.Id == r.StageId && s.IsActive, ct);
        if (stage is null) return Error.Validation("crm.stage", "مرحله را انتخاب کنید.");
        if (stage.Id == l.StageId) return await DetailAsync(l, ct);
        var from = l.StageId;
        var now = clock.GetUtcNow();
        var moved = l.Move(stage, r.LostReason, now);
        if (moved.IsFailure) return moved.Error!;
        l.Touched(now);
        db.LeadActivities.Add(LeadActivity.Log(l.Id, Me, ActivityKind.StageChanged, now, l.LostReason, from, stage.Id));
        await SaveAndPingAsync(l, ct);
        return await DetailAsync(l, ct);
    }

    public async Task<Result<LeadDetailDto>> AssignAsync(Guid id, AssignLeadRequest r, CancellationToken ct)
    {
        if (!CanAssign) return Error.Forbidden();
        var l = await db.Leads.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return NotFound;
        if (r.OwnerId is { } o && !await IsMarketerAsync(o, ct)) return Error.Validation("crm.owner", "این شخص بازاریاب نیست.");
        await AssignCoreAsync(l, r.OwnerId, ct);
        await SaveAndPingAsync(l, ct);
        return await DetailAsync(l, ct);
    }

    /// <summary>Hands the given (or all unassigned open) leads out in turn to the marketers, remembering whose turn is next.</summary>
    public async Task<Result<int>> RoundRobinAsync(RoundRobinRequest r, CancellationToken ct)
    {
        if (!CanAssign) return Error.Forbidden();
        var ids = r.LeadIds ?? [];
        var openStages = await db.LeadStages.AsNoTracking().Where(s => s.Kind == StageKind.Open).Select(s => s.Id).ToListAsync(ct);
        var leads = ids.Count > 0
            ? await db.Leads.Where(l => ids.Contains(l.Id)).OrderBy(l => l.CreatedAt).ToListAsync(ct)
            : await db.Leads.Where(l => l.OwnerId == null && openStages.Contains(l.StageId)).OrderBy(l => l.CreatedAt).Take(200).ToListAsync(ct);
        var n = 0;
        foreach (var l in leads)
        {
            if (await NextInTurnAsync(ct) is not { } owner) return Error.Validation("crm.noMarketers", "هیچ بازاریابی تعریف نشده است.");
            await AssignCoreAsync(l, owner, ct);
            n++;
        }
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return n;
    }

    public async Task<Result<LeadDetailDto>> CallAsync(Guid id, CallRequest r, CancellationToken ct)
    {
        if (!CanUse) return Error.Forbidden();
        var l = await Visible().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return NotFound;
        var now = clock.GetUtcNow();
        var a = LeadActivity.Call(l.Id, Me, r.Direction, r.DurationSeconds, r.Outcome, r.Note, now);
        if (a.IsFailure) return a.Error!;
        db.LeadActivities.Add(a.Value!);
        l.Touched(now);
        if (r.NextFollowUpAt is { } f) l.FollowUp(f, now);
        else if (r.Outcome is CallOutcome.Interested or CallOutcome.Done or CallOutcome.Rejected) l.FollowUp(null, now);
        // The first answered call moves a brand-new lead to the next column.
        if (r.Outcome != CallOutcome.NoAnswer && await NextStageAfterNewAsync(l, ct) is { } next)
        {
            var from = l.StageId;
            l.Move(next, null, now);
            db.LeadActivities.Add(LeadActivity.Log(l.Id, Me, ActivityKind.StageChanged, now, null, from, next.Id));
        }
        await SaveAndPingAsync(l, ct);
        return await DetailAsync(l, ct);
    }

    public async Task<Result<LeadDetailDto>> ActivityAsync(Guid id, ActivityRequest r, CancellationToken ct)
    {
        if (!CanUse) return Error.Forbidden();
        if (r.Kind is not (ActivityKind.Message or ActivityKind.Meeting or ActivityKind.Email or ActivityKind.Note))
            return Error.Validation("crm.kind", "نوع فعالیت درست نیست.");
        if (r.Kind == ActivityKind.Message && r.Channel is null) return Error.Validation("crm.channel", "راه ارتباط را انتخاب کنید.");
        var l = await Visible().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return NotFound;
        var now = clock.GetUtcNow();
        db.LeadActivities.Add(LeadActivity.Contact(l.Id, Me, r.Kind, r.Channel, r.Note, now));
        l.Touched(now);
        if (r.NextFollowUpAt is { } f) l.FollowUp(f, now);
        await SaveAndPingAsync(l, ct);
        return await DetailAsync(l, ct);
    }

    public async Task<Result<LeadDetailDto>> FollowUpAsync(Guid id, FollowUpRequest r, CancellationToken ct)
    {
        if (!CanUse) return Error.Forbidden();
        var l = await Visible().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return NotFound;
        l.FollowUp(r.At, clock.GetUtcNow());
        await SaveAndPingAsync(l, ct);
        return await DetailAsync(l, ct);
    }

    /// <summary>"Won" → a client with one tap (or the existing client with that number); then a package is offered in the client page.</summary>
    public async Task<Result<ConvertedDto>> ConvertAsync(Guid id, CancellationToken ct)
    {
        if (!CanUse) return Error.Forbidden();
        var l = await Visible().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return NotFound;
        if (l.ClientId is { } done) return new ConvertedDto(done, true);
        if (l.Phone is null) return Error.Validation("crm.noPhone", "برای تبدیل به مشتری، شماره‌ی موبایل لازم است.");
        var now = clock.GetUtcNow();
        var existing = await db.Clients.FirstOrDefaultAsync(c => c.Mobile == l.Phone, ct);
        Guid clientId;
        if (existing is not null) clientId = existing.Id;
        else
        {
            var accounts = new List<ClientAccountSpec>();
            if (l.Instagram is { } ig && await db.Platforms.Where(p => p.Key == "instagram").Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct) is { } instagram)
                accounts.Add(new ClientAccountSpec(instagram, ig, Domain.Catalog.PageManagement.ClientManaged));
            var created = Client.Create(new ClientSpec(l.Business ?? l.Name, l.Industry, l.Name, l.Phone, l.City, $"از CRM (سرنخ {l.CreatedAt:yyyy-MM-dd})", ClientStatus.Active, accounts));
            if (created.IsFailure) return created.Error!;
            db.Clients.Add(created.Value!);
            clientId = created.Value!.Id;
        }
        l.ConvertedTo(clientId, now);
        if (await db.LeadStages.AsNoTracking().Where(s => s.Kind == StageKind.Won && s.IsActive).OrderBy(s => s.SortOrder).FirstOrDefaultAsync(ct) is { } won && l.StageId != won.Id)
        {
            var from = l.StageId;
            l.Move(won, null, now);
            db.LeadActivities.Add(LeadActivity.Log(l.Id, Me, ActivityKind.StageChanged, now, null, from, won.Id));
        }
        db.LeadActivities.Add(LeadActivity.Log(l.Id, Me, ActivityKind.Converted, now, null, null, clientId));
        audit.Add("crm.converted", nameof(Lead), l.Id.ToString(), new { clientId, existing = existing is not null });
        var managers = await notifier.StaffWithAsync(Permissions.PackagesAssign, PermissionScope.All, ct);
        await notifier.NotifyAsync(managers.Where(m => m != Me), "crm.won", $"مشتری تازه: {l.Business ?? l.Name}", "پکیج مناسب را در صفحه‌ی مشتری تخصیص دهید.", "/clients", false,
            $"crm-won:{l.Id:N}", ct);
        await SaveAndPingAsync(l, ct);
        return new ConvertedDto(clientId, existing is not null);
    }

    // ================= inbound (website form, bot) =================

    /// <summary>
    /// A request from outside (website form or the bot). An open lead with the same phone (or chat) gets the message
    /// appended instead of a duplicate. New leads are handed out in turn when auto-assign is on.
    /// </summary>
    public async Task<Lead?> IngestAsync(string name, string? phone, string? business, string? message, string? origin, string sourceName, long? chatId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        string? normalized = null;
        if (phone is not null && PhoneNumber.TryNormalize(phone, out var p)) normalized = p;
        var openStages = await db.LeadStages.AsNoTracking().Where(s => s.Kind == StageKind.Open).Select(s => s.Id).ToListAsync(ct);
        var existing = await db.Leads.Where(l => openStages.Contains(l.StageId) && ((normalized != null && l.Phone == normalized) || (chatId != null && l.MessengerChatId == chatId)))
            .OrderByDescending(l => l.LastActivityAt).FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(message)) existing.AppendMessage(message, now);
            if (normalized is not null && existing.Phone is null) existing.SetPhone(normalized, now);
            db.LeadActivities.Add(LeadActivity.Log(existing.Id, null, ActivityKind.Inbound, now, message));
            var to = existing.OwnerId is { } o ? [o] : await notifier.StaffWithAsync(Permissions.CrmAssign, PermissionScope.All, ct);
            await notifier.NotifyAsync(to, "crm.inbound", $"پیام تازه از {existing.Name}", message, "/crm", false, $"crm-in:{existing.Id:N}:{now.ToUnixTimeSeconds() / 600}", ct);
            await SaveAndPingAsync(existing, ct);
            return existing;
        }

        var stage = await db.LeadStages.AsNoTracking().Where(s => s.Kind == StageKind.Open && s.IsActive).OrderBy(s => s.SortOrder).FirstOrDefaultAsync(ct);
        if (stage is null) return null;
        var source = await db.LeadSources.FirstOrDefaultAsync(s => s.Name == sourceName, ct);
        if (source is null)
        {
            source = LeadSource.Create(sourceName, 100).Value!;
            db.LeadSources.Add(source);
        }
        var created = Lead.Create(new LeadSpec(name, normalized, business, null, null, null, source.Id, null, null, null), stage.Id, now, message, origin, chatId);
        if (created.IsFailure) return null;
        var lead = created.Value!;
        db.Leads.Add(lead);
        db.LeadActivities.Add(LeadActivity.Log(lead.Id, null, ActivityKind.Created, now, sourceName));
        var settings = await SettingsAsync(ct);
        if (settings.AutoAssign && await NextInTurnAsync(ct) is { } owner) await AssignCoreAsync(lead, owner, ct, actor: null);
        var who = lead.OwnerId is { } assigned ? [assigned] : await notifier.StaffWithAsync(Permissions.CrmAssign, PermissionScope.All, ct);
        await notifier.NotifyAsync(who, "crm.newLead", $"سرنخ تازه ({sourceName}): {lead.Name}", lead.Business, "/crm", true, $"crm-lead:{lead.Id:N}", ct);
        await SaveAndPingAsync(lead, ct);
        return lead;
    }

    /// <summary>Daily-ish job: follow-ups that came due are reminded once (app, bot, SMS).</summary>
    public async Task<int> RemindFollowUpsAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = await db.Leads.Where(l => l.NextFollowUpAt != null && l.NextFollowUpAt <= now && (l.FollowUpRemindedFor == null || l.FollowUpRemindedFor != l.NextFollowUpAt))
            .Take(200).ToListAsync(ct);
        var assigners = due.Any(l => l.OwnerId is null) ? await notifier.StaffWithAsync(Permissions.CrmAssign, PermissionScope.All, ct) : [];
        foreach (var l in due)
        {
            if (!l.NeedsFollowUpReminder(now)) continue;
            var to = l.OwnerId is { } o ? [o] : assigners;
            await notifier.NotifyAsync(to, "crm.followUp", $"وقت پیگیری: {l.Name}{(l.Business is null ? "" : $" ({l.Business})")}", l.Phone, "/crm", true,
                $"crm-follow:{l.Id:N}:{l.NextFollowUpAt!.Value.ToUnixTimeSeconds()}", ct);
        }
        await db.SaveChangesAsync(ct);
        return due.Count;
    }

    // ================= report =================

    public async Task<Result<CrmReportDto>> ReportAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (!CanUse) return Error.Forbidden();
        if (to < from || to.DayNumber - from.DayNumber > 366) return Error.Validation("crm.range", "بازه درست نیست (حداکثر یک سال).");
        var start = Iran.At(from, TimeOnly.MinValue);
        var end = Iran.At(to.AddDays(1), TimeOnly.MinValue);
        var stages = await db.LeadStages.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct);
        var won = stages.Where(s => s.Kind == StageKind.Won).Select(s => s.Id).ToHashSet();
        var lost = stages.Where(s => s.Kind == StageKind.Lost).Select(s => s.Id).ToHashSet();

        // Marketers see their own row only; managers see everyone.
        var leads = await Visible().AsNoTracking().Where(l => l.CreatedAt >= start && l.CreatedAt < end).ToListAsync(ct);
        var closed = await Visible().AsNoTracking().Where(l => l.ClosedAt >= start && l.ClosedAt < end).ToListAsync(ct);
        var leadIds = leads.Select(l => l.Id).ToList();
        var moves = await db.LeadActivities.AsNoTracking().Where(a => leadIds.Contains(a.LeadId) && a.Kind == ActivityKind.StageChanged && a.ToId != null)
            .Select(a => new { a.LeadId, To = a.ToId!.Value }).ToListAsync(ct);
        var calls = await db.LeadActivities.AsNoTracking().Where(a => a.Kind == ActivityKind.Call && a.At >= start && a.At < end && (SeesAll || a.ActorId == Me))
            .Select(a => new { a.ActorId, a.DurationSeconds }).ToListAsync(ct);

        var marketerIds = SeesAll ? await notifier.StaffWithAsync(Permissions.CrmLeads, PermissionScope.Own, ct) : [Me];
        var names = await db.Users.AsNoTracking().Where(u => marketerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var settings = await SettingsAsync(ct);
        // Sales: packages of the clients a marketer converted, sold in the range.
        var converted = await Visible().AsNoTracking().Where(l => l.ClientId != null && l.OwnerId != null).Select(l => new { l.OwnerId, l.ClientId }).ToListAsync(ct);
        var clientIds = converted.Select(c => c.ClientId!.Value).Distinct().ToList();
        var sales = await db.ClientPackages.AsNoTracking().Where(p => clientIds.Contains(p.ClientId) && p.StartsAt >= start && p.StartsAt < end && p.Status != ClientPackageStatus.Cancelled)
            .Select(p => new { p.ClientId, p.Price }).ToListAsync(ct);
        var marketers = marketerIds.Distinct().Select(id =>
        {
            var mine = converted.Where(c => c.OwnerId == id).Select(c => c.ClientId!.Value).ToHashSet();
            var total = sales.Where(s => mine.Contains(s.ClientId)).Sum(s => s.Price);
            var myCalls = calls.Where(c => c.ActorId == id).ToList();
            return new MarketerReportDto(id, names.GetValueOrDefault(id, "—"), leads.Count(l => l.OwnerId == id), myCalls.Count, myCalls.Sum(c => c.DurationSeconds ?? 0) / 60,
                closed.Count(l => l.OwnerId == id && won.Contains(l.StageId)), closed.Count(l => l.OwnerId == id && lost.Contains(l.StageId)), total,
                settings.CommissionEnabled ? (long)Math.Round(total * settings.CommissionPercent / 100m) : null);
        }).OrderByDescending(m => m.Sales).ThenByDescending(m => m.Won).ToList();

        // A lead "reached" a column if it is there now or passed through it; reaching a later column (or winning)
        // counts the earlier open columns too, because people skip columns.
        var order = stages.ToDictionary(s => s.Id);
        var reached = leads.ToDictionary(l => l.Id, l => new HashSet<Guid> { l.StageId });
        foreach (var m in moves) reached[m.LeadId].Add(m.To);
        int Furthest(HashSet<Guid> r) => r.Select(x => order.TryGetValue(x, out var st) ? (st.Kind == StageKind.Open ? st.SortOrder : st.Kind == StageKind.Won ? int.MaxValue : int.MinValue) : int.MinValue).Max();
        var stageRows = stages.Where(s => s.IsActive).Select(s =>
        {
            var n = s.Kind == StageKind.Open
                ? reached.Values.Count(r => r.Contains(s.Id) || Furthest(r) >= s.SortOrder)
                : reached.Values.Count(r => r.Contains(s.Id));
            return new StageReportDto(s.Id, s.Name, n, leads.Count == 0 ? 0 : Math.Round(100.0 * n / leads.Count, 1));
        }).ToList();

        var sourceNames = await db.LeadSources.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var sources = leads.GroupBy(l => l.SourceId).Select(g =>
        {
            var w = g.Count(l => won.Contains(l.StageId));
            return new SourceReportDto(g.Key, g.Key is { } k ? sourceNames.GetValueOrDefault(k, "—") : "نامشخص", g.Count(), w, Math.Round(100.0 * w / g.Count(), 1));
        }).OrderByDescending(s => s.Rate).ThenByDescending(s => s.Leads).ToList();

        return new CrmReportDto(from, to, leads.Count, marketers, stageRows, sources);
    }

    // ================= helpers =================

    private static LeadSpec Spec(SaveLeadRequest r) => new(r.Name, r.Phone, r.Business, r.Industry, r.City, r.Instagram, r.SourceId, r.Tags, r.Budget, r.Note);

    /// <summary>Same number already in the CRM: say whose it is only if I may see it.</summary>
    private async Task<Error?> DuplicateAsync(string? phone, Guid? self, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone) || !PhoneNumber.TryNormalize(phone, out var normalized)) return null;
        var other = await db.Leads.AsNoTracking().Where(l => l.Phone == normalized && l.Id != self).Select(l => new { l.Id, l.Name, l.OwnerId }).FirstOrDefaultAsync(ct);
        if (other is null) return null;
        return SeesAll || other.OwnerId == Me
            ? Error.Conflict("crm.duplicate", $"این شماره قبلاً ثبت شده است: «{other.Name}». همان را باز کنید.")
            : Error.Conflict("crm.duplicate", "این شماره قبلاً برای همکار دیگری ثبت شده است.");
    }

    private async Task<LeadStage?> StageForAsync(Guid? stageId, CancellationToken ct) => stageId is { } id
        ? await db.LeadStages.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.IsActive, ct)
        : await db.LeadStages.AsNoTracking().Where(s => s.Kind == StageKind.Open && s.IsActive).OrderBy(s => s.SortOrder).FirstOrDefaultAsync(ct);

    private async Task<Error?> CheckSourceAsync(Guid? sourceId, CancellationToken ct) =>
        sourceId is { } s && !await db.LeadSources.AnyAsync(x => x.Id == s, ct) ? Error.Validation("crm.source", "منبع پیدا نشد.") : null;

    private async Task<bool> IsMarketerAsync(Guid userId, CancellationToken ct) =>
        (await notifier.StaffWithAsync(Permissions.CrmLeads, PermissionScope.Own, ct)).Contains(userId);

    private async Task<LeadStage?> NextStageAfterNewAsync(Lead l, CancellationToken ct)
    {
        var open = await db.LeadStages.AsNoTracking().Where(s => s.Kind == StageKind.Open && s.IsActive).OrderBy(s => s.SortOrder).ToListAsync(ct);
        return open.Count > 1 && open[0].Id == l.StageId ? open[1] : null;
    }

    private async Task<Guid?> NextInTurnAsync(CancellationToken ct)
    {
        // Marketers = holders of the "own leads" scope only (managers with every lead aren't in the rotation).
        var all = await notifier.StaffWithAsync(Permissions.CrmLeads, PermissionScope.Own, ct);
        var everyone = await notifier.StaffWithAsync(Permissions.CrmLeads, PermissionScope.All, ct);
        var rotation = all.Except(everyone).Order().ToList();
        if (rotation.Count == 0) rotation = all.Order().ToList();
        if (rotation.Count == 0) return null;
        var settings = await SettingsAsync(ct);
        var i = settings.LastAssignedId is { } last ? rotation.IndexOf(last) : -1;
        var next = rotation[(i + 1) % rotation.Count];
        await WriteSettingsAsync(settings with { LastAssignedId = next }, ct);
        return next;
    }

    private async Task AssignCoreAsync(Lead l, Guid? owner, CancellationToken ct, Guid? actor = default)
    {
        if (l.OwnerId == owner) return;
        var now = clock.GetUtcNow();
        var previous = l.OwnerId;
        l.Assign(owner, now);
        db.LeadActivities.Add(LeadActivity.Log(l.Id, actor == default ? Me : actor, ActivityKind.Assigned, now, null, previous, owner));
        if (owner is { } o) await TellOwnerAsync(l, o, ct);
        changes.Record("lead", l.Id, null, owner, previous, "owner");
    }

    private async Task TellOwnerAsync(Lead l, Guid owner, CancellationToken ct) =>
        await notifier.NotifyAsync([owner], "crm.assigned", $"سرنخ به شما سپرده شد: {l.Name}", l.Business, "/crm", false, $"crm-assign:{l.Id:N}:{owner:N}", ct);

    private async Task SaveAndPingAsync(Lead l, CancellationToken ct)
    {
        changes.Record("lead", l.Id, null, l.OwnerId, null, "lead");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
    }

    private async Task<List<LeadItemDto>> ItemsAsync(IReadOnlyList<Lead> rows, CancellationToken ct)
    {
        var ownerIds = rows.Where(r => r.OwnerId is not null).Select(r => r.OwnerId!.Value).Distinct().ToList();
        var owners = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => ownerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var sources = await db.LeadSources.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return rows.Select(l => Item(l, owners, sources)).ToList();
    }

    private static LeadItemDto Item(Lead l, Dictionary<Guid, string> owners, Dictionary<Guid, string> sources) =>
        new(l.Id, l.Name, l.Business, l.Phone, l.City, l.Instagram, l.SourceId, l.SourceId is { } s ? sources.GetValueOrDefault(s) : null, l.StageId, l.OwnerId,
            l.OwnerId is { } o ? owners.GetValueOrDefault(o) : null, l.Tags, l.Budget, l.NextFollowUpAt, l.LastActivityAt, l.CreatedAt, l.ClientId, l.Version);

    private async Task<LeadDetailDto> DetailAsync(Lead l, CancellationToken ct)
    {
        var acts = await db.LeadActivities.AsNoTracking().Where(a => a.LeadId == l.Id).OrderByDescending(a => a.Id).Take(100).ToListAsync(ct);
        var userIds = acts.Where(a => a.ActorId is not null).Select(a => a.ActorId!.Value)
            .Concat(acts.Where(a => a.Kind == ActivityKind.Assigned).SelectMany(a => new[] { a.FromId, a.ToId }).Where(x => x is not null).Select(x => x!.Value))
            .Append(l.OwnerId ?? Guid.Empty).Distinct().ToList();
        var users = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var stages = await db.LeadStages.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var sources = await db.LeadSources.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        string? NameOf(ActivityKind k, Guid? id) => id is not { } v ? null : k == ActivityKind.StageChanged ? stages.GetValueOrDefault(v) : k == ActivityKind.Assigned ? users.GetValueOrDefault(v) : null;
        return new LeadDetailDto(Item(l, users, sources), l.Industry, l.Message, l.Origin, l.Note, l.LostReason,
            acts.Select(a => new ActivityDto(a.Id, a.Kind, a.ActorId is { } x ? users.GetValueOrDefault(x) : null, a.Direction, a.DurationSeconds, a.Outcome, a.Channel, a.Note,
                NameOf(a.Kind, a.FromId), NameOf(a.Kind, a.ToId), a.At)).ToList());
    }
}
