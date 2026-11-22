using Yakinda.Domain.Users;

namespace Yakinda.Application.Abstractions.Auth;

public interface ICurrentUser
{
    Guid? UserId { get; }

    /// <summary>Giriş yapmamışsa <c>UnauthorizedException</c> fırlatır.</summary>
    Guid RequireUserId();
}

public interface IPasswordHasher
{
    /// <summary>
    /// Şifreden, veritabanında saklanacak tek yönlü özeti (hash) üretir; her çağrıda yeni bir rastgele tuz (salt) kullanır.
    /// </summary>
    string Hash(string password);
    /// <summary>Girilen şifre, saklanan özetle eşleşiyor mu?</summary>
    bool Verify(string password, string hash);

    /// <summary>
    /// Hesap bulunamadığında çağrılır: gerçek doğrulama kadar süre harcar, sonucu her zaman olumsuzdur.
    /// Böylece giriş yanıtının süresinden bir e-postanın kayıtlı olup olmadığı anlaşılamaz.
    /// </summary>
    void VerifyAgainstNothing(string password);
}

public interface ITokenService
{
    /// <summary>
    /// Kullanıcı için kısa ömürlü, imzalı erişim token'ı (JWT) üretir; son kullanma anıyla birlikte döner.
    /// </summary>
    AccessToken CreateAccessToken(User user);
    /// <summary>Tahmin edilemez, rastgele bir refresh token üretir.</summary>
    string CreateRefreshToken();
    /// <summary>
    /// Refresh token'ın veritabanında saklanacak özetini (hash) üretir; token'ın kendisi saklanmaz.
    /// </summary>
    string HashRefreshToken(string refreshToken);
    TimeSpan RefreshTokenLifetime { get; }
}

public sealed record AccessToken(string Token, DateTime ExpiresAtUtc);
