using FluentValidation;
using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Exceptions;

namespace Yakinda.Application.Auth;

/// <summary>
/// Refresh token rotation: kullanılan token iptal edilir, yerine yenisi verilir.
/// Mobil uygulama refresh token'ı SecureStore'da (Keychain / Keystore) saklar.
/// </summary>
public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<AuthResponse>;

internal sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}

internal sealed class RefreshTokenCommandHandler(
    IRefreshTokenRepository refreshTokens,
    IUserRepository users,
    ITokenService tokenService,
    AuthTokenIssuer tokenIssuer,
    TimeProvider timeProvider) : IRequestHandler<RefreshTokenCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);

        var record = await refreshTokens.GetActiveForUpdateAsync(tokenHash, now, cancellationToken)
            ?? throw new UnauthorizedException("auth.invalid_refresh_token", "Oturumun sona erdi, lütfen tekrar giriş yap.");

        await refreshTokens.RevokeAsync(record.Id, now, cancellationToken);

        var user = await users.GetByIdAsync(record.UserId, cancellationToken)
            ?? throw new UnauthorizedException("auth.user_not_found", "Kullanıcı bulunamadı.");

        return await tokenIssuer.IssueAsync(user, cancellationToken);
    }
}
