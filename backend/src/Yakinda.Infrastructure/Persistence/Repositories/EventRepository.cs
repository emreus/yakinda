using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Domain.Events;

namespace Yakinda.Infrastructure.Persistence.Repositories;

internal sealed class EventRepository(DbSession session) : RepositoryBase(session), IEventRepository
{
    private const string SelectColumns = """
        id, organizer_id, category_id, title, description, starts_at, ends_at,
        ST_Y(location::geometry) AS latitude,
        ST_X(location::geometry) AS longitude,
        place_name, address, capacity, join_policy, status, participant_count,
        rating_sum, rating_count, created_at, updated_at
        """;

    /// <summary>
    /// Etkinliği FOR UPDATE ile okur: satır, transaction bitene kadar kilitlenir ve aynı etkinliğe gelen
    /// diğer yazma istekleri sırada bekler. Konum, PostGIS noktasından enlem ve boylama çevrilir.
    /// </summary>
    public Task<Event?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        QuerySingleOrDefaultAsync<Event>(
            $"SELECT {SelectColumns} FROM events WHERE id = @Id FOR UPDATE",
            new { Id = id },
            cancellationToken);

    /// <summary>Etkinliği kilitsiz okur (etkinliği değiştirmeyen işlemler için); yoksa null döner.</summary>
    public Task<Event?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        QuerySingleOrDefaultAsync<Event>(
            $"SELECT {SelectColumns} FROM events WHERE id = @Id",
            new { Id = id },
            cancellationToken);

    /// <summary>
    /// Etkinliği ekler. Enlem ve boylam, PostGIS'in geography noktasına çevrilerek yazılır
    /// (ST_MakePoint önce boylamı, sonra enlemi alır).
    /// </summary>
    public Task AddAsync(Event ev, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            INSERT INTO events (
                id, organizer_id, category_id, title, description, starts_at, ends_at,
                location, place_name, address, capacity, join_policy, status,
                participant_count, rating_sum, rating_count, created_at, updated_at)
            VALUES (
                @Id, @OrganizerId, @CategoryId, @Title, @Description, @StartsAt, @EndsAt,
                ST_SetSRID(ST_MakePoint(@Longitude, @Latitude), 4326)::geography,
                @PlaceName, @Address, @Capacity, @JoinPolicy, @Status,
                @ParticipantCount, @RatingSum, @RatingCount, @CreatedAt, @UpdatedAt)
            """, ev, cancellationToken);

    /// <summary>
    /// Etkinliğin değişebilen bütün alanlarını tek UPDATE ile yazar: kategori, başlık, açıklama, zamanlar,
    /// konum, mekan, adres, kontenjan, durum, katılımcı sayısı, puan toplamları ve son değişiklik zamanı.
    /// </summary>
    public Task UpdateAsync(Event ev, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            UPDATE events
               SET category_id       = @CategoryId,
                   title             = @Title,
                   description       = @Description,
                   starts_at         = @StartsAt,
                   ends_at           = @EndsAt,
                   location          = ST_SetSRID(ST_MakePoint(@Longitude, @Latitude), 4326)::geography,
                   place_name        = @PlaceName,
                   address           = @Address,
                   capacity          = @Capacity,
                   status            = @Status,
                   participant_count = @ParticipantCount,
                   rating_sum        = @RatingSum,
                   rating_count      = @RatingCount,
                   updated_at        = @UpdatedAt
             WHERE id = @Id
            """, ev, cancellationToken);

    /// <summary>
    /// Hatırlatma ve puanlama bildirimi işaretlerini boşaltır; etkinliğin saati değişince bildirimler yeni
    /// saate göre yeniden üretilir.
    /// </summary>
    public Task ResetScheduledNotificationsAsync(Guid eventId, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE events SET reminder_sent_at = NULL, rating_prompt_sent_at = NULL WHERE id = @Id",
            new { Id = eventId },
            cancellationToken);

    /// <summary>SELECT EXISTS ile kategorinin var olup olmadığına bakar.</summary>
    public async Task<bool> CategoryExistsAsync(short categoryId, CancellationToken cancellationToken) =>
        await ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM categories WHERE id = @Id)",
            new { Id = categoryId },
            cancellationToken);

    /// <summary>Organizatörün aktif (status = 0) ve henüz başlamamış etkinliklerinin kimliklerini döner.</summary>
    public Task<IReadOnlyList<Guid>> GetUpcomingActiveIdsByOrganizerAsync(Guid organizerId, DateTime nowUtc, CancellationToken cancellationToken) =>
        QueryAsync<Guid>(
            "SELECT id FROM events WHERE organizer_id = @OrganizerId AND status = 0 AND starts_at > @Now",
            new { OrganizerId = organizerId, Now = nowUtc },
            cancellationToken);
}
