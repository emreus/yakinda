using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Domain.Events;
using Yakinda.Domain.Users;

namespace Yakinda.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(DbSession session) : RepositoryBase(session), IUserRepository
{
    private const string SelectColumns =
        "id, email, password_hash, display_name, bio, rating_sum, rating_count, created_at, " +
        "terms_accepted_at, is_admin, banned_at, deleted_at";

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
            INSERT INTO users (id, email, password_hash, display_name, bio, rating_sum, rating_count, created_at, terms_accepted_at)
            VALUES (@Id, @Email, @PasswordHash, @DisplayName, @Bio, @RatingSum, @RatingCount, @CreatedAt, @TermsAcceptedAt)
            """, user, cancellationToken);

    public Task ApplyRatingDeltaAsync(Guid userId, RatingDelta delta, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            UPDATE users
               SET rating_sum   = rating_sum + @SumDelta,
                   rating_count = rating_count + @CountDelta
             WHERE id = @UserId
            """, new { UserId = userId, delta.SumDelta, delta.CountDelta }, cancellationToken);

    public Task AnonymizeAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            UPDATE users
               SET email         = 'deleted+' || id::text || '@yakinda.invalid',
                   display_name  = 'Silinmiş kullanıcı',
                   bio           = NULL,
                   password_hash = '',
                   is_admin      = false,
                   deleted_at    = @Now
             WHERE id = @Id;
            DELETE FROM refresh_tokens     WHERE user_id = @Id;
            DELETE FROM device_push_tokens WHERE user_id = @Id;
            DELETE FROM notifications      WHERE user_id = @Id;
            DELETE FROM user_blocks        WHERE blocker_id = @Id OR blocked_id = @Id;
            UPDATE event_ratings SET comment = NULL WHERE rater_id = @Id;
            """, new { Id = userId, Now = nowUtc }, cancellationToken);

    public Task BanAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            UPDATE users SET banned_at = COALESCE(banned_at, @Now) WHERE id = @Id;
            DELETE FROM refresh_tokens     WHERE user_id = @Id;
            DELETE FROM device_push_tokens WHERE user_id = @Id;
            """, new { Id = userId, Now = nowUtc }, cancellationToken);

    public Task UpdatePasswordAsync(Guid userId, string passwordHash, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE users SET password_hash = @Hash WHERE id = @Id",
            new { Id = userId, Hash = passwordHash },
            cancellationToken);
}
