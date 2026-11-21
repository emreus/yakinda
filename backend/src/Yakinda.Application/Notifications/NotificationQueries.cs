using Dapper;
using FluentValidation;
using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Common;
using Yakinda.Domain.Notifications;

namespace Yakinda.Application.Notifications;

public sealed class NotificationDto
{
    public Guid Id { get; set; }
    public NotificationType Type { get; set; }

    /// <summary>Bildirime dokununca açılacak etkinlik (varsa).</summary>
    public Guid? EventId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}

public sealed record UnreadCountDto(int Count);

// --------------------------------------------------------------------- Liste

/// <summary>
/// Giriş yapmış kullanıcının bildirimleri, en yeni önce. Cache'lenmez: liste kişiye özel ve
/// sık değişiyor; sorgu (user_id, created_at) indeksinden okunduğu için zaten ucuz.
/// </summary>
public sealed record GetNotificationsQuery(int Page = 1, int PageSize = 20) : IQuery<PagedResult<NotificationDto>>;

internal sealed class GetNotificationsQueryValidator : AbstractValidator<GetNotificationsQuery>
{
    public GetNotificationsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
    }
}

internal sealed class GetNotificationsQueryHandler(IDbConnectionFactory db, ICurrentUser currentUser)
    : IRequestHandler<GetNotificationsQuery, PagedResult<NotificationDto>>
{
    private const string Sql = """
        SELECT id,
               type,
               event_id,
               title,
               body,
               created_at,
               (read_at IS NOT NULL) AS is_read
        FROM notifications
        WHERE user_id = @UserId
        ORDER BY created_at DESC, id DESC
        LIMIT @Limit OFFSET @Offset
        """;

    public async Task<PagedResult<NotificationDto>> Handle(GetNotificationsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        await using var connection = await db.OpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<NotificationDto>(new CommandDefinition(
            Sql,
            new
            {
                UserId = userId,
                Limit = request.PageSize + 1, // bir fazlasını çekip "HasMore" hesaplanır
                Offset = (request.Page - 1) * request.PageSize
            },
            cancellationToken: cancellationToken))).AsList();

        var hasMore = rows.Count > request.PageSize;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        return new PagedResult<NotificationDto>(rows, request.Page, request.PageSize, hasMore);
    }
}

// --------------------------------------------------------------------- Okunmamış sayısı

/// <summary>Sekmedeki rozet için. Mobil uygulama bunu periyodik olarak sorar; kısmi indeks sayesinde ucuz.</summary>
public sealed record GetUnreadNotificationCountQuery : IQuery<UnreadCountDto>;

internal sealed class GetUnreadNotificationCountQueryHandler(IDbConnectionFactory db, ICurrentUser currentUser)
    : IRequestHandler<GetUnreadNotificationCountQuery, UnreadCountDto>
{
    public async Task<UnreadCountDto> Handle(GetUnreadNotificationCountQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        await using var connection = await db.OpenConnectionAsync(cancellationToken);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM notifications WHERE user_id = @UserId AND read_at IS NULL",
            new { UserId = userId },
            cancellationToken: cancellationToken));

        return new UnreadCountDto(count);
    }
}
