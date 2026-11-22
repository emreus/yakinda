using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Notifications;
using Yakinda.Domain.Events;

namespace Yakinda.Application.Events.Common;

/// <summary>
/// Kontenjanda yer açıldığında (biri ayrıldı, çıkarıldı ya da kontenjan arttı) bekleme listesindekileri
/// giriş sırasıyla alır ve haber verir:
///  * Açık etkinlik: doğrudan onaylı katılımcı olur.
///  * Onaylı etkinlik: bekleyen istek olur, organizatöre "katılım isteği" bildirimi gider. Bekleyen istekler
///    de yer tuttuğu varsayılır; böylece tek bir boş yer için bütün liste birden isteğe dönüşmez.
/// Etkinlik satırı çağırandan önce FOR UPDATE ile kilitlenmiş olmalı; değişiklikler aynı transaction'da yazılır.
/// </summary>
internal sealed class WaitlistPromoter(
    IParticipantRepository participants,
    INotificationRepository notifications,
    IUserRepository users)
{
    /// <summary>
    /// Boş yerleri bekleme listesinden doldurur. Etkinlik aktif değilse, başladıysa ya da (kontenjan
    /// sınırsızken) bekleyen kimse yoksa hiçbir şey yapmaz. Yer oldukça listenin başındaki kişiyi alır:
    /// onaylı etkinlikte bekleyen isteğe çevirip organizatöre bildirim ekler, açık etkinlikte doğrudan
    /// onaylı katılımcı yapıp sayacı artırır. Alınan her kişiye "yer açıldı" bildirimi yazılır.
    /// Listeden alınan kullanıcıların kimliklerini döner (cache temizliği için).
    /// </summary>
    public async Task<IReadOnlyList<Guid>> FillOpenSeatsAsync(Event ev, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var promoted = new List<Guid>();
        if (ev.Status != EventStatus.Active || ev.HasStarted(nowUtc) || ev.Capacity is null && !await AnyWaitlistedAsync(ev, cancellationToken))
            return promoted;

        var approvalRequired = ev.JoinPolicy == JoinPolicy.ApprovalRequired;
        var pending = approvalRequired ? await participants.CountAsync(ev.Id, ParticipantStatus.Pending, cancellationToken) : 0;

        while (ev.Capacity is null || ev.ParticipantCount + pending < ev.Capacity)
        {
            var next = await participants.GetFirstWaitlistedAsync(ev.Id, cancellationToken);
            if (next is null)
                break;

            if (approvalRequired)
            {
                next.PromoteFromWaitlist(ParticipantStatus.Pending, nowUtc);
                pending++;

                var user = await users.GetByIdAsync(next.UserId, cancellationToken);
                await notifications.AddAsync(
                    ev.OrganizerId,
                    NotificationTexts.JoinRequested(user?.DisplayName ?? "Bir kullanıcı", ev.Title),
                    ev.Id,
                    next.UserId,
                    nowUtc,
                    cancellationToken);
            }
            else
            {
                next.PromoteFromWaitlist(ParticipantStatus.Approved, nowUtc);
                ev.AddApprovedParticipant();
            }

            await participants.UpsertAsync(next, cancellationToken);
            await notifications.AddAsync(
                next.UserId,
                NotificationTexts.WaitlistPromoted(ev.Title, approved: !approvalRequired),
                ev.Id,
                actorId: null,
                nowUtc,
                cancellationToken);

            promoted.Add(next.UserId);
        }

        return promoted;
    }

    /// <summary>
    /// Bekleme listesinde en az bir kişi var mı? Kontenjanı kaldırılmış (sınırsız) etkinlikte, döngüye
    /// girmeden önce buna bakılır.
    /// </summary>
    private async Task<bool> AnyWaitlistedAsync(Event ev, CancellationToken cancellationToken) =>
        await participants.CountAsync(ev.Id, ParticipantStatus.Waitlisted, cancellationToken) > 0;
}
