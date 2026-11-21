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
}

public interface IParticipantRepository
{
    Task<EventParticipant?> GetAsync(Guid eventId, Guid userId, CancellationToken cancellationToken);
    Task UpsertAsync(EventParticipant participant, CancellationToken cancellationToken);
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

public sealed class RefreshTokenRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTime ExpiresAt { get; set; }
}
