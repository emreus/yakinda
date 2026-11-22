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
    string Hash(string password);
    bool Verify(string password, string hash);

    /// <summary>
    /// Hesap bulunamadığında çağrılır: gerçek doğrulama kadar süre harcar, sonucu her zaman olumsuzdur.
    /// Böylece giriş yanıtının süresinden bir e-postanın kayıtlı olup olmadığı anlaşılamaz.
    /// </summary>
    void VerifyAgainstNothing(string password);
}

public interface ITokenService
{
    AccessToken CreateAccessToken(User user);
    string CreateRefreshToken();
    string HashRefreshToken(string refreshToken);
    TimeSpan RefreshTokenLifetime { get; }
}

public sealed record AccessToken(string Token, DateTime ExpiresAtUtc);
