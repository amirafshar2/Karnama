using Karnama.Application.Notify;
using Karnama.Domain.Notify;

namespace Karnama.Application.Media;

/// <summary>Entry point for every bot update (Bale webhook, Telegram via the gateway): files and buttons, then text commands.</summary>
public sealed class BotRouter(BotUpdateService links, MediaService media)
{
    /// <returns>A plain-text reply for that chat, or null.</returns>
    public async Task<(long ChatId, string? Reply)> HandleAsync(Messenger m, string json, CancellationToken ct)
    {
        if (ParsedUpdate.Parse(json) is not { } u) return (0, null);
        if (!await links.ClaimAsync(m, u.UpdateId, ct)) return (u.ChatId, null);
        if (await media.HandleAsync(m, u, ct)) return (u.ChatId, null);
        return (u.ChatId, await links.HandleTextAsync(m, u.ChatId, u.Text, ct));
    }
}
