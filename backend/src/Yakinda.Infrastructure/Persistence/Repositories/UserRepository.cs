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
                   avatar_key    = NULL,
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

    public Task UpdateProfileAsync(Guid userId, string displayName, string? bio, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE users SET display_name = @DisplayName, bio = @Bio WHERE id = @Id",
            new { Id = userId, DisplayName = displayName, Bio = bio },
            cancellationToken);

    public Task ClearBioAsync(Guid userId, CancellationToken cancellationToken) =>
        ExecuteAsync("UPDATE users SET bio = NULL WHERE id = @Id", new { Id = userId }, cancellationToken);
}

internal sealed class MediaRepository(DbSession session) : RepositoryBase(session), IMediaRepository
{
    // Eski değer, aynı satır kilitlenerek okunur ve dönülür (UPDATE ... RETURNING yeni değeri verir).
    public Task<string?> SetUserAvatarAsync(Guid userId, string? key, CancellationToken cancellationToken) =>
        ExecuteScalarAsync<string?>("""
            UPDATE users u SET avatar_key = @Key
              FROM (SELECT id, avatar_key FROM users WHERE id = @Id FOR UPDATE) old
             WHERE u.id = old.id
            RETURNING old.avatar_key
            """, new { Id = userId, Key = key }, cancellationToken);

    public Task<string?> SetEventImageAsync(Guid eventId, string? key, CancellationToken cancellationToken) =>
        ExecuteScalarAsync<string?>("""
            UPDATE events e SET image_key = @Key
              FROM (SELECT id, image_key FROM events WHERE id = @Id FOR UPDATE) old
             WHERE e.id = old.id
            RETURNING old.image_key
            """, new { Id = eventId, Key = key }, cancellationToken);
}
