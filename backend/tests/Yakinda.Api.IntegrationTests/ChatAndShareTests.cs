using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Yakinda.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ChatAndShareTests(ApiFixture api)
{
    // ------------------------------------------------------------------ erişim

    /// <summary>
    /// Sohbeti yalnızca organizatör ve onaylı katılımcılar kullanabilir. Onaylı üye mesaj yazar ve
    /// organizatör bunu okur; isteği bekleyen kişi okuyamaz, etkinlikle ilgisi olmayan kişi yazamaz
    /// (403 chat.not_member). Üyenin etkinlik detayında canChat true gelir.
    /// </summary>
    [Fact]
    public async Task Only_the_organizer_and_approved_participants_can_use_the_chat()
    {
        var (organizer, member, eventId) = await EventWithMemberAsync(joinPolicy: "ApprovalRequired");
        var pending = await api.RegisterAsync();
        await pending.JoinAsync(eventId);

        await Send(member, eventId, "Taşları ben getiriyorum");

        var page = await Messages(organizer, eventId);
        Assert.Equal("Taşları ben getiriyorum", page["items"]![0]!["body"]!.GetValue<string>());
        Assert.False(page["items"]![0]!["isMine"]!.GetValue<bool>());

        Assert.Equal("chat.not_member", await (await pending.Client.GetAsync($"/api/events/{eventId}/messages")).ProblemCodeAsync(HttpStatusCode.Forbidden));
        Assert.Equal("chat.not_member", await (await (await api.RegisterAsync()).Client.PostAsJsonAsync($"/api/events/{eventId}/messages", new { body = "selam" }))
            .ProblemCodeAsync(HttpStatusCode.Forbidden));

        var detail = await (await member.Client.GetAsync($"/api/events/{eventId}")).ReadJsonAsync(HttpStatusCode.OK);
        Assert.True(detail["canChat"]!.GetValue<bool>());
    }

    // ------------------------------------------------------------------ bildirimler

    /// <summary>
    /// Sohbet bildirimi okunana kadar tektir: art arda üç mesaj organizatöre tek bildirim üretir, yazan
    /// kişiye hiç üretmez. Organizatör sohbeti okundu yaptıktan sonra gelen mesaj ikinci bildirimi oluşturur.
    /// </summary>
    [Fact]
    public async Task Chat_notifies_once_until_the_chat_is_read()
    {
        var (organizer, member, eventId) = await EventWithMemberAsync();

        await Send(member, eventId, "Bir");
        await Send(member, eventId, "İki");
        await Send(member, eventId, "Üç");
        Assert.Single((await organizer.NotificationsAsync()).Types(), t => t == "ChatMessage");
        Assert.DoesNotContain("ChatMessage", (await member.NotificationsAsync()).Types()); // kendi mesajı

        await (await organizer.Client.PostAsync($"/api/events/{eventId}/messages/read", null)).ExpectAsync(HttpStatusCode.NoContent);
        await Send(member, eventId, "Dört");

        Assert.Equal(2, (await organizer.NotificationsAsync()).Types().Count(t => t == "ChatMessage"));
    }

    // ------------------------------------------------------------------ sayfalama

    /// <summary>
    /// Mesajlar iki yönde sayfalanır: limit=2 en yeni iki mesajı ve hasMore=true döner; before ile daha
    /// eskiler gelir; after ile yalnızca verilen mesajdan sonra yazılanlar gelir (yeni mesaj yokken boş liste).
    /// </summary>
    [Fact]
    public async Task Messages_page_backwards_and_poll_forwards()
    {
        var (organizer, member, eventId) = await EventWithMemberAsync();
        for (var i = 1; i <= 5; i++)
            await Send(member, eventId, $"Mesaj {i}");

        var newest = await Messages(organizer, eventId, "?limit=2");
        Assert.Equal(new[] { "Mesaj 5", "Mesaj 4" }, Bodies(newest));
        Assert.True(newest["hasMore"]!.GetValue<bool>());

        var older = await Messages(organizer, eventId, $"?limit=2&before={Id(newest, ^1)}");
        Assert.Equal(new[] { "Mesaj 3", "Mesaj 2" }, Bodies(older));

        Assert.Empty(Bodies(await Messages(organizer, eventId, $"?after={Id(newest, 0)}")));
        await Send(member, eventId, "Mesaj 6");
        Assert.Equal(new[] { "Mesaj 6" }, Bodies(await Messages(organizer, eventId, $"?after={Id(newest, 0)}")));
    }

    // ------------------------------------------------------------------ silme, filtre, engel, şikayet

    /// <summary>
    /// Mesajı yalnızca yazarı ve organizatör silebilir; başka bir katılımcı 403 chat.cannot_delete alır.
    /// Silinen mesajlar listede kalır ama isDeleted=true ve gövdesiz döner.
    /// </summary>
    [Fact]
    public async Task Authors_and_organizers_can_delete_but_others_cannot()
    {
        var (organizer, member, eventId) = await EventWithMemberAsync();
        var other = await api.RegisterAsync();
        await other.JoinAsync(eventId);
        var mine = await Send(member, eventId, "Silinecek");
        var second = await Send(member, eventId, "Organizatör silecek");

        Assert.Equal("chat.cannot_delete", await (await other.Client.DeleteAsync($"/api/events/{eventId}/messages/{mine}")).ProblemCodeAsync(HttpStatusCode.Forbidden));
        await (await member.Client.DeleteAsync($"/api/events/{eventId}/messages/{mine}")).ExpectAsync(HttpStatusCode.NoContent);
        await (await organizer.Client.DeleteAsync($"/api/events/{eventId}/messages/{second}")).ExpectAsync(HttpStatusCode.NoContent);

        var items = (await Messages(other, eventId))["items"]!.AsArray();
        Assert.All(items, m =>
        {
            Assert.True(m!["isDeleted"]!.GetValue<bool>());
            Assert.Null(m["body"]);
        });
    }

    /// <summary>Küfür içeren mesaj içerik filtresine takılır ve 400 ile reddedilir.</summary>
    [Fact]
    public async Task Objectionable_messages_are_rejected()
    {
        var (_, member, eventId) = await EventWithMemberAsync();
        var response = await member.Client.PostAsJsonAsync($"/api/events/{eventId}/messages", new { body = "amk ya" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Engellenen kullanıcının mesajları engelleyene gösterilmez: organizatör üyeyi engelleyince sohbette
    /// yalnızca kendi mesajını görür.
    /// </summary>
    [Fact]
    public async Task Blocked_users_messages_are_hidden()
    {
        var (organizer, member, eventId) = await EventWithMemberAsync();
        await Send(member, eventId, "Görünmeyecek");
        await Send(organizer, eventId, "Görünecek");

        await organizer.Client.PostAsync($"/api/users/{member.Id}/block", null);

        Assert.Equal(new[] { "Görünecek" }, Bodies(await Messages(organizer, eventId)));
    }

    /// <summary>
    /// Şikayet edilen mesaj yönetici listesine düşer (özet olarak mesaj metni, sahibi olarak yazarı görünür).
    /// Yönetici şikayeti "içeriği kaldır" ile kapatınca mesaj sohbette silinmiş olarak görünür. Yönetici
    /// yetkisi test içinde doğrudan veritabanından verilir.
    /// </summary>
    [Fact]
    public async Task Reported_messages_reach_the_admin_and_can_be_removed()
    {
        var (organizer, member, eventId) = await EventWithMemberAsync();
        var messageId = await Send(member, eventId, "Kural dışı ama filtreye takılmayan mesaj");
        await (await organizer.Client.PostAsJsonAsync("/api/reports", new { targetType = "Message", targetId = messageId, reason = "Harassment" }))
            .ExpectAsync(HttpStatusCode.NoContent);

        var admin = await api.RegisterAsync("Yönetici");
        await api.ExecuteSqlAsync("UPDATE users SET is_admin = true WHERE id = @id", new { id = admin.Id });
        var report = (await (await admin.Client.GetAsync("/api/admin/reports")).ReadJsonAsync(HttpStatusCode.OK)).AsArray()
            .Single(r => r!["targetId"]!.GetValue<Guid>() == messageId)!;
        Assert.Equal("Kural dışı ama filtreye takılmayan mesaj", report["targetSummary"]!.GetValue<string>());
        Assert.Equal(member.Id, report["targetOwnerId"]!.GetValue<Guid>());

        await (await admin.Client.PostAsJsonAsync($"/api/admin/reports/{report["id"]}/resolve", new { action = "RemoveContent" }))
            .ExpectAsync(HttpStatusCode.NoContent);

        Assert.True((await Messages(organizer, eventId))["items"]![0]!["isDeleted"]!.GetValue<bool>());
    }

    // ------------------------------------------------------------------ paylaşım bağlantısı

    /// <summary>
    /// Paylaşım sayfası (/e/{id}) oturum olmadan açılır ve önizleme etiketlerini, etkinlik başlığını,
    /// organizatörün adını ve uygulama bağlantısını (yakinda://event/{id}) içerir. Etkinlik iptal edilince
    /// sayfa "Etkinlik bulunamadı" gösterir. Universal Links ayarlanmamışken Apple doğrulama dosyası 404 döner.
    /// </summary>
    [Fact]
    public async Task Share_page_shows_a_preview_and_hides_cancelled_events()
    {
        var organizer = await api.RegisterAsync("Paylaşan");
        var eventId = await organizer.CreateEventAsync("Cuma akşamı okey");
        var anonymous = api.Factory.CreateClient();

        var html = await (await anonymous.GetAsync($"/e/{eventId}")).Content.ReadAsStringAsync();
        Assert.Contains("og:title", html);
        Assert.Contains("Cuma akşamı okey", html);
        Assert.Contains($"yakinda://event/{eventId}", html);
        Assert.Contains("Paylaşan", html);

        await organizer.Client.PostAsync($"/api/events/{eventId}/cancel", null);
        Assert.Contains("Etkinlik bulunamadı", await (await anonymous.GetAsync($"/e/{eventId}")).Content.ReadAsStringAsync());

        // Universal Links ayarlanmadıysa doğrulama dosyası yok
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/.well-known/apple-app-site-association")).StatusCode);
    }

    // ------------------------------------------------------------------ yardımcılar

    /// <summary>
    /// Sohbet testlerinin ortak kurulumu: bir organizatör ve bir üye kaydeder, etkinlik oluşturur ve üyeyi
    /// katar; etkinlik onay gerektiriyorsa organizatör üyeyi onaylar.
    /// </summary>
    private async Task<(TestUser Organizer, TestUser Member, Guid EventId)> EventWithMemberAsync(string joinPolicy = "Open")
    {
        var organizer = await api.RegisterAsync("Organizatör");
        var member = await api.RegisterAsync("Katılımcı");
        var eventId = await organizer.CreateEventAsync(joinPolicy: joinPolicy);
        await member.JoinAsync(eventId);
        if (joinPolicy == "ApprovalRequired")
            await organizer.Client.PostAsync($"/api/events/{eventId}/participants/{member.Id}/approve", null);
        return (organizer, member, eventId);
    }

    /// <summary>Kullanıcı adına sohbete mesaj yazar (200 bekler) ve mesajın kimliğini döner.</summary>
    private static async Task<Guid> Send(TestUser user, Guid eventId, string body)
    {
        var json = await (await user.Client.PostAsJsonAsync($"/api/events/{eventId}/messages", new { body })).ReadJsonAsync(HttpStatusCode.OK);
        return json["id"]!.GetValue<Guid>();
    }

    /// <summary>
    /// Kullanıcı adına sohbet mesajlarını okur; query ile sayfalama parametreleri (limit, before, after) eklenir.
    /// </summary>
    private static async Task<JsonNode> Messages(TestUser user, Guid eventId, string query = "") =>
        await (await user.Client.GetAsync($"/api/events/{eventId}/messages{query}")).ReadJsonAsync(HttpStatusCode.OK);

    /// <summary>Sayfadaki mesajların metinlerini sırasıyla döner (silinmiş mesaj için boş metin).</summary>
    private static string[] Bodies(JsonNode page) =>
        page["items"]!.AsArray().Select(m => m!["body"]?.GetValue<string>() ?? "").ToArray();

    /// <summary>Sayfada verilen sıradaki mesajın kimliğini döner.</summary>
    private static string Id(JsonNode page, Index index) => page["items"]!.AsArray()[index]!["id"]!.GetValue<string>();
}
