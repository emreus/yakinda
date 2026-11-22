using FluentValidation;
using Yakinda.Application.Moderation;
using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Exceptions;
using Yakinda.Domain.Users;

namespace Yakinda.Application.Auth;

/// <param name="AcceptTerms">Kullanım koşulları ve gizlilik politikası kabul edildi mi (zorunlu).</param>
public sealed record RegisterCommand(string Email, string Password, string DisplayName, bool AcceptTerms = false)
    : ICommand<AuthResponse>;

internal sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    /// <summary>
    /// Kayıt kuralları: e-posta dolu, en fazla 256 karakter ve geçerli biçimde; şifre 8 ile 128 karakter
    /// arasında; görünen ad 2 ile 50 karakter arasında ve içerik filtresinden geçmeli; koşullar kabul edilmiş olmalı.
    /// </summary>
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256).ValidEmail().WithName("E-posta");
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128).WithName("Şifre");
        RuleFor(x => x.DisplayName).NotEmpty().MinimumLength(2).MaximumLength(50).NotObjectionable().WithName("Görünen ad");
        RuleFor(x => x.AcceptTerms)
            .Equal(true)
            .WithMessage("Devam etmek için kullanım koşullarını ve gizlilik politikasını kabul etmelisin.")
            .WithName("Koşullar");
    }
}

internal sealed class RegisterCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    AuthTokenIssuer tokenIssuer,
    TimeProvider timeProvider) : IRequestHandler<RegisterCommand, AuthResponse>
{
    /// <summary>
    /// Yeni hesap açar. E-postayı normalize eder; aynı e-postayla hesap varsa 409 (auth.email_taken) döner.
    /// Şifreyi özetler, kullanıcıyı ekler ve giriş yapmış sayılması için token'ları üretip döner.
    /// </summary>
    public async Task<AuthResponse> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var email = User.NormalizeEmail(request.Email);

        if (await users.EmailExistsAsync(email, cancellationToken))
            throw new ConflictException("auth.email_taken", "Bu e-posta adresiyle kayıtlı bir hesap zaten var.");

        var user = User.Register(
            email,
            passwordHasher.Hash(request.Password),
            request.DisplayName,
            timeProvider.GetUtcNow().UtcDateTime);

        await users.AddAsync(user, cancellationToken);
        return await tokenIssuer.IssueAsync(user, cancellationToken);
    }
}
