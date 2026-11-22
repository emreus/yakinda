using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Caching;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Events.Common;
using Yakinda.Application.Notifications;

namespace Yakinda.Application.Events.Participants;

/// <summary>
/// Organizatörün katılım isteğini onaylaması ya da reddetmesi.
/// Reddetme, onaylanmış bir katılımcıyı etkinlikten çıkarmak için de kullanılabilir.
/// </summary>
public sealed record ReviewParticipantCommand(Guid EventId, Guid UserId, bool Approve) : ICommand<Unit>;

internal sealed class ReviewParticipantCommandHandler(
    IEventRepository events,
    IParticipantRepository participants,
    INotificationRepository notifications,
    WaitlistPromoter waitlist,
    ICurrentUser currentUser,
    ICacheInvalidator cacheInvalidator,
    TimeProvider timeProvider) : IRequestHandler<ReviewParticipantCommand, Unit>
{
    public async Task<Unit> Handle(ReviewParticipantCommand request, CancellationToken cancellationToken)
    {
        var organizerId = currentUser.RequireUserId();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var ev = await events.GetForUpdateAsync(request.EventId, cancellationToken)
            ?? throw EventErrors.NotFound();

        if (!ev.IsOrganizer(organizerId))
            throw EventErrors.NotOrganizer();

        ev.EnsureParticipationCanChange(now);

        var participant = await participants.GetAsync(ev.Id, request.UserId, cancellationToken)
            ?? throw EventErrors.ParticipantNotFound();

        NotificationContent content;
        if (request.Approve)
        {
            participant.Approve(now);
            ev.AddApprovedParticipant(); // kontenjan doluysa hata fırlatır, transaction geri alınır
            content = NotificationTexts.RequestApproved(ev.Title);
        }
        else
        {
            var wasApproved = participant.Reject(now);
            if (wasApproved)
                ev.RemoveApprovedParticipant();
            content = NotificationTexts.RequestRejected(ev.Title, wasApproved);
        }

        await participants.UpsertAsync(participant, cancellationToken);
        await notifications.AddAsync(request.UserId, content, ev.Id, organizerId, now, cancellationToken);

        // Ret ile yer ya da bekleyen istek kotası boşaldıysa bekleme listesinden biri alınır
        var promoted = request.Approve ? [] : await waitlist.FillOpenSeatsAsync(ev, now, cancellationToken);
        foreach (var promotedUserId in promoted)
            cacheInvalidator.InvalidateAfterCommit(CacheTag.User(promotedUserId));

        ev.Touch(now);
        await events.UpdateAsync(ev, cancellationToken);

        cacheInvalidator.InvalidateAfterCommit(CacheTag.Event(ev.Id), CacheTag.User(request.UserId));

        return Unit.Value;
    }
}
