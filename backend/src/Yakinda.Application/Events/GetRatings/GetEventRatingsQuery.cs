using Dapper;
using FluentValidation;
using MediatR;
using Yakinda.Application.Abstractions.Caching;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Common;

namespace Yakinda.Application.Events.GetRatings;

/// <summary>Yorum sayfaları 10 dakika cache'lenir; yeni oy gelince "event:{id}" etiketiyle temizlenir.</summary>
public sealed record GetEventRatingsQuery(Guid EventId, int Page = 1, int PageSize = 20)
    : ICachedQuery<PagedResult<RatingDto>>
{
    public string CacheKey => $"event:{EventId}:ratings:{Page}:{PageSize}";
    public TimeSpan CacheDuration => TimeSpan.FromMinutes(10);
    public IReadOnlyCollection<string> CacheTags => new[] { CacheTag.Event(EventId) };
}

public sealed class RatingDto
{
    public Guid RaterId { get; set; }
    public string RaterName { get; set; } = string.Empty;
    public string? RaterAvatarUrl { get; set; }
    public short Score { get; set; }
    public string? Comment { get; set; }
    public DateTime UpdatedAt { get; set; }
}

internal sealed class GetEventRatingsQueryValidator : AbstractValidator<GetEventRatingsQuery>
{
    public GetEventRatingsQueryValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, Paging.MaxPage);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
    }
}

internal sealed class GetEventRatingsQueryHandler(IDbConnectionFactory db)
    : IRequestHandler<GetEventRatingsQuery, PagedResult<RatingDto>>
{
    private static readonly string Sql = $"""
        SELECT r.rater_id,
               u.display_name AS rater_name,
               {Media.MediaUrl.Sql("u.avatar_key")} AS rater_avatar_url,
               r.score,
               r.comment,
               r.updated_at
        FROM event_ratings r
        JOIN users u ON u.id = r.rater_id
        WHERE r.event_id = @EventId
        ORDER BY (r.comment IS NULL), r.updated_at DESC
        LIMIT @Limit OFFSET @Offset
        """;

    public async Task<PagedResult<RatingDto>> Handle(GetEventRatingsQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenConnectionAsync(cancellationToken);

        var rows = (await connection.QueryAsync<RatingDto>(new CommandDefinition(
            Sql,
            new
            {
                request.EventId,
                Limit = request.PageSize + 1,
                Offset = (request.Page - 1) * request.PageSize
            },
            cancellationToken: cancellationToken))).AsList();

        var hasMore = rows.Count > request.PageSize;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        return new PagedResult<RatingDto>(rows, request.Page, request.PageSize, hasMore);
    }
}
