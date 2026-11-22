using Yakinda.Domain.Common;

namespace Yakinda.Domain.Events;

public sealed class EventRating
{
    // Dapper için
    private EventRating() { }

    public Guid EventId { get; private set; }
    public Guid RaterId { get; private set; }
    public short Score { get; private set; }
    public string? Comment { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Bir kullanıcının bir etkinliğe verdiği ilk puanı üretir. Puanın 1 ile 5 arasında olduğunu
    /// denetler, yorumu kırpar (boşsa null olur).
    /// </summary>
    public static EventRating Create(Guid eventId, Guid raterId, short score, string? comment, DateTime nowUtc)
    {
        EnsureValidScore(score);
        return new EventRating
        {
            EventId = eventId,
            RaterId = raterId,
            Score = score,
            Comment = Normalize(comment),
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc
        };
    }

    /// <summary>
    /// Daha önce verilmiş puanı ve yorumu değiştirir. Yeni puanı denetler, yazar ve önceki puanı
    /// döner; çağıran bu farkı etkinliğin ve organizatörün toplamlarına uygular.
    /// </summary>
    public short Update(short score, string? comment, DateTime nowUtc)
    {
        EnsureValidScore(score);
        var previous = Score;
        Score = score;
        Comment = Normalize(comment);
        UpdatedAt = nowUtc;
        return previous;
    }

    /// <summary>Puan 1 ile 5 arasında değilse rating.invalid_score hatası fırlatır.</summary>
    private static void EnsureValidScore(short score)
    {
        if (score < EventRules.MinScore || score > EventRules.MaxScore)
            throw new DomainException("rating.invalid_score", "Puan 1 ile 5 arasında olmalı.");
    }

    /// <summary>Yorumu kırpar; boş ya da yalnızca boşluksa null döner.</summary>
    private static string? Normalize(string? comment) =>
        string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
}
