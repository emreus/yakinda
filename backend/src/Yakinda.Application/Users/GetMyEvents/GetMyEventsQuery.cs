using Dapper;
using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Events;
using Yakinda.Application.Events.Common;
using Yakinda.Domain.Events;

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
    /// <summary>
    /// Kullanıcının kendi etkinliklerini listeler: role göre düzenledikleri ya da katıldıkları (bekleyen,
    /// onaylı ya da bekleme listesinde). Yaklaşanlar önce (en yakın tarih), geçmişler sonra (en yeni önce)
    /// gelir; en fazla 100 satır döner. Katıldıklarından yöneticinin kaldırdığı etkinliklerin başlığı,
    /// mekanı ve görseli gizlenir; organizatörüyle arada engel olan etkinlikler hiç listelenmez.
    /// </summary>
    public async Task<IReadOnlyList<EventListItemDto>> Handle(GetMyEventsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var filter = request.Role == MyEventsRole.Organizing
            ? "e.organizer_id = @UserId"
            // Bekleyen, onaylı, bekleme listesi. Organizatörüyle arada engel olan etkinlik listelenmez: detayı
            // 404 döndüğü için satır "Etkinlik bulunamadı" ekranına açılırdı (geçmiş etkinlikler dahil).
            : "EXISTS (SELECT 1 FROM event_participants p WHERE p.event_id = e.id AND p.user_id = @UserId AND p.status IN (0, 1, 4))"
              + " AND NOT " + Safety.BlockSql.EitherWay("@UserId", "e.organizer_id");

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

        var list = rows.AsList();

        // Katıldığım etkinliklerden yönetici tarafından kaldırılanların içeriği gösterilmez.
        if (request.Role == MyEventsRole.Attending)
        {
            foreach (var row in list.Where(row => row.Status == EventStatus.Removed))
            {
                row.Title = EventTexts.RemovedTitle;
                row.PlaceName = null;
                row.ImageUrl = null;
            }
        }

        return list;
    }
}
