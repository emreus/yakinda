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
