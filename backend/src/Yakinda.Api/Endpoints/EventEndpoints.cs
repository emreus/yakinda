using MediatR;
using Yakinda.Application.Events.Update;
using Yakinda.Application.Events.Cancel;
using Yakinda.Application.Events.Common;
using Yakinda.Application.Events.Create;
using Yakinda.Application.Events.GetDetail;
using Yakinda.Application.Events.GetNearby;
using Yakinda.Application.Events.GetParticipants;
using Yakinda.Application.Events.GetRatings;
using Yakinda.Application.Events.Join;
using Yakinda.Application.Events.Leave;
using Yakinda.Application.Events.Participants;
using Yakinda.Application.Events.Rate;
using Yakinda.Application.Media;
using Yakinda.Api.Infrastructure;

namespace Yakinda.Api.Endpoints;

public sealed record RateEventRequest(short Score, string? Comment);

public sealed record UpdateEventRequest(
    short CategoryId,
    string Title,
    string? Description,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    double Latitude,
    double Longitude,
    string? PlaceName,
    string? Address,
    int? Capacity);

public static class EventEndpoints
{
    /// <summary>
    /// Etkinlik uçlarını /api/events altına bağlar; hepsi giriş gerektirir. Her uç isteği bir command ya da
    /// query nesnesine çevirip MediatR'a gönderir.
    /// </summary>
    public static RouteGroupBuilder MapEventEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/events").WithTags("Events").RequireAuthorization();

        // Kapak görseli (yalnız organizatör): multipart/form-data, "file" alanı
        group.MapPut("/{id:guid}/image", async (Guid id, HttpRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new SetEventImageCommand(id, await request.ReadImageUploadAsync(ct)), ct)));

        // DELETE /api/events/{id}/image: kapak görselini kaldırır (yalnızca organizatör).
        group.MapDelete("/{id:guid}/image", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteEventImageCommand(id), ct);
            return Results.NoContent();
        });

        // GET /api/events/nearby?lat=41.01&lng=28.97&radiusKm=10&categoryId=1&onlyAvailable=true&sort=time&page=1
        group.MapGet("/nearby", async (
            double lat,
            double lng,
            double? radiusKm,
            short? categoryId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            bool? onlyAvailable,
            string? sort,
            int? page,
            int? pageSize,
            string? q,
            ISender sender,
            CancellationToken ct) =>
        {
            var query = new GetNearbyEventsQuery(
                lat,
                lng,
                radiusKm ?? 10,
                categoryId,
                from,
                to,
                onlyAvailable ?? false,
                sort?.ToLowerInvariant() switch
                {
                    "time" => NearbySort.StartTime,
                    "rating" => NearbySort.OrganizerRating,
                    _ => NearbySort.Distance
                },
                page ?? 1,
                pageSize ?? 20,
                q);

            return Results.Ok(await sender.Send(query, ct));
        });

        // GET /api/events/{id}: etkinlik detayı; isteği yapana özel durum ve yetki bayraklarıyla.
        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetEventDetailQuery(id), ct)));

        // POST /api/events: etkinlik oluşturur; 201 ve yeni etkinliğin kimliğini döner.
        group.MapPost("/", async (CreateEventCommand command, ISender sender, CancellationToken ct) =>
        {
            var id = await sender.Send(command, ct);
            return Results.Created($"/api/events/{id}", new { id });
        });

        // PUT /api/events/{id}: organizatör düzenler (başlamadan önce)
        group.MapPut("/{id:guid}", async (Guid id, UpdateEventRequest body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new UpdateEventCommand(
                id, body.CategoryId, body.Title, body.Description, body.StartsAt, body.EndsAt,
                body.Latitude, body.Longitude, body.PlaceName, body.Address, body.Capacity), ct);
            return Results.NoContent();
        });

        // POST /api/events/{id}/cancel: etkinliği iptal eder (yalnızca organizatör).
        group.MapPost("/{id:guid}/cancel", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new CancelEventCommand(id), ct);
            return Results.NoContent();
        });

        // Katılım
        group.MapPost("/{id:guid}/join", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new JoinEventCommand(id), ct)));

        // POST /api/events/{id}/leave: etkinlikten ya da bekleme listesinden ayrılır.
        group.MapPost("/{id:guid}/leave", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new LeaveEventCommand(id), ct);
            return Results.NoContent();
        });

        // GET /api/events/{id}/participants: onaylı katılımcılar; organizatöre ayrıca bekleyen istekler.
        group.MapGet("/{id:guid}/participants", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetEventParticipantsQuery(id), ct)));

        // POST /api/events/{id}/participants/{userId}/approve: organizatör katılım isteğini onaylar.
        group.MapPost("/{id:guid}/participants/{userId:guid}/approve", async (Guid id, Guid userId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ReviewParticipantCommand(id, userId, Approve: true), ct);
            return Results.NoContent();
        });

        // POST /api/events/{id}/participants/{userId}/reject: organizatör isteği reddeder ya da onaylı
        // katılımcıyı çıkarır.
        group.MapPost("/{id:guid}/participants/{userId:guid}/reject", async (Guid id, Guid userId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ReviewParticipantCommand(id, userId, Approve: false), ct);
            return Results.NoContent();
        });

        // Oylama (1-5). PUT: aynı kullanıcı tekrar gönderirse puanı güncellenir.
        group.MapPut("/{id:guid}/rating", async (Guid id, RateEventRequest body, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new RateEventCommand(id, body.Score, body.Comment), ct)));

        // GET /api/events/{id}/ratings: puan ve yorumlar, sayfalı. Sonuç cache'lendiği için önce görünürlük
        // (etkinlik var mı, engel var mı) ayrı bir sorguyla denetlenir.
        group.MapGet("/{id:guid}/ratings", async (Guid id, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            // Yorumlar cache'lenir; etkinlik var mı ve organizatörle aramızda engel var mı kontrolü her istekte yapılır.
            await sender.Send(new EnsureEventVisibleQuery(id), ct);
            return Results.Ok(await sender.Send(new GetEventRatingsQuery(id, page ?? 1, pageSize ?? 20), ct));
        });

        return api;
    }
}
