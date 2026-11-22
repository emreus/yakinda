using Dapper;
using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Events.Common;

namespace Yakinda.Application.Users.GetMyEvents;

public enum MyEventsRole
{
    /// <summary>Benim düzenlediklerim</summary>
    Organizing,

    /// <summary>Katıldıklarım / katılım isteği gönderdiklerim</summary>
    Attending
}

public sealed record GetMyEventsQuery(MyEventsRole Role, bool IncludePast = true) : IQuery<IReadOnlyList<EventListItemDto>>;

internal sealed class GetMyEventsQueryHandler(
    IDbConnectionFactory db,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<GetMyEventsQuery, IReadOnlyList<EventListItemDto>>
{
    public async Task<IReadOnlyList<EventListItemDto>> Handle(GetMyEventsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var filter = request.Role == MyEventsRole.Organizing
            ? "e.organizer_id = @UserId"
            : "EXISTS (SELECT 1 FROM event_participants p WHERE p.event_id = e.id AND p.user_id = @UserId AND p.status IN (0, 1, 4))"; // bekleyen, onaylı, bekleme listesi

        // Yaklaşanlar önce (en yakın tarih), ardından geçmişler (en yeni önce).
        var sql = $"""
            SELECT {EventSql.ListItemColumns},
                   NULL::float8 AS distance_meters
            FROM events e
            JOIN categories c ON c.id = e.category_id
            JOIN users u      ON u.id = e.organizer_id
            WHERE {filter}
              AND (@IncludePast OR {EventSql.EffectiveEndsAt} >= @Now)
            ORDER BY ({EventSql.EffectiveEndsAt} < @Now),
                     CASE WHEN {EventSql.EffectiveEndsAt} >= @Now THEN e.starts_at END ASC,
                     e.starts_at DESC
            LIMIT 100
            """;

        await using var connection = await db.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<EventListItemDto>(new CommandDefinition(
            sql,
            new { UserId = userId, request.IncludePast, Now = timeProvider.GetUtcNow().UtcDateTime },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }
}
