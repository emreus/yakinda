using FluentValidation;
using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Caching;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Exceptions;
using Yakinda.Application.Safety;

namespace Yakinda.Application.Users.DeleteAccount;

/// <summary>
/// Kullanıcı hesabını kalıcı olarak siler (App Store 5.1.1(v), Google Play hesap silme şartı).
///  1. Yaklaşan etkinliklerdeki yeri boşaltılır (bekleme listesi ilerler).
///  2. Düzenlediği yaklaşan etkinlikler iptal edilir, katılımcılara bildirim gider.
///  3. Kişisel veriler silinir, satır anonimleşir ("Silinmiş kullanıcı"); geçmiş etkinlikler ve
///     verdiği oyların sayıları kalır ki diğer kullanıcıların puan ortalamaları değişmesin.
/// Yanlışlıkla ya da başkasının silmesini önlemek için şifre tekrar istenir.
/// </summary>
public sealed record DeleteAccountCommand(string Password) : ICommand<Unit>;

internal sealed class DeleteAccountCommandValidator : AbstractValidator<DeleteAccountCommand>
{
    public DeleteAccountCommandValidator() => RuleFor(x => x.Password).NotEmpty().WithName("Şifre");
}

internal sealed class DeleteAccountCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    AccountCleanup cleanup,
    ICurrentUser currentUser,
    ICacheInvalidator cacheInvalidator,
    TimeProvider timeProvider) : IRequestHandler<DeleteAccountCommand, Unit>
{
    public async Task<Unit> Handle(DeleteAccountCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null || user.DeletedAt is not null)
            throw new NotFoundException("user.not_found", "Kullanıcı bulunamadı.");

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedException("auth.invalid_password", "Şifre hatalı.");

        await cleanup.LeaveUpcomingEventsAsync(userId, now, cancellationToken);
        await cleanup.CloseUpcomingOrganizedEventsAsync(userId, removedByModerator: false, now, cancellationToken);
        await users.AnonymizeAsync(userId, now, cancellationToken);

        cacheInvalidator.InvalidateAfterCommit(CacheTag.User(userId));
        return Unit.Value;
    }
}
