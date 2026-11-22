using Microsoft.AspNetCore.HttpOverrides;

namespace Yakinda.Api.Infrastructure;

public static class ProductionSetup
{
    /// <summary>
    /// ForwardedHeaders:Enabled = true ise (API bir ters proxy arkasındaysa, ör. Caddy) X-Forwarded-For ve
    /// X-Forwarded-Proto başlıkları okunur: istek sınırı gerçek istemci IP'sine göre çalışır, paylaşım
    /// sayfasındaki adresler https olur. Yalnız proxy'nin ulaşabildiği bir API'de açılmalı (compose'da API
    /// dışarıya port açmaz); yoksa istemci başlığı kendisi yazıp IP'sini gizleyebilir.
    /// Ayar istek hattı kurulurken okunur (testler WebApplicationFactory ile değiştirebilsin diye).
    /// </summary>
    public static IServiceCollection AddProxySupport(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            if (!configuration.GetValue("ForwardedHeaders:Enabled", false))
            {
                options.ForwardedHeaders = ForwardedHeaders.None;
                return;
            }

            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Proxy aynı Docker ağında, IP'si sabit değil; yalnız son (proxy'nin eklediği) değer kullanılır.
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
#pragma warning disable ASPDEPR005 // .NET 10'da KnownIPNetworks'e taşındı; eski adı her sürümde derlenir
            options.KnownNetworks.Clear();
#pragma warning restore ASPDEPR005
        });
        return services;
    }

    /// <summary>
    /// Tarayıcıda açılan sayfalar (paylaşım sayfası, yasal metinler) ve diğer tüm yanıtlar için:
    ///  * içerik türü tahmini kapalı (nosniff), sayfalar başka sitelerin çerçevesine alınamaz;
    ///  * Content-Security-Policy: betik çalışmaz; yalnız satır içi stil ve kendi alan adımızdaki görseller yüklenir.
    ///    Kullanıcı içeriği zaten HTML olarak kodlanıyor; bu, bir kodlama hatası olursa ikinci savunma hattıdır.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers.ContentSecurityPolicy =
                "default-src 'none'; style-src 'unsafe-inline'; img-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
            await next(context);
        });

    /// <summary>Geliştirme dışı ortamlarda eksik ya da örnek kalmış ayarlar için açılışta uyarı yazar.</summary>
    public static void WarnAboutMissingSettings(this WebApplication app)
    {
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
            return;

        var config = app.Configuration;
        if (string.IsNullOrWhiteSpace(config["Email:Smtp:Host"]))
            app.Logger.LogWarning("Email:Smtp:Host ayarlı değil: şifre sıfırlama e-postaları gönderilmiyor, kodlar yalnız loga yazılıyor.");
        if ((config["Legal:ContactEmail"] ?? "").EndsWith("@ornek.com", StringComparison.OrdinalIgnoreCase))
            app.Logger.LogWarning("Legal:ContactEmail hâlâ örnek adres; mağaza başvurusundan önce gerçek destek adresini yaz.");
        if (!config.GetValue("ForwardedHeaders:Enabled", false))
            app.Logger.LogWarning("ForwardedHeaders:Enabled kapalı: API bir proxy arkasındaysa istek sınırı tüm kullanıcıları tek IP sayar.");
    }
}
