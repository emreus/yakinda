using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using MediatR;
using Yakinda.Application.Abstractions.Email;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Domain.Users;

namespace Yakinda.Application.Auth;

internal static class PasswordResetRules
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Aynı hesaba art arda kod gönderilmesini sınırlar (e-posta bombardımanına karşı).</summary>
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    public const int MaxAttempts = 5;

    /// <summary>Kod kullanıcıya bağlanarak özetlenir; veritabanı sızsa bile kod okunamaz ve başka hesapta işe yaramaz.</summary>
    public static string Hash(Guid userId, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId:N}:{code}")));

    public static bool Matches(string storedHash, Guid userId, string code) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(storedHash),
            Encoding.ASCII.GetBytes(Hash(userId, code)));
}

// ===================================================================== Kod iste

/// <summary>
/// E-postaya 6 haneli sıfırlama kodu gönderir. Hesap olsun ya da olmasın aynı yanıt döner
/// (e-posta adresinin kayıtlı olup olmadığı dışarıdan anlaşılmasın).
/// </summary>
public sealed record ForgotPasswordCommand(string Email) : ICommand<Unit>;

internal sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator() =>
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256).WithName("E-posta");
}

internal sealed class ForgotPasswordCommandHandler(
    IUserRepository users,
    IPasswordResetRepository resets,
    IEmailSender email,
    TimeProvider timeProvider) : IRequestHandler<ForgotPasswordCommand, Unit>
{
    public async Task<Unit> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByEmailAsync(User.NormalizeEmail(request.Email), cancellationToken);
        if (user is null || user.DeletedAt is not null || user.IsBanned)
            return Unit.Value;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var lastIssued = await resets.GetLastIssuedAtAsync(user.Id, cancellationToken);
        if (lastIssued is not null && now - lastIssued.Value < PasswordResetRules.ResendCooldown)
            return Unit.Value;

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        await resets.AddAsync(user.Id, PasswordResetRules.Hash(user.Id, code), now, now + PasswordResetRules.CodeLifetime, cancellationToken);

        // Gönderim başarısız olursa transaction geri alınır ve kullanıcı tekrar deneyebilir.
        await email.SendAsync(new EmailMessage(
            user.Email,
            $"Yakında şifre sıfırlama kodun: {code}",
            $"""
             Merhaba {user.DisplayName},

             Şifreni sıfırlamak için kodun: {code}

             Kod {PasswordResetRules.CodeLifetime.TotalMinutes:0} dakika geçerli. Bu isteği sen yapmadıysan bu e-postayı yok sayabilirsin;
             şifren değişmez.

             Yakında
             """,
            $"""
             <p>Merhaba {System.Net.WebUtility.HtmlEncode(user.DisplayName)},</p>
             <p>Şifreni sıfırlamak için kodun:</p>
             <p style="font-size:28px;font-weight:700;letter-spacing:6px">{code}</p>
             <p>Kod {PasswordResetRules.CodeLifetime.TotalMinutes:0} dakika geçerli. Bu isteği sen yapmadıysan bu e-postayı yok sayabilirsin; şifren değişmez.</p>
             <p>Yakında</p>
             """), cancellationToken);

        return Unit.Value;
    }
}

// ===================================================================== Kodla sıfırla

/// <summary>Kod doğruysa şifreyi değiştirir, diğer bütün oturumları kapatır ve kullanıcıyı giriş yapmış olarak döner.</summary>
public sealed record ResetPasswordCommand(string Email, string Code, string NewPassword) : ICommand<ResetPasswordResult>;

/// <summary>
/// Hatalı kod denemesi de kaydedilmeli (deneme sayacı); bu yüzden hata istisna olarak değil sonuç olarak
/// döner ve transaction commit edilir. Endpoint bunu 400'e çevirir.
/// </summary>
public sealed record ResetPasswordResult(AuthResponse? Auth, string? ErrorCode = null, string? ErrorMessage = null)
{
    public static ResetPasswordResult Fail(string code, string message) => new(null, code, message);
}

internal sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithName("E-posta");
        RuleFor(x => x.Code).Matches("^[0-9]{6}$").WithMessage("Kod 6 haneli olmalı.").WithName("Kod");
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128).WithName("Yeni şifre");
    }
}

internal sealed class ResetPasswordCommandHandler(
    IUserRepository users,
    IPasswordResetRepository resets,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher passwordHasher,
    AuthTokenIssuer tokenIssuer,
    TimeProvider timeProvider) : IRequestHandler<ResetPasswordCommand, ResetPasswordResult>
{
    public async Task<ResetPasswordResult> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var user = await users.GetByEmailAsync(User.NormalizeEmail(request.Email), cancellationToken);
        if (user is null || user.DeletedAt is not null || user.IsBanned)
            return Expired();

        var record = await resets.GetActiveForUpdateAsync(user.Id, now, cancellationToken);
        if (record is null)
            return Expired();

        if (record.Attempts >= PasswordResetRules.MaxAttempts)
            return TooManyAttempts();

        if (!PasswordResetRules.Matches(record.CodeHash, user.Id, request.Code))
        {
            await resets.IncrementAttemptsAsync(record.Id, cancellationToken);
            return record.Attempts + 1 >= PasswordResetRules.MaxAttempts
                ? TooManyAttempts()
                : ResetPasswordResult.Fail("auth.invalid_code", "Kod hatalı. Lütfen e-postandaki kodu kontrol et.");
        }

        await users.UpdatePasswordAsync(user.Id, passwordHasher.Hash(request.NewPassword), cancellationToken);
        await resets.MarkUsedAsync(record.Id, now, cancellationToken);
        await refreshTokens.RevokeAllForUserAsync(user.Id, now, cancellationToken);

        return new ResetPasswordResult(await tokenIssuer.IssueAsync(user, cancellationToken));
    }

    private static ResetPasswordResult Expired() =>
        ResetPasswordResult.Fail("auth.code_expired", "Kodun süresi dolmuş ya da geçersiz. Yeni bir kod iste.");

    private static ResetPasswordResult TooManyAttempts() =>
        ResetPasswordResult.Fail("auth.code_attempts_exceeded", "Çok fazla hatalı deneme yapıldı. Yeni bir kod iste.");
}
