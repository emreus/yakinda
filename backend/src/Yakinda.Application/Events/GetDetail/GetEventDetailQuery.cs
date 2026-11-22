using Dapper;
using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Caching;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Events.Common;
using Yakinda.Domain.Events;

namespace Yakinda.Application.Events.GetDetail;

/// <summary>
/// Etkinlik detayı iki parçadan kurulur:
///  1) Herkes için aynı olan kısım (başlık, yer, sayılar, puanlar) → <see cref="EventSnapshot"/>,
///     Redis'te 5 dakika cache'lenir, etkinlik değişince "event:{id}" etiketiyle temizlenir.
///  2) İsteği yapana özel kısım (katılım durumum, puanım, bekleyen istekler) → her seferinde
///     veritabanından okunur; birincil anahtarla birkaç küçük okuma, çok ucuz.
/// </summary>
public sealed record GetEventDetailQuery(Guid EventId) : IQuery<EventDetailDto>;

/// <summary>Etkinliğin herkes için aynı olan, cache'lenen kısmı.</summary>
public sealed class EventSnapshot
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public short CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryEmoji { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public string? PlaceName { get; set; }
    public string? Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int? Capacity { get; set; }
    public int ParticipantCount { get; set; }
    public JoinPolicy JoinPolicy { get; set; }
    public EventStatus Status { get; set; }
    public double? AverageRating { get; set; }
    public int RatingCount { get; set; }
    public Guid OrganizerId { get; set; }
    public string OrganizerName { get; set; } = string.Empty;
    public double? OrganizerRating { get; set; }
    public int OrganizerRatingCount { get; set; }
    public int WaitlistCount { get; set; }
}

/// <summary>İsteği yapan kullanıcıya özel kısım (cache'lenmez).</summary>
internal sealed class ViewerState
{
    public ParticipantStatus? MyParticipationStatus { get; set; }
    public short? MyScore { get; set; }
    public string? MyComment { get; set; }
    public int PendingCount { get; set; }
    public int MyWaitlistPosition { get; set; }
}

/// <summary>API'nin döndüğü model; mobil uygulamanın beklediği alanlar değişmedi.</summary>
public sealed class EventDetailDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public short CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryEmoji { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public string? PlaceName { get; set; }
    public string? Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int? Capacity { get; set; }
    public int ParticipantCount { get; set; }
    public JoinPolicy JoinPolicy { get; set; }
    public EventStatus Status { get; set; }

    public double? AverageRating { get; set; }
    public int RatingCount { get; set; }

    public Guid OrganizerId { get; set; }
    public string OrganizerName { get; set; } = string.Empty;
    public double? OrganizerRating { get; set; }
    public int OrganizerRatingCount { get; set; }

    // Giriş yapan kullanıcıya özel alanlar
    public ParticipantStatus? MyParticipationStatus { get; set; }
    public short? MyScore { get; set; }
    public string? MyComment { get; set; }

    /// <summary>Sadece organizatör için anlamlı: onay bekleyen istek sayısı.</summary>
    public int PendingCount { get; set; }

    /// <summary>Bekleme listesindeki kişi sayısı.</summary>
    public int WaitlistCount { get; set; }

    /// <summary>Giriş yapan kullanıcı bekleme listesindeyse sırası (1'den başlar).</summary>
    public int? MyWaitlistPosition { get; set; }

    public int? SpotsLeft => Capacity is null ? null : Math.Max(0, Capacity.Value - ParticipantCount);

    // Sunucu tarafında hesaplanan yetkiler: mobil uygulama buton göster/gizle için kullanır.
    public bool IsOrganizer { get; set; }
    public bool CanJoin { get; set; }
    public bool CanLeave { get; set; }
    public bool CanCancel { get; set; }
    public bool CanEdit { get; set; }
    public bool CanRate { get; set; }
    public DateTime RatingOpensAt { get; set; }
    public DateTime RatingClosesAt { get; set; }
}

