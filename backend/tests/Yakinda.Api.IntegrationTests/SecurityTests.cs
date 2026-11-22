using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Yakinda.Api.IntegrationTests;

/// <summary>
/// Güvenlik testleri. Her test bir kötüye kullanım senaryosunu gerçek API'ye (gerçek PostGIS, gerçek HTTP hattı)
/// karşı dener ve reddedildiğini doğrular. Bölümler: kimlik doğrulama ve oturum, yetki ve gizlilik,
/// kötüye kullanım sınırları, enjeksiyon ve çıktı kodlama.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SecurityTests(ApiFixture api)
{
    // ================================================================== 1) Kimlik doğrulama ve oturum

    private static readonly string[] PublicApiRoutes = ["/api/auth/", "/api/categories"];

    [Fact]
    public async Task Every_api_endpoint_except_the_public_ones_requires_a_token()
    {
        var anonymous = api.Factory.CreateClient();
        var endpoints = api.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } route && route.StartsWith("/api/", StringComparison.Ordinal))
            .ToList();
        Assert.True(endpoints.Count >= 40, $"Beklenenden az uç nokta bulundu: {endpoints.Count}");

        var open = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var route = endpoint.RoutePattern.RawText!;
            if (PublicApiRoutes.Any(prefix => route.StartsWith(prefix, StringComparison.Ordinal)))
                continue;

            var path = Regex.Replace(route, "\\{[^}]+\\}", Guid.NewGuid().ToString());
            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), path);
                if (method != "GET")
                    request.Content = JsonContent.Create(new { });

                var response = await anonymous.SendAsync(request);
                if (response.StatusCode != HttpStatusCode.Unauthorized)
                    open.Add($"{method} {route} → {(int)response.StatusCode}");
            }
        }

        Assert.True(open.Count == 0, "Token istemeyen uç noktalar: " + string.Join(", ", open));
    }

    [Fact]
    public async Task Forged_or_tampered_tokens_are_rejected()
    {
        var victim = await api.RegisterAsync("Kurban");
        var attacker = await api.RegisterAsync("Saldırgan");
        var parts = attacker.Client.DefaultRequestHeaders.Authorization!.Parameter!.Split('.');
        var (header, payload) = (parts[0], parts[1]);
        var past = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds();

        // Kontrol: aynı içerik doğru anahtarla imzalanınca geçerli (yardımcı doğru imzalıyor)
        Assert.Equal(HttpStatusCode.OK, await StatusWithTokenAsync(Sign(header, payload, ApiFixture.SigningKey)));

        var forged = new Dictionary<string, string>
        {
            // Gövdede kullanıcı kimliği kurbanınkiyle değiştirilir, imza eski hâliyle kalır
            ["kimlik değiştirme"] = $"{header}.{EditPayload(payload, p => p["sub"] = victim.Id.ToString())}.{parts[2]}",
            ["imzasız (alg=none)"] = $"{Base64Url.EncodeToString("{\"alg\":\"none\",\"typ\":\"JWT\"}"u8)}.{EditPayload(payload, p => p["sub"] = victim.Id.ToString())}.",
            ["başka anahtar"] = Sign(header, EditPayload(payload, p => p["sub"] = victim.Id.ToString()), "saldirganin-kendi-anahtari-0123456789abcdef"),
            ["süresi dolmuş"] = Sign(header, EditPayload(payload, p => { p["exp"] = past + 60; p["nbf"] = past; p["iat"] = past; }), ApiFixture.SigningKey),
            ["başka hedef (aud)"] = Sign(header, EditPayload(payload, p => p["aud"] = "baska-uygulama"), ApiFixture.SigningKey),
            ["başka yayıncı (iss)"] = Sign(header, EditPayload(payload, p => p["iss"] = "baska-api"), ApiFixture.SigningKey),
            ["bozuk"] = "abc.def.ghi"
        };

        foreach (var (name, token) in forged)
            Assert.True(await StatusWithTokenAsync(token) == HttpStatusCode.Unauthorized, $"Sahte token kabul edildi: {name}");
    }

    [Fact]
    public async Task Login_gives_the_same_answer_for_unknown_email_and_wrong_password()
    {
        var user = await api.RegisterAsync();
        var client = api.Factory.CreateClient();

        var wrongPassword = await (await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = "yanlis-sifre-1" }))
            .ReadJsonAsync(HttpStatusCode.Unauthorized);
        var unknownEmail = await (await client.PostAsJsonAsync("/api/auth/login", new { email = $"{Guid.NewGuid():N}@test.local", password = "yanlis-sifre-1" }))
            .ReadJsonAsync(HttpStatusCode.Unauthorized);

        Assert.Equal(wrongPassword["code"]!.GetValue<string>(), unknownEmail["code"]!.GetValue<string>());
        Assert.Equal(wrongPassword["detail"]!.GetValue<string>(), unknownEmail["detail"]!.GetValue<string>());
    }

    [Fact]
    public async Task Banned_users_lose_access_immediately()
    {
        var target = await api.RegisterAsync("Yasaklanacak");
        var reporter = await api.RegisterAsync();
        await (await target.Client.GetAsync("/api/users/me")).ExpectAsync(HttpStatusCode.OK);

        await ReportAndResolveAsync(reporter, "User", target.Id, "BanUser");

        // Yasaklanmadan önce alınmış (30 dakika geçerli) token artık kabul edilmemeli
        Assert.Equal(HttpStatusCode.Unauthorized, (await target.Client.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await target.Client.PostAsJsonAsync("/api/events", EventBody("Yasakli etkinligi"))).StatusCode);
    }

    [Fact]
    public async Task A_deleted_accounts_token_stops_working()
    {
        var user = await api.RegisterAsync();
        await (await user.Client.PostAsJsonAsync("/api/users/me/delete", new { password = user.Password })).ExpectAsync(HttpStatusCode.NoContent);

        Assert.Equal(HttpStatusCode.Unauthorized, (await user.Client.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.Client.PostAsJsonAsync("/api/events", EventBody("Silinmis hesap etkinligi"))).StatusCode);
    }

    [Fact]
    public async Task Password_reset_ends_existing_sessions_including_access_tokens()
    {
        var user = await api.RegisterAsync(); // bu oturumun ele geçirildiğini düşün
        var anonymous = api.Factory.CreateClient();

        await (await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { email = user.Email })).ExpectAsync(HttpStatusCode.NoContent);
        var reset = await (await anonymous.PostAsJsonAsync("/api/auth/reset-password",
            new { email = user.Email, code = api.Emails.LastResetCode(user.Email), newPassword = "YepyeniSifre123" })).ReadJsonAsync(HttpStatusCode.OK);

        Assert.Equal(HttpStatusCode.Unauthorized, (await user.Client.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await StatusWithTokenAsync(reset["accessToken"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var client = api.Factory.CreateClient();
        var auth = await (await client.PostAsJsonAsync("/api/auth/register",
            new { email = $"{Guid.NewGuid():N}@test.local", password = "Sifre12345", displayName = "Çıkış yapan", acceptTerms = true }))
            .ReadJsonAsync(HttpStatusCode.OK);
        var refreshToken = auth["refreshToken"]!.GetValue<string>();

        await (await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken })).ExpectAsync(HttpStatusCode.NoContent);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
        Assert.Equal("auth.invalid_refresh_token", await refresh.ProblemCodeAsync(HttpStatusCode.Unauthorized));

        // Bilinmeyen token da aynı yanıtı alır (token var mı yok mu anlaşılmaz)
        await (await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = "olmayan-token" })).ExpectAsync(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Reset_codes_are_capped_per_account_per_day()
    {
        var user = await api.RegisterAsync();
        var anonymous = api.Factory.CreateClient();

        for (var i = 0; i < 8; i++)
        {
            await (await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { email = user.Email })).ExpectAsync(HttpStatusCode.NoContent);
            // Kodlar arası 60 saniyelik beklemeyi atla (kayıtlar hâlâ son 24 saat içinde kalır)
            await api.ExecuteSqlAsync(
                "UPDATE password_reset_codes SET created_at = created_at - interval '2 minutes' WHERE user_id = @id", new { id = user.Id });
        }

        // Kod başına 5 deneme hakkı var; günde en fazla 5 kod = günde en fazla 25 tahmin (milyonda 25)
        Assert.Equal(5, api.Emails.To(user.Email).Count);
    }

    [Fact]
    public async Task Reset_code_is_not_in_the_email_subject()
    {
        var user = await api.RegisterAsync();
        await api.Factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password", new { email = user.Email });

        var mail = api.Emails.To(user.Email)[^1];
        Assert.DoesNotMatch("[0-9]{6}", mail.Subject); // konu satırı kilit ekranında ve sunucu loglarında görünür
        Assert.Matches("[0-9]{6}", mail.TextBody);
    }

    [Theory]
    [InlineData("a@test.local,b@test.local")]
    [InlineData("Sahte Ad <sahte@test.local>")]
    [InlineData("bosluk var@test.local")]
    [InlineData("satir@test.local\r\nBcc: baskasi@test.local")]
    [InlineData("cift@@test.local")]
    [InlineData("noktasiz@alanadi")]
    public async Task Malformed_email_addresses_cannot_register(string email)
    {
        var response = await api.Factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { email, password = "Sifre12345", displayName = "Deneme", acceptTerms = true });

        Assert.Equal("validation_failed", await response.ProblemCodeAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Ordinary_email_addresses_still_register()
    {
        var response = await api.Factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { email = $"Ad.Soyad+etiket-{Guid.NewGuid():N}@posta.ornek.com.tr", password = "Sifre12345", displayName = "Deneme", acceptTerms = true });

        await response.ExpectAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Extra_json_fields_cannot_grant_privileges()
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        var response = await api.Factory.CreateClient().PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "Sifre12345",
            displayName = "Meraklı",
            acceptTerms = true,
            isAdmin = true,
            is_admin = true,
            role = "admin",
            ratingSum = 500,
            ratingCount = 100
        });
        await response.ExpectAsync(HttpStatusCode.OK);

        Assert.False(await api.ScalarAsync<bool>("SELECT is_admin FROM users WHERE email = @email", new { email }));
        Assert.Equal(0, await api.ScalarAsync<int>("SELECT rating_count FROM users WHERE email = @email", new { email }));
    }

    // ================================================================== 2) Yetki ve gizlilik

    [Fact]
    public async Task Users_cannot_manage_other_peoples_events()
    {
        var organizer = await api.RegisterAsync("Organizatör");
        var member = await api.RegisterAsync("Bekleyen");
        var stranger = await api.RegisterAsync("Yabancı");
        var eventId = await organizer.CreateEventAsync(joinPolicy: "ApprovalRequired");
        await member.JoinAsync(eventId); // onay bekliyor

        (HttpMethod Method, string Path, object? Body)[] attempts =
        [
            (HttpMethod.Put, $"/api/events/{eventId}", EventBody("Ele gecirildi")),
            (HttpMethod.Post, $"/api/events/{eventId}/cancel", null),
            (HttpMethod.Post, $"/api/events/{eventId}/participants/{member.Id}/approve", null),
            (HttpMethod.Post, $"/api/events/{eventId}/participants/{member.Id}/reject", null),
            (HttpMethod.Delete, $"/api/events/{eventId}/image", null)
        ];

        foreach (var intruder in new[] { stranger, member })
        {
            foreach (var (method, path, body) in attempts)
            {
                using var request = new HttpRequestMessage(method, path);
                if (body is not null)
                    request.Content = JsonContent.Create(body);
                var response = await intruder.Client.SendAsync(request);
                Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{intruder.DisplayName}: {method} {path} → {(int)response.StatusCode}");
            }
        }

        // Onay bekleyenler yalnız organizatöre görünür
        Assert.DoesNotContain(member.Id, await ParticipantIdsAsync(stranger, eventId));
        Assert.Contains(member.Id, await ParticipantIdsAsync(organizer, eventId));

        // Yönetici uçları sıradan kullanıcıya kapalı
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.Client.GetAsync("/api/admin/reports")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await stranger.Client.PostAsJsonAsync($"/api/admin/reports/{Guid.NewGuid()}/resolve", new { action = "BanUser" })).StatusCode);
    }

    [Fact]
    public async Task Blocked_users_do_not_see_each_other_in_participant_lists()
    {
        var organizer = await api.RegisterAsync("Organizatör");
        var alice = await api.RegisterAsync("Engelleyen");
        var bob = await api.RegisterAsync("Engellenen");
        var eventId = await organizer.CreateEventAsync();
        await alice.JoinAsync(eventId);
        await bob.JoinAsync(eventId);

        await (await alice.Client.PostAsync($"/api/users/{bob.Id}/block", null)).ExpectAsync(HttpStatusCode.NoContent);

        // Engellenen kişi, engelleyenin hangi etkinliğe (nerede, ne zaman) gideceğini listeden öğrenememeli
        var seenByBob = await ParticipantIdsAsync(bob, eventId);
        Assert.DoesNotContain(alice.Id, seenByBob);
        Assert.Contains(bob.Id, seenByBob);
        Assert.DoesNotContain(bob.Id, await ParticipantIdsAsync(alice, eventId));

        // Organizatör kendi etkinliğinin tüm katılımcılarını görmeye devam eder
        var seenByOrganizer = await ParticipantIdsAsync(organizer, eventId);
        Assert.Contains(alice.Id, seenByOrganizer);
        Assert.Contains(bob.Id, seenByOrganizer);

        // Organizatörün engellediği kişi için etkinlik yoktur: detay gibi katılımcılar ve yorumlar da açılmaz
        var blocked = await api.RegisterAsync("Organizatörün engellediği");
        await (await organizer.Client.PostAsync($"/api/users/{blocked.Id}/block", null)).ExpectAsync(HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.NotFound, (await blocked.Client.GetAsync($"/api/events/{eventId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await blocked.Client.GetAsync($"/api/events/{eventId}/participants")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await blocked.Client.GetAsync($"/api/events/{eventId}/ratings")).StatusCode);
    }

    [Fact]
    public async Task Removed_event_content_is_hidden_from_everyone_but_the_organizer()
    {
        const string marker = "kaldirilan-icerik-7f3a";
        var organizer = await api.RegisterAsync("Organizatör");
        var member = await api.RegisterAsync("Katılımcı");
        var outsider = await api.RegisterAsync("Şikayet eden");
        var eventId = await CreateAsync(organizer, EventBody($"Baslik {marker}", $"Aciklama {marker}", $"Mekan {marker}", $"Adres {marker}"));
        await member.JoinAsync(eventId);

        await ReportAndResolveAsync(outsider, "Event", eventId, "RemoveContent");

        // Yönetici kaldırdıktan sonra içerik, bağlantıyı bilen ya da daha önce katılmış kişilere de gösterilmemeli
        foreach (var viewer in new[] { member, outsider })
        {
            var detail = await (await viewer.Client.GetAsync($"/api/events/{eventId}")).Content.ReadAsStringAsync();
            Assert.DoesNotContain(marker, detail);
        }

        var attending = await (await member.Client.GetAsync("/api/users/me/events?role=attending")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(marker, attending);

        // Organizatör neyin kaldırıldığını görebilir
        var own = await (await organizer.Client.GetAsync($"/api/events/{eventId}")).ReadJsonAsync(HttpStatusCode.OK);
        Assert.Contains(marker, own["title"]!.GetValue<string>());
        Assert.Equal("Removed", own["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Deleted_messages_disappear_from_notification_previews_too()
    {
        const string marker = "silinecek-mesaj-4d2b";
        var organizer = await api.RegisterAsync("Organizatör");
        var member = await api.RegisterAsync("Yazan");
        var eventId = await organizer.CreateEventAsync();
        await member.JoinAsync(eventId);
        var message = await (await member.Client.PostAsJsonAsync($"/api/events/{eventId}/messages", new { body = $"Yanlislikla yazdim {marker}" }))
            .ReadJsonAsync(HttpStatusCode.OK);
        Assert.Contains(marker, await (await organizer.Client.GetAsync("/api/notifications")).Content.ReadAsStringAsync());

        await (await member.Client.DeleteAsync($"/api/events/{eventId}/messages/{message["id"]}")).ExpectAsync(HttpStatusCode.NoContent);

        Assert.DoesNotContain(marker, await (await organizer.Client.GetAsync("/api/notifications")).Content.ReadAsStringAsync());
        Assert.DoesNotContain(marker, await (await organizer.Client.GetAsync($"/api/events/{eventId}/messages")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Deleting_an_account_erases_its_messages_and_private_event_details()
    {
        const string marker = "ozel-veri-9c1e";
        var organizer = await api.RegisterAsync("Kalan");
        var leaving = await api.RegisterAsync("Giden");
        var eventId = await organizer.CreateEventAsync();
        await leaving.JoinAsync(eventId);
        await (await leaving.Client.PostAsJsonAsync($"/api/events/{eventId}/messages", new { body = $"Numaram 0555 111 22 33 {marker}" }))
            .ExpectAsync(HttpStatusCode.OK);
        await CreateAsync(leaving, EventBody("Giden kisinin etkinligi", $"Aciklama {marker}", "Evim", $"Adres {marker}"));

        await (await leaving.Client.PostAsJsonAsync("/api/users/me/delete", new { password = leaving.Password })).ExpectAsync(HttpStatusCode.NoContent);

        var chat = await (await organizer.Client.GetAsync($"/api/events/{eventId}/messages")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(marker, chat);
        // Mesajın önizlemesini taşıyan bildirim de gitmiş olmalı
        var notifications = await (await organizer.Client.GetAsync("/api/notifications")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(marker, notifications);
        Assert.Equal(0L, await api.ScalarAsync<long>(
            "SELECT count(*) FROM event_messages WHERE author_id = @id AND body <> ''", new { id = leaving.Id }));
        Assert.Equal(0L, await api.ScalarAsync<long>(
            "SELECT count(*) FROM events WHERE organizer_id = @id AND (description IS NOT NULL OR address IS NOT NULL OR image_key IS NOT NULL)",
            new { id = leaving.Id }));
    }

    // ================================================================== 3) Kötüye kullanım sınırları

    [Fact]
    public async Task Authenticated_requests_are_rate_limited_per_user()
    {
        await using var factory = api.CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:UserWritePermitLimit"] = "5",
            ["RateLimiting:UserRequestPermitLimit"] = "12",
            ["RateLimiting:UserWindowSeconds"] = "60"
        });
        var spammer = await api.RegisterAsync("Spamcı", factory.CreateClient());
        var other = await api.RegisterAsync("Başkası", factory.CreateClient());

        // Yazma istekleri: 5 serbest, 6.sı reddedilir
        for (var i = 0; i < 5; i++)
            await (await spammer.Client.PostAsync("/api/notifications/read-all", null)).ExpectAsync(HttpStatusCode.NoContent);
        var throttledWrite = await spammer.Client.PostAsync("/api/notifications/read-all", null);
        Assert.Equal("rate_limited", await throttledWrite.ProblemCodeAsync(HttpStatusCode.TooManyRequests));
        Assert.True(throttledWrite.Headers.Contains("Retry-After"));

        // Okumalar yazma sınırına takılmaz; ama onların da (daha yüksek) bir sınırı vardır
        await (await spammer.Client.GetAsync("/api/notifications/unread-count")).ExpectAsync(HttpStatusCode.OK);
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 12; i++)
            statuses.Add((await spammer.Client.GetAsync("/api/notifications/unread-count")).StatusCode);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);

        // Sınır kullanıcı başınadır; başkası etkilenmez
        await (await other.Client.PostAsync("/api/notifications/read-all", null)).ExpectAsync(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Rejoining_repeatedly_does_not_spam_the_organizer()
    {
        var organizer = await api.RegisterAsync("Organizatör");
        var pest = await api.RegisterAsync("Gir çık");
        var eventId = await organizer.CreateEventAsync();

        for (var i = 0; i < 3; i++)
        {
            await (await pest.JoinAsync(eventId)).ExpectAsync(HttpStatusCode.OK);
            await (await pest.Client.PostAsync($"/api/events/{eventId}/leave", null)).ExpectAsync(HttpStatusCode.NoContent);
        }
        await (await pest.JoinAsync(eventId)).ExpectAsync(HttpStatusCode.OK);

        Assert.Single((await organizer.NotificationsAsync()).Types(), type => type == "ParticipantJoined");
    }

    [Fact]
    public async Task One_account_cannot_flood_the_map_with_events()
    {
        var spammer = await api.RegisterAsync("Seri üretici");

        // Her istek 13 etkinlik açar (12 hafta tekrar): 3 istek = 39
        for (var i = 0; i < 3; i++)
            await (await spammer.Client.PostAsJsonAsync("/api/events", EventBody($"Seri {i}", repeatWeeks: 12, latitude: -45, longitude: 100)))
                .ExpectAsync(HttpStatusCode.Created);

        var overLimit = await spammer.Client.PostAsJsonAsync("/api/events", EventBody("Seri 4", repeatWeeks: 12, latitude: -45, longitude: 100));
        Assert.Equal("event.too_many_upcoming", await overLimit.ProblemCodeAsync(HttpStatusCode.UnprocessableEntity));
    }

    [Fact]
    public async Task Device_tokens_are_capped_per_user()
    {
        var user = await api.RegisterAsync();
        for (var i = 0; i < 14; i++)
            await (await user.Client.PostAsJsonAsync("/api/devices", new { token = $"ExponentPushToken[{Guid.NewGuid():N}]", platform = "android" }))
                .ExpectAsync(HttpStatusCode.NoContent);

        Assert.Equal(10L, await api.ScalarAsync<long>("SELECT count(*) FROM device_push_tokens WHERE user_id = @id", new { id = user.Id }));
    }

    [Fact]
    public async Task Absurd_page_numbers_are_rejected_instead_of_crashing()
    {
        var user = await api.RegisterAsync();
        var eventId = await user.CreateEventAsync(latitude: -45, longitude: 101);

        string[] urls =
        [
            "/api/notifications?page=2147483647&pageSize=50",
            "/api/events/nearby?lat=41&lng=29&page=2147483647&pageSize=50",
            $"/api/events/{eventId}/ratings?page=2147483647&pageSize=50"
        ];

        foreach (var url in urls)
        {
            var response = await user.Client.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{url} → {(int)response.StatusCode}");
        }
    }

    // ================================================================== 4) Enjeksiyon ve çıktı kodlama

    [Theory]
    [InlineData("' OR '1'='1")]
    [InlineData("'; DROP TABLE users; --")]
    [InlineData("%' UNION SELECT password_hash FROM users --")]
    [InlineData("\\")]
    [InlineData("%")]
    [InlineData("_")]
    public async Task Sql_metacharacters_in_search_are_plain_text(string search)
    {
        // Başka testlerin etkinliklerinden uzak, boş bir bölge
        const string where = "lat=-33.3&lng=77.7&radiusKm=2";
        var user = await api.RegisterAsync();
        await user.CreateEventAsync("Siradan etkinlik", latitude: -33.3, longitude: 77.7);

        var probe = await (await user.Client.GetAsync($"/api/events/nearby?{where}&q={Uri.EscapeDataString(search)}")).ReadJsonAsync(HttpStatusCode.OK);
        Assert.Empty(probe["items"]!.AsArray());

        // Kontrol: normal arama aynı etkinliği bulur, tablolar yerinde
        var control = await (await user.Client.GetAsync($"/api/events/nearby?{where}&q=siradan")).ReadJsonAsync(HttpStatusCode.OK);
        Assert.NotEmpty(control["items"]!.AsArray());
        Assert.True(await api.ScalarAsync<long>("SELECT count(*) FROM users") > 0);
    }

    [Fact]
    public async Task Sql_metacharacters_in_stored_text_round_trip_unchanged()
    {
        const string title = "Robert'); DROP TABLE events;--";
        var user = await api.RegisterAsync("O'Brien \"; --");
        var eventId = await CreateAsync(user, EventBody(title, "'; UPDATE users SET is_admin = true; --", "x' OR 'a'='a"));

        var detail = await (await user.Client.GetAsync($"/api/events/{eventId}")).ReadJsonAsync(HttpStatusCode.OK);
        Assert.Equal(title, detail["title"]!.GetValue<string>());
        Assert.Equal("O'Brien \"; --", detail["organizerName"]!.GetValue<string>());
        Assert.False(await api.ScalarAsync<bool>("SELECT is_admin FROM users WHERE id = @id", new { id = user.Id }));
    }

    [Fact]
    public async Task Html_in_user_content_is_escaped_on_public_pages_and_emails()
    {
        var organizer = await api.RegisterAsync("<b>Org</b><img src=x onerror=alert(1)>");
        var eventId = await CreateAsync(organizer, EventBody("<script>alert('xss')</script>", placeName: "\"><svg onload=alert(2)>"));

        var html = await (await api.Factory.CreateClient().GetAsync($"/e/{eventId}")).Content.ReadAsStringAsync();
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert", html);
        Assert.DoesNotContain("<svg onload", html);
        Assert.DoesNotContain("<img src=x", html);
        Assert.DoesNotContain("<b>Org</b>", html);

        await api.Factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password", new { email = organizer.Email });
        var mail = api.Emails.To(organizer.Email)[^1];
        Assert.DoesNotContain("<img src=x", mail.HtmlBody);
        Assert.Contains("&lt;b&gt;Org", mail.HtmlBody);
    }

    [Fact]
    public async Task Responses_carry_browser_hardening_headers()
    {
        var client = api.Factory.CreateClient();

        var page = await client.GetAsync("/legal/privacy");
        Assert.Equal("nosniff", Header(page, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(page, "X-Frame-Options"));
        Assert.Equal("no-referrer", Header(page, "Referrer-Policy"));
        var policy = Header(page, "Content-Security-Policy") ?? "";
        Assert.Contains("default-src 'none'", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
        Assert.DoesNotContain("script-src", policy); // sayfalarda betik yok; default-src 'none' hepsini engeller

        var json = await client.GetAsync("/api/categories");
        Assert.Equal("nosniff", Header(json, "X-Content-Type-Options"));
    }

    [Fact]
    public async Task Media_route_serves_only_stored_images()
    {
        var folder = Path.Combine(api.MediaRoot, "avatars");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "sayfa.html"), "<script>alert(1)</script>");
        await File.WriteAllTextAsync(Path.Combine(folder, "not.txt"), "gizli not");
        await File.WriteAllTextAsync(Path.Combine(folder, ".yarim.jpg.tmp"), "yarım yazılmış dosya");
        var client = api.Factory.CreateClient();

        string[] paths =
        [
            "/media/avatars/sayfa.html",          // görsel olmayan dosya, bir şekilde klasöre girse bile sunulmaz
            "/media/avatars/not.txt",
            "/media/avatars/.yarim.jpg.tmp",      // geçici (gizli) dosya
            "/media/avatars/",                    // klasör listesi
            "/media/..%2Fappsettings.json",       // klasörün dışına çıkma denemeleri
            "/media/avatars/..%2F..%2Fappsettings.json",
            "/media/%2e%2e%2fappsettings.json"
        ];

        foreach (var path in paths)
        {
            var response = await client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{path} → {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task Errors_do_not_leak_internals()
    {
        var client = api.Factory.CreateClient();

        var malformed = await client.PostAsync("/api/auth/login", new StringContent("{ \"email\": ", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);

        var user = await api.RegisterAsync();
        var missing = await user.Client.GetAsync($"/api/events/{Guid.NewGuid()}");
        var unknownRoute = await user.Client.GetAsync("/api/boyle-bir-adres-yok");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownRoute.StatusCode);

        foreach (var response in new[] { malformed, missing, unknownRoute })
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Exception", body);
            Assert.DoesNotContain("   at ", body);
            Assert.DoesNotContain("Npgsql", body);
            Assert.False(response.Headers.Contains("Server"));
            Assert.False(response.Headers.Contains("X-Powered-By"));
        }
    }

    // ================================================================== yardımcılar

    private static object EventBody(
        string title,
        string? description = "Guvenlik testi",
        string? placeName = "Test Kafe",
        string? address = null,
        int repeatWeeks = 0,
        double latitude = 41.0082,
        double longitude = 28.9784) => new
    {
        categoryId = 1,
        title,
        description,
        startsAt = DateTimeOffset.UtcNow.AddDays(2),
        endsAt = (DateTimeOffset?)null,
        latitude,
        longitude,
        placeName,
        address,
        capacity = (int?)null,
        joinPolicy = "Open",
        repeatWeeks
    };

    private static async Task<Guid> CreateAsync(TestUser organizer, object body)
    {
        var json = await (await organizer.Client.PostAsJsonAsync("/api/events", body)).ReadJsonAsync(HttpStatusCode.Created);
        return Guid.Parse(json["id"]!.GetValue<string>());
    }

    private static async Task<List<Guid>> ParticipantIdsAsync(TestUser viewer, Guid eventId)
    {
        var json = await (await viewer.Client.GetAsync($"/api/events/{eventId}/participants")).ReadJsonAsync(HttpStatusCode.OK);
        return json.AsArray().Select(p => p!["userId"]!.GetValue<Guid>()).ToList();
    }

    /// <summary>Şikayet oluşturur ve yeni bir yönetici hesabıyla sonuçlandırır.</summary>
    private async Task ReportAndResolveAsync(TestUser reporter, string targetType, Guid targetId, string action)
    {
        await (await reporter.Client.PostAsJsonAsync("/api/reports", new { targetType, targetId, reason = "Inappropriate" }))
            .ExpectAsync(HttpStatusCode.NoContent);

        var admin = await api.RegisterAsync("Yönetici");
        await api.ExecuteSqlAsync("UPDATE users SET is_admin = true WHERE id = @id", new { id = admin.Id });

        var reports = (await (await admin.Client.GetAsync("/api/admin/reports")).ReadJsonAsync(HttpStatusCode.OK)).AsArray();
        var report = reports.First(r => r!["targetId"]!.GetValue<Guid>() == targetId)!;
        await (await admin.Client.PostAsJsonAsync($"/api/admin/reports/{report["id"]}/resolve", new { action }))
            .ExpectAsync(HttpStatusCode.NoContent);
    }

    private async Task<HttpStatusCode> StatusWithTokenAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await api.Factory.CreateClient().SendAsync(request)).StatusCode;
    }

    private static string Sign(string header, string payload, string key)
    {
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.ASCII.GetBytes($"{header}.{payload}"));
        return $"{header}.{payload}.{Base64Url.EncodeToString(signature)}";
    }

    private static string EditPayload(string payload, Action<JsonObject> edit)
    {
        var json = JsonNode.Parse(Encoding.UTF8.GetString(Base64Url.DecodeFromChars(payload)))!.AsObject();
        edit(json);
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json.ToJsonString()));
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(",", values)
            : null;
}
