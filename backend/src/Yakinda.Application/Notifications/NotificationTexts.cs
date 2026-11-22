using Yakinda.Domain.Notifications;

namespace Yakinda.Application.Notifications;

/// <summary>Bir bildirimin başlığı ve metni.</summary>
public sealed record NotificationContent(NotificationType Type, string Title, string Body);

/// <summary>
/// Bildirim metinleri tek yerde. Hem komutlar (katılım, onay, iptal) hem de zamanlanmış
/// bildirimleri üreten arka plan servisi bunları kullanır.
/// </summary>
public static class NotificationTexts
{
    /// <summary>Zamanlanmış hatırlatmanın etkinlik başlangıcından ne kadar önce gideceği.</summary>
    public static readonly TimeSpan ReminderLeadTime = TimeSpan.FromHours(1);

    /// <summary>Organizatöre giden "yeni katılım isteği" bildirimi (onaylı etkinliğe istek geldiğinde).</summary>
    public static NotificationContent JoinRequested(string actorName, string eventTitle) =>
        new(NotificationType.JoinRequested,
            "Yeni katılım isteği",
            $"{actorName}, \"{eventTitle}\" etkinliğine katılmak istiyor.");

    /// <summary>
    /// Organizatöre giden "yeni katılımcı" bildirimi (açık etkinliğe biri doğrudan katıldığında).
    /// </summary>
    public static NotificationContent ParticipantJoined(string actorName, string eventTitle) =>
        new(NotificationType.ParticipantJoined,
            "Yeni katılımcı",
            $"{actorName}, \"{eventTitle}\" etkinliğine katıldı.");

    /// <summary>Katılımcıya giden "isteğin onaylandı" bildirimi.</summary>
    public static NotificationContent RequestApproved(string eventTitle) =>
        new(NotificationType.RequestApproved,
            "Katılım isteğin onaylandı",
            $"\"{eventTitle}\" etkinliğine katılıyorsun.");

    /// <summary>
    /// Katılımcıya giden ret bildirimi. wasApproved true ise kişi onaylıyken çıkarılmıştır ve metin
    /// "etkinlikten çıkarıldın" olur; false ise "katılım isteğin kabul edilmedi" olur.
    /// </summary>
    public static NotificationContent RequestRejected(string eventTitle, bool wasApproved) =>
        wasApproved
            ? new(NotificationType.RequestRejected,
                "Etkinlikten çıkarıldın",
                $"Organizatör seni \"{eventTitle}\" etkinliğinin katılımcılarından çıkardı.")
            : new(NotificationType.RequestRejected,
                "Katılım isteğin kabul edilmedi",
                $"\"{eventTitle}\" etkinliğine katılım isteğin organizatör tarafından kabul edilmedi.");

    /// <summary>Katılımcılara giden "etkinlik iptal edildi" bildirimi (organizatör iptal ettiğinde).</summary>
    public static NotificationContent EventCancelled(string eventTitle) =>
        new(NotificationType.EventCancelled,
            "Etkinlik iptal edildi",
            $"\"{eventTitle}\" etkinliği organizatör tarafından iptal edildi.");

    /// <summary>Yönetici kurallara aykırı bulduğu etkinliği kaldırdı.</summary>
    public static NotificationContent EventRemoved(string eventTitle) =>
        new(NotificationType.EventCancelled,
            "Etkinlik kaldırıldı",
            $"\"{eventTitle}\" etkinliği topluluk kurallarına aykırı bulunduğu için kaldırıldı.");

    /// <summary>Etkinlik başlamadan 1 saat önce giden hatırlatma bildirimi.</summary>
    public static NotificationContent EventReminder(string eventTitle) =>
        new(NotificationType.EventReminder,
            "Etkinlik yaklaşıyor",
            $"\"{eventTitle}\" 1 saat içinde başlıyor.");

    /// <summary>
    /// Katılımcılara giden "etkinlik güncellendi" bildirimi. Metin neyin değiştiğine göre kurulur:
    /// "saati", "yeri" ya da "saati ve yeri".
    /// </summary>
    public static NotificationContent EventUpdated(string eventTitle, bool timeChanged, bool placeChanged)
    {
        var what = (timeChanged, placeChanged) switch
        {
            (true, true) => "saati ve yeri",
            (true, false) => "saati",
            _ => "yeri"
        };
        return new(NotificationType.EventUpdated,
            "Etkinlik güncellendi",
            $"\"{eventTitle}\" etkinliğinin {what} değişti. Ayrıntılara göz at.");
    }

    /// <summary>
    /// Bekleme listesinden alınan kişiye giden bildirim. approved true ise (açık etkinlik) "artık
    /// katılımcısın", false ise (onaylı etkinlik) "isteğin organizatöre iletildi" metni kullanılır.
    /// </summary>
    public static NotificationContent WaitlistPromoted(string eventTitle, bool approved) =>
        approved
            ? new(NotificationType.WaitlistPromoted,
                "Yer açıldı!",
                $"\"{eventTitle}\" etkinliğinde yer açıldı, artık katılımcısın.")
            : new(NotificationType.WaitlistPromoted,
                "Sıran geldi",
                $"\"{eventTitle}\" etkinliğinde yer açıldı; katılım isteğin organizatöre iletildi.");

    /// <summary>
    /// Sohbet mesajı bildirimi. Başlık "yazan · etkinlik" biçimindedir; gövde mesajın önizlemesidir
    /// (120 karakterden uzunsa 117 karakter ve üç nokta).
    /// </summary>
    public static NotificationContent ChatMessage(string authorName, string eventTitle, string body)
    {
        var preview = body.Length > 120 ? body[..117] + "…" : body;
        return new(NotificationType.ChatMessage, $"{authorName} · {eventTitle}", preview);
    }

    /// <summary>Etkinlik bittikten sonra katılımcılara giden "etkinlik nasıldı, puanla" bildirimi.</summary>
    public static NotificationContent RatingPrompt(string eventTitle) =>
        new(NotificationType.RatingPrompt,
            "Etkinlik nasıldı?",
            $"\"{eventTitle}\" etkinliğini puanla; puanın organizatörün ortalamasına yansır.");
}
