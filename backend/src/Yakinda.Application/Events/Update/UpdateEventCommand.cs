using FluentValidation;
using Yakinda.Application.Moderation;
using FluentValidation.Results;
using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Caching;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Events.Common;
using Yakinda.Application.Notifications;
using Yakinda.Domain.Events;

namespace Yakinda.Application.Events.Update;

/// <summary>
/// Organizatör etkinliği düzenler (başlamadan önce). Saat ya da yer değiştiyse onaylı katılımcılara ve
/// bekleyen isteklere bildirim gider, hatırlatma yeni saate göre tekrar kurulur. Kontenjan arttıysa
/// bekleme listesinden kişi alınır. Katılım politikası (açık / onaylı) değiştirilemez.
/// </summary>
public sealed record UpdateEventCommand(
    Guid EventId,
    short CategoryId,
    string Title,
    string? Description,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    double Latitude,
    double Longitude,
    string? PlaceName,
    string? Address,
    int? Capacity) : ICommand<Unit>;

internal sealed class UpdateEventCommandValidator : AbstractValidator<UpdateEventCommand>
{
    /// <summary>
    /// Etkinlik düzenleme kuralları: oluşturmadakiyle aynı alan sınırları ve içerik filtresi; başlangıç
    /// gelecekte; bitiş başlangıçtan sonra ve en fazla 7 gün sonra; kontenjan verildiyse 1 ile 10.000 arasında.
    /// </summary>
    public UpdateEventCommandValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.CategoryId).GreaterThan((short)0).WithName("Kategori");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(EventRules.TitleMaxLength).NotObjectionable().WithName("Başlık");
        RuleFor(x => x.Description).MaximumLength(EventRules.DescriptionMaxLength).NotObjectionable().WithName("Açıklama");
        RuleFor(x => x.StartsAt)
            .Must(startsAt => startsAt > timeProvider.GetUtcNow())
            .WithMessage("Başlangıç zamanı gelecekte olmalı.");
        RuleFor(x => x.EndsAt)
            .Must((command, endsAt) => endsAt is null || endsAt > command.StartsAt)
            .WithMessage("Bitiş zamanı başlangıçtan sonra olmalı.")
            .Must((command, endsAt) => endsAt is null || endsAt - command.StartsAt <= EventRules.MaxDuration)
            .WithMessage("Bir etkinlik en fazla 7 gün sürebilir.");
        RuleFor(x => x.Latitude).InclusiveBetween(-90d, 90d);
        RuleFor(x => x.Longitude).InclusiveBetween(-180d, 180d);
        RuleFor(x => x.PlaceName).MaximumLength(EventRules.PlaceNameMaxLength).NotObjectionable().WithName("Mekan");
        RuleFor(x => x.Address).MaximumLength(EventRules.AddressMaxLength).WithName("Adres");
        RuleFor(x => x.Capacity)
            .InclusiveBetween(1, EventRules.MaxCapacity)
            .When(x => x.Capacity is not null)
            .WithName("Kontenjan");
    }
}

internal sealed class UpdateEventCommandHandler(
    IEventRepository events,
    INotificationRepository notifications,
    WaitlistPromoter waitlist,
    ICurrentUser currentUser,
    ICacheInvalidator cacheInvalidator,
    TimeProvider timeProvider) : IRequestHandler<UpdateEventCommand, Unit>
{
    /// <summary>
    /// Etkinliği düzenler. Etkinliği satır kilidiyle okur; organizatör değilse 403, kategori değiştiyse ve
    /// yenisi yoksa 400 döner. Kuralları ev.Update uygular ve neyin değiştiğini bildirir: kontenjan
    /// arttıysa bekleme listesinden kişi alınır, saat değiştiyse hatırlatma işaretleri sıfırlanır, saat ya da
    /// yer değiştiyse onaylı, bekleyen ve bekleme listesindeki herkese bildirim eklenir. En sonda etkinliğin,
    /// organizatörün ve listeden alınanların cache etiketleri temizlenmek üzere kuyruğa eklenir.
    /// </summary>
    public async Task<Unit> Handle(UpdateEventCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var ev = await events.GetForUpdateAsync(request.EventId, cancellationToken)
            ?? throw EventErrors.NotFound();

        if (!ev.IsOrganizer(userId))
            throw EventErrors.NotOrganizer();

        if (request.CategoryId != ev.CategoryId && !await events.CategoryExistsAsync(request.CategoryId, cancellationToken))
            throw new ValidationException(new[] { new ValidationFailure(nameof(request.CategoryId), "Geçersiz kategori.") });

        var changes = ev.Update(
            request.CategoryId,
            request.Title,
            request.Description,
            request.StartsAt.UtcDateTime,
            request.EndsAt?.UtcDateTime,
            request.Latitude,
            request.Longitude,
            request.PlaceName,
            request.Address,
            request.Capacity,
            now);

        var promoted = changes.CapacityIncreased
            ? await waitlist.FillOpenSeatsAsync(ev, now, cancellationToken)
            : [];

        await events.UpdateAsync(ev, cancellationToken);

        if (changes.TimeChanged)
            await events.ResetScheduledNotificationsAsync(ev.Id, cancellationToken);

        if (changes.ShouldNotifyParticipants)
        {
            await notifications.AddForParticipantsAsync(
                ev.Id,
                [ParticipantStatus.Approved, ParticipantStatus.Pending, ParticipantStatus.Waitlisted],
                NotificationTexts.EventUpdated(ev.Title, changes.TimeChanged, changes.PlaceChanged),
                actorId: userId,
                now,
                cancellationToken);
        }

        cacheInvalidator.InvalidateAfterCommit(
            [CacheTag.Event(ev.Id), CacheTag.User(userId), .. promoted.Select(CacheTag.User)]);

        return Unit.Value;
    }
}
