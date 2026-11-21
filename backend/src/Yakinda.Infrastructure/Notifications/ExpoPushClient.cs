using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Yakinda.Infrastructure.Notifications;

public sealed class PushOptions
{
    public const string SectionName = "Push";

    /// <summary>false ise PushDispatcher çalışmaz; bildirimler yine uygulama içi listede görünür.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Expo hesabında "Enhanced security for push notifications" açıksa gereken erişim anahtarı
    /// (expo.dev → Account settings → Access tokens). Kapalıysa boş bırakılır.
    /// </summary>
    public string? ExpoAccessToken { get; set; }

    /// <summary>Bekleyen bildirimlerin kaç saniyede bir kontrol edileceği.</summary>
    public int PollIntervalSeconds { get; set; } = 5;
}

/// <summary>
/// Expo Push servisi (https://exp.host/--/api/v2/push/send). Expo, mesajı Android'de FCM'e,
/// iOS'ta APNs'e iletir; böylece backend'in Firebase/Apple anahtarlarını tutması gerekmez.
/// Tek istekte en fazla 100 mesaj gönderilebilir.
/// </summary>
internal sealed class ExpoPushClient(IHttpClientFactory httpClientFactory, IOptions<PushOptions> options)
{
    public const string HttpClientName = "expo-push";
    public const int MaxBatchSize = 100;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Mesajları gönderir. Dönen ticket'lar mesajlarla aynı sıradadır.</summary>
    /// <exception cref="HttpRequestException">Servise ulaşılamadı ya da istek reddedildi.</exception>
    public async Task<IReadOnlyList<ExpoPushTicket>> SendAsync(
        IReadOnlyList<ExpoPushMessage> messages,
        CancellationToken cancellationToken)
    {
        if (messages.Count > MaxBatchSize)
            throw new ArgumentException($"Tek istekte en fazla {MaxBatchSize} mesaj gönderilebilir.", nameof(messages));

        using var request = new HttpRequestMessage(HttpMethod.Post, "--/api/v2/push/send")
        {
            Content = JsonContent.Create(messages, options: JsonOptions)
        };

        var accessToken = options.Value.ExpoAccessToken;
        if (!string.IsNullOrWhiteSpace(accessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var http = httpClientFactory.CreateClient(HttpClientName);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ExpoPushResponse>(JsonOptions, cancellationToken);
        return body?.Data ?? [];
    }
}

/// <param name="Data">Uygulamaya giden ek bilgi; mobil taraf eventId ile ilgili etkinliği açar.</param>
internal sealed record ExpoPushMessage(
    string To,
    string Title,
    string Body,
    IReadOnlyDictionary<string, string?> Data,
    string Sound = "default",
    string ChannelId = "default",
    string Priority = "high");

internal sealed class ExpoPushResponse
{
    public List<ExpoPushTicket>? Data { get; set; }
}

internal sealed class ExpoPushTicket
{
    /// <summary>"ok" ya da "error"</summary>
    public string Status { get; set; } = string.Empty;

    public string? Id { get; set; }
    public string? Message { get; set; }
    public ExpoPushTicketDetails? Details { get; set; }

    public bool IsDeviceNotRegistered =>
        Status == "error" && Details?.Error == "DeviceNotRegistered";
}

internal sealed class ExpoPushTicketDetails
{
    /// <summary>Örn. "DeviceNotRegistered" (uygulama silinmiş / token geçersiz), "MessageRateExceeded".</summary>
    public string? Error { get; set; }
}
