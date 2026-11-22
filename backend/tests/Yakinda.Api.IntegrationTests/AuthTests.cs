using System.Net;
using System.Net.Http.Json;

namespace Yakinda.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFixture api)
{
    /// <summary>Sağlık ucu oturum gerektirmeden 200 ve status "ok" döner.</summary>
    [Fact]
    public async Task Health_endpoint_is_up()
    {
        var response = await api.Factory.CreateClient().GetAsync("/health");
        var json = await response.ReadJsonAsync(HttpStatusCode.OK);
        Assert.Equal("ok", json["status"]!.GetValue<string>());
    }

    /// <summary>
    /// Kayıt olan kullanıcı aynı e-posta ve şifreyle giriş yapabilir (access ve refresh token gelir) ve
    /// kendi profilini okuyabilir; görünen ad kayıtta verilen addır.
    /// </summary>
    [Fact]
    public async Task Registered_user_can_log_in_and_read_profile()
    {
        var user = await api.RegisterAsync("Ayşe");

        var login = await api.Factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        var auth = await login.ReadJsonAsync(HttpStatusCode.OK);
        Assert.False(string.IsNullOrEmpty(auth["accessToken"]?.GetValue<string>()));
        Assert.False(string.IsNullOrEmpty(auth["refreshToken"]?.GetValue<string>()));

        var me = await (await user.Client.GetAsync("/api/users/me")).ReadJsonAsync(HttpStatusCode.OK);
        Assert.Equal("Ayşe", me["displayName"]!.GetValue<string>());
    }

    /// <summary>
    /// Aynı e-postayla ikinci kayıt 409 döner; adres büyük harfle yazılsa da aynı adres sayılır.
    /// </summary>
    [Fact]
    public async Task Same_email_cannot_register_twice()
    {
        var user = await api.RegisterAsync();

        var response = await api.Factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { email = user.Email.ToUpperInvariant(), password = "BaskaSifre123", displayName = "İkinci", acceptTerms = true });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>Yanlış şifreyle giriş 401 döner.</summary>
    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var user = await api.RegisterAsync();

        var response = await api.Factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = "yanlis-sifre" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Refresh token her kullanımda yenisiyle değiştirilir: yenileme farklı bir token döner ve eski
    /// token ikinci kez kullanılınca 401 gelir.
    /// </summary>
    [Fact]
    public async Task Refresh_token_is_rotated_and_old_one_stops_working()
    {
        var client = api.Factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@test.local";
        var register = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Sifre12345", displayName = "Yenileme", acceptTerms = true });
        var first = (await register.ReadJsonAsync(HttpStatusCode.OK))["refreshToken"]!.GetValue<string>();

        var refreshed = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = first });
        var second = (await refreshed.ReadJsonAsync(HttpStatusCode.OK))["refreshToken"]!.GetValue<string>();
        Assert.NotEqual(first, second);

        var reused = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = first });
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
    }

    /// <summary>
    /// Kullanım koşulları kabul edilmeden (acceptTerms gönderilmeden) kayıt olunamaz: 400 ve acceptTerms
    /// alanı için doğrulama hatası.
    /// </summary>
    [Fact]
    public async Task Registration_requires_accepting_the_terms()
    {
        var response = await api.Factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { email = $"{Guid.NewGuid():N}@test.local", password = "Sifre12345", displayName = "Koşulsuz" });

        var problem = await response.ReadJsonAsync(HttpStatusCode.BadRequest);
        Assert.NotNull(problem["errors"]!["acceptTerms"]);
    }

    /// <summary>Token gönderilmeden korumalı bir uca (kendi profilim) yapılan istek 401 döner.</summary>
    [Fact]
    public async Task Protected_endpoints_require_a_token()
    {
        var response = await api.Factory.CreateClient().GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Geçersiz girdiler (bozuk e-posta, kısa şifre, boş ad) 400 ve alan alan hata listesi (errors) döner.
    /// </summary>
    [Fact]
    public async Task Invalid_input_returns_validation_problem()
    {
        var response = await api.Factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { email = "gecersiz", password = "123", displayName = "" });

        var problem = await response.ReadJsonAsync(HttpStatusCode.BadRequest);
        Assert.NotNull(problem["errors"]);
    }
}

