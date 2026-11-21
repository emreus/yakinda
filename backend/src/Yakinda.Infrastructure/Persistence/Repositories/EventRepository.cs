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

    public Task<Event?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        QuerySingleOrDefaultAsync<Event>(
            $"SELECT {SelectColumns} FROM events WHERE id = @Id FOR UPDATE",
            new { Id = id },
            cancellationToken);

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

    public Task UpdateAsync(Event ev, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            UPDATE events
               SET status            = @Status,
                   participant_count = @ParticipantCount,
                   rating_sum        = @RatingSum,
                   rating_count      = @RatingCount,
                   updated_at        = @UpdatedAt
             WHERE id = @Id
            """, ev, cancellationToken);

    public async Task<bool> CategoryExistsAsync(short categoryId, CancellationToken cancellationToken) =>
        await ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM categories WHERE id = @Id)",
            new { Id = categoryId },
            cancellationToken);
}
