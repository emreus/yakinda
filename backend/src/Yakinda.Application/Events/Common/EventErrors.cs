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

internal static class EventTexts
{
    /// <summary>
    /// Yöneticinin kaldırdığı etkinlik, organizatörü dışındakilere bu başlıkla ve açıklaması, mekanı, adresi
    /// ve görseli olmadan gösterilir: kaldırılan içerik bağlantıyı bilenlere ya da katılmış olanlara sunulmaz.
    /// </summary>
    public const string RemovedTitle = "Kaldırılan etkinlik";
}
