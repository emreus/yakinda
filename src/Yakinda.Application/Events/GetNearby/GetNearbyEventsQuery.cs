using Dapper;
using FluentValidation;
using MediatR;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Common;
using Yakinda.Application.Events.Common;

namespace Yakinda.Application.Events.GetNearby;

public enum NearbySort
{
    Distance,
    StartTime
}

/// <summary>
/// Kullanıcının konumuna göre yarıçap içindeki aktif (bitmemiş, iptal edilmemiş) etkinlikler.
/// </summary>
public sealed record GetNearbyEventsQuery(
    double Latitude,
    double Longitude,
    double RadiusKm = 10,
    short? CategoryId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    bool OnlyAvailable = false,
    NearbySort Sort = NearbySort.Distance,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<EventListItemDto>>;

internal sealed class GetNearbyEventsQueryValidator : AbstractValidator<GetNearbyEventsQuery>
{
    public GetNearbyEventsQueryValidator()
    {
        RuleFor(x => x.Latitude).InclusiveBetween(-90d, 90d);
        RuleFor(x => x.Longitude).InclusiveBetween(-180d, 180d);
        RuleFor(x => x.RadiusKm).InclusiveBetween(0.1d, 100d).WithName("Yarıçap");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
        RuleFor(x => x.To)
            .Must((query, to) => to is null || query.From is null || to > query.From)
            .WithMessage("Bitiş tarihi başlangıç tarihinden sonra olmalı.");
    }
}

internal sealed class GetNearbyEventsQueryHandler(IDbConnectionFactory db, TimeProvider timeProvider)
    : IRequestHandler<GetNearbyEventsQuery, PagedResult<EventListItemDto>>
{
    public async Task<PagedResult<EventListItemDto>> Handle(GetNearbyEventsQuery query, CancellationToken cancellationToken)
    {
        // ORDER BY kullanıcı girdisinden değil enum'dan üretilir (SQL injection riski yok).
        var orderBy = query.Sort == NearbySort.StartTime
            ? "e.starts_at ASC, distance_meters ASC"
            : "distance_meters ASC, e.starts_at ASC";

        // ST_DWithin + GiST index => yarıçap filtresi index üzerinden çalışır.
        var sql = $"""
            WITH origin AS (
                SELECT ST_SetSRID(ST_MakePoint(@Longitude, @Latitude), 4326)::geography AS point
            )
            SELECT {EventSql.ListItemColumns},
                   ST_Distance(e.location, o.point) AS distance_meters
            FROM events e
            JOIN categories c ON c.id = e.category_id
            JOIN users u      ON u.id = e.organizer_id
            CROSS JOIN origin o
            WHERE e.status = 0
              AND ST_DWithin(e.location, o.point, @RadiusMeters)
              AND {EventSql.EffectiveEndsAt} >= @Now
              AND (@CategoryId::smallint IS NULL OR e.category_id = @CategoryId::smallint)
              AND (@From::timestamptz IS NULL OR e.starts_at >= @From::timestamptz)
              AND (@To::timestamptz IS NULL OR e.starts_at <= @To::timestamptz)
              AND (NOT @OnlyAvailable OR e.capacity IS NULL OR e.participant_count < e.capacity)
            ORDER BY {orderBy}, e.id
            LIMIT @Limit OFFSET @Offset
            """;

        var parameters = new
        {
            query.Latitude,
            query.Longitude,
            RadiusMeters = query.RadiusKm * 1000,
            Now = timeProvider.GetUtcNow().UtcDateTime,
            query.CategoryId,
            From = query.From?.UtcDateTime,
            To = query.To?.UtcDateTime,
            query.OnlyAvailable,
            Limit = query.PageSize + 1, // bir fazlasını çekip "HasMore" hesaplanır
            Offset = (query.Page - 1) * query.PageSize
        };

        await using var connection = await db.OpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<EventListItemDto>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))).AsList();

        var hasMore = rows.Count > query.PageSize;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        return new PagedResult<EventListItemDto>(rows, query.Page, query.PageSize, hasMore);
    }
}
