namespace Yakinda.Application.Events.Common;

/// <summary>Okuma tarafında tekrar kullanılan SQL parçaları.</summary>
internal static class EventSql
{
    /// <summary>Puan ortalaması: hiç oy yoksa NULL döner.</summary>
    public static string Average(string sumColumn, string countColumn) =>
        $"round({sumColumn}::numeric / NULLIF({countColumn}, 0), 2)::float8";

    /// <summary>Bitiş saati girilmemişse varsayılan süre (3 saat) eklenir. EventRules.DefaultDuration ile aynı olmalı.</summary>
    public const string EffectiveEndsAt = "COALESCE(e.ends_at, e.starts_at + interval '3 hours')";

    /// <summary>
    /// EventListItemDto kolonları. Beklenen alias'lar: e = events, c = categories, u = organizatör (users).
    /// </summary>
    public static readonly string ListItemColumns = $"""
        e.id,
        e.title,
        e.category_id,
        c.name  AS category_name,
        c.emoji AS category_emoji,
        e.starts_at,
        e.ends_at,
        e.place_name,
        ST_Y(e.location::geometry) AS latitude,
        ST_X(e.location::geometry) AS longitude,
        e.capacity,
        e.participant_count,
        e.join_policy,
        e.status,
        e.organizer_id,
        u.display_name AS organizer_name,
        {Average("u.rating_sum", "u.rating_count")} AS organizer_rating,
        u.rating_count AS organizer_rating_count,
        {Media.MediaUrl.Sql("e.image_key")} AS image_url,
        {Media.MediaUrl.Sql("u.avatar_key")} AS organizer_avatar_url
        """;
}
