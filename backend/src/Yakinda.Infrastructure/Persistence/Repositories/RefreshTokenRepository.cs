using Yakinda.Application.Abstractions.Persistence;

namespace Yakinda.Infrastructure.Persistence.Repositories;

internal sealed class RefreshTokenRepository(DbSession session) : RepositoryBase(session), IRefreshTokenRepository
{
    public Task AddAsync(Guid userId, string tokenHash, DateTime expiresAtUtc, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            INSERT INTO refresh_tokens (id, user_id, token_hash, expires_at)
            VALUES (@Id, @UserId, @TokenHash, @ExpiresAt)
            """,
            new { Id = Guid.CreateVersion7(), UserId = userId, TokenHash = tokenHash, ExpiresAt = expiresAtUtc },
            cancellationToken);

    public Task<RefreshTokenRecord?> GetActiveForUpdateAsync(string tokenHash, DateTime nowUtc, CancellationToken cancellationToken) =>
        QuerySingleOrDefaultAsync<RefreshTokenRecord>("""
            SELECT id, user_id, expires_at
            FROM refresh_tokens
            WHERE token_hash = @TokenHash
              AND revoked_at IS NULL
              AND expires_at > @Now
            FOR UPDATE
            """, new { TokenHash = tokenHash, Now = nowUtc }, cancellationToken);

    public Task RevokeAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE refresh_tokens SET revoked_at = @Now WHERE id = @Id",
            new { Id = id, Now = nowUtc },
            cancellationToken);

    public Task RevokeAllForUserAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE refresh_tokens SET revoked_at = @Now WHERE user_id = @UserId AND revoked_at IS NULL",
            new { UserId = userId, Now = nowUtc },
            cancellationToken);

    public Task RevokeByHashAsync(string tokenHash, DateTime nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE refresh_tokens SET revoked_at = @Now WHERE token_hash = @TokenHash AND revoked_at IS NULL",
            new { TokenHash = tokenHash, Now = nowUtc },
            cancellationToken);
}

internal sealed class PasswordResetRepository(DbSession session) : RepositoryBase(session), IPasswordResetRepository
{
    public Task AddAsync(Guid userId, string codeHash, DateTime nowUtc, DateTime expiresAtUtc, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            UPDATE password_reset_codes SET used_at = @Now WHERE user_id = @UserId AND used_at IS NULL;
            INSERT INTO password_reset_codes (id, user_id, code_hash, created_at, expires_at)
            VALUES (@Id, @UserId, @CodeHash, @Now, @ExpiresAt);
            """,
            new { Id = Guid.CreateVersion7(), UserId = userId, CodeHash = codeHash, Now = nowUtc, ExpiresAt = expiresAtUtc },
            cancellationToken);

    public Task<DateTime?> GetLastIssuedAtAsync(Guid userId, CancellationToken cancellationToken) =>
        ExecuteScalarAsync<DateTime?>(
            "SELECT max(created_at) FROM password_reset_codes WHERE user_id = @UserId",
            new { UserId = userId },
            cancellationToken);

    public Task<int> CountIssuedSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken cancellationToken) =>
        ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM password_reset_codes WHERE user_id = @UserId AND created_at > @Since",
            new { UserId = userId, Since = sinceUtc },
            cancellationToken);

    public Task<PasswordResetRecord?> GetActiveForUpdateAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        QuerySingleOrDefaultAsync<PasswordResetRecord>("""
            SELECT id, user_id, code_hash, attempts
            FROM password_reset_codes
            WHERE user_id = @UserId AND used_at IS NULL AND expires_at > @Now
            ORDER BY created_at DESC
            LIMIT 1
            FOR UPDATE
            """, new { UserId = userId, Now = nowUtc }, cancellationToken);

    public Task IncrementAttemptsAsync(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync("UPDATE password_reset_codes SET attempts = attempts + 1 WHERE id = @Id", new { Id = id }, cancellationToken);

    public Task MarkUsedAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync("UPDATE password_reset_codes SET used_at = @Now WHERE id = @Id", new { Id = id, Now = nowUtc }, cancellationToken);
}
