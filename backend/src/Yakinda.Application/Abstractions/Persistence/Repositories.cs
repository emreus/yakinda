using Yakinda.Application.Notifications;
using Yakinda.Domain.Events;
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
}

public interface IParticipantRepository
{
    Task<EventParticipant?> GetAsync(Guid eventId, Guid userId, CancellationToken cancellationToken);
    Task UpsertAsync(EventParticipant participant, CancellationToken cancellationToken);

    /// <summary>Bekleme listesinin başındaki kişi (en erken giren). Etkinlik satırı kilitliyken çağrılmalı.</summary>
    Task<EventParticipant?> GetFirstWaitlistedAsync(Guid eventId, CancellationToken cancellationToken);

    Task<int> CountAsync(Guid eventId, ParticipantStatus status, CancellationToken cancellationToken);
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
