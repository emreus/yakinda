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
}
