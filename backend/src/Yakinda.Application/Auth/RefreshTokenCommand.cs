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
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
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

        if (user.DeletedAt is not null)
            throw new UnauthorizedException("auth.user_not_found", "Kullanıcı bulunamadı.");
        if (user.IsBanned)
            throw AuthErrors.Banned();

        return await tokenIssuer.IssueAsync(user, cancellationToken);
    }
}

/// <summary>
/// Çıkış: bu cihazın refresh token'ı sunucuda da iptal edilir. Yalnız telefondan silmek yetmez; token
/// başka bir yere kopyalandıysa 30 gün boyunca yeni oturum açmaya yarardı. Token bilinmiyorsa da yanıt aynıdır.
/// </summary>
public sealed record LogoutCommand(string RefreshToken) : ICommand<Unit>;

internal sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
}

internal sealed class LogoutCommandHandler(
    IRefreshTokenRepository refreshTokens,
    ITokenService tokenService,
    TimeProvider timeProvider) : IRequestHandler<LogoutCommand, Unit>
{
    public async Task<Unit> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        await refreshTokens.RevokeByHashAsync(
            tokenService.HashRefreshToken(request.RefreshToken),
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
        return Unit.Value;
    }
}
