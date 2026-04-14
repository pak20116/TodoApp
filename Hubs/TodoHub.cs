using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace TodoApp.Hubs;

[Authorize]
public class TodoHub : Hub
{
    public async Task JoinUserGroup()
    {
        var userId = Context.UserIdentifier;
        if (userId is not null)
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);
    }

    public static async Task NotifyTodoChanged(IHubContext<TodoHub> hub, string userId)
    {
        await hub.Clients.Group(userId).SendAsync("TodosChanged");
    }
}
