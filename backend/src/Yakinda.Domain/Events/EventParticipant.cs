using Yakinda.Domain.Common;

namespace Yakinda.Domain.Events;

public sealed class EventParticipant
{
    // Dapper için
    private EventParticipant() { }

    public Guid EventId { get; private set; }
    public Guid UserId { get; private set; }
    public ParticipantStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public bool IsActive => Status is ParticipantStatus.Pending or ParticipantStatus.Approved or ParticipantStatus.Waitlisted;

    public static EventParticipant Create(Guid eventId, Guid userId, ParticipantStatus status, DateTime nowUtc) => new()
    {
        EventId = eventId,
        UserId = userId,
        Status = status,
        CreatedAt = nowUtc,
        UpdatedAt = nowUtc
    };

    /// <summary>Daha önce kaydı olan kullanıcı tekrar katılmak isterse.</summary>
    public void EnsureCanRejoin()
    {
        switch (Status)
        {
            case ParticipantStatus.Pending:
                throw new DomainException("participant.already_pending", "Katılım isteğin zaten organizatör onayı bekliyor.");
            case ParticipantStatus.Approved:
                throw new DomainException("participant.already_joined", "Bu etkinliğe zaten katılıyorsun.");
            case ParticipantStatus.Rejected:
                throw new DomainException("participant.rejected", "Bu etkinliğe katılım isteğin reddedildi.");
            case ParticipantStatus.Waitlisted:
                throw new DomainException("participant.already_waitlisted", "Zaten bekleme listesindesin.");
        }
    }

    /// <summary>Yer açıldığında bekleme listesinden alır: açık etkinlikte onaylı, onaylı etkinlikte bekleyen istek olur.</summary>
    public void PromoteFromWaitlist(ParticipantStatus newStatus, DateTime nowUtc)
    {
        if (Status != ParticipantStatus.Waitlisted)
            throw new DomainException("participant.not_waitlisted", "Bu kullanıcı bekleme listesinde değil.");
        if (newStatus is not (ParticipantStatus.Approved or ParticipantStatus.Pending))
            throw new ArgumentOutOfRangeException(nameof(newStatus));

        Status = newStatus;
        UpdatedAt = nowUtc;
    }

    public void Rejoin(ParticipantStatus newStatus, DateTime nowUtc)
    {
        EnsureCanRejoin();
        Status = newStatus;
        UpdatedAt = nowUtc;
    }

    public void Approve(DateTime nowUtc)
    {
        if (Status != ParticipantStatus.Pending)
            throw new DomainException("participant.not_pending", "Sadece bekleyen istekler onaylanabilir.");
        Status = ParticipantStatus.Approved;
        UpdatedAt = nowUtc;
    }

    /// <returns>Kullanıcı reddedilmeden önce onaylı katılımcı mıydı? (sayaç düşülsün diye)</returns>
    public bool Reject(DateTime nowUtc)
    {
        if (!IsActive)
            throw new DomainException("participant.not_active", "Bu kullanıcının aktif bir katılımı yok.");
        var wasApproved = Status == ParticipantStatus.Approved;
        Status = ParticipantStatus.Rejected;
        UpdatedAt = nowUtc;
        return wasApproved;
    }

    /// <returns>Kullanıcı ayrılmadan önce onaylı katılımcı mıydı?</returns>
    public bool Leave(DateTime nowUtc)
    {
        if (!IsActive)
            throw new DomainException("participant.not_active", "Bu etkinlikte aktif bir katılımın yok.");
        var wasApproved = Status == ParticipantStatus.Approved;
        Status = ParticipantStatus.Left;
        UpdatedAt = nowUtc;
        return wasApproved;
    }
}