[Collection(ApiCollection.Name)]
public sealed class RateLimitTests(ApiFixture api)
{
    /// <summary>
    /// Hız sınırı açık ikinci bir API örneğinde (dakikada 3 istek) üç hatalı giriş 401 alır; dördüncüsü
    /// 429, "rate_limited" kodu ve Retry-After başlığıyla reddedilir.
    /// </summary>
    [Fact]
    public async Task Too_many_login_attempts_are_throttled()
    {
        await using var factory = api.CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:AuthPermitLimit"] = "3",
            ["RateLimiting:AuthWindowSeconds"] = "60"
        });
        var client = factory.CreateClient();
        var body = new { email = "yok@test.local", password = "yanlis-sifre" };

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", body)).StatusCode);

        var throttled = await client.PostAsJsonAsync("/api/auth/login", body);
        Assert.Equal("rate_limited", await throttled.ProblemCodeAsync(HttpStatusCode.TooManyRequests));
        Assert.True(throttled.Headers.Contains("Retry-After"));
    }
}

[Collection(ApiCollection.Name)]
public sealed class ProxyTests(ApiFixture api)
{
    /// <summary>
    /// Proxy arkasında hız sınırı, X-Forwarded-For başlığındaki gerçek istemci adresine göre sayılır:
    /// sınır 2 iken aynı adresin üçüncü isteği 429 alır, başka bir adresten gelen istek etkilenmez.
    /// </summary>
    [Fact]
    public async Task Behind_a_proxy_limits_apply_per_real_client_ip()
    {
        await using var factory = api.CreateFactory(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:Enabled"] = "true",
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:AuthPermitLimit"] = "2",
            ["RateLimiting:AuthWindowSeconds"] = "60"
        });
        var body = new { email = "yok@test.local", password = "yanlis-sifre" };

        // Verilen adresi X-Forwarded-For başlığına koyup hatalı bir giriş isteği gönderir, durum kodunu döner.
        async Task<HttpStatusCode> LoginFrom(string ip)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(body) };
            request.Headers.Add("X-Forwarded-For", ip);
            return (await factory.CreateClient().SendAsync(request)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFrom("203.0.113.10"));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFrom("203.0.113.10"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await LoginFrom("203.0.113.10"));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFrom("203.0.113.20")); // başka kullanıcı etkilenmez
    }

    /// <summary>
    /// Proxy isteğin https ile geldiğini bildirdiğinde (X-Forwarded-Proto) paylaşım sayfasındaki görsel
    /// adresi de https ile başlar.
    /// </summary>
    [Fact]
    public async Task Share_links_use_https_behind_a_proxy()
    {
        await using var factory = api.CreateFactory(new Dictionary<string, string?> { ["ForwardedHeaders:Enabled"] = "true" });
        var organizer = await api.RegisterAsync();
        var eventId = await organizer.CreateEventAsync();
        await organizer.Client.PutAsync($"/api/events/{eventId}/image", ImageForm());

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/e/{eventId}");
        request.Headers.Add("X-Forwarded-Proto", "https");
        var html = await (await factory.CreateClient().SendAsync(request)).Content.ReadAsStringAsync();

        Assert.Contains("og:image\" content=\"https://localhost/media/events/", html);
    }

    /// <summary>Küçük bir JPEG test görselini "file" alanında taşıyan çok parçalı form gövdesi üretir.</summary>
    private static MultipartFormDataContent ImageForm()
    {
        var file = new ByteArrayContent(TestImages.Jpeg());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        return new MultipartFormDataContent { { file, "file", "kapak.jpg" } };
    }
}
