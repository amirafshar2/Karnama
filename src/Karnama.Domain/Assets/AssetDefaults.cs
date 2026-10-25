namespace Karnama.Domain.Assets;

/// <summary>Starting equipment categories (PROMPT2 §3), editable in the panel. Fixed ids so demo data can refer to them.</summary>
public static class AssetDefaults
{
    public static readonly (Guid Id, string Name)[] Categories =
    [
        (new("0193a100-0000-7000-8000-000000000001"), "لپ‌تاپ"),
        (new("0193a100-0000-7000-8000-000000000002"), "دوربین"),
        (new("0193a100-0000-7000-8000-000000000003"), "لنز"),
        (new("0193a100-0000-7000-8000-000000000004"), "میکروفون"),
        (new("0193a100-0000-7000-8000-000000000005"), "نور"),
        (new("0193a100-0000-7000-8000-000000000006"), "سه‌پایه و گیمبال"),
        (new("0193a100-0000-7000-8000-000000000007"), "کارت حافظه و هارد"),
        (new("0193a100-0000-7000-8000-000000000008"), "سیم‌کارت و گوشی"),
        (new("0193a100-0000-7000-8000-000000000009"), "سایر"),
    ];

    public static Guid Laptop => Categories[0].Id;
    public static Guid Camera => Categories[1].Id;
    public static Guid Lens => Categories[2].Id;
    public static Guid Mic => Categories[3].Id;
    public static Guid Light => Categories[4].Id;
    public static Guid Tripod => Categories[5].Id;
    public static Guid Storage => Categories[6].Id;
}
