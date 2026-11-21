using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Caching;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;

namespace Yakinda.Application.Events.Leave;

public sealed record LeaveEventCommand(Guid EventId) : ICommand<Unit>;

internal sealed class LeaveEventCommandHandler(
    IEventRepository events,
    IParticipantRepository participants,
    ICurrentUser currentUser,
    ICacheInvalidator cacheInvalidator,
    TimeProvider timeProvider) : IRequestHandler<LeaveEventCommand, Unit>
{
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

        ev.Touch(now);
        await events.UpdateAsync(ev, cancellationToken);

        cacheInvalidator.InvalidateAfterCommit(CacheTag.Event(ev.Id), CacheTag.User(userId));

        return Unit.Value;
    }
}
