using Yakinda.Application.Abstractions.Caching;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Events.Common;
using Yakinda.Application.Notifications;
using Yakinda.Domain.Events;

namespace Yakinda.Application.Safety;

/// <summary>
/// Hesap silme ve yasaklamada ortak adımlar: kullanıcının yaklaşan etkinliklerdeki yerini boşaltmak
/// (bekleme listesi ilerler) ve düzenlediği yaklaşan etkinlikleri kapatıp katılımcılara haber vermek.
/// Aktif transaction içinde çalışır.
/// </summary>
internal sealed class AccountCleanup(
    IEventRepository events,
    IParticipantRepository participants,
    INotificationRepository notifications,
    WaitlistPromoter waitlist,
    ICacheInvalidator cacheInvalidator)
{
    /// <summary>
    /// Kullanıcının yaklaşan etkinliklerdeki yerini boşaltır (hesap silme ve yasaklamada). Aktif katılımı
    /// olan her yaklaşan etkinlik için: etkinliği satır kilidiyle okur, katılımı Left yapar, kişi onaylıydıysa
    /// sayacı düşürür, boşalan yere bekleme listesinden kişi alır ve ilgili cache etiketlerini temizletir.
    /// </summary>
    public async Task LeaveUpcomingEventsAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        foreach (var eventId in await participants.GetUpcomingActiveEventIdsAsync(userId, nowUtc, cancellationToken))
        {
            var ev = await events.GetForUpdateAsync(eventId, cancellationToken);
            var participant = await participants.GetAsync(eventId, userId, cancellationToken);
            if (ev is null || participant is null || !participant.IsActive)
                continue;

            if (participant.Leave(nowUtc))
                ev.RemoveApprovedParticipant();
            await participants.UpsertAsync(participant, cancellationToken);

            var promoted = await waitlist.FillOpenSeatsAsync(ev, nowUtc, cancellationToken);
            ev.Touch(nowUtc);
            await events.UpdateAsync(ev, cancellationToken);

            cacheInvalidator.InvalidateAfterCommit([CacheTag.Event(ev.Id), CacheTag.User(ev.OrganizerId), .. promoted.Select(CacheTag.User)]);
        }
    }

    /// <summary>
    /// Kullanıcının düzenlediği, aktif ve başlamamış bütün etkinlikleri kapatır. removedByModerator true
    /// ise yönetici kaldırmış sayılır (yasaklama); false ise organizatör hesabını silmiştir ve etkinlikler
    /// iptal edilir. Her etkinlik satır kilidiyle okunur ve CloseAsync ile kapatılır.
    /// </summary>
    public async Task CloseUpcomingOrganizedEventsAsync(
        Guid organizerId,
        bool removedByModerator,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        foreach (var eventId in await events.GetUpcomingActiveIdsByOrganizerAsync(organizerId, nowUtc, cancellationToken))
        {
            var ev = await events.GetForUpdateAsync(eventId, cancellationToken);
            if (ev is null || ev.Status != EventStatus.Active)
                continue;

            await CloseAsync(ev, removedByModerator, nowUtc, cancellationToken);
        }
    }

    /// <summary>Etkinliği kapatır (iptal ya da kaldırma) ve aktif katılımcılara bildirir.</summary>
    public async Task CloseAsync(Event ev, bool removedByModerator, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var wasActive = removedByModerator ? ev.RemoveByModerator(nowUtc) : CancelIfPossible(ev, nowUtc);
        await events.UpdateAsync(ev, cancellationToken);

        if (wasActive)
        {
            await notifications.AddForParticipantsAsync(
                ev.Id,
                [ParticipantStatus.Approved, ParticipantStatus.Pending, ParticipantStatus.Waitlisted],
                removedByModerator ? NotificationTexts.EventRemoved(ev.Title) : NotificationTexts.EventCancelled(ev.Title),
                actorId: null,
                nowUtc,
                cancellationToken);
        }

        cacheInvalidator.InvalidateAfterCommit(CacheTag.Event(ev.Id), CacheTag.User(ev.OrganizerId));
    }

    /// <summary>
    /// Etkinlik aktifse ve başlamadıysa iptal eder ve true döner; aksi halde dokunmadan false döner
    /// (hata fırlatmaz).
    /// </summary>
    private static bool CancelIfPossible(Event ev, DateTime nowUtc)
    {
        if (ev.Status != EventStatus.Active || ev.HasStarted(nowUtc))
            return false;
        ev.Cancel(nowUtc);
        return true;
    }
}
