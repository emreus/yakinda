using Yakinda.Application.Exceptions;

namespace Yakinda.Application.Events;

internal static class EventErrors
{
    /// <summary>
    /// "Etkinlik bulunamadı" hatası (event.not_found, 404). Engel nedeniyle gizlenen etkinlikte de aynı
    /// hata döner; etkinliğin var olduğu sızdırılmaz.
    /// </summary>
    public static NotFoundException NotFound() =>
        new("event.not_found", "Etkinlik bulunamadı.");

    /// <summary>
    /// Yetkisiz işlem hatası (event.not_organizer, 403): işlemi yalnızca etkinliğin organizatörü yapabilir.
    /// </summary>
    public static ForbiddenException NotOrganizer() =>
        new("event.not_organizer", "Bu işlemi sadece etkinliğin organizatörü yapabilir.");

    /// <summary>"Katılım kaydı bulunamadı" hatası (participant.not_found, 404).</summary>
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
