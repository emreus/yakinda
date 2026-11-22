using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Yakinda.Application.Abstractions.Caching;
using Yakinda.Application.Auth;
using Yakinda.Application.Behaviors;

namespace Yakinda.Application;

public static class DependencyInjection
{
    /// <param name="mediatRLicenseKey">
    /// MediatR lisans anahtarı. Boş bırakılırsa MEDIATR_LICENSE_KEY ortam değişkenine bakılır;
    /// o da yoksa MediatR çalışır ama log'a lisans uyarısı yazar.
    /// </param>
    public static IServiceCollection AddApplication(this IServiceCollection services, string? mediatRLicenseKey)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);

            if (!string.IsNullOrWhiteSpace(mediatRLicenseKey))
                cfg.LicenseKey = mediatRLicenseKey;

            // Sıra önemli: ilk eklenen en dışta çalışır.
            // Logging -> Validation -> QueryCaching -> CacheInvalidation -> Transaction -> Handler
            // (CacheInvalidation, Transaction'ı sarmalar: etiketler commit'ten sonra temizlenir.)
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(QueryCachingBehavior<,>));
            cfg.AddOpenBehavior(typeof(CacheInvalidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // FluentValidation'ın varsayılan mesajları Türkçe olsun.
        ValidatorOptions.Global.LanguageManager.Culture = new CultureInfo("tr");

        services.AddScoped<AuthTokenIssuer>();
        services.AddScoped<Events.Common.WaitlistPromoter>();

        // Command başına biriken cache etiketleri
        services.AddScoped<CacheInvalidationQueue>();
        services.AddScoped<ICacheInvalidator>(sp => sp.GetRequiredService<CacheInvalidationQueue>());

        return services;
    }
}
