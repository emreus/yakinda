using System.Data.Common;
using Dapper;
using MediatR;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Application.Safety;

namespace Yakinda.Application.Events.Common;

internal sealed class EventAccessRow
{
    public Guid OrganizerId { get; set; }
    public bool IsBlocked { get; set; }
}

/// <summary>
/// Etkinliğe bağlı okumalar (katılımcılar, yorumlar) için ortak kural: organizatörle aramızda engel varsa
/// etkinlik yokmuş gibi davranılır. Detay sorgusu aynı kuralı kendi içinde uygular.
/// </summary>
internal static class EventVisibility
{
    private static readonly string Sql = $"""
        SELECT e.organizer_id, {BlockSql.EitherWay("@UserId", "e.organizer_id")} AS is_blocked
        FROM events e
        WHERE e.id = @EventId
        """;

    /// <returns>Organizatörün kimliği.</returns>
    public static async Task<Guid> RequireVisibleAsync(
        DbConnection connection, Guid eventId, Guid userId, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<EventAccessRow>(
            new CommandDefinition(Sql, new { EventId = eventId, UserId = userId }, cancellationToken: cancellationToken));

        if (row is null || row.IsBlocked)
            throw EventErrors.NotFound();

        return row.OrganizerId;
    }
}

/// <summary>Cache'lenen etkinlik okumalarından (yorumlar) önce çağrılır; etkinlik yoksa ya da engel varsa 404.</summary>
public sealed record EnsureEventVisibleQuery(Guid EventId) : IQuery<Unit>;

internal sealed class EnsureEventVisibleQueryHandler(IDbConnectionFactory db, ICurrentUser currentUser)
    : IRequestHandler<EnsureEventVisibleQuery, Unit>
{
    public async Task<Unit> Handle(EnsureEventVisibleQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenConnectionAsync(cancellationToken);
        await EventVisibility.RequireVisibleAsync(connection, request.EventId, currentUser.RequireUserId(), cancellationToken);
        return Unit.Value;
    }
}
