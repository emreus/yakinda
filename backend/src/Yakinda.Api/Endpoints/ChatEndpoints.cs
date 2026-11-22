using MediatR;
using Yakinda.Application.Chat;

namespace Yakinda.Api.Endpoints;

public static class ChatEndpoints
{
    public static RouteGroupBuilder MapChatEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/events/{eventId:guid}/messages").WithTags("Chat").RequireAuthorization();

        // GET /api/events/{id}/messages?before=<mesajId>  (eski mesajlar)  ya da  ?after=<mesajId>  (yeni mesajlar)
        group.MapGet("/", async (Guid eventId, Guid? before, Guid? after, int? limit, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetChatMessagesQuery(eventId, before, after, limit ?? 30), ct)));

        // POST /api/events/{id}/messages  { "body": "Taşları ben getiriyorum" }
        group.MapPost("/", async (Guid eventId, SendMessageRequest body, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new SendChatMessageCommand(eventId, body.Body), ct)));

        group.MapDelete("/{messageId:guid}", async (Guid eventId, Guid messageId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteChatMessageCommand(eventId, messageId), ct);
            return Results.NoContent();
        });

        group.MapPost("/read", async (Guid eventId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new MarkChatReadCommand(eventId), ct);
            return Results.NoContent();
        });

        return api;
    }
}

public sealed record SendMessageRequest(string Body);
