using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Domain.Events;

namespace Yakinda.Infrastructure.Persistence.Repositories;

internal sealed class ParticipantRepository(DbSession session) : RepositoryBase(session), IParticipantRepository
{
    public Task<EventParticipant?> GetAsync(Guid eventId, Guid userId, CancellationToken cancellationToken) =>
        QuerySingleOrDefaultAsync<EventParticipant>("""
            SELECT event_id, user_id, status, created_at, updated_at
            FROM event_participants
            WHERE event_id = @EventId AND user_id = @UserId
            """, new { EventId = eventId, UserId = userId }, cancellationToken);

    public Task UpsertAsync(EventParticipant participant, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            INSERT INTO event_participants (event_id, user_id, status, created_at, updated_at)
            VALUES (@EventId, @UserId, @Status, @CreatedAt, @UpdatedAt)
            ON CONFLICT (event_id, user_id) DO UPDATE
               SET status     = EXCLUDED.status,
                   updated_at = EXCLUDED.updated_at
            """, participant, cancellationToken);
}
