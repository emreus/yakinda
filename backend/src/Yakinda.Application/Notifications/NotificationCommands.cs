using FluentValidation;
using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Exceptions;

namespace Yakinda.Application.Notifications;

// --------------------------------------------------------------------- Okundu işaretleme

public sealed record MarkNotificationReadCommand(Guid NotificationId) : ICommand<Unit>;

internal sealed class MarkNotificationReadCommandHandler(
    INotificationRepository notifications,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<MarkNotificationReadCommand, Unit>
{
    /// <summary>
    /// Tek bir bildirimi okundu yapar. Bildirim yoksa ya da başkasına aitse 404 döner; ikisi ayırt edilmez.
    /// </summary>
    public async Task<Unit> Handle(MarkNotificationReadCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Başkasının bildirimi de "bulunamadı" döner; varlığı sızdırılmaz.
        if (!await notifications.MarkReadAsync(request.NotificationId, userId, now, cancellationToken))
            throw new NotFoundException("notification.not_found", "Bildirim bulunamadı.");

        return Unit.Value;
    }
}

public sealed record MarkAllNotificationsReadCommand : ICommand<Unit>;

internal sealed class MarkAllNotificationsReadCommandHandler(
    INotificationRepository notifications,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<MarkAllNotificationsReadCommand, Unit>
{
    /// <summary>İsteği yapan kullanıcının okunmamış bütün bildirimlerini okundu yapar.</summary>
    public async Task<Unit> Handle(MarkAllNotificationsReadCommand request, CancellationToken cancellationToken)
    {
        await notifications.MarkAllReadAsync(currentUser.RequireUserId(), timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        return Unit.Value;
    }
}

// --------------------------------------------------------------------- Push token'ları

/// <summary>
/// Telefonun Expo push token'ını kaydeder. Mobil uygulama her girişte ve açılışta çağırır;
/// aynı token tekrar gelirse sadece last_seen_at güncellenir.
/// </summary>
public sealed record RegisterDeviceTokenCommand(string Token, string Platform) : ICommand<Unit>;

internal sealed class RegisterDeviceTokenCommandValidator : AbstractValidator<RegisterDeviceTokenCommand>
{
    /// <summary>
    /// Token dolu, en fazla 255 karakter ve Expo push token'ı biçiminde olmalı (ExponentPushToken[ ya da
    /// ExpoPushToken[ ile başlar); platform ios ya da android olmalı.
    /// </summary>
    public RegisterDeviceTokenCommandValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty()
            .MaximumLength(255)
            .Must(t => t.StartsWith("ExponentPushToken[", StringComparison.Ordinal)
                       || t.StartsWith("ExpoPushToken[", StringComparison.Ordinal))
            .WithMessage("Geçerli bir Expo push token'ı değil.")
            .WithName("Token");

        RuleFor(x => x.Platform)
            .Must(p => p is "ios" or "android")
            .WithMessage("Platform 'ios' ya da 'android' olmalı.")
            .WithName("Platform");
    }
}

internal sealed class RegisterDeviceTokenCommandHandler(
    IDeviceTokenRepository tokens,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<RegisterDeviceTokenCommand, Unit>
{
    /// <summary>Cihazın push token'ını isteği yapan kullanıcıya kaydeder (varsa günceller).</summary>
    public async Task<Unit> Handle(RegisterDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        await tokens.UpsertAsync(
            request.Token,
            currentUser.RequireUserId(),
            request.Platform,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        return Unit.Value;
    }
}

/// <summary>Çıkış yaparken çağrılır; bu cihaza artık bu hesabın bildirimleri gitmez.</summary>
public sealed record UnregisterDeviceTokenCommand(string Token) : ICommand<Unit>;

internal sealed class UnregisterDeviceTokenCommandHandler(
    IDeviceTokenRepository tokens,
    ICurrentUser currentUser) : IRequestHandler<UnregisterDeviceTokenCommand, Unit>
{
    /// <summary>Çıkışta cihazın push token'ını siler; token başkasına aitse hiçbir şey olmaz.</summary>
    public async Task<Unit> Handle(UnregisterDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        await tokens.DeleteAsync(request.Token, currentUser.RequireUserId(), cancellationToken);
        return Unit.Value;
    }
}
