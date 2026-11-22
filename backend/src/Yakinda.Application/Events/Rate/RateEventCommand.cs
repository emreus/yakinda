using FluentValidation;
using Yakinda.Application.Moderation;
using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Caching;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Domain.Events;

namespace Yakinda.Application.Events.Rate;

/// <summary>
/// Etkinliği 1-5 arası puanlar (varsa önceki puanı günceller).
/// Aynı transaction içinde:
///   1) event_ratings satırı eklenir/güncellenir
///   2) etkinliğin rating_sum / rating_count alanları güncellenir
///   3) organizatörün rating_sum / rating_count alanları güncellenir
/// Böylece kullanıcının puan ortalaması her zaman tutarlıdır ve okurken hesaplama gerekmez.
/// </summary>
public sealed record RateEventCommand(Guid EventId, short Score, string? Comment) : ICommand<RateEventResult>;

public sealed record RateEventResult(short Score, double? EventAverageRating, int EventRatingCount);

internal sealed class RateEventCommandValidator : AbstractValidator<RateEventCommand>
{
    /// <summary>
    /// Puan 1 ile 5 arasında olmalı; yorum en fazla 500 karakter olmalı ve içerik filtresinden geçmeli.
    /// </summary>
    public RateEventCommandValidator()
    {
        RuleFor(x => x.Score).InclusiveBetween(EventRules.MinScore, EventRules.MaxScore).WithName("Puan");
        RuleFor(x => x.Comment).MaximumLength(EventRules.CommentMaxLength).NotObjectionable().WithName("Yorum");
    }
}

internal sealed class RateEventCommandHandler(
    IEventRepository events,
    IParticipantRepository participants,
    IRatingRepository ratings,
    IUserRepository users,
    ICurrentUser currentUser,
    ICacheInvalidator cacheInvalidator,
    TimeProvider timeProvider) : IRequestHandler<RateEventCommand, RateEventResult>
{
    /// <summary>
    /// Etkinliği puanlar ya da önceki puanı günceller. Etkinliği satır kilidiyle okur, kullanıcının katılım
    /// durumunu alır ve ev.EnsureCanBeRatedBy ile oy verip veremeyeceğini denetler. Puan kaydı yoksa
    /// oluşturur, varsa günceller ve eski puanı alır. ev.ApplyRating farkı hesaplar; puan, etkinliğin
    /// toplamları ve organizatörün toplamları aynı transaction'da yazılır. Etkinliğin ve organizatörün cache
    /// etiketlerini temizletir; verilen puanı, etkinliğin yeni ortalamasını ve oy sayısını döner.
    /// </summary>
    public async Task<RateEventResult> Handle(RateEventCommand request, CancellationToken cancellationToken)
    {
        var raterId = currentUser.RequireUserId();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Etkinlik satırı kilitlenir: aynı etkinliğe gelen eşzamanlı oylar sıraya girer,
        // toplamlar kaybolmaz.
        var ev = await events.GetForUpdateAsync(request.EventId, cancellationToken)
            ?? throw EventErrors.NotFound();

        var participant = await participants.GetAsync(ev.Id, raterId, cancellationToken);
        ev.EnsureCanBeRatedBy(raterId, participant?.Status, now);

        var rating = await ratings.GetAsync(ev.Id, raterId, cancellationToken);
        short? previousScore = null;

        if (rating is null)
            rating = EventRating.Create(ev.Id, raterId, request.Score, request.Comment, now);
        else
            previousScore = rating.Update(request.Score, request.Comment, now);

        var delta = ev.ApplyRating(request.Score, previousScore);
        ev.Touch(now);

        await ratings.UpsertAsync(rating, cancellationToken);
        await events.UpdateAsync(ev, cancellationToken);
        await users.ApplyRatingDeltaAsync(ev.OrganizerId, delta, cancellationToken);

        cacheInvalidator.InvalidateAfterCommit(CacheTag.Event(ev.Id), CacheTag.User(ev.OrganizerId));

        return new RateEventResult(rating.Score, ev.AverageRating, ev.RatingCount);
    }
}
