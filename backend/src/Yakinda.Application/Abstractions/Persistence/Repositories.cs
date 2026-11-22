using Yakinda.Application.Notifications;
using Yakinda.Domain.Events;
using Yakinda.Domain.Moderation;
using Yakinda.Domain.Notifications;
using Yakinda.Domain.Users;

namespace Yakinda.Application.Abstractions.Persistence;

// Yazma (command) tarafının repository'leri. Hepsi aynı DbSession üzerinden,
// TransactionBehavior'ın açtığı transaction içinde çalışır.

public interface IEventRepository
{
    /// <summary>Etkinliği satır kilidiyle (FOR UPDATE) okur; eşzamanlı katılım/oylamalar sıraya girer.</summary>
    Task<Event?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Kilitsiz okuma (sadece yetki kontrolü gibi değiştirmeyen işler için).</summary>
    Task<Event?> GetAsync(Guid id, CancellationToken cancellationToken);
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

    /// <summary>Yasaklar; oturumlarını ve cihaz token'larını siler, oturum sürümünü artırır.</summary>
    Task BanAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Şifreyi değiştirir ve oturum sürümünü artırır (eski erişim token'ları geçersiz olur). Yeni sürümü döner.</summary>
    Task<int> UpdatePasswordAsync(Guid userId, string passwordHash, CancellationToken cancellationToken);

    Task UpdateProfileAsync(Guid userId, string displayName, string? bio, CancellationToken cancellationToken);

    Task ClearBioAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Görsel anahtarları. Her yöntem yerine yenisi yazılan eski anahtarı döner (dosyası silinsin diye).</summary>
public interface IMediaRepository
{
    Task<string?> SetUserAvatarAsync(Guid userId, string? key, CancellationToken cancellationToken);

    Task<string?> SetEventImageAsync(Guid eventId, string? key, CancellationToken cancellationToken);

    /// <summary>Organizatörün tüm etkinliklerinin görsellerini kaldırır (hesap silme); silinecek dosya anahtarlarını döner.</summary>
    Task<IReadOnlyList<string>> ClearEventImagesByOrganizerAsync(Guid organizerId, CancellationToken cancellationToken);
}

public interface IChatRepository
{
    Task AddAsync(Guid id, Guid eventId, Guid authorId, string body, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Mesajın etkinliği ve yazarı (silme yetkisi için).</summary>
    Task<(Guid EventId, Guid AuthorId)?> GetOwnershipAsync(Guid messageId, CancellationToken cancellationToken);

    Task SoftDeleteAsync(Guid messageId, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Organizatöre ve onaylı katılımcılara (yazar ve yazarla arasında engel olanlar hariç) bildirim ekler.
    /// Aynı etkinlik için okunmamış sohbet bildirimi olan kişiye yenisi eklenmez: okuyana kadar tek bildirim.
    /// </summary>
    Task NotifyMembersAsync(Guid eventId, Guid authorId, NotificationContent content, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Sohbet açılınca o etkinliğin sohbet bildirimleri okundu sayılır.</summary>
    Task MarkNotificationsReadAsync(Guid eventId, Guid userId, DateTime nowUtc, CancellationToken cancellationToken);
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

    /// <summary>Kullanıcının bütün oturumlarını kapatır (şifre değişince).</summary>
    Task RevokeAllForUserAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Çıkış: token varsa iptal eder; yoksa sessizce geçer.</summary>
    Task RevokeByHashAsync(string tokenHash, DateTime nowUtc, CancellationToken cancellationToken);
}

public interface IPasswordResetRepository
{
    /// <summary>Yeni kod ekler; kullanıcının kullanılmamış eski kodları geçersiz olur.</summary>
    Task AddAsync(Guid userId, string codeHash, DateTime nowUtc, DateTime expiresAtUtc, CancellationToken cancellationToken);

    /// <summary>Son kodun ne zaman gönderildiği (art arda istekleri sınırlamak için).</summary>
    Task<DateTime?> GetLastIssuedAtAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Verilen andan sonra bu hesap için üretilen kod sayısı (kullanılmış olanlar dahil).</summary>
    Task<int> CountIssuedSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken cancellationToken);

    /// <summary>Kullanılmamış ve süresi dolmamış en yeni kod (satır kilidiyle).</summary>
    Task<PasswordResetRecord?> GetActiveForUpdateAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken);

    Task IncrementAttemptsAsync(Guid id, CancellationToken cancellationToken);
    Task MarkUsedAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken);
}

public sealed class PasswordResetRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public int Attempts { get; set; }
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

    /// <summary>Aynı kişiden, aynı etkinlik için, aynı türde bildirim verilen andan sonra zaten gönderilmiş mi?</summary>
    Task<bool> ExistsSinceAsync(
        Guid userId, NotificationType type, Guid eventId, Guid actorId, DateTime sinceUtc, CancellationToken cancellationToken);

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
