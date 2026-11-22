using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Notifications;
using Yakinda.Domain.Events;
using Yakinda.Domain.Notifications;

namespace Yakinda.Infrastructure.Persistence.Repositories;

internal sealed class NotificationRepository(DbSession session) : RepositoryBase(session), INotificationRepository
{
    /// <summary>
    /// Tek kullanıcıya bildirim satırı ekler. push_processed_at boş bırakılır; PushDispatcher satırı buradan
    /// bulup telefona gönderir.
    /// </summary>
    public Task AddAsync(
        Guid userId,
        NotificationContent content,
        Guid? eventId,
        Guid? actorId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        ExecuteAsync("""
            INSERT INTO notifications (id, user_id, type, event_id, actor_id, title, body, created_at)
            VALUES (@Id, @UserId, @Type, @EventId, @ActorId, @Title, @Body, @Now)
            """,
            new
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                Type = (short)content.Type,
                EventId = eventId,
                ActorId = actorId,
                content.Title,
                content.Body,
                Now = nowUtc
            },
            cancellationToken);

    /// <summary>
    /// Tek INSERT ... SELECT ile etkinliğin verilen durumlardaki bütün katılımcılarına aynı bildirimi ekler;
    /// eklenen satır sayısını döner.
    /// </summary>
    public Task<int> AddForParticipantsAsync(
        Guid eventId,
        IReadOnlyCollection<ParticipantStatus> statuses,
        NotificationContent content,
        Guid? actorId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        ExecuteAsync("""
            INSERT INTO notifications (id, user_id, type, event_id, actor_id, title, body, created_at)
            SELECT gen_random_uuid(), p.user_id, @Type, p.event_id, @ActorId, @Title, @Body, @Now
            FROM event_participants p
            WHERE p.event_id = @EventId
              AND p.status = ANY(@Statuses)
            """,
            new
            {
                EventId = eventId,
                Statuses = statuses.Select(s => (short)s).ToArray(),
                Type = (short)content.Type,
                ActorId = actorId,
                content.Title,
                content.Body,
                Now = nowUtc
            },
            cancellationToken);

    /// <summary>
    /// Bildirimi okundu yapar (zaten okunduysa okunma zamanı değişmez). Satır yoksa ya da başka kullanıcıya
    /// aitse false döner.
    /// </summary>
    public async Task<bool> MarkReadAsync(Guid id, Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        await ExecuteAsync("""
            UPDATE notifications
               SET read_at = COALESCE(read_at, @Now)
             WHERE id = @Id AND user_id = @UserId
            """, new { Id = id, UserId = userId, Now = nowUtc }, cancellationToken) > 0;

    /// <summary>
    /// Aynı alıcı, tür, etkinlik ve aktör için verilen andan sonra üretilmiş bir bildirim var mı?
    /// </summary>
    public Task<bool> ExistsSinceAsync(
        Guid userId, NotificationType type, Guid eventId, Guid actorId, DateTime sinceUtc, CancellationToken cancellationToken) =>
        ExecuteScalarAsync<bool>("""
            SELECT EXISTS (SELECT 1 FROM notifications
                           WHERE user_id = @UserId AND event_id = @EventId AND actor_id = @ActorId
                             AND type = @Type AND created_at > @Since)
            """,
            new { UserId = userId, EventId = eventId, ActorId = actorId, Type = (short)type, Since = sinceUtc },
            cancellationToken);

    /// <summary>Kullanıcının okunmamış bütün bildirimlerini okundu yapar.</summary>
    public Task MarkAllReadAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            UPDATE notifications
               SET read_at = @Now
             WHERE user_id = @UserId AND read_at IS NULL
            """, new { UserId = userId, Now = nowUtc }, cancellationToken);
}

internal sealed class DeviceTokenRepository(DbSession session) : RepositoryBase(session), IDeviceTokenRepository
{
    /// <summary>Bir hesap sınırsız token kaydedip push kuyruğunu şişiremesin.</summary>
    private const int MaxPerUser = 10;

    /// <summary>
    /// Token'ı ekler; token zaten kayıtlıysa sahibini, platformunu ve son görülme zamanını günceller. Ardından
    /// kullanıcının en son görülen 10 cihazı dışındaki token'larını siler.
    /// </summary>
    public Task UpsertAsync(string token, Guid userId, string platform, DateTime nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            INSERT INTO device_push_tokens (token, user_id, platform, created_at, last_seen_at)
            VALUES (@Token, @UserId, @Platform, @Now, @Now)
            ON CONFLICT (token) DO UPDATE
               SET user_id      = EXCLUDED.user_id,
                   platform     = EXCLUDED.platform,
                   last_seen_at = EXCLUDED.last_seen_at;

            -- Hesap başına en fazla MaxPerUser cihaz: en uzun süredir görülmeyenler silinir
            DELETE FROM device_push_tokens
             WHERE user_id = @UserId
               AND token NOT IN (SELECT token FROM device_push_tokens
                                  WHERE user_id = @UserId
                                  ORDER BY last_seen_at DESC, token
                                  LIMIT @MaxPerUser);
            """, new { Token = token, UserId = userId, Platform = platform, Now = nowUtc, MaxPerUser }, cancellationToken);

    /// <summary>Token'ı siler; yalnızca token bu kullanıcıya kayıtlıysa.</summary>
    public Task DeleteAsync(string token, Guid userId, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "DELETE FROM device_push_tokens WHERE token = @Token AND user_id = @UserId",
            new { Token = token, UserId = userId },
            cancellationToken);
}