internal sealed class GetEventDetailQueryHandler(
    IDbConnectionFactory db,
    ICacheService cache,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<GetEventDetailQuery, EventDetailDto>
{
    private static readonly TimeSpan SnapshotDuration = TimeSpan.FromMinutes(5);

    private static readonly string SnapshotSql = $"""
        SELECT e.id,
               e.title,
               e.description,
               e.category_id,
               c.name  AS category_name,
               c.emoji AS category_emoji,
               e.starts_at,
               e.ends_at,
               e.place_name,
               e.address,
               ST_Y(e.location::geometry) AS latitude,
               ST_X(e.location::geometry) AS longitude,
               e.capacity,
               e.participant_count,
               e.join_policy,
               e.status,
               {EventSql.Average("e.rating_sum", "e.rating_count")} AS average_rating,
               e.rating_count,
               e.organizer_id,
               u.display_name AS organizer_name,
               {EventSql.Average("u.rating_sum", "u.rating_count")} AS organizer_rating,
               u.rating_count AS organizer_rating_count,
               (SELECT count(*) FROM event_participants w
                 WHERE w.event_id = e.id AND w.status = 4)::int AS waitlist_count
        FROM events e
        JOIN categories c ON c.id = e.category_id
        JOIN users u      ON u.id = e.organizer_id
        WHERE e.id = @EventId
        """;

    // Her zaman tam olarak bir satır döner; kayıt yoksa ilgili kolon NULL olur.
    private const string ViewerSql = """
        SELECT
            (SELECT p.status FROM event_participants p
              WHERE p.event_id = @EventId AND p.user_id = @UserId) AS my_participation_status,
            (SELECT r.score FROM event_ratings r
              WHERE r.event_id = @EventId AND r.rater_id = @UserId) AS my_score,
            (SELECT r.comment FROM event_ratings r
              WHERE r.event_id = @EventId AND r.rater_id = @UserId) AS my_comment,
            CASE WHEN @IsOrganizer
                 THEN (SELECT count(*) FROM event_participants pp
                        WHERE pp.event_id = @EventId AND pp.status = 0)
                 ELSE 0
            END::int AS pending_count,
            -- Bekleme listesindeysem kaçıncı sıradayım (değilsem 0)
            (SELECT count(*) FROM event_participants w
               JOIN event_participants me
                 ON me.event_id = w.event_id AND me.user_id = @UserId AND me.status = 4
              WHERE w.event_id = @EventId AND w.status = 4
                AND (w.updated_at, w.created_at, w.user_id) <= (me.updated_at, me.created_at, me.user_id))::int AS my_waitlist_position
        """;

    public async Task<EventDetailDto> Handle(GetEventDetailQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // 1) Paylaşılan kısım: cache'ten ya da (yoksa) veritabanından
        var snapshot = await cache.GetOrCreateAsync(
            $"event:{request.EventId}:snapshot",
            ct => LoadSnapshotAsync(request.EventId, ct),
            SnapshotDuration,
            new[] { CacheTag.Event(request.EventId) },
            cancellationToken);

        // 2) Kullanıcıya özel kısım: her zaman taze
        var isOrganizer = snapshot.OrganizerId == userId;
        ViewerState viewer;
        await using (var connection = await db.OpenConnectionAsync(cancellationToken))
        {
            viewer = await connection.QuerySingleAsync<ViewerState>(new CommandDefinition(
                ViewerSql,
                new { request.EventId, UserId = userId, IsOrganizer = isOrganizer },
                cancellationToken: cancellationToken));
        }

        return Compose(snapshot, viewer, isOrganizer, now);
    }

    private async Task<EventSnapshot> LoadSnapshotAsync(Guid eventId, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenConnectionAsync(cancellationToken);
        // Bulunamazsa hata fırlatılır: hata cache'lenmez, var olmayan id'ler cache'i doldurmaz.
        return await connection.QuerySingleOrDefaultAsync<EventSnapshot>(
                   new CommandDefinition(SnapshotSql, new { EventId = eventId }, cancellationToken: cancellationToken))
               ?? throw EventErrors.NotFound();
    }

    /// <summary>Paylaşılan kısım + kullanıcıya özel kısım → yetkileri de hesaplanmış DTO.</summary>
    private static EventDetailDto Compose(EventSnapshot s, ViewerState viewer, bool isOrganizer, DateTime now)
    {
        var isActive = s.Status == EventStatus.Active;
        var notStarted = now < s.StartsAt;
        var myStatus = viewer.MyParticipationStatus;
        var hasActiveParticipation =
            myStatus is ParticipantStatus.Pending or ParticipantStatus.Approved or ParticipantStatus.Waitlisted;

        return new EventDetailDto
        {
            Id = s.Id,
            Title = s.Title,
            Description = s.Description,
            CategoryId = s.CategoryId,
            CategoryName = s.CategoryName,
            CategoryEmoji = s.CategoryEmoji,
            StartsAt = s.StartsAt,
            EndsAt = s.EndsAt,
            PlaceName = s.PlaceName,
            Address = s.Address,
            Latitude = s.Latitude,
            Longitude = s.Longitude,
            Capacity = s.Capacity,
            ParticipantCount = s.ParticipantCount,
            JoinPolicy = s.JoinPolicy,
            Status = s.Status,
            AverageRating = s.AverageRating,
            RatingCount = s.RatingCount,
            OrganizerId = s.OrganizerId,
            OrganizerName = s.OrganizerName,
            OrganizerRating = s.OrganizerRating,
            OrganizerRatingCount = s.OrganizerRatingCount,

            MyParticipationStatus = myStatus,
            MyScore = viewer.MyScore,
            MyComment = viewer.MyComment,
            PendingCount = isOrganizer ? viewer.PendingCount : 0,
            WaitlistCount = s.WaitlistCount,
            MyWaitlistPosition = viewer.MyWaitlistPosition > 0 ? viewer.MyWaitlistPosition : null,

            IsOrganizer = isOrganizer,
            RatingOpensAt = EventRules.RatingOpensAt(s.StartsAt),
            RatingClosesAt = EventRules.RatingClosesAt(s.StartsAt, s.EndsAt),
            // Kontenjan doluysa da katılınabilir: kullanıcı bekleme listesine girer.
            CanJoin = isActive && notStarted && !isOrganizer
                      && (myStatus is null or ParticipantStatus.Left),
            CanLeave = isActive && notStarted && hasActiveParticipation,
            CanCancel = isActive && notStarted && isOrganizer,
            CanEdit = isActive && notStarted && isOrganizer,
            CanRate = isActive && !isOrganizer
                      && myStatus == ParticipantStatus.Approved
                      && EventRules.IsRatingWindowOpen(s.StartsAt, s.EndsAt, now)
        };
    }
}
