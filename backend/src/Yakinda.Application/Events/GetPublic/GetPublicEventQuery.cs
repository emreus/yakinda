using Dapper;
using MediatR;
using Yakinda.Application.Abstractions.Messaging;
using Yakinda.Application.Abstractions.Persistence;

namespace Yakinda.Application.Events.GetPublic;

/// <summary>
/// Paylaşım bağlantısının açılış sayfası için herkese açık özet (giriş gerektirmez).
/// Adres ve koordinat verilmez; sadece mekan adı ve saat. İptal/kaldırılmış etkinlikte null.
/// </summary>
public sealed class PublicEventDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryEmoji { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public string? PlaceName { get; set; }
    public string OrganizerName { get; set; } = string.Empty;
    public int ParticipantCount { get; set; }
    public int? Capacity { get; set; }
}

public sealed record GetPublicEventQuery(Guid EventId) : IQuery<PublicEventDto?>;

internal sealed class GetPublicEventQueryHandler(IDbConnectionFactory db) : IRequestHandler<GetPublicEventQuery, PublicEventDto?>
{
    public async Task<PublicEventDto?> Handle(GetPublicEventQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<PublicEventDto>(new CommandDefinition("""
            SELECT e.id, e.title, c.name AS category_name, c.emoji AS category_emoji, e.starts_at, e.ends_at,
                   e.place_name, u.display_name AS organizer_name, e.participant_count, e.capacity
            FROM events e
            JOIN categories c ON c.id = e.category_id
            JOIN users u      ON u.id = e.organizer_id
            WHERE e.id = @EventId AND e.status = 0
            """, new { request.EventId }, cancellationToken: cancellationToken));
    }
}
