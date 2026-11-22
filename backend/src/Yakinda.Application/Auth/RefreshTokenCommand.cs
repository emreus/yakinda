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
    /// <summary>Refresh token dolu olmalı ve 200 karakteri geçmemeli.</summary>
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
    /// <summary>
    /// Oturumu yeniler. Gelen token'ın özetini alır ve geçerli kaydı satır kilidiyle okur; kayıt yoksa
    /// 401 döner. Eski token'ı iptal eder (her token tek kullanımlıktır), kullanıcının hâlâ var olduğunu ve
    /// yasaklı olmadığını denetler, sonra yeni erişim ve refresh token'larını üretir.
    /// </summary>
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
    /// <summary>Refresh token dolu olmalı ve 200 karakteri geçmemeli.</summary>
    public LogoutCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
}

internal sealed class LogoutCommandHandler(
    IRefreshTokenRepository refreshTokens,
    ITokenService tokenService,
    TimeProvider timeProvider) : IRequestHandler<LogoutCommand, Unit>
{
    /// <summary>
    /// Çıkış yapar: gelen refresh token'ın özetini alır ve eşleşen kaydı iptal eder. Kayıt yoksa da hata vermez.
    /// </summary>
    public async Task<Unit> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        await refreshTokens.RevokeByHashAsync(
            tokenService.HashRefreshToken(request.RefreshToken),
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
        return Unit.Value;
    }
}
