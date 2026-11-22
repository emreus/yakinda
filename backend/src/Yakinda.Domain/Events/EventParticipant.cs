using Yakinda.Domain.Common;

namespace Yakinda.Domain.Events;

public sealed class EventParticipant
{
    /// <summary>
    /// Parametresiz kurucu yalnızca Dapper içindir: veritabanı satırından nesne kurarken bunu kullanır.
    /// Uygulama kodu katılım kaydını Create ile oluşturur.
    /// </summary>
    private EventParticipant() { }

    public Guid EventId { get; private set; }
    public Guid UserId { get; private set; }
    public ParticipantStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public bool IsActive => Status is ParticipantStatus.Pending or ParticipantStatus.Approved or ParticipantStatus.Waitlisted;

    /// <summary>
    /// Yeni katılım kaydı üretir. Durumu (onaylı, bekleyen ya da bekleme listesinde) Event.Admit belirler.
    /// </summary>
    public static EventParticipant Create(Guid eventId, Guid userId, ParticipantStatus status, DateTime nowUtc) => new()
    {
        EventId = eventId,
        UserId = userId,
        Status = status,
        CreatedAt = nowUtc,
        UpdatedAt = nowUtc
    };

    /// <summary>
    /// Daha önce kaydı olan kullanıcı yeniden katılmak istediğinde çağrılır. Kayıt hâlâ geçerliyse
    /// (bekleyen, onaylı, reddedilmiş ya da bekleme listesinde) duruma özel bir DomainException fırlatır;
    /// yalnızca ayrılmış (Left) kullanıcı için hata vermeden döner.
    /// </summary>
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

    /// <summary>
    /// Daha önce ayrılmış kullanıcıyı yeniden katılımcı yapar. Önce EnsureCanRejoin ile mevcut durumu
    /// denetler (yalnızca Left durumundan dönülebilir), sonra Event.Admit'in verdiği yeni durumu yazar.
    /// </summary>
    public void Rejoin(ParticipantStatus newStatus, DateTime nowUtc)
    {
        EnsureCanRejoin();
        Status = newStatus;
        UpdatedAt = nowUtc;
    }

    /// <summary>
    /// Organizatörün katılım isteğini onaylaması. Yalnızca bekleyen (Pending) istek onaylanabilir;
    /// durum Approved olur. Etkinliğin katılımcı sayacını artırmak çağıranın işidir.
    /// </summary>
    public void Approve(DateTime nowUtc)
    {
        if (Status != ParticipantStatus.Pending)
            throw new DomainException("participant.not_pending", "Sadece bekleyen istekler onaylanabilir.");
        Status = ParticipantStatus.Approved;
        UpdatedAt = nowUtc;
    }

    /// <summary>
    /// İsteği reddeder ya da onaylı katılımcıyı etkinlikten çıkarır; durum Rejected olur.
    /// Aktif bir katılım (bekleyen, onaylı ya da bekleme listesinde) yoksa hata verir.
    /// Dönen değer kullanıcının önceden onaylı olup olmadığıdır; true ise çağıran, etkinliğin
    /// katılımcı sayacını düşürür.
    /// </summary>
    public bool Reject(DateTime nowUtc)
    {
        if (!IsActive)
            throw new DomainException("participant.not_active", "Bu kullanıcının aktif bir katılımı yok.");
        var wasApproved = Status == ParticipantStatus.Approved;
        Status = ParticipantStatus.Rejected;
        UpdatedAt = nowUtc;
        return wasApproved;
    }

    /// <summary>
    /// Kullanıcının etkinlikten (ya da bekleme listesinden) ayrılması; durum Left olur.
    /// Aktif bir katılım yoksa hata verir. Dönen değer kullanıcının ayrılmadan önce onaylı katılımcı
    /// olup olmadığıdır; true ise çağıran, etkinliğin katılımcı sayacını düşürür.
    /// </summary>
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
