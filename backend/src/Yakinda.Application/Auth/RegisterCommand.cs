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
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256).WithName("E-posta");
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
