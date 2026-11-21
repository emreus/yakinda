using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Domain.Events;
using Yakinda.Domain.Users;

namespace Yakinda.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(DbSession session) : RepositoryBase(session), IUserRepository
{
    private const string SelectColumns =
        "id, email, password_hash, display_name, bio, rating_sum, rating_count, created_at";

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        QuerySingleOrDefaultAsync<User>(
            $"SELECT {SelectColumns} FROM users WHERE id = @Id",
            new { Id = id },
            cancellationToken);

    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        QuerySingleOrDefaultAsync<User>(
            $"SELECT {SelectColumns} FROM users WHERE email = @Email",
            new { Email = normalizedEmail },
            cancellationToken);

    public async Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        await ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM users WHERE email = @Email)",
            new { Email = normalizedEmail },
            cancellationToken);

    public Task AddAsync(User user, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            INSERT INTO users (id, email, password_hash, display_name, bio, rating_sum, rating_count, created_at)
            VALUES (@Id, @Email, @PasswordHash, @DisplayName, @Bio, @RatingSum, @RatingCount, @CreatedAt)
            """, user, cancellationToken);

    public Task ApplyRatingDeltaAsync(Guid userId, RatingDelta delta, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            UPDATE users
               SET rating_sum   = rating_sum + @SumDelta,
                   rating_count = rating_count + @CountDelta
             WHERE id = @UserId
            """, new { UserId = userId, delta.SumDelta, delta.CountDelta }, cancellationToken);
}
