using Karnama.Application.Abstractions;
using Karnama.Application.Common;
using Karnama.Domain.Catalog;
using Karnama.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Catalog;

public sealed record StageDto(Guid? Id, Dictionary<string, string> Name, Guid? RoleId, int EstimatedMinutes, int? SlaHours, bool NeedsAdminReview, bool NeedsClientApproval);

public sealed record ServiceTypeListItemDto(Guid Id, string Name, string? Icon, long? BasePrice, bool ConsumesQuota, int StageCount, int FieldCount, bool IsActive, bool IsDeleted, uint Version);

public sealed record ServiceTypeDto(
    Guid Id, IReadOnlyDictionary<string, string> NameValues, string? Description, string? Icon, long? BasePrice, bool ConsumesQuota,
    int Weight, int SortOrder, bool IsActive, IReadOnlyList<FormField> Form, int FormVersion, IReadOnlyList<StageDto> Stages, bool IsDeleted, uint Version);

public sealed record UpsertServiceTypeRequest(
    Dictionary<string, string> Name, string? Description, string? Icon, long? BasePrice, bool ConsumesQuota, int Weight, int SortOrder,
    bool IsActive, List<FormField>? Form, List<StageDto> Stages, uint? Version);

public sealed class ServiceTypeService(IAppDbContext db, ICurrentUser me, IAuditWriter audit, TimeProvider clock, IExcelExporter excel)
{
    public Task<KeysetPage<ServiceTypeListItemDto>> ListAsync(KeysetQuery q, CancellationToken ct)
    {
        var query = q.IncludeDeleted ? db.ServiceTypes.IgnoreQueryFilters() : db.ServiceTypes;
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var like = Keyset.Like(q.Search);
            query = query.Where(s => EF.Functions.ILike(EF.Property<string>(s, "NameSearch"), like) || EF.Functions.ILike(s.Description ?? "", like));
        }
        var lang = me.LanguageCode;
        return ProjectAsync(query, q, lang, ct);
    }

    private static async Task<KeysetPage<ServiceTypeListItemDto>> ProjectAsync(IQueryable<ServiceType> query, KeysetQuery q, string lang, CancellationToken ct)
    {
        var page = await Keyset.PageAsync(query.AsNoTracking(), q,
            x => x.Select(s => new { s.Id, s.Name, s.Icon, s.BasePrice, s.ConsumesQuota, Stages = s.Stages.Count, s.FormSchemaJson, s.IsActive, s.IsDeleted, s.Version }),
            r => r.Id, ct);
        return new KeysetPage<ServiceTypeListItemDto>(
            page.Items.Select(r => new ServiceTypeListItemDto(r.Id, r.Name.Get(lang), r.Icon, r.BasePrice, r.ConsumesQuota, r.Stages,
                FormSchema.Parse(r.FormSchemaJson).Count, r.IsActive, r.IsDeleted, r.Version)).ToList(),
            page.NextCursor);
    }

    public async Task<IReadOnlyList<(Guid Id, string Name)>> OptionsAsync(CancellationToken ct)
    {
        var rows = await db.ServiceTypes.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.SortOrder).Select(s => new { s.Id, s.Name }).ToListAsync(ct);
        return rows.Select(r => (r.Id, r.Name.Get(me.LanguageCode))).ToList();
    }

    public async Task<Result<ServiceTypeDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var s = await db.ServiceTypes.IgnoreQueryFilters().AsNoTracking().Include(x => x.Stages).FirstOrDefaultAsync(x => x.Id == id, ct);
        return s is null ? Error.NotFound("services.notFound", "خدمت پیدا نشد.") : ToDto(s);
    }

    public async Task<Result<ServiceTypeDto>> CreateAsync(UpsertServiceTypeRequest r, CancellationToken ct)
    {
        var created = ServiceType.Create(new LocalizedText(r.Name), r.Description, r.Icon, r.BasePrice, r.ConsumesQuota, r.Weight, r.SortOrder);
        if (created.IsFailure) return created.Error!;
        var s = created.Value;
        var applied = await ApplyAsync(s, r, ct);
        if (applied.IsFailure) return applied.Error!;
        db.ServiceTypes.Add(s);
        audit.Add("services.created", nameof(ServiceType), s.Id.ToString(), new { r.Name, stages = r.Stages.Count, fields = r.Form?.Count ?? 0 });
        await db.SaveChangesAsync(ct);
        return ToDto(s);
    }

    public async Task<Result<ServiceTypeDto>> UpdateAsync(Guid id, UpsertServiceTypeRequest r, CancellationToken ct)
    {
        var s = await db.ServiceTypes.Include(x => x.Stages).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return Error.NotFound("services.notFound", "خدمت پیدا نشد.");
        if (r.Version is not { } v) return Error.Validation("common.versionRequired", "نسخه‌ی رکورد لازم است؛ صفحه را تازه کنید.");
        db.SetOriginalVersion(s, v);
        var u = s.Update(new LocalizedText(r.Name), r.Description, r.Icon, r.BasePrice, r.ConsumesQuota, r.Weight, r.SortOrder, r.IsActive);
        if (u.IsFailure) return u.Error!;
        var applied = await ApplyAsync(s, r, ct);
        if (applied.IsFailure) return applied.Error!;
        db.MarkModified(s);
        audit.Add("services.updated", nameof(ServiceType), id.ToString(), new { r.Name, stages = r.Stages.Count, formVersion = s.FormVersion });
        await db.SaveChangesAsync(ct);
        return ToDto(s);
    }

    private async Task<Result> ApplyAsync(ServiceType s, UpsertServiceTypeRequest r, CancellationToken ct)
    {
        var schema = FormSchema.Normalize(r.Form);
        if (schema.IsFailure) return schema.Error!;
        // jsonb re-formats stored JSON, so compare canonical forms, not raw strings.
        var current = s.FormVersion == 0 ? null : FormSchema.Normalize(FormSchema.Parse(s.FormSchemaJson)).Value;
        if (current != schema.Value)
        {
            var version = s.SetForm(schema.Value);
            db.ServiceFormVersions.Add(new ServiceFormVersion(s.Id, version, schema.Value, clock.GetUtcNow()));
        }

        var roleIds = r.Stages.Where(x => x.RoleId is not null).Select(x => x.RoleId!.Value).Distinct().ToList();
        if (roleIds.Count > 0 && await db.Roles.CountAsync(x => roleIds.Contains(x.Id), ct) != roleIds.Count)
            return Error.Validation("services.unknownRole", "نقش انتخاب‌شده برای یکی از مراحل وجود ندارد.");
        return s.SetStages(r.Stages.Select(x => new StageSpec(x.Id, new LocalizedText(x.Name), x.RoleId, x.EstimatedMinutes, x.SlaHours, x.NeedsAdminReview, x.NeedsClientApproval)).ToList());
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        var s = await db.ServiceTypes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return Error.NotFound("services.notFound", "خدمت پیدا نشد.");
        s.Delete(me.UserId, clock.GetUtcNow());
        audit.Add("services.deleted", nameof(ServiceType), id.ToString());
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<ServiceTypeDto>> RestoreAsync(Guid id, CancellationToken ct)
    {
        var s = await db.ServiceTypes.IgnoreQueryFilters().Include(x => x.Stages).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return Error.NotFound("services.notFound", "خدمت پیدا نشد.");
        s.Restore();
        audit.Add("services.restored", nameof(ServiceType), id.ToString());
        await db.SaveChangesAsync(ct);
        return ToDto(s);
    }

    public async Task<byte[]> ExportAsync(CancellationToken ct)
    {
        var rows = await db.ServiceTypes.AsNoTracking().Include(s => s.Stages).OrderBy(s => s.SortOrder).ToListAsync(ct);
        return excel.Export("Services", ["نام", "قیمت پایه (ریال)", "کم از پکیج", "وزن", "مراحل", "تعداد فیلد فرم", "فعال"],
            rows.Select(s => (IReadOnlyList<object?>)[s.Name.Get(me.LanguageCode), s.BasePrice, s.ConsumesQuota ? "بله" : "نه", s.Weight,
                string.Join(" ← ", s.Stages.OrderBy(x => x.Order).Select(x => x.Name.Get(me.LanguageCode))), FormSchema.Parse(s.FormSchemaJson).Count, s.IsActive ? "بله" : "نه"]));
    }

    private ServiceTypeDto ToDto(ServiceType s) => new(
        s.Id, s.Name.Values, s.Description, s.Icon, s.BasePrice, s.ConsumesQuota, s.Weight, s.SortOrder, s.IsActive,
        FormSchema.Parse(s.FormSchemaJson), s.FormVersion,
        s.Stages.OrderBy(x => x.Order).Select(x => new StageDto(x.Id, x.Name.Values.ToDictionary(), x.RoleId, x.EstimatedMinutes, x.SlaHours, x.NeedsAdminReview, x.NeedsClientApproval)).ToList(),
        s.IsDeleted, s.Version);
}
