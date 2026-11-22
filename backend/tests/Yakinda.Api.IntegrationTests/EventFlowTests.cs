using System.Net;
using System.Net.Http.Json;
using Yakinda.Infrastructure.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Yakinda.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class EventFlowTests(ApiFixture api)
{
    // ------------------------------------------------------------------ yakındakiler

    /// <summary>
    /// Yakındakiler listesi yalnızca yarıçapın içindeki etkinlikleri döner: 10 km'lik aramada arama noktasının
    /// hemen yanındaki etkinlik gelir, yaklaşık 55 km uzaktaki gelmez. Diğer testlerle karışmaması için ıssız
    /// bir konum kullanılır.
    /// </summary>
    [Fact]
    public async Task Nearby_returns_events_inside_the_radius_only()
    {
        var organizer = await api.RegisterAsync("Organizatör");
        // Kuzey Kutbu'na yakın, diğer testlerin etkinliklerinden uzak bir nokta
        var near = await organizer.CreateEventAsync("Yakındaki", latitude: 80.0, longitude: 10.0);
        var far = await organizer.CreateEventAsync("Uzaktaki", latitude: 80.5, longitude: 10.0); // ~55 km

        var json = await (await organizer.Client.GetAsync("/api/events/nearby?lat=80.0&lng=10.01&radiusKm=10"))
            .ReadJsonAsync(HttpStatusCode.OK);
        var ids = json["items"]!.AsArray().Select(i => Guid.Parse(i!["id"]!.GetValue<string>())).ToList();

        Assert.Contains(near, ids);
        Assert.DoesNotContain(far, ids);
    }

    /// <summary>Boş başlıklı ve başlangıcı geçmişte olan etkinlik isteği 400 ile reddedilir.</summary>
    [Fact]
    public async Task Event_creation_validates_input()
    {
        var organizer = await api.RegisterAsync();

        var response = await organizer.Client.PostAsJsonAsync("/api/events", new
        {
            categoryId = 1,
            title = "",
            startsAt = DateTimeOffset.UtcNow.AddHours(-1),
            latitude = 41.0,
            longitude = 29.0,
            joinPolicy = "Open"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------------ katılım + bildirim

    /// <summary>
    /// Herkese açık etkinliğe katılan kişi hemen onaylanır, katılımcı sayısı 1 olur ve organizatöre içinde
    /// katılanın adı geçen bir ParticipantJoined bildirimi gider.
    /// </summary>
    [Fact]
    public async Task Joining_an_open_event_approves_and_notifies_the_organizer()
    {
        var organizer = await api.RegisterAsync("Ayşe");
        var guest = await api.RegisterAsync("Burak");
        var eventId = await organizer.CreateEventAsync();

        var join = await (await guest.JoinAsync(eventId)).ReadJsonAsync(HttpStatusCode.OK);
        Assert.Equal("Approved", join["status"]!.GetValue<string>());

        var detail = await (await guest.Client.GetAsync($"/api/events/{eventId}")).ReadJsonAsync(HttpStatusCode.OK);
        Assert.Equal(1, detail["participantCount"]!.GetValue<int>());

        var notifications = await organizer.NotificationsAsync();
        Assert.Contains("ParticipantJoined", notifications.Types());
        Assert.Contains("Burak", notifications[0]!["body"]!.GetValue<string>());
    }

    /// <summary>
    /// Onaylı katılım akışı: istek Pending olur ve organizatöre JoinRequested bildirimi gider; organizatör
    /// onaylayınca katılımcıya RequestApproved gelir ve okunmamış sayısı 1 olur; "tümünü okundu yap"tan
    /// sonra sayı 0'a iner.
    /// </summary>
    [Fact]
    public async Task Approval_flow_notifies_both_sides_and_marks_read()
    {
        var organizer = await api.RegisterAsync("Ayşe");
        var guest = await api.RegisterAsync("Cem");
        var eventId = await organizer.CreateEventAsync(joinPolicy: "ApprovalRequired");

        var join = await (await guest.JoinAsync(eventId)).ReadJsonAsync(HttpStatusCode.OK);
        Assert.Equal("Pending", join["status"]!.GetValue<string>());
        Assert.Equal(new[] { "JoinRequested" }, (await organizer.NotificationsAsync()).Types());

        await (await organizer.Client.PostAsync($"/api/events/{eventId}/participants/{guest.Id}/approve", null))
            .ExpectAsync(HttpStatusCode.NoContent);

        Assert.Equal(new[] { "RequestApproved" }, (await guest.NotificationsAsync()).Types());
        Assert.Equal(1, await guest.UnreadCountAsync());

        await (await guest.Client.PostAsync("/api/notifications/read-all", null)).ExpectAsync(HttpStatusCode.NoContent);
        Assert.Equal(0, await guest.UnreadCountAsync());
    }

    /// <summary>
    /// Kullanıcı başkasına ait bir bildirimi okundu yapamaz; bildirim ona yokmuş gibi 404 notification.not_found döner.
    /// </summary>
    [Fact]
    public async Task Users_cannot_mark_someone_elses_notification_read()
    {
        var organizer = await api.RegisterAsync();
        var guest = await api.RegisterAsync();
        await guest.JoinAsync(await organizer.CreateEventAsync());
        var notificationId = (await organizer.NotificationsAsync())[0]!["id"]!.GetValue<string>();

        var response = await guest.Client.PostAsync($"/api/notifications/{notificationId}/read", null);

        Assert.Equal("notification.not_found", await response.ProblemCodeAsync(HttpStatusCode.NotFound));
    }

    /// <summary>
    /// Dolu ve herkese açık etkinlikte sonradan gelenler bekleme listesine girer ve sıralarını görür.
    /// Katılımcı ayrılınca sıradaki ilk kişi kendiliğinden onaylanır ve WaitlistPromoted bildirimi alır;
    /// arkasındaki kişinin sırası 2'den 1'e iner.
    /// </summary>
    [Fact]
    public async Task Full_open_event_waitlists_and_promotes_when_a_seat_frees()
    {
        var organizer = await api.RegisterAsync();
        var first = await api.RegisterAsync();
        var second = await api.RegisterAsync("Bekleyen 1");
        var third = await api.RegisterAsync("Bekleyen 2");
        var eventId = await organizer.CreateEventAsync(capacity: 1);
        await (await first.JoinAsync(eventId)).ExpectAsync(HttpStatusCode.OK);

        Assert.Equal("Waitlisted", (await (await second.JoinAsync(eventId)).ReadJsonAsync(HttpStatusCode.OK))["status"]!.GetValue<string>());
        await third.JoinAsync(eventId);

        var detail = await Detail(third, eventId);
        Assert.Equal(2, detail["waitlistCount"]!.GetValue<int>());
        Assert.Equal(2, detail["myWaitlistPosition"]!.GetValue<int>());

        // İlk katılımcı ayrılınca sıradaki (second) otomatik katılır ve haber alır
        await (await first.Client.PostAsync($"/api/events/{eventId}/leave", null)).ExpectAsync(HttpStatusCode.NoContent);

        var promoted = await Detail(second, eventId);
        Assert.Equal("Approved", promoted["myParticipationStatus"]!.GetValue<string>());
        Assert.Equal(1, promoted["participantCount"]!.GetValue<int>());
        Assert.Contains("WaitlistPromoted", (await second.NotificationsAsync()).Types());
        Assert.Equal(1, (await Detail(third, eventId))["myWaitlistPosition"]!.GetValue<int>());
    }

    /// <summary>
    /// Onay gerektiren dolu etkinlikte yer açılınca (organizatör onaylı katılımcıyı çıkarınca) bekleme
    /// listesindeki kişi doğrudan katılmaz, isteği Pending olur; kendisine WaitlistPromoted bildirimi,
    /// organizatöre de adının geçtiği bir bildirim gider.
    /// </summary>
    [Fact]
    public async Task Approval_event_promotes_waitlist_to_a_request_after_rejection()
    {
        var organizer = await api.RegisterAsync();
        var approved = await api.RegisterAsync();
        var waiting = await api.RegisterAsync("Sırada");
        var eventId = await organizer.CreateEventAsync(capacity: 1, joinPolicy: "ApprovalRequired");
        await approved.JoinAsync(eventId);
        await organizer.Client.PostAsync($"/api/events/{eventId}/participants/{approved.Id}/approve", null);
        Assert.Equal("Waitlisted", (await (await waiting.JoinAsync(eventId)).ReadJsonAsync(HttpStatusCode.OK))["status"]!.GetValue<string>());

        // Organizatör onaylı katılımcıyı çıkarınca bekleyen kişi "istek" olur, organizatöre haber gider
        await (await organizer.Client.PostAsync($"/api/events/{eventId}/participants/{approved.Id}/reject", null))
            .ExpectAsync(HttpStatusCode.NoContent);

        Assert.Equal("Pending", (await Detail(waiting, eventId))["myParticipationStatus"]!.GetValue<string>());
        Assert.Contains("WaitlistPromoted", (await waiting.NotificationsAsync()).Types());
        Assert.Contains("Sırada", (await organizer.NotificationsAsync())[0]!["body"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------ düzenleme

    /// <summary>
    /// Etkinliğin saati değişince katılımcıya EventUpdated bildirimi gider ve daha önce gönderilmiş
    /// hatırlatma işareti (reminder_sent_at) temizlenir; böylece yeni saate göre yeniden hatırlatılır.
    /// </summary>
    [Fact]
    public async Task Editing_time_notifies_participants_and_resets_the_reminder()
    {
        var organizer = await api.RegisterAsync();
        var participant = await api.RegisterAsync();
        var eventId = await organizer.CreateEventAsync(title: "Eski başlık");
        await participant.JoinAsync(eventId);
        await api.ExecuteSqlAsync("UPDATE events SET reminder_sent_at = now() WHERE id = @id", new { id = eventId });

        var newStart = DateTimeOffset.UtcNow.AddDays(3);
        await (await organizer.Client.PutAsJsonAsync($"/api/events/{eventId}", EditBody("Yeni başlık", newStart, capacity: null)))
            .ExpectAsync(HttpStatusCode.NoContent);

        var detail = await Detail(participant, eventId);
        Assert.Equal("Yeni başlık", detail["title"]!.GetValue<string>());
        Assert.Contains("EventUpdated", (await participant.NotificationsAsync()).Types());
        Assert.False(await api.ScalarAsync<bool>("SELECT reminder_sent_at IS NOT NULL FROM events WHERE id = @id", new { id = eventId }));
    }

    /// <summary>
    /// Etkinliği yalnızca organizatörü düzenleyebilir (katılımcı 403 alır); organizatör de kontenjanı mevcut
    /// katılımcı sayısının altına indiremez (422 event.capacity_below_participants).
    /// </summary>
    [Fact]
    public async Task Only_the_organizer_can_edit_and_capacity_cannot_drop_below_participants()
    {
        var organizer = await api.RegisterAsync();
        var eventId = await organizer.CreateEventAsync(capacity: 3);
        var one = await api.RegisterAsync();
        var two = await api.RegisterAsync();
        await one.JoinAsync(eventId);
        await two.JoinAsync(eventId);
        var start = DateTimeOffset.UtcNow.AddDays(1);

        var stranger = await one.Client.PutAsJsonAsync($"/api/events/{eventId}", EditBody("Başkası", start, capacity: 3));
        Assert.Equal(HttpStatusCode.Forbidden, stranger.StatusCode);

        var tooSmall = await organizer.Client.PutAsJsonAsync($"/api/events/{eventId}", EditBody("Küçük", start, capacity: 1));
        Assert.Equal("event.capacity_below_participants", await tooSmall.ProblemCodeAsync(HttpStatusCode.UnprocessableEntity));
    }

    /// <summary>Kontenjan artırılınca bekleme listesindeki kişi kendiliğinden onaylanır.</summary>
    [Fact]
    public async Task Raising_capacity_lets_the_waitlist_in()
    {
        var organizer = await api.RegisterAsync();
        var eventId = await organizer.CreateEventAsync(capacity: 1);
        await (await api.RegisterAsync()).JoinAsync(eventId);
        var waiting = await api.RegisterAsync();
        await waiting.JoinAsync(eventId);

        await (await organizer.Client.PutAsJsonAsync($"/api/events/{eventId}",
                EditBody("Test etkinliği", (await Detail(organizer, eventId))["startsAt"]!.GetValue<DateTimeOffset>(), capacity: 2)))
            .ExpectAsync(HttpStatusCode.NoContent);

        Assert.Equal("Approved", (await Detail(waiting, eventId))["myParticipationStatus"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------ tekrar, arama, sıralama

    /// <summary>
    /// Haftalık tekrar: repeatWeeks = 3 ile oluşturulan etkinlik, ilkiyle birlikte toplam 4 etkinlik kaydı
    /// üretir (aynı başlıkla, veritabanından sayılır).
    /// </summary>
    [Fact]
    public async Task Recurring_event_creates_one_event_per_week()
    {
        var organizer = await api.RegisterAsync();
        var title = $"Salı okeyi {Guid.NewGuid():N}";

        var response = await organizer.Client.PostAsJsonAsync("/api/events", new
        {
            categoryId = 1,
            title,
            startsAt = DateTimeOffset.UtcNow.AddDays(1),
            latitude = 41.0082,
            longitude = 28.9784,
            joinPolicy = "Open",
            repeatWeeks = 3
        });
        await response.ExpectAsync(HttpStatusCode.Created);

        Assert.Equal(4, await api.ScalarAsync<long>("SELECT count(*) FROM events WHERE title = @title", new { title }));
    }

    /// <summary>
    /// Arama başlığı büyük/küçük harf ayırmadan eşleştirir ve yalnızca eşleşen etkinliği döner. "%" işareti
    /// joker sayılmaz, düz metin olarak aranır; bu yüzden tek başına "%" araması hiçbir şey bulmaz.
    /// </summary>
    [Fact]
    public async Task Search_matches_title_case_insensitively_and_treats_wildcards_literally()
    {
        var organizer = await api.RegisterAsync();
        var token = Guid.NewGuid().ToString("N")[..8];
        var match = await organizer.CreateEventAsync($"Kutu Oyunu Gecesi {token}", latitude: -60.0, longitude: 20.0);
        await organizer.CreateEventAsync("Sinema", latitude: -60.0, longitude: 20.0);

        var found = await NearbyIds(organizer, -60.0, 20.0, $"&q=kutu oyunu gecesi {token}");
        Assert.Equal(new[] { match }, found);

        Assert.Empty(await NearbyIds(organizer, -60.0, 20.0, "&q=%25"));  // "%" joker değil, düz metin
    }

    /// <summary>
    /// Puana göre sıralamada az oylu ortalamalar genel ortalamaya doğru çekilir: tek oyla 5,0 almış
    /// organizatörün etkinliği, 200 oyla 4,8 almış organizatörün etkinliğinin önüne geçemez. Puanlar ve
    /// genel ortalama test içinde doğrudan veritabanına yazılır.
    /// </summary>
    [Fact]
    public async Task Rating_sort_does_not_let_a_single_vote_win()
    {
        var newcomer = await api.RegisterAsync("Tek oylu");
        var veteran = await api.RegisterAsync("Tecrübeli");
        var newcomerEvent = await newcomer.CreateEventAsync(latitude: 60.0, longitude: -150.0);
        var veteranEvent = await veteran.CreateEventAsync(latitude: 60.0, longitude: -150.0);
        var others = await api.RegisterAsync("Diğer organizatörler");
        await api.ExecuteSqlAsync("UPDATE users SET rating_sum = 5,    rating_count = 1    WHERE id = @id", new { id = newcomer.Id });
        await api.ExecuteSqlAsync("UPDATE users SET rating_sum = 960,  rating_count = 200  WHERE id = @id", new { id = veteran.Id }); // 4,8
        // Genel ortalama ~3,3 (sistemdeki diğer oylar). Tek oylu 5,0 → ~3,6; 200 oylu 4,8 → ~4,8
        await api.ExecuteSqlAsync("UPDATE users SET rating_sum = 3000, rating_count = 1000 WHERE id = @id", new { id = others.Id });

        var ids = await NearbyIds(newcomer, 60.0, -150.0, "&sort=rating");

        Assert.Equal(new[] { veteranEvent, newcomerEvent }, ids);
    }

    /// <summary>Organizatör kendi etkinliğine katılamaz: 422 event.organizer_cannot_join.</summary>
    [Fact]
    public async Task Organizer_cannot_join_own_event()
    {
        var organizer = await api.RegisterAsync();
        var eventId = await organizer.CreateEventAsync();

        var response = await organizer.JoinAsync(eventId);

        Assert.Equal("event.organizer_cannot_join", await response.ProblemCodeAsync(HttpStatusCode.UnprocessableEntity));
    }

    /// <summary>
    /// İptal edilen etkinlikte hem onaylı katılımcıya hem isteği bekleyen kişiye EventCancelled bildirimi
    /// gider; sonradan katılmak isteyen 422 event.cancelled alır.
    /// </summary>
    [Fact]
    public async Task Cancelling_notifies_participants_and_closes_the_event()
    {
        var organizer = await api.RegisterAsync();
        var approved = await api.RegisterAsync();
        var pending = await api.RegisterAsync();
        var eventId = await organizer.CreateEventAsync(joinPolicy: "ApprovalRequired");
        await approved.JoinAsync(eventId);
        await organizer.Client.PostAsync($"/api/events/{eventId}/participants/{approved.Id}/approve", null);
        await pending.JoinAsync(eventId);

        await (await organizer.Client.PostAsync($"/api/events/{eventId}/cancel", null)).ExpectAsync(HttpStatusCode.NoContent);

        Assert.Contains("EventCancelled", (await approved.NotificationsAsync()).Types());
        Assert.Contains("EventCancelled", (await pending.NotificationsAsync()).Types());

        var late = await (await api.RegisterAsync()).JoinAsync(eventId);
        Assert.Equal("event.cancelled", await late.ProblemCodeAsync(HttpStatusCode.UnprocessableEntity));
    }

    // ------------------------------------------------------------------ oylama

    /// <summary>
    /// Verilen puanlar etkinliğin ortalamasını günceller (5 ve 2 → 3,5); puanını değiştiren kişi oy sayısını
    /// artırmaz, yalnızca ortalamayı değiştirir (3 ve 2 → 2,5). Aynı ortalama ve oy sayısı organizatörün
    /// profiline de yansır.
    /// </summary>
    [Fact]
    public async Task Ratings_update_event_and_organizer_averages()
    {
        var organizer = await api.RegisterAsync("Organizatör");
        var first = await api.RegisterAsync();
        var second = await api.RegisterAsync();
        var eventId = await organizer.CreateEventAsync();
        await first.JoinAsync(eventId);
        await second.JoinAsync(eventId);
        await StartEventAsync(eventId);

        await Rate(first, eventId, 5);
        var result = await Rate(second, eventId, 2);
        Assert.Equal(3.5, result["eventAverageRating"]!.GetValue<double>());
        Assert.Equal(2, result["eventRatingCount"]!.GetValue<int>());

        // İlk oyu değiştirmek sayıyı artırmaz, sadece ortalamayı değiştirir: (3 + 2) / 2
        result = await Rate(first, eventId, 3);
        Assert.Equal(2.5, result["eventAverageRating"]!.GetValue<double>());
        Assert.Equal(2, result["eventRatingCount"]!.GetValue<int>());

        var profile = await (await first.Client.GetAsync($"/api/users/{organizer.Id}")).ReadJsonAsync(HttpStatusCode.OK);
        Assert.Equal(2.5, profile["averageRating"]!.GetValue<double>());
        Assert.Equal(2, profile["ratingCount"]!.GetValue<int>());
    }

    /// <summary>
    /// Oylama kuralları API üzerinden: etkinlik başlamadan puan verilemez (rating.not_open_yet); başladıktan
    /// sonra katılımcı olmayan (rating.not_participant) ve organizatörün kendisi (rating.own_event) veremez.
    /// Üçü de 422 döner.
    /// </summary>
    [Fact]
    public async Task Only_participants_can_rate_and_not_before_start()
    {
        var organizer = await api.RegisterAsync();
        var participant = await api.RegisterAsync();
        var outsider = await api.RegisterAsync();
        var eventId = await organizer.CreateEventAsync();
        await participant.JoinAsync(eventId);

        var early = await participant.Client.PutAsJsonAsync($"/api/events/{eventId}/rating", new { score = 4 });
        Assert.Equal("rating.not_open_yet", await early.ProblemCodeAsync(HttpStatusCode.UnprocessableEntity));

        await StartEventAsync(eventId);

        var stranger = await outsider.Client.PutAsJsonAsync($"/api/events/{eventId}/rating", new { score = 4 });
        Assert.Equal("rating.not_participant", await stranger.ProblemCodeAsync(HttpStatusCode.UnprocessableEntity));

        var own = await organizer.Client.PutAsJsonAsync($"/api/events/{eventId}/rating", new { score = 5 });
        Assert.Equal("rating.own_event", await own.ProblemCodeAsync(HttpStatusCode.UnprocessableEntity));
    }

    // ------------------------------------------------------------------ zamanlanmış bildirimler

    /// <summary>
    /// Zamanlayıcı elle çalıştırılır. Başlangıca 30 dakika kala organizatöre ve katılımcıya birer hatırlatma
    /// gönderir, ikinci turda tekrarlamaz. Etkinlik bittikten sonra (başlangıç 5 saat geriye çekilir)
    /// yalnızca katılımcıya puanlama bildirimi gönderir.
    /// </summary>
    [Fact]
    public async Task Scheduler_sends_reminder_once_and_rating_prompt_after_the_event()
    {
        var organizer = await api.RegisterAsync();
        var participant = await api.RegisterAsync();
        var eventId = await organizer.CreateEventAsync();
        await participant.JoinAsync(eventId);
        var scheduler = CreateScheduler();

        // 30 dakika sonra başlıyor → hatırlatma (organizatöre ve katılımcıya), ikinci turda tekrar yok
        await api.ExecuteSqlAsync("UPDATE events SET starts_at = now() + interval '30 minutes' WHERE id = @id", new { id = eventId });
        await scheduler.RunOnceAsync(CancellationToken.None);
        await scheduler.RunOnceAsync(CancellationToken.None);

        Assert.Equal(new[] { "EventReminder" }, (await participant.NotificationsAsync()).Types());
        Assert.Single((await organizer.NotificationsAsync()).Types(), t => t == "EventReminder");

        // 5 saat önce başladı (varsayılan süre 3 saat) → bitti, puanlama bildirimi sadece katılımcıya
        await api.ExecuteSqlAsync("UPDATE events SET starts_at = now() - interval '5 hours' WHERE id = @id", new { id = eventId });
        await scheduler.RunOnceAsync(CancellationToken.None);

        Assert.Contains("RatingPrompt", (await participant.NotificationsAsync()).Types());
        Assert.DoesNotContain("RatingPrompt", (await organizer.NotificationsAsync()).Types());
    }

    // ------------------------------------------------------------------ yardımcılar

    /// <summary>
    /// Zamanlanmış bildirim servisini test veritabanına bağlı olarak elle kurar (API içinde kapalıdır);
    /// testler RunOnceAsync ile tek tur çalıştırır.
    /// </summary>
    private ScheduledNotificationService CreateScheduler() =>
        new(api.Factory.Services.GetRequiredService<NpgsqlDataSource>(),
            TimeProvider.System,
            NullLogger<ScheduledNotificationService>.Instance);

    /// <summary>Oylama etkinlik başlayınca açılır; zamanı beklemek yerine başlangıcı geriye çeker.</summary>
    private Task StartEventAsync(Guid eventId) =>
        api.ExecuteSqlAsync("UPDATE events SET starts_at = now() - interval '1 hour' WHERE id = @id", new { id = eventId });

    /// <summary>Etkinlik detayını verilen kullanıcının gözünden okur (200 bekler).</summary>
    private static async Task<System.Text.Json.Nodes.JsonNode> Detail(TestUser user, Guid eventId) =>
        await (await user.Client.GetAsync($"/api/events/{eventId}")).ReadJsonAsync(HttpStatusCode.OK);

    /// <summary>
    /// Verilen noktanın 5 km çevresindeki etkinliklerin kimliklerini, listedeki sırasıyla döner. extra ile
    /// arama ya da sıralama parametresi eklenir; koordinatlar noktayla yazılsın diye sabit kültür kullanılır.
    /// </summary>
    private static async Task<List<Guid>> NearbyIds(TestUser user, double lat, double lng, string extra = "")
    {
        var url = FormattableString.Invariant($"/api/events/nearby?lat={lat}&lng={lng}&radiusKm=5{extra}");
        var json = await (await user.Client.GetAsync(url)).ReadJsonAsync(HttpStatusCode.OK);
        return json["items"]!.AsArray().Select(i => Guid.Parse(i!["id"]!.GetValue<string>())).ToList();
    }

    /// <summary>
    /// Etkinlik düzenleme isteğinin gövdesi: başlık, başlangıç ve kontenjan parametreden, diğer alanlar sabit.
    /// </summary>
    private static object EditBody(string title, DateTimeOffset startsAt, int? capacity) => new
    {
        categoryId = 1,
        title,
        description = "Düzenlendi",
        startsAt,
        endsAt = (DateTimeOffset?)null,
        latitude = 41.0082,
        longitude = 28.9784,
        placeName = "Test Kafe",
        address = (string?)null,
        capacity
    };

    /// <summary>
    /// Kullanıcı adına etkinliğe puan verir (200 bekler) ve yeni ortalamayı içeren yanıtı döner.
    /// </summary>
    private static async Task<System.Text.Json.Nodes.JsonNode> Rate(TestUser user, Guid eventId, int score) =>
        await (await user.Client.PutAsJsonAsync($"/api/events/{eventId}/rating", new { score, comment = "test" }))
            .ReadJsonAsync(HttpStatusCode.OK);
}
