using FluentValidation;
using Yakinda.Application.Moderation;
using FluentValidation.Results;
using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Caching;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Domain.Events;

namespace Yakinda.Application.Events.Create;

public sealed record CreateEventCommand(
    short CategoryId,
    string Title,
    string? Description,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    double Latitude,
    double Longitude,
    string? PlaceName,
    string? Address,
    int? Capacity,
    JoinPolicy JoinPolicy,
    // 0: tek seferlik. N: aynı gün ve saatte N hafta daha tekrarlanır ("her salı okey"); her biri ayrı etkinliktir.
    int RepeatWeeks = 0) : ICommand<Guid>;

internal sealed class CreateEventCommandValidator : AbstractValidator<CreateEventCommand>
{
    public const int MaxRepeatWeeks = 12;

    public CreateEventCommandValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.CategoryId).GreaterThan((short)0).WithName("Kategori");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(EventRules.TitleMaxLength).NotObjectionable().WithName("Başlık");
        RuleFor(x => x.RepeatWeeks).InclusiveBetween(0, MaxRepeatWeeks).WithName("Tekrar");
        RuleFor(x => x.Description).MaximumLength(EventRules.DescriptionMaxLength).NotObjectionable().WithName("Açıklama");
        RuleFor(x => x.PlaceName).MaximumLength(EventRules.PlaceNameMaxLength).NotObjectionable().WithName("Mekan adı");
        RuleFor(x => x.Address).MaximumLength(EventRules.AddressMaxLength).WithName("Adres");

        RuleFor(x => x.StartsAt)
            .Must(startsAt => startsAt > timeProvider.GetUtcNow())
            .WithMessage("Başlangıç zamanı gelecekte olmalı.")
            .Must(startsAt => startsAt < timeProvider.GetUtcNow().AddYears(1))
            .WithMessage("Etkinlik en fazla 1 yıl sonrası için oluşturulabilir.");

        RuleFor(x => x.EndsAt)
            .Must((command, endsAt) => endsAt is null || endsAt > command.StartsAt)
            .WithMessage("Bitiş zamanı başlangıçtan sonra olmalı.")
            .Must((command, endsAt) => endsAt is null || endsAt - command.StartsAt <= EventRules.MaxDuration)
            .WithMessage("Bir etkinlik en fazla 7 gün sürebilir.");

        RuleFor(x => x.Latitude).InclusiveBetween(-90d, 90d);
        RuleFor(x => x.Longitude).InclusiveBetween(-180d, 180d);
        RuleFor(x => x.Capacity).InclusiveBetween(1, EventRules.MaxCapacity).WithName("Kontenjan");
        RuleFor(x => x.JoinPolicy).IsInEnum();
    }
}

internal sealed class CreateEventCommandHandler(
    IEventRepository events,
    ICurrentUser currentUser,
    ICacheInvalidator cacheInvalidator,
    TimeProvider timeProvider) : IRequestHandler<CreateEventCommand, Guid>
{
    public async Task<Guid> Handle(CreateEventCommand request, CancellationToken cancellationToken)
    {
        var organizerId = currentUser.RequireUserId();

        if (!await events.CategoryExistsAsync(request.CategoryId, cancellationToken))
            throw new ValidationException(new[] { new ValidationFailure(nameof(request.CategoryId), "Geçersiz kategori.") });

        var now = timeProvider.GetUtcNow().UtcDateTime;
        Guid firstId = default;

        // Tekrarlayan etkinlikler: her hafta için ayrı bir etkinlik (katılım ve oylar her buluşmaya ayrı).
        for (var week = 0; week <= request.RepeatWeeks; week++)
        {
            var offset = TimeSpan.FromDays(7 * week);
            var ev = Event.Create(
                organizerId,
                request.CategoryId,
                request.Title,
                request.Description,
                request.StartsAt.UtcDateTime + offset,
                request.EndsAt?.UtcDateTime + offset,
                request.Latitude,
                request.Longitude,
                request.PlaceName,
                request.Address,
                request.Capacity,
                request.JoinPolicy,
                now);

            await events.AddAsync(ev, cancellationToken);
            if (week == 0)
                firstId = ev.Id;
        }

        cacheInvalidator.InvalidateAfterCommit(CacheTag.User(organizerId));
        return firstId;
    }
}
