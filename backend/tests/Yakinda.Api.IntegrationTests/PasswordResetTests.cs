using System.Net;
using System.Net.Http.Json;

namespace Yakinda.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PasswordResetTests(ApiFixture api)
{
    [Fact]
    public async Task Unknown_email_gets_the_same_answer_and_no_email()
    {
        var address = $"{Guid.NewGuid():N}@test.local";
        var response = await Forgot(address);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(api.Emails.To(address));
    }

    [Fact]
    public async Task Code_resets_password_logs_in_and_ends_other_sessions()
    {
        var user = await api.RegisterAsync("Unutkan");
        var oldSession = await (await Login(user.Email, user.Password)).ReadJsonAsync(HttpStatusCode.OK);

        await (await Forgot(user.Email)).ExpectAsync(HttpStatusCode.NoContent);
        var code = api.Emails.LastResetCode(user.Email);
        Assert.Matches("^[0-9]{6}$", code);
        Assert.Contains("Unutkan", api.Emails.To(user.Email)[^1].TextBody);

        var wrongCode = code == "000000" ? "111111" : "000000";
        Assert.Equal("auth.invalid_code", await (await Reset(user.Email, wrongCode, "YeniSifre123")).ProblemCodeAsync(HttpStatusCode.BadRequest));

        var auth = await (await Reset(user.Email, code, "YeniSifre123")).ReadJsonAsync(HttpStatusCode.OK);
        Assert.False(string.IsNullOrEmpty(auth["accessToken"]?.GetValue<string>()));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(user.Email, user.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(user.Email, "YeniSifre123")).StatusCode);

        var refresh = await api.Factory.CreateClient().PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = oldSession["refreshToken"]!.GetValue<string>() });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        // Kod bir kez kullanılır
        Assert.Equal("auth.code_expired", await (await Reset(user.Email, code, "BaskaSifre123")).ProblemCodeAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Five_wrong_attempts_burn_the_code()
    {
        var user = await api.RegisterAsync();
        await Forgot(user.Email);
        var code = api.Emails.LastResetCode(user.Email);
        var wrong = code == "999999" ? "888888" : "999999";

        for (var i = 1; i < 5; i++)
            Assert.Equal("auth.invalid_code", await (await Reset(user.Email, wrong, "YeniSifre123")).ProblemCodeAsync(HttpStatusCode.BadRequest));
        Assert.Equal("auth.code_attempts_exceeded", await (await Reset(user.Email, wrong, "YeniSifre123")).ProblemCodeAsync(HttpStatusCode.BadRequest));

        // Doğru kod artık işe yaramaz
        Assert.Equal("auth.code_attempts_exceeded", await (await Reset(user.Email, code, "YeniSifre123")).ProblemCodeAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Repeated_requests_within_a_minute_send_one_email()
    {
        var user = await api.RegisterAsync();

        await Forgot(user.Email);
        await Forgot(user.Email);

        Assert.Single(api.Emails.To(user.Email));
    }

    private Task<HttpResponseMessage> Forgot(string email) =>
        api.Factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password", new { email });

    private Task<HttpResponseMessage> Reset(string email, string code, string newPassword) =>
        api.Factory.CreateClient().PostAsJsonAsync("/api/auth/reset-password", new { email, code, newPassword });

    private Task<HttpResponseMessage> Login(string email, string password) =>
        api.Factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });
}
