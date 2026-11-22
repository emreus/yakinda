namespace Yakinda.Domain.Events;

/// <summary>
/// Etkinlik ve oylama kuralları tek yerde. Hem domain nesneleri hem de
/// okuma tarafındaki sorgular (canRate vb.) aynı kuralları kullanır.
/// </summary>
public static class EventRules
{
    public const int TitleMaxLength = 120;
    public const int DescriptionMaxLength = 2000;
    public const int PlaceNameMaxLength = 150;
    public const int AddressMaxLength = 300;
    public const int MaxCapacity = 10_000;

    /// <summary>Bir hesabın aynı anda sahip olabileceği yaklaşan (aktif) etkinlik sayısı; haritanın tek hesapla doldurulmasını önler.</summary>
    public const int MaxUpcomingEventsPerOrganizer = 50;
    public const int CommentMaxLength = 500;

    public const short MinScore = 1;
    public const short MaxScore = 5;

    /// <summary>Bitiş saati girilmemiş etkinlikler bu süre sonunda bitmiş sayılır.</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromHours(3);

    /// <summary>Bir etkinlik en fazla bu kadar sürebilir.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(7);

    /// <summary>Etkinlik bittikten sonra oylamanın açık kalacağı süre.</summary>
    public static readonly TimeSpan RatingWindowAfterEnd = TimeSpan.FromDays(14);

    /// <summary>
    /// Etkinliğin bittiği sayılan an: bitiş saati girilmişse o, girilmemişse başlangıç + 3 saat.
    /// </summary>
    public static DateTime EffectiveEndsAt(DateTime startsAt, DateTime? endsAt) =>
        endsAt ?? startsAt + DefaultDuration;

    /// <summary>Oylama etkinlik başladığı anda açılır.</summary>
    public static DateTime RatingOpensAt(DateTime startsAt) => startsAt;

    /// <summary>Oylamanın kapandığı an: etkinliğin bittiği sayılan andan 14 gün sonra.</summary>
    public static DateTime RatingClosesAt(DateTime startsAt, DateTime? endsAt) =>
        EffectiveEndsAt(startsAt, endsAt) + RatingWindowAfterEnd;

    /// <summary>
    /// Verilen anda oylama açık mı? Etkinlik başladığı andan, bitişten 14 gün sonrasına kadar true döner.
    /// </summary>
    public static bool IsRatingWindowOpen(DateTime startsAt, DateTime? endsAt, DateTime nowUtc) =>
        nowUtc >= RatingOpensAt(startsAt) && nowUtc <= RatingClosesAt(startsAt, endsAt);
}
