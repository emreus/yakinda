using System.Net;
using System.Net.Http.Json;

namespace Yakinda.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFixture api)
{
    [Fact]
    public async Task Health_endpoint_is_up()
    {
        var response = await api.Factory.CreateClient().GetAsync("/health");
        var json = await response.ReadJsonAsync(HttpStatusCode.OK);
        Assert.Equal("ok", json["status"]!.GetValue<string>());
    }

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

    [Fact]
    public async Task Same_email_cannot_register_twice()
    {
        var user = await api.RegisterAsync();

        var response = await api.Factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { email = user.Email.ToUpperInvariant(), password = "BaskaSifre123", displayName = "İkinci" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var user = await api.RegisterAsync();

        var response = await api.Factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = "yanlis-sifre" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_token_is_rotated_and_old_one_stops_working()
    {
        var client = api.Factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@test.local";
        var register = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Sifre12345", displayName = "Yenileme" });
        var first = (await register.ReadJsonAsync(HttpStatusCode.OK))["refreshToken"]!.GetValue<string>();

        var refreshed = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = first });
        var second = (await refreshed.ReadJsonAsync(HttpStatusCode.OK))["refreshToken"]!.GetValue<string>();
        Assert.NotEqual(first, second);

        var reused = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = first });
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoints_require_a_token()
    {
        var response = await api.Factory.CreateClient().GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

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
