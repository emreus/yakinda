using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Caching;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Events.Common;

namespace Yakinda.Application.Events.Leave;

public sealed record LeaveEventCommand(Guid EventId) : ICommand<Unit>;

internal sealed class LeaveEventCommandHandler(
    IEventRepository events,
    IParticipantRepository participants,
    WaitlistPromoter waitlist,
    ICurrentUser currentUser,
    ICacheInvalidator cacheInvalidator,
    TimeProvider timeProvider) : IRequestHandler<LeaveEventCommand, Unit>
{
    /// <summary>
    /// Etkinlikten (ya da bekleme listesinden) ayrılır. Etkinliği satır kilidiyle okur; etkinlik aktif ve
    /// başlamamış olmalıdır. Katılım kaydı yoksa 404 döner. Kullanıcı onaylı katılımcıysa sayaç bir düşer;
    /// boşalan yere bekleme listesinden kişi alınır. Etkinliğin, ayrılanın ve listeden alınanların cache
    /// etiketleri commit sonrası temizlenir.
    /// </summary>
    public async Task<Unit> Handle(LeaveEventCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var ev = await events.GetForUpdateAsync(request.EventId, cancellationToken)
            ?? throw EventErrors.NotFound();

        ev.EnsureParticipationCanChange(now);

        var participant = await participants.GetAsync(ev.Id, userId, cancellationToken)
            ?? throw EventErrors.ParticipantNotFound();

        if (participant.Leave(now))
            ev.RemoveApprovedParticipant();

        await participants.UpsertAsync(participant, cancellationToken);

        // Boşalan yere bekleme listesinden biri alınır
        var promoted = await waitlist.FillOpenSeatsAsync(ev, now, cancellationToken);

        ev.Touch(now);
        await events.UpdateAsync(ev, cancellationToken);

        cacheInvalidator.InvalidateAfterCommit(
            [CacheTag.Event(ev.Id), CacheTag.User(userId), .. promoted.Select(CacheTag.User)]);

        return Unit.Value;
    }
}
