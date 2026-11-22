using FluentValidation;
using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Exceptions;
using Yakinda.Domain.Users;

namespace Yakinda.Application.Auth;

// Login bir command'dır çünkü yeni bir refresh token kaydeder.
public sealed record LoginCommand(string Email, string Password) : ICommand<AuthResponse>;

internal sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        // Üst sınırlar: çok büyük gövdelerle şifre özetleme hattını meşgul etmeyi önler.
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256).WithName("E-posta");
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128).WithName("Şifre");
    }
}

internal sealed class LoginCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    AuthTokenIssuer tokenIssuer) : IRequestHandler<LoginCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByEmailAsync(User.NormalizeEmail(request.Email), cancellationToken);

        if (user is null || user.DeletedAt is not null)
        {
            // Hesap yokken de aynı süre harcanır; yanıt süresinden e-postanın kayıtlı olup olmadığı anlaşılmaz.
            passwordHasher.VerifyAgainstNothing(request.Password);
            throw InvalidCredentials();
        }

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
            throw InvalidCredentials();

        if (user.IsBanned)
            throw AuthErrors.Banned();

        return await tokenIssuer.IssueAsync(user, cancellationToken);
    }

    private static UnauthorizedException InvalidCredentials() =>
        new("auth.invalid_credentials", "E-posta veya şifre hatalı.");
}
