using Dapper;
using MediatR;
using Yakinda.Application.Abstractions.Caching;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Events.Common;
using Yakinda.Application.Exceptions;

namespace Yakinda.Application.Users.GetProfile;

/// <summary>
/// Profil 10 dakika cache'lenir. Kullanıcının organize ettiği bir etkinliğe oy verildiğinde,
/// etkinlik oluşturduğunda/iptal ettiğinde ya da bir etkinliğe katıldığında/ayrıldığında
/// "user:{id}" etiketi temizlenir.
/// </summary>
public sealed record GetUserProfileQuery(Guid UserId) : ICachedQuery<UserProfileDto>
{
    public string CacheKey => $"user:{UserId}:profile";
    public TimeSpan CacheDuration => TimeSpan.FromMinutes(10);
    public IReadOnlyCollection<string> CacheTags => new[] { CacheTag.User(UserId) };
}

public sealed class UserProfileDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Organize ettiği etkinliklere verilen tüm oyların ortalaması.</summary>
    public double? AverageRating { get; set; }
    public int RatingCount { get; set; }

    public int OrganizedEventCount { get; set; }
    public int JoinedEventCount { get; set; }

    /// <summary>[1 yıldız adedi, 2 yıldız adedi, ..., 5 yıldız adedi]</summary>
    public int[] RatingDistribution { get; set; } = new int[5];
}

internal sealed class GetUserProfileQueryHandler(IDbConnectionFactory db)
    : IRequestHandler<GetUserProfileQuery, UserProfileDto>
{
    private static readonly string ProfileSql = $"""
        SELECT u.id,
               u.display_name,
               u.bio,
               u.created_at,
               {EventSql.Average("u.rating_sum", "u.rating_count")} AS average_rating,
               u.rating_count,
               (SELECT count(*) FROM events e
                 WHERE e.organizer_id = u.id AND e.status = 0)::int AS organized_event_count,
               (SELECT count(*) FROM event_participants p
                 WHERE p.user_id = u.id AND p.status = 1)::int AS joined_event_count
        FROM users u
        WHERE u.id = @UserId
        """;

    private const string DistributionSql = """
        SELECT r.score, count(*)::int AS count
        FROM event_ratings r
        JOIN events e ON e.id = r.event_id
        WHERE e.organizer_id = @UserId
        GROUP BY r.score
        """;

    public async Task<UserProfileDto> Handle(GetUserProfileQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenConnectionAsync(cancellationToken);

        var profile = await connection.QuerySingleOrDefaultAsync<UserProfileDto>(
            new CommandDefinition(ProfileSql, new { request.UserId }, cancellationToken: cancellationToken))
            ?? throw new NotFoundException("user.not_found", "Kullanıcı bulunamadı.");

        var distribution = await connection.QueryAsync<(short Score, int Count)>(
            new CommandDefinition(DistributionSql, new { request.UserId }, cancellationToken: cancellationToken));

        foreach (var (score, count) in distribution)
        {
            if (score is >= 1 and <= 5)
                profile.RatingDistribution[score - 1] = count;
        }

        return profile;
    }
}
