using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Users.GetMyEvents;
using Yakinda.Application.Users.GetProfile;

namespace Yakinda.Api.Endpoints;

public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/users").WithTags("Users").RequireAuthorization();

        group.MapGet("/me", async (ICurrentUser currentUser, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetUserProfileQuery(currentUser.RequireUserId()), ct)));

        // GET /api/users/me/events?role=organizing|attending&includePast=true
        group.MapGet("/me/events", async (string? role, bool? includePast, ISender sender, CancellationToken ct) =>
        {
            var parsedRole = string.Equals(role, "attending", StringComparison.OrdinalIgnoreCase)
                ? MyEventsRole.Attending
                : MyEventsRole.Organizing;

            return Results.Ok(await sender.Send(new GetMyEventsQuery(parsedRole, includePast ?? true), ct));
        });

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetUserProfileQuery(id), ct)));

        return api;
    }
}
