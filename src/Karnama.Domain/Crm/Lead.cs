using Karnama.Domain.Common;

namespace Karnama.Domain.Crm;

public enum LeadStatus { New = 1, Contacted = 2, Won = 3, Lost = 4 }

/// <summary>A consultation request from the website (the start of the CRM, N7).</summary>
public sealed class Lead : Entity
{
    private Lead() { Name = string.Empty; Phone = string.Empty; }

    public string Name { get; private set; }
    public string Phone { get; private set; }
    public string? Business { get; private set; }
    public string? Message { get; private set; }
    public string? Source { get; private set; }
    public LeadStatus Status { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public static Result<Lead> Create(string name, string phone, string? business, string? message, string? source, DateTimeOffset now)
    {
        name = name?.Trim() ?? "";
        if (name.Length is < 2 or > 60) return Error.Validation("leads.name", "نام را بنویسید.");
        if (!PhoneNumber.TryNormalize(phone, out var normalized)) return Error.Validation("leads.phone", "شماره‌ی موبایل درست نیست (مثلاً ۰۹۱۲۳۴۵۶۷۸۹).");
        return new Lead
        {
            Name = name, Phone = normalized, Business = Cut(business, 80), Message = Cut(message, 1000), Source = Cut(source, 200), Status = LeadStatus.New, CreatedAt = now,
        };
    }

    private static string? Cut(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(max, s.Trim().Length)];

    public void Update(LeadStatus status, string? note, DateTimeOffset now)
    {
        Status = status; Note = Cut(note, 1000); UpdatedAt = now;
    }
}
