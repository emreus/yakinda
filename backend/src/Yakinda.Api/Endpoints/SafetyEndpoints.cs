using MediatR;
using Yakinda.Application.Safety;
using Yakinda.Domain.Moderation;

namespace Yakinda.Api.Endpoints;

public static class SafetyEndpoints
{
    public static RouteGroupBuilder MapSafetyEndpoints(this RouteGroupBuilder api)
    {
        // POST /api/reports  { "targetType": "Event", "targetId": "...", "reason": "Inappropriate", "details": "..." }
        api.MapPost("/reports", async (CreateReportCommand command, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(command, ct);
                return Results.NoContent();
            })
            .WithTags("Safety")
            .RequireAuthorization();

        // Yönetici (users.is_admin = true)
        var admin = api.MapGroup("/admin").WithTags("Admin").RequireAuthorization();

        admin.MapGet("/reports", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetOpenReportsQuery(), ct)));

        // POST /api/admin/reports/{id}/resolve  { "action": "Dismiss" | "RemoveContent" | "BanUser", "note": "..." }
        admin.MapPost("/reports/{id:guid}/resolve", async (Guid id, ResolveReportRequest body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ResolveReportCommand(id, body.Action, body.Note), ct);
            return Results.NoContent();
        });

        return api;
    }
}

public sealed record ResolveReportRequest(ModerationAction Action, string? Note);
