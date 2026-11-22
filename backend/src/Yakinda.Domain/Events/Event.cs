using Yakinda.Domain.Common;

namespace Yakinda.Domain.Events;

/// <summary>
/// Etkinlik aggregate'i. "Okeye 4. aranıyor"dan 200 kişilik partiye kadar her şey.
/// Katılımcı listesi bu nesnede tutulmaz (büyük etkinliklerde her katılımda tüm listeyi
/// yüklememek için); onun yerine onaylı katılımcı sayısı denormalize edilir ve
/// tutarlılık, satır kilidi (SELECT ... FOR UPDATE) ile sağlanır.
/// </summary>
public sealed class Event
{
    // Dapper için
    private Event() { }

    public Guid Id { get; private set; }
    public Guid OrganizerId { get; private set; }
    public short CategoryId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DateTime StartsAt { get; private set; }
    public DateTime? EndsAt { get; private set; }
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public string? PlaceName { get; private set; }
    public string? Address { get; private set; }

    /// <summary>Organizatör hariç kaç kişi katılabilir. null = sınırsız.</summary>
    public int? Capacity { get; private set; }

    public JoinPolicy JoinPolicy { get; private set; }
    public EventStatus Status { get; private set; }
    public int ParticipantCount { get; private set; }
    public long RatingSum { get; private set; }
    public int RatingCount { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public bool IsFull => Capacity is not null && ParticipantCount >= Capacity;

    public double? AverageRating =>
        RatingCount == 0 ? null : Math.Round((double)RatingSum / RatingCount, 2);

    public bool HasStarted(DateTime nowUtc) => nowUtc >= StartsAt;

    public bool IsOrganizer(Guid userId) => OrganizerId == userId;

    public static Event Create(
        Guid organizerId,
        short categoryId,
        string title,
        string? description,
        DateTime startsAtUtc,
        DateTime? endsAtUtc,
        double latitude,
        double longitude,
        string? placeName,
        string? address,
        int? capacity,
        JoinPolicy joinPolicy,
        DateTime nowUtc)
    {
        Validate(title, startsAtUtc, endsAtUtc, capacity, latitude, longitude, nowUtc);

        return new Event
        {
            Id = Guid.CreateVersion7(),
            OrganizerId = organizerId,
            CategoryId = categoryId,
            Title = title.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            StartsAt = startsAtUtc,
            EndsAt = endsAtUtc,
            Latitude = latitude,
            Longitude = longitude,
            PlaceName = string.IsNullOrWhiteSpace(placeName) ? null : placeName.Trim(),
            Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim(),
            Capacity = capacity,
            JoinPolicy = joinPolicy,
            Status = EventStatus.Active,
            ParticipantCount = 0,
            RatingSum = 0,
            RatingCount = 0,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc
        };
    }

    private static void Validate(
        string title,
        DateTime startsAtUtc,
        DateTime? endsAtUtc,
        int? capacity,
        double latitude,
        double longitude,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("event.title_required", "Etkinlik başlığı boş olamaz.");

        if (startsAtUtc <= nowUtc)
            throw new DomainException("event.start_in_past", "Etkinliğin başlangıç zamanı gelecekte olmalı.");

        if (endsAtUtc is not null && endsAtUtc <= startsAtUtc)
            throw new DomainException("event.invalid_end", "Bitiş zamanı başlangıçtan sonra olmalı.");

        if (endsAtUtc is not null && endsAtUtc - startsAtUtc > EventRules.MaxDuration)
            throw new DomainException("event.too_long", "Bir etkinlik en fazla 7 gün sürebilir.");

        if (capacity is <= 0 or > EventRules.MaxCapacity)
            throw new DomainException("event.invalid_capacity", "Kontenjan 1 ile 10.000 arasında olmalı.");

        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
            throw new DomainException("event.invalid_location", "Geçersiz konum.");
    }

    /// <summary>
    /// Organizatörün etkinliği düzenlemesi. Sadece başlamamış ve iptal edilmemiş etkinlikler düzenlenebilir;
    /// kontenjan mevcut katılımcı sayısının altına indirilemez. Katılım politikası değiştirilemez
    /// (bekleyen isteklerin ne olacağı belirsiz kalırdı).
    /// </summary>
    /// <returns>Katılımcılara haber verilmesi gereken değişiklikler.</returns>
    public EventChanges Update(
        short categoryId,
        string title,
        string? description,
        DateTime startsAtUtc,
        DateTime? endsAtUtc,
        double latitude,
        double longitude,
        string? placeName,
        string? address,
        int? capacity,
        DateTime nowUtc)
    {
        EnsureIsActive();
        if (HasStarted(nowUtc))
            throw new DomainException("event.already_started", "Başlamış bir etkinlik düzenlenemez.");

        Validate(title, startsAtUtc, endsAtUtc, capacity, latitude, longitude, nowUtc);

        if (capacity is not null && capacity < ParticipantCount)
            throw new DomainException(
                "event.capacity_below_participants",
                $"Kontenjan mevcut katılımcı sayısından ({ParticipantCount}) az olamaz.");

        var newPlace = Normalize(placeName);
        var newAddress = Normalize(address);

        var changes = new EventChanges(
            TimeChanged: StartsAt != startsAtUtc || EndsAt != endsAtUtc,
            PlaceChanged: Math.Abs(Latitude - latitude) > 1e-7
                          || Math.Abs(Longitude - longitude) > 1e-7
                          || PlaceName != newPlace
                          || Address != newAddress,
            CapacityIncreased: Capacity is not null && (capacity is null || capacity > Capacity));

        CategoryId = categoryId;
        Title = title.Trim();
        Description = Normalize(description);
        StartsAt = startsAtUtc;
        EndsAt = endsAtUtc;
        Latitude = latitude;
        Longitude = longitude;
        PlaceName = newPlace;
        Address = newAddress;
        Capacity = capacity;
        Touch(nowUtc);

        return changes;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void EnsureIsActive()
    {
        if (Status == EventStatus.Cancelled)
            throw new DomainException("event.cancelled", "Bu etkinlik iptal edilmiş.");
    }

    /// <summary>Katılım değişiklikleri (katıl/ayrıl/onayla/reddet) sadece etkinlik başlamadan yapılabilir.</summary>
    public void EnsureParticipationCanChange(DateTime nowUtc)
    {
        EnsureIsActive();
        if (HasStarted(nowUtc))
            throw new DomainException("event.already_started", "Etkinlik başladı, katılım bilgileri artık değiştirilemez.");
    }

    /// <summary>
    /// Yeni bir katılım isteğini değerlendirir ve kullanıcının alacağı durumu döner.
    /// Açık etkinliklerde katılımcı sayısı hemen artar.
    /// </summary>
    public ParticipantStatus Admit(Guid userId, DateTime nowUtc)
    {
        EnsureParticipationCanChange(nowUtc);

        if (IsOrganizer(userId))
            throw new DomainException("event.organizer_cannot_join", "Kendi etkinliğine katılımcı olarak katılamazsın.");

        // Kontenjan doluysa bekleme listesine; yer açılınca sırayla alınır.
        if (IsFull)
            return ParticipantStatus.Waitlisted;

        // Bekleyen istekler kontenjandan düşmez; organizatör onayladıkça yer dolar.
        if (JoinPolicy == JoinPolicy.ApprovalRequired)
            return ParticipantStatus.Pending;

        AddApprovedParticipant();
        return ParticipantStatus.Approved;
    }

    public void AddApprovedParticipant()
    {
        if (IsFull)
            throw new DomainException("event.full", "Kontenjan dolu.");
        ParticipantCount++;
    }

    public void RemoveApprovedParticipant()
    {
        if (ParticipantCount > 0)
            ParticipantCount--;
    }

    public void Cancel(DateTime nowUtc)
    {
        EnsureIsActive();
        if (HasStarted(nowUtc))
            throw new DomainException("event.already_started", "Başlamış bir etkinlik iptal edilemez.");

        Status = EventStatus.Cancelled;
        Touch(nowUtc);
    }

    /// <summary>
    /// Oylama kuralları:
    /// - Etkinlik iptal edilmemiş olmalı
    /// - Organizatör kendi etkinliğini oylayamaz
    /// - Sadece onaylı katılımcılar oylayabilir
    /// - Oylama etkinlik başlayınca açılır, bitişten 14 gün sonra kapanır
    /// </summary>
    public void EnsureCanBeRatedBy(Guid userId, ParticipantStatus? participantStatus, DateTime nowUtc)
    {
        EnsureIsActive();

        if (IsOrganizer(userId))
            throw new DomainException("rating.own_event", "Kendi etkinliğini oylayamazsın.");

        if (participantStatus != ParticipantStatus.Approved)
            throw new DomainException("rating.not_participant", "Sadece etkinliğe katılanlar oy verebilir.");

        if (nowUtc < EventRules.RatingOpensAt(StartsAt))
            throw new DomainException("rating.not_open_yet", "Oylama etkinlik başladığında açılır.");

        if (nowUtc > EventRules.RatingClosesAt(StartsAt, EndsAt))
            throw new DomainException("rating.closed", "Bu etkinliğin oylama süresi doldu.");
    }

    /// <summary>
    /// Yeni ya da güncellenen puanı etkinliğin toplamına yansıtır ve organizatörün
    /// toplamına uygulanması gereken farkı döner.
    /// </summary>
    public RatingDelta ApplyRating(short newScore, short? previousScore)
    {
        var delta = previousScore is null
            ? new RatingDelta(newScore, 1)
            : new RatingDelta(newScore - previousScore.Value, 0);

        RatingSum += delta.SumDelta;
        RatingCount += delta.CountDelta;
        return delta;
    }

    public void Touch(DateTime nowUtc) => UpdatedAt = nowUtc;
}

/// <summary>Bir puan işleminin toplam/adet üzerindeki etkisi.</summary>
public readonly record struct RatingDelta(long SumDelta, int CountDelta);

/// <param name="TimeChanged">Başlangıç ya da bitiş değişti (hatırlatma yeniden kurulmalı, katılımcılara haber verilmeli).</param>
/// <param name="PlaceChanged">Konum, mekan adı ya da adres değişti.</param>
/// <param name="CapacityIncreased">Kontenjan arttı ya da kaldırıldı (bekleme listesinden alınabilir).</param>
public readonly record struct EventChanges(bool TimeChanged, bool PlaceChanged, bool CapacityIncreased)
{
    public bool ShouldNotifyParticipants => TimeChanged || PlaceChanged;
}
