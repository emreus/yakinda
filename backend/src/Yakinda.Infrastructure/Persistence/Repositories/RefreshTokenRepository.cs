using Yakinda.Application.Abstractions.Persistence;

namespace Yakinda.Infrastructure.Persistence.Repositories;

internal sealed class RefreshTokenRepository(DbSession session) : RepositoryBase(session), IRefreshTokenRepository
{
    /// <summary>Yeni refresh token kaydını ekler: kullanıcı, token özeti ve son kullanma zamanı.</summary>
    public Task AddAsync(Guid userId, string tokenHash, DateTime expiresAtUtc, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            INSERT INTO refresh_tokens (id, user_id, token_hash, expires_at)
            VALUES (@Id, @UserId, @TokenHash, @ExpiresAt)
            """,
            new { Id = Guid.CreateVersion7(), UserId = userId, TokenHash = tokenHash, ExpiresAt = expiresAtUtc },
            cancellationToken);

    /// <summary>
    /// Özeti eşleşen, iptal edilmemiş ve süresi dolmamış kaydı FOR UPDATE ile okur. Aynı token aynı anda
    /// iki istekte kullanılırsa ikincisi bekler ve kaydı iptal edilmiş bulur.
    /// </summary>
    public Task<RefreshTokenRecord?> GetActiveForUpdateAsync(string tokenHash, DateTime nowUtc, CancellationToken cancellationToken) =>
        QuerySingleOrDefaultAsync<RefreshTokenRecord>("""
            SELECT id, user_id, expires_at
            FROM refresh_tokens
            WHERE token_hash = @TokenHash
              AND revoked_at IS NULL
              AND expires_at > @Now
            FOR UPDATE
            """, new { TokenHash = tokenHash, Now = nowUtc }, cancellationToken);

    /// <summary>Tek kaydın revoked_at alanını doldurur.</summary>
    public Task RevokeAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE refresh_tokens SET revoked_at = @Now WHERE id = @Id",
            new { Id = id, Now = nowUtc },
            cancellationToken);

    /// <summary>
    /// Kullanıcının iptal edilmemiş bütün refresh token'larını iptal eder; bütün cihazlardaki oturumlar kapanır.
    /// </summary>
    public Task RevokeAllForUserAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE refresh_tokens SET revoked_at = @Now WHERE user_id = @UserId AND revoked_at IS NULL",
            new { UserId = userId, Now = nowUtc },
            cancellationToken);

    /// <summary>
    /// Özeti verilen token'ı iptal eder; böyle bir kayıt yoksa hiçbir satır değişmez ve hata oluşmaz.
    /// </summary>
    public Task RevokeByHashAsync(string tokenHash, DateTime nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE refresh_tokens SET revoked_at = @Now WHERE token_hash = @TokenHash AND revoked_at IS NULL",
            new { TokenHash = tokenHash, Now = nowUtc },
            cancellationToken);
}

internal sealed class PasswordResetRepository(DbSession session) : RepositoryBase(session), IPasswordResetRepository
{
    /// <summary>
    /// Kullanıcının kullanılmamış eski kodlarını geçersiz kılar ve yeni kodun özetini son kullanma
    /// zamanıyla ekler; aynı anda yalnızca en son gönderilen kod geçerlidir.
    /// </summary>
    public Task AddAsync(Guid userId, string codeHash, DateTime nowUtc, DateTime expiresAtUtc, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            UPDATE password_reset_codes SET used_at = @Now WHERE user_id = @UserId AND used_at IS NULL;
            INSERT INTO password_reset_codes (id, user_id, code_hash, created_at, expires_at)
            VALUES (@Id, @UserId, @CodeHash, @Now, @ExpiresAt);
            """,
            new { Id = Guid.CreateVersion7(), UserId = userId, CodeHash = codeHash, Now = nowUtc, ExpiresAt = expiresAtUtc },
            cancellationToken);

    /// <summary>Bu hesap için en son kodun üretildiği zamanı döner; hiç kod yoksa null.</summary>
    public Task<DateTime?> GetLastIssuedAtAsync(Guid userId, CancellationToken cancellationToken) =>
        ExecuteScalarAsync<DateTime?>(
            "SELECT max(created_at) FROM password_reset_codes WHERE user_id = @UserId",
            new { UserId = userId },
            cancellationToken);

    /// <summary>
    /// Verilen andan sonra bu hesap için üretilen kodları sayar (kullanılmış ve geçersiz kılınmış olanlar dahil).
    /// </summary>
    public Task<int> CountIssuedSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken cancellationToken) =>
        ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM password_reset_codes WHERE user_id = @UserId AND created_at > @Since",
            new { UserId = userId, Since = sinceUtc },
            cancellationToken);

    /// <summary>
    /// Kullanılmamış ve süresi dolmamış en yeni kodu FOR UPDATE ile okur; aynı anda gelen iki deneme
    /// sıraya girer ve deneme sayacı doğru artar.
    /// </summary>
    public Task<PasswordResetRecord?> GetActiveForUpdateAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        QuerySingleOrDefaultAsync<PasswordResetRecord>("""
            SELECT id, user_id, code_hash, attempts
            FROM password_reset_codes
            WHERE user_id = @UserId AND used_at IS NULL AND expires_at > @Now
            ORDER BY created_at DESC
            LIMIT 1
            FOR UPDATE
            """, new { UserId = userId, Now = nowUtc }, cancellationToken);

    /// <summary>Kodun deneme sayacını veritabanında bir artırır.</summary>
    public Task IncrementAttemptsAsync(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync("UPDATE password_reset_codes SET attempts = attempts + 1 WHERE id = @Id", new { Id = id }, cancellationToken);

    /// <summary>Kodun used_at alanını doldurur; kod bir daha kullanılamaz.</summary>
    public Task MarkUsedAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync("UPDATE password_reset_codes SET used_at = @Now WHERE id = @Id", new { Id = id, Now = nowUtc }, cancellationToken);
}
