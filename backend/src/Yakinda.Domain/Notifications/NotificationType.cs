namespace Yakinda.Domain.Notifications;

/// <summary>Bildirim türü. Veritabanında smallint olarak tutulur; sayılar değiştirilmemeli.</summary>
public enum NotificationType : short
{
    /// <summary>Organizatöre: biri onaylı etkinliğe katılım isteği gönderdi.</summary>
    JoinRequested = 1,

    /// <summary>Organizatöre: biri açık etkinliğe katıldı.</summary>
    ParticipantJoined = 2,

    /// <summary>Katılımcıya: isteği onaylandı.</summary>
    RequestApproved = 3,

    /// <summary>Katılımcıya: isteği reddedildi ya da etkinlikten çıkarıldı.</summary>
    RequestRejected = 4,

    /// <summary>Katılımcılara: etkinlik iptal edildi.</summary>
    EventCancelled = 5,

    /// <summary>Katılımcılara ve organizatöre: etkinlik 1 saat içinde başlıyor.</summary>
    EventReminder = 6,

    /// <summary>Katılımcılara: etkinlik bitti, puanla.</summary>
    RatingPrompt = 7,

    /// <summary>Katılımcılara: organizatör etkinliğin saatini ya da yerini değiştirdi.</summary>
    EventUpdated = 8,

    /// <summary>Bekleme listesindekine: yer açıldı (katıldı ya da isteği organizatöre iletildi).</summary>
    WaitlistPromoted = 9
}
