using MediatR;
using Yakinda.Application.Chat;

namespace Yakinda.Api.Endpoints;

public static class ChatEndpoints
{
    /// <summary>Sohbet uçlarını /api/events/{eventId}/messages altına bağlar; hepsi giriş gerektirir.</summary>
    public static RouteGroupBuilder MapChatEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/events/{eventId:guid}/messages").WithTags("Chat").RequireAuthorization();

        // GET /api/events/{id}/messages: sohbet mesajlarını en yeni önce döner (limit verilmezse 30 mesaj).
        // ?before=<mesajId> o mesajdan eskileri, ?after=<mesajId> o mesajdan sonra yazılanları getirir.
        group.MapGet("/", async (Guid eventId, Guid? before, Guid? after, int? limit, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetChatMessagesQuery(eventId, before, after, limit ?? 30), ct)));

        // POST /api/events/{id}/messages  { "body": "Taşları ben getiriyorum" }: sohbete mesaj yazar ve
        // kaydedilen mesajı döner. Yalnızca organizatör ve onaylı katılımcılar yazabilir.
        group.MapPost("/", async (Guid eventId, SendMessageRequest body, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new SendChatMessageCommand(eventId, body.Body), ct)));

        // DELETE .../messages/{messageId}: mesajı siler (yazarı ya da organizatör).
        group.MapDelete("/{messageId:guid}", async (Guid eventId, Guid messageId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteChatMessageCommand(eventId, messageId), ct);
            return Results.NoContent();
        });

        // POST .../messages/read: sohbet açıldığında bu etkinliğin sohbet bildirimlerini okundu yapar.
        group.MapPost("/read", async (Guid eventId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new MarkChatReadCommand(eventId), ct);
            return Results.NoContent();
        });

        return api;
    }
}

public sealed record SendMessageRequest(string Body);
