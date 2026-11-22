using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Domain.Users;

namespace Yakinda.Application.Auth;

public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    AuthUserDto User);

public sealed record AuthUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    double? AverageRating,
    int RatingCount);

/// <summary>Register / Login / Refresh akışlarının ortak token üretimi.</summary>
internal sealed class AuthTokenIssuer(
    ITokenService tokenService,
    IRefreshTokenRepository refreshTokens,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Kullanıcıya yeni bir oturum verir: erişim token'ı ve rastgele bir refresh token üretir, refresh
    /// token'ın özetini son kullanma zamanıyla veritabanına kaydeder ve mobilin beklediği yanıtı
    /// (token'lar + kullanıcı özeti) döner.
    /// </summary>
    public async Task<AuthResponse> IssueAsync(User user, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var accessToken = tokenService.CreateAccessToken(user);
        var refreshToken = tokenService.CreateRefreshToken();

        await refreshTokens.AddAsync(
            user.Id,
            tokenService.HashRefreshToken(refreshToken),
            now + tokenService.RefreshTokenLifetime,
            cancellationToken);

        return new AuthResponse(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            refreshToken,
            new AuthUserDto(user.Id, user.Email, user.DisplayName, user.AverageRating, user.RatingCount));
    }
}
