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

    /// <returns>Önceki puan (toplamlara farkı uygulamak için).</returns>
    public short Update(short score, string? comment, DateTime nowUtc)
    {
        EnsureValidScore(score);
        var previous = Score;
        Score = score;
        Comment = Normalize(comment);
        UpdatedAt = nowUtc;
        return previous;
    }

    private static void EnsureValidScore(short score)
    {
        if (score < EventRules.MinScore || score > EventRules.MaxScore)
            throw new DomainException("rating.invalid_score", "Puan 1 ile 5 arasında olmalı.");
    }

    private static string? Normalize(string? comment) =>
        string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
}
