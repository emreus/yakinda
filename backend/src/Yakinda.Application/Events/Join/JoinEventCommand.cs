using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Caching;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Exceptions;
using Yakinda.Application.Notifications;
using Yakinda.Domain.Events;

namespace Yakinda.Application.Events.Join;

public sealed record JoinEventCommand(Guid EventId) : ICommand<JoinEventResult>;

/// <param name="Status">Approved = katıldın, Pending = organizatör onayı bekleniyor.</param>
public sealed record JoinEventResult(ParticipantStatus Status);

internal sealed class JoinEventCommandHandler(
    IEventRepository events,
    IParticipantRepository participants,
    IUserRepository users,
    IBlockRepository blocks,
    INotificationRepository notifications,
    ICurrentUser currentUser,
    ICacheInvalidator cacheInvalidator,
    TimeProvider timeProvider) : IRequestHandler<JoinEventCommand, JoinEventResult>
{
    private static readonly TimeSpan RepeatNotificationWindow = TimeSpan.FromHours(1);

    public async Task<JoinEventResult> Handle(JoinEventCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Satır kilidi: aynı anda son boş yere iki kişi katılmaya çalışırsa biri bekler,
        // sonra kontenjanı dolu görür. (Okeye 4. aranıyor senaryosu.)
        var ev = await events.GetForUpdateAsync(request.EventId, cancellationToken)
            ?? throw EventErrors.NotFound();

        if (await blocks.IsBlockedEitherWayAsync(userId, ev.OrganizerId, cancellationToken))
            throw new ForbiddenException("event.blocked", "Bu etkinliğe katılamazsın.");

        var participant = await participants.GetAsync(ev.Id, userId, cancellationToken);
        participant?.EnsureCanRejoin();

        var status = ev.Admit(userId, now);

        if (participant is null)
            participant = EventParticipant.Create(ev.Id, userId, status, now);
        else
            participant.Rejoin(status, now);

        await participants.UpsertAsync(participant, cancellationToken);

        ev.Touch(now);
        await events.UpdateAsync(ev, cancellationToken);

        // Organizatöre bildirim: onaylı etkinlikte "istek geldi", açık etkinlikte "katıldı".
        // Bekleme listesine girmek organizatörü ilgilendirmez; sırası gelince haber verilir.
        if (status != ParticipantStatus.Waitlisted)
        {
            var actor = await users.GetByIdAsync(userId, cancellationToken);
            var actorName = actor?.DisplayName ?? "Bir kullanıcı";
            var content = status == ParticipantStatus.Pending
                ? NotificationTexts.JoinRequested(actorName, ev.Title)
                : NotificationTexts.ParticipantJoined(actorName, ev.Title);

            // Katıl-ayrıl-katıl yaparak organizatöre art arda bildirim (ve push) gönderilemesin:
            // aynı kişi, aynı etkinlik için saatte en fazla bir bildirim üretir.
            var alreadyNotified = await notifications.ExistsSinceAsync(
                ev.OrganizerId, content.Type, ev.Id, userId, now - RepeatNotificationWindow, cancellationToken);
            if (!alreadyNotified)
                await notifications.AddAsync(ev.OrganizerId, content, ev.Id, userId, now, cancellationToken);
        }

        // Katılımcı sayısı (etkinlik) ve "katıldığı etkinlik" sayısı (kullanıcı) değişebilir
        cacheInvalidator.InvalidateAfterCommit(CacheTag.Event(ev.Id), CacheTag.User(userId));

        return new JoinEventResult(status);
    }
}
