namespace Karnama.Domain.Crm;

/// <summary>The starting pipeline and sources (editable in the panel). Fixed ids so the N7 migration can map old leads.</summary>
public static class CrmDefaults
{
    public static readonly Guid New = new("0193a000-0000-7000-8000-000000000001");
    public static readonly Guid FirstCall = new("0193a000-0000-7000-8000-000000000002");
    public static readonly Guid Meeting = new("0193a000-0000-7000-8000-000000000003");
    public static readonly Guid Proposal = new("0193a000-0000-7000-8000-000000000004");
    public static readonly Guid Negotiation = new("0193a000-0000-7000-8000-000000000005");
    public static readonly Guid Won = new("0193a000-0000-7000-8000-000000000006");
    public static readonly Guid Lost = new("0193a000-0000-7000-8000-000000000007");

    public static IReadOnlyList<(Guid Id, string Name, StageKind Kind, int Order)> Stages { get; } =
    [
        (New, "جدید", StageKind.Open, 10),
        (FirstCall, "تماس اول", StageKind.Open, 20),
        (Meeting, "جلسه / مشاوره", StageKind.Open, 30),
        (Proposal, "پیشنهاد قیمت", StageKind.Open, 40),
        (Negotiation, "مذاکره", StageKind.Open, 50),
        (Won, "بسته شد (برد)", StageKind.Won, 60),
        (Lost, "از دست رفت", StageKind.Lost, 70),
    ];

    public static IReadOnlyList<string> Sources { get; } = ["معرفی", "اینستاگرام", "تماس ورودی", "فرم سایت", "حضوری", "ربات"];
}
