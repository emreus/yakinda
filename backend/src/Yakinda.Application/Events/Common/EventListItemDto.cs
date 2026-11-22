using Yakinda.Domain.Events;

namespace Yakinda.Application.Events.Common;

/// <summary>
/// Liste/harita görünümü için hafif etkinlik modeli. Dapper doğrudan bu sınıfa okur
/// (snake_case kolon adları otomatik eşleşir).
/// </summary>
public sealed class EventListItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public short CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryEmoji { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public string? PlaceName { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    /// <summary>Sadece "yakınımdakiler" sorgusunda dolar.</summary>
    public double? DistanceMeters { get; set; }

    public int? Capacity { get; set; }
    public int ParticipantCount { get; set; }
    public JoinPolicy JoinPolicy { get; set; }
    public EventStatus Status { get; set; }

    public Guid OrganizerId { get; set; }
    public string OrganizerName { get; set; } = string.Empty;
    public double? OrganizerRating { get; set; }
    public int OrganizerRatingCount { get; set; }
    public string? OrganizerAvatarUrl { get; set; }

    /// <summary>Kapak görseli; API köküne göre: "/media/events/....jpg".</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Kalan yer. null = sınırsız. "Okeye 4. aranıyor" => 1</summary>
    public int? SpotsLeft => Capacity is null ? null : Math.Max(0, Capacity.Value - ParticipantCount);
}
