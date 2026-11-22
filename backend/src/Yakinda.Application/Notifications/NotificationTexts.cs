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

    public static NotificationContent JoinRequested(string actorName, string eventTitle) =>
        new(NotificationType.JoinRequested,
            "Yeni katılım isteği",
            $"{actorName}, \"{eventTitle}\" etkinliğine katılmak istiyor.");

    public static NotificationContent ParticipantJoined(string actorName, string eventTitle) =>
        new(NotificationType.ParticipantJoined,
            "Yeni katılımcı",
            $"{actorName}, \"{eventTitle}\" etkinliğine katıldı.");

    public static NotificationContent RequestApproved(string eventTitle) =>
        new(NotificationType.RequestApproved,
            "Katılım isteğin onaylandı",
            $"\"{eventTitle}\" etkinliğine katılıyorsun.");

    /// <param name="wasApproved">true ise kişi onaylıyken etkinlikten çıkarıldı.</param>
    public static NotificationContent RequestRejected(string eventTitle, bool wasApproved) =>
        wasApproved
            ? new(NotificationType.RequestRejected,
                "Etkinlikten çıkarıldın",
                $"Organizatör seni \"{eventTitle}\" etkinliğinin katılımcılarından çıkardı.")
            : new(NotificationType.RequestRejected,
                "Katılım isteğin kabul edilmedi",
                $"\"{eventTitle}\" etkinliğine katılım isteğin organizatör tarafından kabul edilmedi.");

    public static NotificationContent EventCancelled(string eventTitle) =>
        new(NotificationType.EventCancelled,
            "Etkinlik iptal edildi",
            $"\"{eventTitle}\" etkinliği organizatör tarafından iptal edildi.");

    public static NotificationContent EventReminder(string eventTitle) =>
        new(NotificationType.EventReminder,
            "Etkinlik yaklaşıyor",
            $"\"{eventTitle}\" 1 saat içinde başlıyor.");

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

    /// <param name="approved">true: açık etkinlik, doğrudan katıldı. false: onaylı etkinlik, isteği organizatöre iletildi.</param>
    public static NotificationContent WaitlistPromoted(string eventTitle, bool approved) =>
        approved
            ? new(NotificationType.WaitlistPromoted,
                "Yer açıldı!",
                $"\"{eventTitle}\" etkinliğinde yer açıldı, artık katılımcısın.")
            : new(NotificationType.WaitlistPromoted,
                "Sıran geldi",
                $"\"{eventTitle}\" etkinliğinde yer açıldı; katılım isteğin organizatöre iletildi.");

    public static NotificationContent RatingPrompt(string eventTitle) =>
        new(NotificationType.RatingPrompt,
            "Etkinlik nasıldı?",
            $"\"{eventTitle}\" etkinliğini puanla; puanın organizatörün ortalamasına yansır.");
}
