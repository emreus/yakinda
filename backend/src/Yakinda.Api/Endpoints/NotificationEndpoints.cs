using MediatR;
using Yakinda.Application.Notifications;

namespace Yakinda.Api.Endpoints;

public static class NotificationEndpoints
{
    public static RouteGroupBuilder MapNotificationEndpoints(this RouteGroupBuilder api)
    {
        var notifications = api.MapGroup("/notifications").WithTags("Notifications").RequireAuthorization();

        // GET /api/notifications?page=1&pageSize=20
        notifications.MapGet("/", async (int? page, int? pageSize, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetNotificationsQuery(page ?? 1, pageSize ?? 20), ct)));

        // GET /api/notifications/unread-count  →  { "count": 3 }
        notifications.MapGet("/unread-count", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetUnreadNotificationCountQuery(), ct)));

        notifications.MapPost("/{id:guid}/read", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new MarkNotificationReadCommand(id), ct);
            return Results.NoContent();
        });

        notifications.MapPost("/read-all", async (ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new MarkAllNotificationsReadCommand(), ct);
            return Results.NoContent();
        });

        var devices = api.MapGroup("/devices").WithTags("Devices").RequireAuthorization();

        // POST /api/devices  { "token": "ExponentPushToken[...]", "platform": "android" }
        devices.MapPost("/", async (RegisterDeviceTokenCommand command, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(command, ct);
            return Results.NoContent();
        });

        // DELETE /api/devices?token=ExponentPushToken[...]   (çıkış yaparken)
        devices.MapDelete("/", async (string token, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new UnregisterDeviceTokenCommand(token), ct);
            return Results.NoContent();
        });

        return api;
    }
}
