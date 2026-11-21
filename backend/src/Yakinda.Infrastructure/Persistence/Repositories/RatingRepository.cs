using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Domain.Events;

namespace Yakinda.Infrastructure.Persistence.Repositories;

internal sealed class RatingRepository(DbSession session) : RepositoryBase(session), IRatingRepository
{
    public Task<EventRating?> GetAsync(Guid eventId, Guid raterId, CancellationToken cancellationToken) =>
        QuerySingleOrDefaultAsync<EventRating>("""
            SELECT event_id, rater_id, score, comment, created_at, updated_at
            FROM event_ratings
            WHERE event_id = @EventId AND rater_id = @RaterId
            """, new { EventId = eventId, RaterId = raterId }, cancellationToken);

    public Task UpsertAsync(EventRating rating, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            INSERT INTO event_ratings (event_id, rater_id, score, comment, created_at, updated_at)
            VALUES (@EventId, @RaterId, @Score, @Comment, @CreatedAt, @UpdatedAt)
            ON CONFLICT (event_id, rater_id) DO UPDATE
               SET score      = EXCLUDED.score,
                   comment    = EXCLUDED.comment,
                   updated_at = EXCLUDED.updated_at
            """, rating, cancellationToken);
}
