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
}

/// <summary>
/// Giriş, kayıt ve token yenileme uçlarına IP başına istek sınırı (şifre deneme saldırılarına karşı).
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
                    Title = "Çok fazla deneme",
                    Detail = "Çok fazla deneme yaptın. Lütfen biraz bekleyip tekrar dene."
                };
                problem.Extensions["code"] = "rate_limited";

                await response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken);
            };

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
}
