using System.Security.Claims;
using Dapper;
using Yakinda.Application.Abstractions.Persistence;

namespace Yakinda.Infrastructure.Auth;

/// <summary>
/// İmzası ve süresi geçerli bir token'ın hâlâ kullanılabilir olup olmadığını her istekte veritabanından
/// doğrular: kullanıcı yasaklanmamış ve silinmemiş olmalı, token kullanıcının güncel oturum sürümünü taşımalı.
/// Bu kontrol olmadan yasaklanan kullanıcı ya da şifresi sıfırlanan hesabın eski token'ı süresi dolana kadar
/// (30 dakika) çalışmaya devam eder. Birincil anahtarla tek satırlık bir okuma olduğu için ucuzdur.
/// </summary>
internal sealed class UserAccessGuard(IDbConnectionFactory db)
{
    private const string Sql = """
        SELECT EXISTS (SELECT 1 FROM users
                       WHERE id = @Id
                         AND banned_at IS NULL
                         AND deleted_at IS NULL
                         AND token_version = @Version)
        """;

    /// <summary>
    /// Token'daki kullanıcı kimliğini ve oturum sürümünü okur, kullanıcıyı birincil anahtarla sorgular.
    /// Kullanıcı yoksa, yasaklıysa, silinmişse ya da token'daki sürüm güncel sürümle aynı değilse false
    /// döner; istek 401 alır.
    /// </summary>
    public async Task<bool> IsStillValidAsync(ClaimsPrincipal? principal, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal?.FindFirst(ClaimNames.Subject)?.Value, out var userId))
            return false;

        // Sürüm bilgisi olmayan (bu kontrolden önce üretilmiş) token'lar 0 sayılır.
        _ = int.TryParse(principal?.FindFirst(ClaimNames.TokenVersion)?.Value, out var version);

        await using var connection = await db.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(Sql, new { Id = userId, Version = version }, cancellationToken: cancellationToken));
    }
}
