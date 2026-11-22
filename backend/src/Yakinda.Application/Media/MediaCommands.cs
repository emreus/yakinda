using FluentValidation;
using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Caching;
using Yakinda.Application.Abstractions.Media;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Events;
using Yakinda.Application.Exceptions;
using Yakinda.Application.Moderation;
using Yakinda.Domain.Common;
using Yakinda.Domain.Events;

namespace Yakinda.Application.Media;

/// <summary>Görselin herkese açık adresi (API kökünden göreli: "/media/avatars/....jpg"); kaldırıldıysa null.</summary>
public sealed record MediaResult(string? Url);

public static class MediaUrl
{
    public const string Prefix = "/media/";

    public static string? For(string? key) => key is null ? null : Prefix + key;

    /// <summary>SQL'de anahtar kolonundan adres üretir (kolon NULL ise sonuç da NULL).</summary>
    public static string Sql(string column) => $"('{Prefix}' || {column})";
}

internal static class ImageRules
{
    public const string InvalidMessage =
        "Görsel okunamadı. JPEG, PNG ya da WebP biçiminde, en fazla 5 MB ve 8000×8000 piksel bir fotoğraf seç.";

    public static IRuleBuilderOptions<T, byte[]> ValidImage<T>(this IRuleBuilder<T, byte[]> rule) =>
        rule.Must(data => ImageFile.TryRead(data) is not null).WithMessage(InvalidMessage).OverridePropertyName("file");

    public static ImageFile Read(byte[] data) =>
        ImageFile.TryRead(data) ?? throw new DomainException("media.invalid_image", InvalidMessage);
}

// ------------------------------------------------------------------ profil fotoğrafı

public sealed record SetMyAvatarCommand(byte[] Data) : ICommand<MediaResult>;

internal sealed class SetMyAvatarCommandValidator : AbstractValidator<SetMyAvatarCommand>
{
    public SetMyAvatarCommandValidator() => RuleFor(x => x.Data).ValidImage();
}

internal sealed class SetMyAvatarCommandHandler(
    IMediaRepository media,
    MediaChanges changes,
    ICurrentUser currentUser,
    ICacheInvalidator cacheInvalidator) : IRequestHandler<SetMyAvatarCommand, MediaResult>
{
    public async Task<MediaResult> Handle(SetMyAvatarCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var key = await changes.SaveAsync(MediaFolders.Avatars, ImageRules.Read(request.Data), cancellationToken);

        changes.DeleteAfterCommit(await media.SetUserAvatarAsync(userId, key, cancellationToken));
        cacheInvalidator.InvalidateAfterCommit(CacheTag.User(userId));
        return new MediaResult(MediaUrl.For(key));
    }
}

public sealed record DeleteMyAvatarCommand : ICommand<Unit>;

internal sealed class DeleteMyAvatarCommandHandler(
    IMediaRepository media,
    MediaChanges changes,
    ICurrentUser currentUser,
    ICacheInvalidator cacheInvalidator) : IRequestHandler<DeleteMyAvatarCommand, Unit>
{
    public async Task<Unit> Handle(DeleteMyAvatarCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        changes.DeleteAfterCommit(await media.SetUserAvatarAsync(userId, null, cancellationToken));
        cacheInvalidator.InvalidateAfterCommit(CacheTag.User(userId));
        return Unit.Value;
    }
}

// ------------------------------------------------------------------ etkinlik görseli

public sealed record SetEventImageCommand(Guid EventId, byte[] Data) : ICommand<MediaResult>;

internal sealed class SetEventImageCommandValidator : AbstractValidator<SetEventImageCommand>
{
    public SetEventImageCommandValidator() => RuleFor(x => x.Data).ValidImage();
}

internal sealed class SetEventImageCommandHandler(
    IEventRepository events,
    IMediaRepository media,
    MediaChanges changes,
    ICurrentUser currentUser,
    ICacheInvalidator cacheInvalidator) : IRequestHandler<SetEventImageCommand, MediaResult>
{
    public async Task<MediaResult> Handle(SetEventImageCommand request, CancellationToken cancellationToken)
    {
        await EventImageAccess.RequireOrganizerAsync(events, currentUser, request.EventId, cancellationToken);

        var key = await changes.SaveAsync(MediaFolders.Events, ImageRules.Read(request.Data), cancellationToken);
        changes.DeleteAfterCommit(await media.SetEventImageAsync(request.EventId, key, cancellationToken));
        cacheInvalidator.InvalidateAfterCommit(CacheTag.Event(request.EventId));
        return new MediaResult(MediaUrl.For(key));
    }
}

public sealed record DeleteEventImageCommand(Guid EventId) : ICommand<Unit>;

internal sealed class DeleteEventImageCommandHandler(
    IEventRepository events,
    IMediaRepository media,
    MediaChanges changes,
    ICurrentUser currentUser,
    ICacheInvalidator cacheInvalidator) : IRequestHandler<DeleteEventImageCommand, Unit>
{
    public async Task<Unit> Handle(DeleteEventImageCommand request, CancellationToken cancellationToken)
    {
        await EventImageAccess.RequireOrganizerAsync(events, currentUser, request.EventId, cancellationToken);

        changes.DeleteAfterCommit(await media.SetEventImageAsync(request.EventId, null, cancellationToken));
        cacheInvalidator.InvalidateAfterCommit(CacheTag.Event(request.EventId));
        return Unit.Value;
    }
}

internal static class EventImageAccess
{
    public static async Task RequireOrganizerAsync(
        IEventRepository events, ICurrentUser currentUser, Guid eventId, CancellationToken cancellationToken)
    {
        var ev = await events.GetForUpdateAsync(eventId, cancellationToken) ?? throw EventErrors.NotFound();
        if (ev.OrganizerId != currentUser.RequireUserId())
            throw EventErrors.NotOrganizer();
        if (ev.Status != EventStatus.Active)
            throw new DomainException("event.not_active", "İptal edilmiş ya da kaldırılmış etkinliğin görseli değiştirilemez.");
    }
}

// ------------------------------------------------------------------ profil düzenleme

public sealed record UpdateProfileCommand(string DisplayName, string? Bio) : ICommand<Unit>;

internal sealed class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public const int BioMaxLength = 300;

    public UpdateProfileCommandValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MinimumLength(2).MaximumLength(50).NotObjectionable().WithName("Görünen ad");
        RuleFor(x => x.Bio).MaximumLength(BioMaxLength).NotObjectionable().WithName("Hakkımda");
    }
}

internal sealed class UpdateProfileCommandHandler(
    IUserRepository users,
    ICurrentUser currentUser,
    ICacheInvalidator cacheInvalidator) : IRequestHandler<UpdateProfileCommand, Unit>
{
    public async Task<Unit> Handle(UpdateProfileCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var bio = string.IsNullOrWhiteSpace(request.Bio) ? null : request.Bio.Trim();
        await users.UpdateProfileAsync(userId, request.DisplayName.Trim(), bio, cancellationToken);
        cacheInvalidator.InvalidateAfterCommit(CacheTag.User(userId));
        return Unit.Value;
    }
}
