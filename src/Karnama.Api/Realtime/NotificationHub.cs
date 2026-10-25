using Karnama.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Karnama.Api.Realtime;

/// <summary>Messages are small deltas: a type plus ids, never whole objects.</summary>
public interface INotificationClient
{
    /// <summary>The user's roles changed: refresh the access token and rebuild menus.</summary>
    Task PermissionsChanged();
}

public static class HubGroups
{
    public static string User(Guid id) => $"user:{id:N}";
}

[Authorize]
public sealed class NotificationHub(ICurrentUser me) : Hub<INotificationClient>
{
    public override async Task OnConnectedAsync()
    {
        // Group membership is decided on the server from the validated token, never from client input.
        if (me.UserId is { } id) await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.User(id));
        await base.OnConnectedAsync();
    }
}

internal sealed class SignalRRealtimeNotifier(IHubContext<NotificationHub, INotificationClient> hub) : IRealtimeNotifier
{
    public Task PermissionsChangedAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct) =>
        hub.Clients.Groups(userIds.Select(HubGroups.User).ToList()).PermissionsChanged();
}
