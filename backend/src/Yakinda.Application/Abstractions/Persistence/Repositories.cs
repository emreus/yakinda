using Yakinda.Application.Notifications;
using Yakinda.Domain.Events;
using Yakinda.Domain.Moderation;
using Yakinda.Domain.Users;

namespace Yakinda.Application.Abstractions.Persistence;

// Yazma (command) tarafının repository'leri. Hepsi aynı DbSession üzerinden,
// TransactionBehavior'ın açtığı transaction içinde çalışır.

public interface IEventRepository
{
    /// <summary>Etkinliği satır kilidiyle (FOR UPDATE) okur; eşzamanlı katılım/oylamalar sıraya girer.</summary>
    Task<Event?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(Event ev, CancellationToken cancellationToken);
    Task UpdateAsync(Event ev, CancellationToken cancellationToken);
    Task<bool> CategoryExistsAsync(short categoryId, CancellationToken cancellationToken);

    /// <summary>Saat değişince hatırlatma ve puanlama bildirimlerinin yeni saate göre tekrar gitmesi için.</summary>
    Task ResetScheduledNotificationsAsync(Guid eventId, CancellationToken cancellationToken);

    /// <summary>Kullanıcının düzenlediği, başlamamış ve aktif etkinlikler (hesap silme / yasaklama için).</summary>
    Task<IReadOnlyList<Guid>> GetUpcomingActiveIdsByOrganizerAsync(Guid organizerId, DateTime nowUtc, CancellationToken cancellationToken);
}

public interface IParticipantRepository
{
    Task<EventParticipant?> GetAsync(Guid eventId, Guid userId, CancellationToken cancellationToken);
    Task UpsertAsync(EventParticipant participant, CancellationToken cancellationToken);

    /// <summary>Bekleme listesinin başındaki kişi (en erken giren). Etkinlik satırı kilitliyken çağrılmalı.</summary>
    Task<EventParticipant?> GetFirstWaitlistedAsync(Guid eventId, CancellationToken cancellationToken);

    Task<int> CountAsync(Guid eventId, ParticipantStatus status, CancellationToken cancellationToken);

    /// <summary>Kullanıcının aktif katılımı olan (onaylı, bekleyen, bekleme listesi), başlamamış etkinlikler.</summary>
    Task<IReadOnlyList<Guid>> GetUpcomingActiveEventIdsAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken);
}

public interface IRatingRepository
{
    Task<EventRating?> GetAsync(Guid eventId, Guid raterId, CancellationToken cancellationToken);
    Task UpsertAsync(EventRating rating, CancellationToken cancellationToken);
}

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task AddAsync(User user, CancellationToken cancellationToken);

    /// <summary>Organizatörün puan toplamını atomik olarak günceller (rating_sum = rating_sum + @delta).</summary>
    Task ApplyRatingDeltaAsync(Guid userId, RatingDelta delta, CancellationToken cancellationToken);

    /// <summary>
    /// Hesap silme: kişisel veriler (e-posta, ad, biyografi, şifre, oturumlar, cihazlar, bildirimler,
    /// engellemeler, yorum metinleri) silinir; satır anonim olarak kalır ki geçmiş etkinlikler ve
    /// organizatör ortalamaları bozulmasın.
    /// </summary>
    Task AnonymizeAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Yasaklar; oturumlarını ve cihaz token'larını siler.</summary>
    Task BanAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken);
}

public interface IBlockRepository
{
    Task BlockAsync(Guid blockerId, Guid blockedId, DateTime nowUtc, CancellationToken cancellationToken);
    Task UnblockAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken);

    /// <summary>İkisinden biri diğerini engellemiş mi?</summary>
    Task<bool> IsBlockedEitherWayAsync(Guid userA, Guid userB, CancellationToken cancellationToken);
}

public interface IReportRepository
{
    /// <summary>Aynı kişinin aynı içerik için açık şikayeti yoksa ekler. Eklendiyse true.</summary>
    Task<bool> AddIfNotOpenAsync(ReportRecord report, CancellationToken cancellationToken);

    Task<ReportRecord?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Aynı içerik için açık olan bütün şikayetleri kapatır.</summary>
    Task<int> ResolveOpenForTargetAsync(
        ReportTargetType targetType,
        Guid targetId,
        ReportStatus status,
        Guid adminId,
        string? note,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

public sealed class ReportRecord
{
    public Guid Id { get; set; }
    public Guid ReporterId { get; set; }
    public ReportTargetType TargetType { get; set; }
    public Guid TargetId { get; set; }
    public ReportReason Reason { get; set; }
    public string? Details { get; set; }
    public ReportStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

public interface IRefreshTokenRepository
{
    Task AddAsync(Guid userId, string tokenHash, DateTime expiresAtUtc, CancellationToken cancellationToken);
    Task<RefreshTokenRecord?> GetActiveForUpdateAsync(string tokenHash, DateTime nowUtc, CancellationToken cancellationToken);
    Task RevokeAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken);
}

public interface INotificationRepository
{
    /// <summary>Tek kullanıcıya bildirim ekler. Aktif transaction'a dahildir; işlem geri alınırsa bildirim de yazılmaz.</summary>
    Task AddAsync(Guid userId, NotificationContent content, Guid? eventId, Guid? actorId, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Etkinliğin verilen durumlardaki tüm katılımcılarına aynı bildirimi ekler. Eklenen sayıyı döner.</summary>
    Task<int> AddForParticipantsAsync(
        Guid eventId,
        IReadOnlyCollection<ParticipantStatus> statuses,
        NotificationContent content,
        Guid? actorId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>Bildirimi okundu yapar (zaten okunduysa dokunmaz). Bildirim bu kullanıcıya ait değilse false döner.</summary>
    Task<bool> MarkReadAsync(Guid id, Guid userId, DateTime nowUtc, CancellationToken cancellationToken);

    Task MarkAllReadAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken);
}

public interface IDeviceTokenRepository
{
    /// <summary>Token'ı kaydeder. Aynı cihazda başka hesaba geçildiyse token yeni kullanıcıya taşınır.</summary>
    Task UpsertAsync(string token, Guid userId, string platform, DateTime nowUtc, CancellationToken cancellationToken);

    Task DeleteAsync(string token, Guid userId, CancellationToken cancellationToken);
}

public sealed class RefreshTokenRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTime ExpiresAt { get; set; }
}
