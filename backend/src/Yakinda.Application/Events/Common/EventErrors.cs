using Yakinda.Application.Exceptions;

namespace Yakinda.Application.Events;

internal static class EventErrors
{
    public static NotFoundException NotFound() =>
        new("event.not_found", "Etkinlik bulunamadı.");

    public static ForbiddenException NotOrganizer() =>
        new("event.not_organizer", "Bu işlemi sadece etkinliğin organizatörü yapabilir.");

    public static NotFoundException ParticipantNotFound() =>
        new("participant.not_found", "Katılım kaydı bulunamadı.");
}
