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
        ORDER BY p.status, p.created_at
        """;

    public async Task<IReadOnlyList<ParticipantDto>> Handle(GetEventParticipantsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        await using var connection = await db.OpenConnectionAsync(cancellationToken);

        var organizerId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT organizer_id FROM events WHERE id = @EventId",
            new { request.EventId },
            cancellationToken: cancellationToken))
            ?? throw EventErrors.NotFound();

        var rows = await connection.QueryAsync<ParticipantDto>(new CommandDefinition(
            Sql,
            new { request.EventId, IsOrganizer = organizerId == userId },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }
}
