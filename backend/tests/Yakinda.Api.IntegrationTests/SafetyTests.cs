using System.Net;
using System.Net.Http.Json;
using Yakinda.Application.Moderation;

namespace Yakinda.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SafetyTests(ApiFixture api)
{
    // ------------------------------------------------------------------ engelleme

    /// <summary>
    /// Engelleme iki yönlü çalışır: iki taraf da diğerinin etkinliğini yakındakiler listesinde görmez.
    /// Engelleyen kişi etkinliğin detayını ve organizatörün profilini açamaz (404), etkinliğe katılamaz
    /// (403 event.blocked). Engel listesinde kişinin adı görünür; engel kaldırılınca etkinlik yeniden
    /// listelenir ve katılım mümkün olur.
    /// </summary>
    [Fact]
    public async Task Blocking_hides_events_both_ways_and_prevents_joining()
    {
        var organizer = await api.RegisterAsync("Organizatör");
        var blocker = await api.RegisterAsync("Engelleyen");
        var eventId = await organizer.CreateEventAsync(latitude: 10.0, longitude: 10.0);
        var blockersEvent = await blocker.CreateEventAsync(latitude: 10.0, longitude: 10.0);

        await (await blocker.Client.PostAsync($"/api/users/{organizer.Id}/block", null)).ExpectAsync(HttpStatusCode.NoContent);

        Assert.DoesNotContain(eventId, await NearbyIds(blocker));
        Assert.DoesNotContain(blockersEvent, await NearbyIds(organizer)); // iki yönlü
        Assert.Equal(HttpStatusCode.NotFound, (await blocker.Client.GetAsync($"/api/events/{eventId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await blocker.Client.GetAsync($"/api/users/{organizer.Id}")).StatusCode);
        Assert.Equal("event.blocked", await (await blocker.JoinAsync(eventId)).ProblemCodeAsync(HttpStatusCode.Forbidden));

        var blocks = await (await blocker.Client.GetAsync("/api/users/me/blocks")).ReadJsonAsync(HttpStatusCode.OK);
        Assert.Equal("Organizatör", blocks[0]!["displayName"]!.GetValue<string>());

        await (await blocker.Client.DeleteAsync($"/api/users/{organizer.Id}/block")).ExpectAsync(HttpStatusCode.NoContent);
        Assert.Contains(eventId, await NearbyIds(blocker));
        await (await blocker.JoinAsync(eventId)).ExpectAsync(HttpStatusCode.OK);
    }

    /// <summary>
    /// Engelleme, iki tarafın birbirinin yaklaşan etkinliklerindeki katılımını da sona erdirir. Katılımcı
    /// organizatörü engelleyince yeri boşalır ve bekleme listesindeki kişi alınır; etkinlik engelleyenin
    /// "katıldıklarım" listesinden çıkar, organizatörün katılımcı listesinde de artık görünmez. Ters yönde
    /// de aynıdır: organizatörün engellediği katılımcı etkinlikten ayrılmış olur. Engel kaldırılınca kişi
    /// yeniden katılabilir (kaydı "ayrıldı" durumundadır, reddedilmiş değildir).
    /// </summary>
    [Fact]
    public async Task Blocking_ends_participation_in_each_others_upcoming_events()
    {
        var organizer = await api.RegisterAsync("Organizatör");
        var blocker = await api.RegisterAsync("Engelleyen");
        var removed = await api.RegisterAsync("Engellenen");
        var waiting = await api.RegisterAsync("Sırada");
        var eventId = await organizer.CreateEventAsync(capacity: 2);
        await blocker.JoinAsync(eventId);
        await removed.JoinAsync(eventId);
        Assert.Equal("Waitlisted", (await (await waiting.JoinAsync(eventId)).ReadJsonAsync(HttpStatusCode.OK))["status"]!.GetValue<string>());

        // Katılımcı organizatörü engeller: yeri boşalır, sıradaki kişi alınır
        await (await blocker.Client.PostAsync($"/api/users/{organizer.Id}/block", null)).ExpectAsync(HttpStatusCode.NoContent);

        var detail = await (await waiting.Client.GetAsync($"/api/events/{eventId}")).ReadJsonAsync(HttpStatusCode.OK);
        Assert.Equal("Approved", detail["myParticipationStatus"]!.GetValue<string>());
        Assert.Equal(2, detail["participantCount"]!.GetValue<int>());
        Assert.DoesNotContain(eventId, await AttendingIds(blocker));
        Assert.DoesNotContain(blocker.Id, await ParticipantIds(organizer, eventId));

        // Organizatör bir katılımcıyı engeller: o da etkinlikten ayrılmış olur
        await (await organizer.Client.PostAsync($"/api/users/{removed.Id}/block", null)).ExpectAsync(HttpStatusCode.NoContent);

        var participants = await ParticipantIds(organizer, eventId);
        Assert.DoesNotContain(removed.Id, participants);
        Assert.Contains(waiting.Id, participants);
        Assert.DoesNotContain(eventId, await AttendingIds(removed));

        // Engel kalkınca kişi yeniden katılabilir
        await (await blocker.Client.DeleteAsync($"/api/users/{organizer.Id}/block")).ExpectAsync(HttpStatusCode.NoContent);
        Assert.Equal("Approved", (await (await blocker.JoinAsync(eventId)).ReadJsonAsync(HttpStatusCode.OK))["status"]!.GetValue<string>());
    }

    /// <summary>
    /// Organizatörüyle arada engel olan etkinlik, geçmişte kalmış olsa da "katıldıklarım" listesinde
    /// görünmez (detayı 404 döndüğü için satır açılamazdı). Geçmiş etkinlikteki katılım kaydına dokunulmaz:
    /// engel kaldırılınca etkinlik listeye geri gelir.
    /// </summary>
    [Fact]
    public async Task Blocked_organizers_past_events_are_hidden_from_my_events()
    {
        var organizer = await api.RegisterAsync();
        var member = await api.RegisterAsync();
        var eventId = await organizer.CreateEventAsync();
        await member.JoinAsync(eventId);
        // Etkinlik geçmişte kalır (dün başladı)
        await api.ExecuteSqlAsync("UPDATE events SET starts_at = now() - interval '1 day' WHERE id = @id", new { id = eventId });
        Assert.Contains(eventId, await AttendingIds(member));

        await (await member.Client.PostAsync($"/api/users/{organizer.Id}/block", null)).ExpectAsync(HttpStatusCode.NoContent);
        Assert.DoesNotContain(eventId, await AttendingIds(member));

        await (await member.Client.DeleteAsync($"/api/users/{organizer.Id}/block")).ExpectAsync(HttpStatusCode.NoContent);
        Assert.Contains(eventId, await AttendingIds(member));
    }

    /// <summary>Kullanıcı kendisini engelleyemez: 409 block.self.</summary>
    [Fact]
    public async Task Users_cannot_block_themselves()
    {
        var user = await api.RegisterAsync();
        var response = await user.Client.PostAsync($"/api/users/{user.Id}/block", null);
        Assert.Equal("block.self", await response.ProblemCodeAsync(HttpStatusCode.Conflict));
    }

    // ------------------------------------------------------------------ şikayet + moderasyon

    /// <summary>
    /// Aynı kişinin aynı içerik için ikinci şikayeti yeni kayıt açmaz (veritabanında tek açık şikayet kalır).
    /// Kişi kendi içeriğini şikayet edemez (409 report.own_content) ve şikayet listesini yalnızca yönetici
    /// görebilir (403 auth.admin_only).
    /// </summary>
    [Fact]
    public async Task Reports_are_deduplicated_and_only_admins_can_review_them()
    {
        var organizer = await api.RegisterAsync();
        var reporter = await api.RegisterAsync("Şikayetçi");
        var eventId = await organizer.CreateEventAsync();

        for (var i = 0; i < 2; i++)
            await (await Report(reporter, "Event", eventId)).ExpectAsync(HttpStatusCode.NoContent);

        Assert.Equal(1L, await api.ScalarAsync<long>(
            "SELECT count(*) FROM reports WHERE target_id = @id AND status = 0", new { id = eventId }));

        Assert.Equal("report.own_content", await (await Report(organizer, "Event", eventId)).ProblemCodeAsync(HttpStatusCode.Conflict));
        Assert.Equal("auth.admin_only", await (await reporter.Client.GetAsync("/api/admin/reports")).ProblemCodeAsync(HttpStatusCode.Forbidden));
    }

    /// <summary>
    /// Yönetici şikayet edilen etkinliği kaldırınca etkinlik listelerden çıkar, katılımcıya "Etkinlik
    /// kaldırıldı" bildirimi gider, yeni katılım 422 event.removed alır ve etkinlik için açık şikayet kalmaz.
    /// </summary>
    [Fact]
    public async Task Admin_removing_an_event_hides_it_and_notifies_participants()
    {
        var admin = await AdminAsync();
        var organizer = await api.RegisterAsync();
        var participant = await api.RegisterAsync();
        var reporter = await api.RegisterAsync();
        var eventId = await organizer.CreateEventAsync("Uygunsuz etkinlik", latitude: 20.0, longitude: 20.0);
        await participant.JoinAsync(eventId);
        await Report(reporter, "Event", eventId);

        var reportId = await OpenReportId(admin, eventId);
        await (await admin.Client.PostAsJsonAsync($"/api/admin/reports/{reportId}/resolve", new { action = "RemoveContent", note = "test" }))
            .ExpectAsync(HttpStatusCode.NoContent);

        Assert.DoesNotContain(eventId, await NearbyIds(participant, 20.0, 20.0));
        var notifications = await participant.NotificationsAsync();
        Assert.Contains(notifications, n => n!["title"]!.GetValue<string>() == "Etkinlik kaldırıldı");
        Assert.Equal("event.removed", await (await (await api.RegisterAsync()).JoinAsync(eventId)).ProblemCodeAsync(HttpStatusCode.UnprocessableEntity));
        Assert.Equal(0L, await api.ScalarAsync<long>("SELECT count(*) FROM reports WHERE target_id = @id AND status = 0", new { id = eventId }));
    }

    /// <summary>
    /// Yönetici kullanıcıyı yasaklayınca o kullanıcı giriş yapamaz (403 auth.banned) ve etkinlikleri
    /// yakındakiler listesinden çıkar.
    /// </summary>
    [Fact]
    public async Task Banned_users_cannot_log_in_and_their_events_disappear()
    {
        var admin = await AdminAsync();
        var troll = await api.RegisterAsync("Trol");
        var reporter = await api.RegisterAsync();
        var trollEvent = await troll.CreateEventAsync(latitude: 30.0, longitude: 30.0);
        await Report(reporter, "User", troll.Id);

        var reportId = await OpenReportId(admin, troll.Id);
        await (await admin.Client.PostAsJsonAsync($"/api/admin/reports/{reportId}/resolve", new { action = "BanUser" }))
            .ExpectAsync(HttpStatusCode.NoContent);

        var login = await api.Factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = troll.Email, password = troll.Password });
        Assert.Equal("auth.banned", await login.ProblemCodeAsync(HttpStatusCode.Forbidden));
        Assert.DoesNotContain(trollEvent, await NearbyIds(reporter, 30.0, 30.0));
    }

    // ------------------------------------------------------------------ hesap silme

    /// <summary>
    /// Hesap silme, şifre doğruysa çalışır (yanlış şifre 401 auth.invalid_password). Sonrasında: eski
    /// bilgilerle giriş yapılamaz ve e-posta yeniden kayıt için serbest kalır; kullanıcının düzenlediği
    /// etkinlik iptal edilip katılımcısına haber verilir; katıldığı etkinlikteki yeri bekleme listesindeki
    /// kişiye geçer; herkese açık profilinde adı "Silinmiş kullanıcı" olarak görünür.
    /// </summary>
    [Fact]
    public async Task Deleting_an_account_frees_seats_cancels_events_and_removes_personal_data()
    {
        var leaving = await api.RegisterAsync("Ayrılan");
        var participant = await api.RegisterAsync();
        var other = await api.RegisterAsync();
        var ownEvent = await leaving.CreateEventAsync();
        await participant.JoinAsync(ownEvent);
        var othersEvent = await other.CreateEventAsync(capacity: 1);
        await leaving.JoinAsync(othersEvent);
        var waiting = await api.RegisterAsync();
        await waiting.JoinAsync(othersEvent); // bekleme listesinde

        var wrong = await leaving.Client.PostAsJsonAsync("/api/users/me/delete", new { password = "yanlis-sifre" });
        Assert.Equal("auth.invalid_password", await wrong.ProblemCodeAsync(HttpStatusCode.Unauthorized));

        await (await leaving.Client.PostAsJsonAsync("/api/users/me/delete", new { password = leaving.Password }))
            .ExpectAsync(HttpStatusCode.NoContent);

        // Giriş yapılamaz, e-posta yeniden kullanılabilir
        var login = await api.Factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = leaving.Email, password = leaving.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        var again = await api.Factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { email = leaving.Email, password = "YeniSifre123", displayName = "Yeni", acceptTerms = true });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        // Düzenlediği etkinlik iptal, katılımcıya haber; katıldığı etkinlikte yeri bekleme listesine geçti
        Assert.Contains("EventCancelled", (await participant.NotificationsAsync()).Types());
        var detail = await (await waiting.Client.GetAsync($"/api/events/{othersEvent}")).ReadJsonAsync(HttpStatusCode.OK);
        Assert.Equal("Approved", detail["myParticipationStatus"]!.GetValue<string>());

        var profile = await (await participant.Client.GetAsync($"/api/users/{leaving.Id}")).ReadJsonAsync(HttpStatusCode.OK);
        Assert.Equal("Silinmiş kullanıcı", profile["displayName"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------ içerik filtresi + yasal sayfalar

    /// <summary>
    /// Harf yerine rakam yazılarak gizlenmiş küfür içeren etkinlik başlığı 400 ve title alanı hatasıyla reddedilir.
    /// </summary>
    [Fact]
    public async Task Objectionable_event_titles_are_rejected()
    {
        var user = await api.RegisterAsync();
        var response = await user.Client.PostAsJsonAsync("/api/events", new
        {
            categoryId = 1,
            title = "S1KT1R gidin",
            startsAt = DateTimeOffset.UtcNow.AddDays(1),
            latitude = 41.0,
            longitude = 29.0,
            joinPolicy = "Open"
        });

        var problem = await response.ReadJsonAsync(HttpStatusCode.BadRequest);
        Assert.NotNull(problem["errors"]!["title"]);
    }

    /// <summary>
    /// Yasal sayfalar (gizlilik, kullanım koşulları, hesap silme) oturum gerektirmeden HTML olarak sunulur
    /// ve beklenen ifadeyi içerir.
    /// </summary>
    [Theory]
    [InlineData("/legal/privacy", "Gizlilik Politikası")]
    [InlineData("/legal/terms", "sıfır tolerans")]
    [InlineData("/legal/delete-account", "Hesabımı sil")]
    public async Task Legal_pages_are_public_html(string path, string expected)
    {
        var response = await api.Factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync());
    }

    // ------------------------------------------------------------------ yardımcılar

    /// <summary>
    /// Yeni bir kullanıcı kaydeder ve yönetici yetkisini doğrudan veritabanından verir (API'de bunun ucu yoktur).
    /// </summary>
    private async Task<TestUser> AdminAsync()
    {
        var admin = await api.RegisterAsync("Yönetici");
        await api.ExecuteSqlAsync("UPDATE users SET is_admin = true WHERE id = @id", new { id = admin.Id });
        return admin;
    }

    /// <summary>Verilen hedefi (etkinlik ya da kullanıcı) "uygunsuz içerik" nedeniyle şikayet eder.</summary>
    private static Task<HttpResponseMessage> Report(TestUser reporter, string targetType, Guid targetId) =>
        reporter.Client.PostAsJsonAsync("/api/reports", new { targetType, targetId, reason = "Inappropriate", details = "test" });

    /// <summary>
    /// Yöneticinin açık şikayet listesinden verilen hedefe ait şikayetin kimliğini bulur (tam bir tane bekler).
    /// </summary>
    private static async Task<Guid> OpenReportId(TestUser admin, Guid targetId)
    {
        var reports = (await (await admin.Client.GetAsync("/api/admin/reports")).ReadJsonAsync(HttpStatusCode.OK)).AsArray();
        var report = reports.Single(r => r!["targetId"]!.GetValue<Guid>() == targetId)!;
        return report["id"]!.GetValue<Guid>();
    }

    /// <summary>
    /// Verilen noktanın 5 km çevresinde kullanıcıya listelenen etkinliklerin kimliklerini döner.
    /// </summary>
    private static async Task<List<Guid>> NearbyIds(TestUser user, double lat = 10.0, double lng = 10.0)
    {
        var url = FormattableString.Invariant($"/api/events/nearby?lat={lat}&lng={lng}&radiusKm=5");
        var json = await (await user.Client.GetAsync(url)).ReadJsonAsync(HttpStatusCode.OK);
        return json["items"]!.AsArray().Select(i => Guid.Parse(i!["id"]!.GetValue<string>())).ToList();
    }

    /// <summary>Kullanıcının "katıldıklarım" listesindeki (geçmişler dahil) etkinlik kimliklerini döner.</summary>
    private static async Task<List<Guid>> AttendingIds(TestUser user)
    {
        var json = await (await user.Client.GetAsync("/api/users/me/events?role=attending")).ReadJsonAsync(HttpStatusCode.OK);
        return json.AsArray().Select(i => Guid.Parse(i!["id"]!.GetValue<string>())).ToList();
    }

    /// <summary>Etkinliğin katılımcı listesini verilen kullanıcının gözünden okur ve kullanıcı kimliklerini döner.</summary>
    private static async Task<List<Guid>> ParticipantIds(TestUser viewer, Guid eventId)
    {
        var json = await (await viewer.Client.GetAsync($"/api/events/{eventId}/participants")).ReadJsonAsync(HttpStatusCode.OK);
        return json.AsArray().Select(p => Guid.Parse(p!["userId"]!.GetValue<string>())).ToList();
    }
}

public sealed class ContentFilterTests
{
    /// <summary>
    /// Filtre küfürleri ve bilinen kaçamakları yakalar: harf yerine rakam ya da simge, uzatılmış harfler,
    /// büyük harfle yazım ve bitişik yazılmış sözcükler.
    /// </summary>
    [Theory]
    [InlineData("amk")]
    [InlineData("S1KT1R git")]
    [InlineData("FUUUCK this")]
    [InlineData("orospuçocuğu")]
    [InlineData("bu ne $hit")]
    [InlineData("WHAT A BITCH")]
    public void Catches_profanity_and_common_evasions(string text) =>
        Assert.True(ContentFilter.IsObjectionable(text));

    /// <summary>
    /// Filtre sıradan metne dokunmaz: yasaklı bir sözcüğü yalnızca harf dizisi olarak içeren masum
    /// sözcükler, boş metin ve null uygunsuz sayılmaz.
    /// </summary>
    [Theory]
    [InlineData("Sıkıcı bir pazar, sıkıntıdan kurtulalım")]  // "sık" ≠ "sik"
    [InlineData("Beni eve götür")]                          // "göt" sadece tam kelime
    [InlineData("Ama ne güzel bir gün, amca da gelsin")]     // "am" listede yok
    [InlineData("Okeye 4. aranıyor")]
    [InlineData("Shitake mantarlı makarna akşamı")]          // "shit" sadece tam kelime
    [InlineData("")]
    [InlineData(null)]
    public void Leaves_ordinary_text_alone(string? text) =>
        Assert.False(ContentFilter.IsObjectionable(text));
}
