using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Caching;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Notifications;
using Yakinda.Domain.Events;

namespace Yakinda.Application.Events.Cancel;

public sealed record CancelEventCommand(Guid EventId) : ICommand<Unit>;

internal sealed class CancelEventCommandHandler(
    IEventRepository events,
    INotificationRepository notifications,
    ICurrentUser currentUser,
    ICacheInvalidator cacheInvalidator,
    TimeProvider timeProvider) : IRequestHandler<CancelEventCommand, Unit>
{
    /// <summary>
    /// Etkinliği iptal eder. Etkinliği satır kilidiyle okur; yoksa 404, isteği yapan organizatör değilse
    /// 403 döner. ev.Cancel kuralı uygular (başlamış etkinlik iptal edilemez), etkinlik kaydedilir ve onaylı
    /// katılımcılarla isteği bekleyenlere "iptal edildi" bildirimi eklenir. Etkinliğin ve organizatörün
    /// cache etiketleri commit sonrası temizlenir.
    /// </summary>
    public async Task<Unit> Handle(CancelEventCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var ev = await events.GetForUpdateAsync(request.EventId, cancellationToken)
            ?? throw EventErrors.NotFound();

        if (!ev.IsOrganizer(userId))
            throw EventErrors.NotOrganizer();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        ev.Cancel(now);
        cacheInvalidator.InvalidateAfterCommit(CacheTag.Event(ev.Id), CacheTag.User(ev.OrganizerId));
        await events.UpdateAsync(ev, cancellationToken);

        // Onaylı katılımcılara ve isteği bekleyenlere haber ver
        await notifications.AddForParticipantsAsync(
            ev.Id,
            [ParticipantStatus.Pending, ParticipantStatus.Approved],
            NotificationTexts.EventCancelled(ev.Title),
            actorId: userId,
            now,
            cancellationToken);

        return Unit.Value;
    }
}
