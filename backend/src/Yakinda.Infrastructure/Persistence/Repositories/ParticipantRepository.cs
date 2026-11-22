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

    public Task<EventParticipant?> GetFirstWaitlistedAsync(Guid eventId, CancellationToken cancellationToken) =>
        QuerySingleOrDefaultAsync<EventParticipant>("""
            SELECT event_id, user_id, status, created_at, updated_at
            FROM event_participants
            WHERE event_id = @EventId AND status = @Waitlisted
            ORDER BY updated_at, created_at, user_id
            LIMIT 1
            """, new { EventId = eventId, Waitlisted = (short)ParticipantStatus.Waitlisted }, cancellationToken);

    public Task<int> CountAsync(Guid eventId, ParticipantStatus status, CancellationToken cancellationToken) =>
        ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM event_participants WHERE event_id = @EventId AND status = @Status",
            new { EventId = eventId, Status = (short)status },
            cancellationToken);

    public Task<IReadOnlyList<Guid>> GetUpcomingActiveEventIdsAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        QueryAsync<Guid>("""
            SELECT p.event_id
            FROM event_participants p
            JOIN events e ON e.id = p.event_id
            WHERE p.user_id = @UserId
              AND p.status IN (0, 1, 4)
              AND e.status = 0
              AND e.starts_at > @Now
            """, new { UserId = userId, Now = nowUtc }, cancellationToken);
}
