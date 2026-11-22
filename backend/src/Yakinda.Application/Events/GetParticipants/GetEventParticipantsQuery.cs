using Dapper;
using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Events.Common;
using Yakinda.Domain.Events;

namespace Yakinda.Application.Events.GetParticipants;

/// <summary>
/// Onaylı katılımcıları herkes görür; onay bekleyenleri sadece organizatör görür.
/// Aralarında engel olan kişiler listede birbirini görmez: engellenen biri, engelleyenin hangi etkinliğe
/// (nerede, ne zaman) gideceğini buradan öğrenememeli. Organizatör kendi etkinliğinin tüm listesini görür;
/// organizatörle arasında engel olan kişi için etkinlik yoktur (404).
/// </summary>
public sealed record GetEventParticipantsQuery(Guid EventId) : IQuery<IReadOnlyList<ParticipantDto>>;

public sealed class ParticipantDto
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public ParticipantStatus Status { get; set; }
    public DateTime RequestedAt { get; set; }

    /// <summary>Katılımcının kendi organizatör puanı (organizatör onay verirken görsün diye).</summary>
    public double? AverageRating { get; set; }
    public int RatingCount { get; set; }
}

internal sealed class GetEventParticipantsQueryHandler(IDbConnectionFactory db, ICurrentUser currentUser)
    : IRequestHandler<GetEventParticipantsQuery, IReadOnlyList<ParticipantDto>>
{
    private static readonly string Sql = $"""
        SELECT p.user_id,
               u.display_name,
               {Media.MediaUrl.Sql("u.avatar_key")} AS avatar_url,
               p.status,
               p.created_at AS requested_at,
               {EventSql.Average("u.rating_sum", "u.rating_count")} AS average_rating,
               u.rating_count
        FROM event_participants p
        JOIN users u ON u.id = p.user_id
        WHERE p.event_id = @EventId
          AND (p.status = 1 OR (@IsOrganizer AND p.status = 0))
          AND (@IsOrganizer OR NOT {Safety.BlockSql.EitherWay("@UserId", "p.user_id")})
        ORDER BY p.status, p.created_at
        """;

    public async Task<IReadOnlyList<ParticipantDto>> Handle(GetEventParticipantsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        await using var connection = await db.OpenConnectionAsync(cancellationToken);

        var organizerId = await EventVisibility.RequireVisibleAsync(connection, request.EventId, userId, cancellationToken);

        var rows = await connection.QueryAsync<ParticipantDto>(new CommandDefinition(
            Sql,
            new { request.EventId, UserId = userId, IsOrganizer = organizerId == userId },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }
}
