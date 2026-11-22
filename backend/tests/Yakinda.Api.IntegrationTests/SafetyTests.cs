using System.Net;
using System.Net.Http.Json;
using Yakinda.Application.Moderation;

namespace Yakinda.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SafetyTests(ApiFixture api)
{
    // ------------------------------------------------------------------ engelleme

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

    [Fact]
    public async Task Users_cannot_block_themselves()
    {
        var user = await api.RegisterAsync();
        var response = await user.Client.PostAsync($"/api/users/{user.Id}/block", null);
        Assert.Equal("block.self", await response.ProblemCodeAsync(HttpStatusCode.Conflict));
    }

    // ------------------------------------------------------------------ şikayet + moderasyon

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

    private async Task<TestUser> AdminAsync()
    {
        var admin = await api.RegisterAsync("Yönetici");
        await api.ExecuteSqlAsync("UPDATE users SET is_admin = true WHERE id = @id", new { id = admin.Id });
        return admin;
    }

    private static Task<HttpResponseMessage> Report(TestUser reporter, string targetType, Guid targetId) =>
        reporter.Client.PostAsJsonAsync("/api/reports", new { targetType, targetId, reason = "Inappropriate", details = "test" });

    private static async Task<Guid> OpenReportId(TestUser admin, Guid targetId)
    {
        var reports = (await (await admin.Client.GetAsync("/api/admin/reports")).ReadJsonAsync(HttpStatusCode.OK)).AsArray();
        var report = reports.Single(r => r!["targetId"]!.GetValue<Guid>() == targetId)!;
        return report["id"]!.GetValue<Guid>();
    }

    private static async Task<List<Guid>> NearbyIds(TestUser user, double lat = 10.0, double lng = 10.0)
    {
        var url = FormattableString.Invariant($"/api/events/nearby?lat={lat}&lng={lng}&radiusKm=5");
        var json = await (await user.Client.GetAsync(url)).ReadJsonAsync(HttpStatusCode.OK);
        return json["items"]!.AsArray().Select(i => Guid.Parse(i!["id"]!.GetValue<string>())).ToList();
    }
}

public sealed class ContentFilterTests
{
    [Theory]
    [InlineData("amk")]
    [InlineData("S1KT1R git")]
    [InlineData("FUUUCK this")]
    [InlineData("orospuçocuğu")]
    [InlineData("bu ne $hit")]
    [InlineData("WHAT A BITCH")]
    public void Catches_profanity_and_common_evasions(string text) =>
        Assert.True(ContentFilter.IsObjectionable(text));

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
