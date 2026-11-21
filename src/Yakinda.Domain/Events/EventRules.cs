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
    public const int CommentMaxLength = 500;

    public const short MinScore = 1;
    public const short MaxScore = 5;

    /// <summary>Bitiş saati girilmemiş etkinlikler bu süre sonunda bitmiş sayılır.</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromHours(3);

    /// <summary>Bir etkinlik en fazla bu kadar sürebilir.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(7);

    /// <summary>Etkinlik bittikten sonra oylamanın açık kalacağı süre.</summary>
    public static readonly TimeSpan RatingWindowAfterEnd = TimeSpan.FromDays(14);

    public static DateTime EffectiveEndsAt(DateTime startsAt, DateTime? endsAt) =>
        endsAt ?? startsAt + DefaultDuration;

    /// <summary>Oylama etkinlik başladığı anda açılır.</summary>
    public static DateTime RatingOpensAt(DateTime startsAt) => startsAt;

    public static DateTime RatingClosesAt(DateTime startsAt, DateTime? endsAt) =>
        EffectiveEndsAt(startsAt, endsAt) + RatingWindowAfterEnd;

    public static bool IsRatingWindowOpen(DateTime startsAt, DateTime? endsAt, DateTime nowUtc) =>
        nowUtc >= RatingOpensAt(startsAt) && nowUtc <= RatingClosesAt(startsAt, endsAt);
}
