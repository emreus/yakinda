using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Yakinda.Application.Abstractions.Email;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Yakinda.Api.IntegrationTests;

/// <summary>
/// Tüm entegrasyon testleri için tek bir PostGIS konteyneri ve tek bir API örneği.
/// Konteyner açılışı ~10 sn sürdüğü için testler arasında paylaşılır; her test kendi
/// kullanıcılarını ve etkinliklerini oluşturduğu için birbirini etkilemez.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    // Apple Silicon'da bu imaj (yalnızca amd64) önceden indirilmiş olmalı: docker compose up -d
    // ya da: docker pull --platform linux/amd64 postgis/postgis:17-3.5
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgis/postgis:17-3.5")
        .WithDatabase("yakinda")
        .WithUsername("yakinda")
        .WithPassword("yakinda_test")
        .Build();

    /// <summary>Testlerde token imzalama anahtarı (sahte token testleri aynı anahtarla imzalayıp süre/hedef kontrolünü dener).</summary>
    public const string SigningKey = "integration-test-signing-key-0123456789abcdef";

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    /// <summary>API'nin gönderdiği e-postalar (gerçekten gönderilmez).</summary>
    public CapturingEmailSender Emails { get; } = new();

    public string ConnectionString => _database.GetConnectionString();

    /// <summary>Testlerde yüklenen görsellerin yazıldığı geçici klasör.</summary>
    public string MediaRoot { get; } = Path.Combine(Path.GetTempPath(), "yakinda-media-" + Guid.NewGuid().ToString("N"));

    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync();

        // Program.cs bazı ayarları uygulama kurulurken okuduğu için (bağlantı dizesi gibi)
        // ortam değişkeni olarak verilir; bunlar her ortamda en baştan okunur.
        Set("ConnectionStrings__Default", ConnectionString);
        Set("ConnectionStrings__Redis", "");               // sadece bellek içi cache
        Set("Database__MigrateOnStartup", "true");
        Set("Jwt__SigningKey", SigningKey);
        Set("RateLimiting__Enabled", "false");
        Set("Notifications__SchedulerEnabled", "false");    // testler zamanlayıcıyı kendisi çalıştırır
        Set("Push__Enabled", "false");
        Set("Media__RootPath", MediaRoot);                  // yüklenen görseller geçici klasöre

        Factory = CreateFactory();
        _ = Factory.Server; // uygulamayı başlatır; migration'lar burada çalışır
    }

    /// <summary>Farklı ayarlarla ikinci bir API örneği (aynı veritabanı).</summary>
    public WebApplicationFactory<Program> CreateFactory(IDictionary<string, string?>? overrides = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(Emails));
            if (overrides is not null)
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(overrides));
        });

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _database.DisposeAsync();
        if (Directory.Exists(MediaRoot))
            Directory.Delete(MediaRoot, recursive: true);
    }

    private static void Set(string key, string value) => Environment.SetEnvironmentVariable(key, value);

    // ------------------------------------------------------------------ yardımcılar

    public async Task<TestUser> RegisterAsync(string displayName = "Test Kullanıcı", HttpClient? client = null)
    {
        client ??= Factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@test.local";
        const string password = "Sifre12345";

        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password, displayName, acceptTerms = true });
        var auth = await response.ReadJsonAsync(HttpStatusCode.OK);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth["accessToken"]!.GetValue<string>());

        return new TestUser(Guid.Parse(auth["user"]!["id"]!.GetValue<string>()), email, password, displayName, client);
    }

    /// <summary>Testlerde zamanı ileri almak yerine etkinliğin saatini geriye çekmek için.</summary>
    public async Task ExecuteSqlAsync(string sql, object? parameters = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        if (parameters is not null)
        {
            foreach (var property in parameters.GetType().GetProperties())
                command.Parameters.AddWithValue(property.Name, property.GetValue(parameters) ?? DBNull.Value);
        }
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        if (parameters is not null)
        {
            foreach (var property in parameters.GetType().GetProperties())
                command.Parameters.AddWithValue(property.Name, property.GetValue(parameters) ?? DBNull.Value);
        }
        return (T)(await command.ExecuteScalarAsync())!;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}

public sealed record TestUser(Guid Id, string Email, string Password, string DisplayName, HttpClient Client);

public static class TestExtensions
{
    /// <summary>Beklenen durum kodunu doğrular ve gövdeyi JSON olarak okur; hata varsa gövdeyi mesaja ekler.</summary>
    public static async Task<JsonNode> ReadJsonAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,
            $"Beklenen {(int)expected}, gelen {(int)response.StatusCode}: {body}");
        return string.IsNullOrEmpty(body) ? new JsonObject() : JsonNode.Parse(body)!;
    }

    public static async Task ExpectAsync(this HttpResponseMessage response, HttpStatusCode expected) =>
        await response.ReadJsonAsync(expected);

    /// <summary>Hata yanıtının ProblemDetails "code" alanını döner.</summary>
    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response, HttpStatusCode expected) =>
        (await response.ReadJsonAsync(expected))["code"]?.GetValue<string>();

    public static async Task<Guid> CreateEventAsync(
        this TestUser organizer,
        string title = "Test etkinliği",
        int? capacity = null,
        string joinPolicy = "Open",
        DateTimeOffset? startsAt = null,
        double latitude = 41.0082,
        double longitude = 28.9784)
    {
        var response = await organizer.Client.PostAsJsonAsync("/api/events", new
        {
            categoryId = 1,
            title,
            description = "Entegrasyon testi",
            startsAt = startsAt ?? DateTimeOffset.UtcNow.AddDays(1),
            endsAt = (DateTimeOffset?)null,
            latitude,
            longitude,
            placeName = "Test Kafe",
            address = (string?)null,
            capacity,
            joinPolicy
        });

        var json = await response.ReadJsonAsync(HttpStatusCode.Created);
        return Guid.Parse(json["id"]!.GetValue<string>());
    }

    public static Task<HttpResponseMessage> JoinAsync(this TestUser user, Guid eventId) =>
        user.Client.PostAsync($"/api/events/{eventId}/join", null);

    public static async Task<JsonArray> NotificationsAsync(this TestUser user)
    {
        var json = await (await user.Client.GetAsync("/api/notifications")).ReadJsonAsync(HttpStatusCode.OK);
        return json["items"]!.AsArray();
    }

    public static async Task<int> UnreadCountAsync(this TestUser user)
    {
        var json = await (await user.Client.GetAsync("/api/notifications/unread-count")).ReadJsonAsync(HttpStatusCode.OK);
        return json["count"]!.GetValue<int>();
    }

    public static IEnumerable<string> Types(this JsonArray notifications) =>
        notifications.Select(n => n!["type"]!.GetValue<string>());
}

public sealed class CapturingEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _messages = [];

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        lock (_messages)
            _messages.Add(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> To(string address)
    {
        lock (_messages)
            return _messages.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Son şifre sıfırlama e-postasının gövdesindeki 6 haneli kod.</summary>
    public string LastResetCode(string address) =>
        System.Text.RegularExpressions.Regex.Match(To(address)[^1].TextBody, "kodun: ([0-9]{6})").Groups[1].Value;
}
