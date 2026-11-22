using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Yakinda.Api.Infrastructure;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;

    /// <summary>Bir IP adresinin pencere başına yapabileceği giriş/kayıt/yenileme isteği.</summary>
    public int AuthPermitLimit { get; set; } = 20;

    public int AuthWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Giriş yapmış bir kullanıcının pencere başına toplam istek hakkı (okuma + yazma). Açık sohbet ekranı
    /// dakikada ~15, bildirim rozeti ~2 istek atar; normal kullanım bunun çok altında kalır.
    /// </summary>
    public int UserRequestPermitLimit { get; set; } = 300;

    /// <summary>
    /// Giriş yapmış bir kullanıcının pencere başına yazma (GET dışı) isteği: mesaj, katılım, şikayet, etkinlik
    /// oluşturma, yükleme. Mesaj/bildirim yağmuruna ve toplu içerik üretimine karşı.
    /// </summary>
    public int UserWritePermitLimit { get; set; } = 60;

    public int UserWindowSeconds { get; set; } = 60;

    /// <summary>Giriş yapmamış bir IP adresinin pencere başına istek hakkı (paylaşım sayfası, yasal sayfalar, giriş uçları).</summary>
    public int AnonymousPermitLimit { get; set; } = 300;
}

/// <summary>
/// İstek sınırları:
///  * "auth" politikası: giriş, kayıt, token yenileme ve şifre sıfırlama uçlarına IP başına sıkı sınır
///    (şifre deneme saldırılarına karşı).
///  * Genel sınır: her istek için. Giriş yapmış kullanıcılar kullanıcı başına (toplam istek + ayrıca yazma
///    istekleri), diğerleri IP başına sayılır. UseRateLimiter bu yüzden UseAuthentication'dan sonra gelir.
/// Sunucu bir ters proxy (Caddy, Nginx) arkasındaysa gerçek istemci IP'si için ForwardedHeaders
/// ayarlı olmalı; aksi halde tüm istekler proxy'nin IP'sinden geliyormuş gibi görünür.
/// </summary>
public static class RateLimitingSetup
{
    public const string AuthPolicy = "auth";

    public static IServiceCollection AddAppRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        // Ayarlar istek anında okunur; testler ve ortam değişkenleri sonradan değiştirebilir.
        services.AddOptions<RateLimitingOptions>().Bind(configuration.GetSection(RateLimitingOptions.SectionName));

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                var response = context.HttpContext.Response;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Çok fazla istek",
                    Detail = "Kısa sürede çok fazla istek gönderdin. Lütfen biraz bekleyip tekrar dene."
                };
                problem.Extensions["code"] = "rate_limited";

                await response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken);
            };

            // Sıra önemli: önce toplam istek sayacı, sonra yazma sayacı (reddedilen yazma da toplam sayaca işler).
            limiter.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(AllRequests),
                PartitionedRateLimiter.Create<HttpContext, string>(UserWrites));

            limiter.AddPolicy(AuthPolicy, httpContext =>
            {
                var options = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                return options.Enabled
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = options.AuthPermitLimit,
                            Window = TimeSpan.FromSeconds(options.AuthWindowSeconds),
                            QueueLimit = 0
                        })
                    : RateLimitPartition.GetNoLimiter("disabled");
            });
        });

        return services;
    }

    private static RateLimitPartition<string> AllRequests(HttpContext context)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
        if (!options.Enabled)
            return RateLimitPartition.GetNoLimiter("disabled");

        var userId = context.User.FindFirst("sub")?.Value;
        return userId is not null
            ? FixedWindow("user:" + userId, options.UserRequestPermitLimit, options.UserWindowSeconds)
            : FixedWindow("ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"), options.AnonymousPermitLimit, options.UserWindowSeconds);
    }

    private static RateLimitPartition<string> UserWrites(HttpContext context)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
        var userId = context.User.FindFirst("sub")?.Value;
        var method = context.Request.Method;
        var isWrite = !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));

        return options.Enabled && isWrite && userId is not null
            ? FixedWindow("user:" + userId, options.UserWritePermitLimit, options.UserWindowSeconds)
            : RateLimitPartition.GetNoLimiter("not-limited");
    }

    private static RateLimitPartition<string> FixedWindow(string key, int permitLimit, int windowSeconds) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueLimit = 0
        });
}
