using Karnama.Domain.Common;

namespace Karnama.Domain.Catalog;

/// <summary>
/// A sellable service (reel, single post, carousel, story, caption, influencer campaign, monthly page management, …).
/// It owns an order form (versioned JSON schema) and an ordered workflow of stages.
/// </summary>
public sealed class ServiceType : AuditableEntity
{
    public const int MaxStages = 20;
    private readonly List<WorkflowStage> _stages = [];

    private ServiceType() { Name = new LocalizedText(); FormSchemaJson = "[]"; }

    public LocalizedText Name { get; private set; }
    public string? Description { get; private set; }
    public string? Icon { get; private set; }
    /// <summary>Optional base price in rials (never double).</summary>
    public long? BasePrice { get; private set; }
    /// <summary>Whether an order of this service uses up package quota.</summary>
    public bool ConsumesQuota { get; private set; } = true;
    /// <summary>Weight for workload and scoring (a reel weighs more than a story).</summary>
    public int Weight { get; private set; } = 1;
    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }

    /// <summary>Current order-form fields (JSON array). Every change creates a new <see cref="FormVersion"/>.</summary>
    public string FormSchemaJson { get; private set; }
    public int FormVersion { get; private set; }

    public uint Version { get; private set; }
    public IReadOnlyCollection<WorkflowStage> Stages => _stages;

    public static Result<ServiceType> Create(LocalizedText name, string? description, string? icon, long? basePrice, bool consumesQuota, int weight, int sortOrder)
    {
        var s = new ServiceType();
        var r = s.Update(name, description, icon, basePrice, consumesQuota, weight, sortOrder, true);
        return r.IsSuccess ? s : r.Error!;
    }

    public Result Update(LocalizedText name, string? description, string? icon, long? basePrice, bool consumesQuota, int weight, int sortOrder, bool isActive)
    {
        if (name.IsEmpty) return Error.Validation("services.nameRequired", "نام خدمت الزامی است.");
        if (basePrice is < 0) return Error.Validation("services.invalidPrice", "قیمت نمی‌تواند منفی باشد.");
        if (weight is < 1 or > 100) return Error.Validation("services.invalidWeight", "وزن باید بین ۱ تا ۱۰۰ باشد.");
        Name = name;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Icon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();
        BasePrice = basePrice;
        ConsumesQuota = consumesQuota;
        Weight = weight;
        SortOrder = sortOrder;
        IsActive = isActive;
        return Result.Success();
    }

    /// <summary>Sets a new form; returns the new version number, or the current one when nothing changed.</summary>
    public int SetForm(string normalizedSchemaJson)
    {
        if (normalizedSchemaJson == FormSchemaJson && FormVersion > 0) return FormVersion;
        FormSchemaJson = normalizedSchemaJson;
        FormVersion++;
        return FormVersion;
    }

    public Result SetStages(IReadOnlyList<StageSpec> specs)
    {
        if (specs.Count == 0) return Error.Validation("services.stagesRequired", "حداقل یک مرحله‌ی کار لازم است.");
        if (specs.Count > MaxStages) return Error.Validation("services.tooManyStages", "حداکثر ۲۰ مرحله.");
        foreach (var s in specs)
        {
            if (s.Name.IsEmpty) return Error.Validation("services.stageName", "نام هر مرحله الزامی است.");
            if (s.EstimatedMinutes is < 0 or > 100_000) return Error.Validation("services.stageEstimate", "زمان تخمینی معتبر نیست.");
            if (s.SlaHours is < 0 or > 10_000) return Error.Validation("services.stageSla", "مهلت (SLA) معتبر نیست.");
        }

        // Keep ids of stages that still exist (by id) so history stays linked; add new ones; remove the rest.
        var keep = specs.Where(s => s.Id is not null).Select(s => s.Id!.Value).ToHashSet();
        _stages.RemoveAll(s => !keep.Contains(s.Id));
        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var existing = spec.Id is { } id ? _stages.Find(s => s.Id == id) : null;
            if (existing is null) _stages.Add(WorkflowStage.Create(Id, i + 1, spec));
            else existing.Apply(i + 1, spec);
        }
        return Result.Success();
    }

    public void Delete(Guid? actor, DateTimeOffset now) => MarkDeleted(actor, now);
    public void Restore() => MarkRestored();
}

public sealed record StageSpec(Guid? Id, LocalizedText Name, Guid? RoleId, int EstimatedMinutes, int? SlaHours, bool NeedsAdminReview, bool NeedsClientApproval);

public sealed class WorkflowStage : Entity
{
    private WorkflowStage() { Name = new LocalizedText(); }

    public Guid ServiceTypeId { get; private set; }
    public int Order { get; private set; }
    public LocalizedText Name { get; private set; }
    /// <summary>Role responsible for this stage (e.g. "Video editor"); used to suggest who to assign.</summary>
    public Guid? RoleId { get; private set; }
    public int EstimatedMinutes { get; private set; }
    public int? SlaHours { get; private set; }
    public bool NeedsAdminReview { get; private set; }
    public bool NeedsClientApproval { get; private set; }

    internal static WorkflowStage Create(Guid serviceTypeId, int order, StageSpec spec)
    {
        var s = new WorkflowStage { ServiceTypeId = serviceTypeId };
        s.Apply(order, spec);
        return s;
    }

    internal void Apply(int order, StageSpec spec)
    {
        Order = order;
        Name = spec.Name;
        RoleId = spec.RoleId;
        EstimatedMinutes = spec.EstimatedMinutes;
        SlaHours = spec.SlaHours;
        NeedsAdminReview = spec.NeedsAdminReview;
        NeedsClientApproval = spec.NeedsClientApproval;
    }
}

/// <summary>Append-only history of order forms: old orders are read with the form version they were placed with.</summary>
public sealed class ServiceFormVersion
{
    private ServiceFormVersion() { SchemaJson = "[]"; }

    public ServiceFormVersion(Guid serviceTypeId, int version, string schemaJson, DateTimeOffset createdAt)
    {
        ServiceTypeId = serviceTypeId;
        Version = version;
        SchemaJson = schemaJson;
        CreatedAt = createdAt;
    }

    public Guid ServiceTypeId { get; private set; }
    public int Version { get; private set; }
    public string SchemaJson { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
