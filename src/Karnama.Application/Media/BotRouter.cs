using System.Text.RegularExpressions;
using Karnama.Application.Abstractions;
using Karnama.Application.Notify;
using Karnama.Domain.Common;
using Karnama.Domain.Notify;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Media;

/// <summary>
/// Entry point for every bot update (Bale webhook, Telegram via the gateway): files and buttons, then text commands.
/// A message from someone who is not a linked user is a sales lead (PROMPT2 §4 "bot messages create leads").
/// </summary>
public sealed partial class BotRouter(BotUpdateService links, MediaService media, IAppDbContext db, Crm.CrmService crm)
{
    [GeneratedRegex(@"(?:\+98|0098|0)?9\d{9}")]
    private static partial Regex MobileInText();

    /// <returns>A plain-text reply for that chat, or null.</returns>
    public async Task<(long ChatId, string? Reply)> HandleAsync(Messenger m, string json, CancellationToken ct)
    {
        if (ParsedUpdate.Parse(json) is not { } u) return (0, null);
        if (!await links.ClaimAsync(m, u.UpdateId, ct)) return (u.ChatId, null);
        if (await media.HandleAsync(m, u, ct)) return (u.ChatId, null);
        var text = (u.Text ?? "").Trim();
        if (text.StartsWith('/') || (text.Length == 0 && u.ContactPhone is null)) return (u.ChatId, await links.HandleTextAsync(m, u.ChatId, u.Text, ct));
        if (await db.MessengerLinks.AsNoTracking().AnyAsync(l => l.Messenger == m && l.ChatId == u.ChatId, ct)) return (u.ChatId, null);

        // A prospect writing to the agency's bot.
        var latin = Auth.Digits.ToLatin(text);
        var phone = u.ContactPhone ?? (MobileInText().Match(latin) is { Success: true } match ? match.Value : null);
        if (phone is not null && !PhoneNumber.TryNormalize(phone, out _)) phone = null;
        var lead = await crm.IngestAsync(u.FromName ?? (m == Messenger.Telegram ? "کاربر تلگرام" : "کاربر بله"), phone, null,
            text.Length > 0 ? text : null, m == Messenger.Telegram ? "telegram" : "bale", "ربات", u.ChatId, ct);
        if (lead is null) return (u.ChatId, null);
        return (u.ChatId, lead.Phone is null
            ? "پیامتان رسید ✅ همکاران ما به‌زودی پاسخ می‌دهند. اگر مایلید، شماره‌ی موبایلتان را بنویسید تا با شما تماس بگیریم."
            : "ممنون، پیامتان رسید ✅ همکاران ما به‌زودی با شما تماس می‌گیرند.");
    }
}
