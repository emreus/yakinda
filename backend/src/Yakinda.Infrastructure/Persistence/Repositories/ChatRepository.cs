using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Notifications;
using Yakinda.Domain.Notifications;

namespace Yakinda.Infrastructure.Persistence.Repositories;

internal sealed class ChatRepository(DbSession session) : RepositoryBase(session), IChatRepository
{
    /// <summary>Sohbet mesajını ekler.</summary>
    public Task AddAsync(Guid id, Guid eventId, Guid authorId, string body, DateTime nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            INSERT INTO event_messages (id, event_id, author_id, body, created_at)
            VALUES (@Id, @EventId, @AuthorId, @Body, @Now)
            """, new { Id = id, EventId = eventId, AuthorId = authorId, Body = body, Now = nowUtc }, cancellationToken);

    /// <summary>
    /// Mesajın hangi etkinliğe ait olduğunu ve yazarını okur; mesaj yoksa null döner. Silme isteğinde
    /// yetki kontrolü (yazar mı, organizatör mü) bu bilgiyle yapılır.
    /// </summary>
    public async Task<(Guid EventId, Guid AuthorId)?> GetOwnershipAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var row = await QuerySingleOrDefaultAsync<MessageOwnership>(
            "SELECT event_id, author_id FROM event_messages WHERE id = @Id",
            new { Id = messageId },
            cancellationToken);
        return row is null ? null : (row.EventId, row.AuthorId);
    }

    internal const string SoftDeleteSql = """
            UPDATE event_messages SET deleted_at = COALESCE(deleted_at, @Now) WHERE id = @Id;

            -- Mesajın ürettiği sohbet bildirimi (aynı anda, aynı yazardan) metnin ilk 120 karakterini taşır:
            -- silinen mesajın önizlemesi bildirim listesinde kalmasın.
            UPDATE notifications n
               SET body = 'Mesaj silindi'
              FROM event_messages m
             WHERE m.id = @Id
               AND n.event_id = m.event_id AND n.actor_id = m.author_id
               AND n.type = @ChatType AND n.created_at = m.created_at;
            """;

    /// <summary>
    /// Mesajın deleted_at alanını doldurur ve o mesajın ürettiği sohbet bildirimlerinin gövdesini "Mesaj
    /// silindi" yapar; böylece silinen metin bildirim listesinde kalmaz.
    /// </summary>
    public Task SoftDeleteAsync(Guid messageId, DateTime nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync(
            SoftDeleteSql,
            new { Id = messageId, Now = nowUtc, ChatType = (short)NotificationType.ChatMessage },
            cancellationToken);

    /// <summary>
    /// Tek INSERT ... SELECT ile organizatöre ve onaylı katılımcılara sohbet bildirimi ekler. Yazan kişi,
    /// bu etkinlik için okunmamış sohbet bildirimi bulunanlar ve yazanla arasında engel olanlar hariç tutulur.
    /// </summary>
    public Task NotifyMembersAsync(Guid eventId, Guid authorId, NotificationContent content, DateTime nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            INSERT INTO notifications (id, user_id, type, event_id, actor_id, title, body, created_at)
            SELECT gen_random_uuid(), m.user_id, @Type, @EventId, @AuthorId, @Title, @Body, @Now
            FROM (
                  SELECT e.organizer_id AS user_id FROM events e WHERE e.id = @EventId
                  UNION
                  SELECT p.user_id FROM event_participants p WHERE p.event_id = @EventId AND p.status = 1
                 ) m
            WHERE m.user_id <> @AuthorId
              AND NOT EXISTS (
                    SELECT 1 FROM notifications n
                    WHERE n.user_id = m.user_id AND n.event_id = @EventId AND n.type = @Type AND n.read_at IS NULL)
              AND NOT EXISTS (
                    SELECT 1 FROM user_blocks b
                    WHERE (b.blocker_id = m.user_id AND b.blocked_id = @AuthorId)
                       OR (b.blocker_id = @AuthorId AND b.blocked_id = m.user_id))
            """,
            new
            {
                Type = (short)NotificationType.ChatMessage,
                EventId = eventId,
                AuthorId = authorId,
                content.Title,
                content.Body,
                Now = nowUtc
            },
            cancellationToken);

    /// <summary>Kullanıcının bu etkinliğe ait okunmamış sohbet bildirimlerini okundu yapar.</summary>
    public Task MarkNotificationsReadAsync(Guid eventId, Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            UPDATE notifications SET read_at = @Now
            WHERE user_id = @UserId AND event_id = @EventId AND type = @Type AND read_at IS NULL
            """, new { UserId = userId, EventId = eventId, Type = (short)NotificationType.ChatMessage, Now = nowUtc }, cancellationToken);
}

internal sealed class MessageOwnership
{
    public Guid EventId { get; set; }
    public Guid AuthorId { get; set; }
}
