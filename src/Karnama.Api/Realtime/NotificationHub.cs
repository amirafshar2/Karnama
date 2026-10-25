using Karnama.Application.Abstractions;
using Karnama.Application.Work;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Karnama.Api.Realtime;

/// <summary>Messages are small deltas: a type plus ids, never whole objects.</summary>
public interface INotificationClient
{
    /// <summary>The user's roles changed: refresh the access token and rebuild menus.</summary>
    Task PermissionsChanged();

    /// <summary>Something the user can see changed (work card, order). The client refetches what it shows.</summary>
    Task Changed(ChangeMessage change);

    /// <summary>Someone is typing in a chat I'm in (not stored, not replayed).</summary>
    Task Typing(TypingMessage typing);
}

public sealed record TypingMessage(Guid ConversationId, Guid UserId);

public sealed record ChangeMessage(long Seq, string Kind, Guid Id, IReadOnlyList<string> Fields);

public static class HubGroups
{
    public const string AllWork = "work:all";
    public const string AllPresence = "presence:all";
    public static string User(Guid id) => $"user:{id:N}";
    public static string Client(Guid id) => $"client:{id:N}";
}

/// <summary>
/// One connection per device for everything live (work, orders, notifications) — cheaper on battery and data than
/// one hub per topic. Group membership is decided here from the validated token, never from client input.
/// </summary>
[Authorize]
public sealed class NotificationHub(ICurrentUser me, WorkAccess access, Karnama.Application.Chat.ChatService chat) : Hub<INotificationClient>
{
    /// <summary>"Typing…" goes only to the other members of that chat; non-members are ignored.</summary>
    public async Task Typing(Guid conversationId)
    {
        if (me.UserId is not { } id) return;
        var targets = await chat.TypingTargetsAsync(conversationId, Context.ConnectionAborted);
        if (targets is null || targets.Count == 0) return;
        await Clients.Groups(targets.Select(HubGroups.User).ToList()).Typing(new TypingMessage(conversationId, id));
    }

    public override async Task OnConnectedAsync()
    {
        if (me.UserId is { } id) await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.User(id));
        if (access.SeesAllWork) await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.AllWork);
        if (me.Kind == Karnama.Domain.Identity.UserKind.Staff && me.Has(Karnama.Domain.Identity.Permissions.AttendanceView, Karnama.Domain.Identity.PermissionScope.All))
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.AllPresence);
        if (await access.PortalClientIdAsync(Context.ConnectionAborted) is { } clientId && clientId != Guid.Empty)
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Client(clientId));
        await base.OnConnectedAsync();
    }
}

internal sealed class SignalRRealtimeNotifier(IHubContext<NotificationHub, INotificationClient> hub) : IRealtimeNotifier
{
    public Task PermissionsChangedAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct) =>
        hub.Clients.Groups(userIds.Select(HubGroups.User).ToList()).PermissionsChanged();

    public async Task ChangesAsync(IReadOnlyList<ChangeDelta> changes, CancellationToken ct)
    {
        foreach (var c in changes)
        {
            var msg = new ChangeMessage(c.Seq, c.Kind, c.Id, c.Fields);
            // Presence (who is in, on break, timers) goes to attendance managers and the person; everything else to work managers.
            // A notification is private: only its owner hears about it.
            var groups = c.Kind is "notification" or "chat" ? new List<string>() : new List<string> { c.Kind == "presence" ? HubGroups.AllPresence : HubGroups.AllWork };
            if (c.AssigneeId is { } a) groups.Add(HubGroups.User(a));
            if (c.PreviousAssigneeId is { } p) groups.Add(HubGroups.User(p));
            if (c.ClientId is { } cl) groups.Add(HubGroups.Client(cl));
            await hub.Clients.Groups(groups).Changed(msg);
        }
    }
}
