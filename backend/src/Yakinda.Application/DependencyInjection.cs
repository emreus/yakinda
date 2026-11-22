using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Yakinda.Application.Abstractions.Caching;
using Yakinda.Application.Auth;
using Yakinda.Application.Behaviors;

namespace Yakinda.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Application katmanını DI'a kaydeder: MediatR handler'larını bulur, altı pipeline adımını sırasıyla
    /// ekler (Logging, Validation, QueryCaching, CacheInvalidation, MediaChanges, Transaction), bütün
    /// validator'ları kaydeder, doğrulama mesajlarını Türkçe yapar ve istek başına kullanılan yardımcı
    /// servisleri (token üretici, bekleme listesi, hesap temizliği, görsel değişiklikleri, cache kuyruğu) ekler.
    /// Lisans anahtarı boş bırakılırsa MEDIATR_LICENSE_KEY ortam değişkenine bakılır; o da yoksa MediatR
    /// çalışır ama log'a lisans uyarısı yazar.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services, string? mediatRLicenseKey)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);

            if (!string.IsNullOrWhiteSpace(mediatRLicenseKey))
                cfg.LicenseKey = mediatRLicenseKey;

            // Sıra önemli: ilk eklenen en dışta çalışır.
            // Logging -> Validation -> QueryCaching -> CacheInvalidation -> MediaChanges -> Transaction -> Handler
            // (CacheInvalidation ve MediaChanges, Transaction'ı sarmalar: commit'ten sonra çalışırlar.)
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(QueryCachingBehavior<,>));
            cfg.AddOpenBehavior(typeof(CacheInvalidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(Media.MediaChangesBehavior<,>));
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // FluentValidation'ın varsayılan mesajları Türkçe olsun.
        ValidatorOptions.Global.LanguageManager.Culture = new CultureInfo("tr");

        services.AddScoped<AuthTokenIssuer>();
        services.AddScoped<Events.Common.WaitlistPromoter>();
        services.AddScoped<Safety.AccountCleanup>();
        services.AddScoped<Media.MediaChanges>();

        // Command başına biriken cache etiketleri
        services.AddScoped<CacheInvalidationQueue>();
        services.AddScoped<ICacheInvalidator>(sp => sp.GetRequiredService<CacheInvalidationQueue>());

        return services;
    }
}
