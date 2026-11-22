using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Yakinda.Application.Abstractions.Auth;
using Yakinda.Application.Abstractions.Caching;
using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Infrastructure.Auth;
using Yakinda.Infrastructure.Caching;
using Yakinda.Infrastructure.Notifications;
using Yakinda.Infrastructure.Persistence;
using Yakinda.Infrastructure.Persistence.Repositories;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace Yakinda.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // snake_case kolonlar <-> PascalCase property'ler (starts_at -> StartsAt)
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        AddPersistence(services, configuration);
        AddAuth(services, configuration);
        AddCaching(services, configuration);
        AddNotifications(services, configuration);

        services.AddSingleton(TimeProvider.System);

        return services;
    }

    /// <summary>
    /// FusionCache: L1 bellek + L2 Redis + Redis backplane.
    /// ConnectionStrings:Redis boşsa sadece bellek içi cache ile çalışır (Redis'siz geliştirme için).
    /// </summary>
    private static void AddCaching(IServiceCollection services, IConfiguration configuration)
    {
        var redis = configuration.GetConnectionString("Redis");

        var fusion = services.AddFusionCache()
            .WithDefaultEntryOptions(FusionCacheService.EntryOptions(TimeSpan.FromMinutes(5)))
            .WithSerializer(new FusionCacheSystemTextJsonSerializer());

        if (!string.IsNullOrWhiteSpace(redis))
        {
            fusion
                .WithDistributedCache(new RedisCache(new RedisCacheOptions
                {
                    Configuration = redis,
                    InstanceName = "yakinda:"
                }))
                .WithBackplane(new RedisBackplane(new RedisBackplaneOptions
                {
                    Configuration = redis
                }));
        }

        services.AddSingleton<ICacheService, FusionCacheService>();
    }

    /// <summary>
    /// Bildirimler: repository'ler (command tarafı), zamanlanmış bildirim üreticisi ve
    /// Expo Push gönderici. İkisi de arka plan servisi (BackgroundService) olarak API ile birlikte çalışır.
    /// </summary>
    private static void AddNotifications(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IDeviceTokenRepository, DeviceTokenRepository>();

        services.AddOptions<PushOptions>().Bind(configuration.GetSection(PushOptions.SectionName));

        services.AddHttpClient(ExpoPushClient.HttpClientName, client =>
            {
                client.BaseAddress = new Uri("https://exp.host/");
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All
            });

        services.AddSingleton<ExpoPushClient>();
        services.AddHostedService<PushDispatcher>();

        // Testler zamanı kendileri yönettiği için kapatabilir (Notifications:SchedulerEnabled = false).
        if (configuration.GetValue("Notifications:SchedulerEnabled", true))
            services.AddHostedService<ScheduledNotificationService>();
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default tanımlı değil.");

        // NpgsqlDataSource bağlantı havuzunu yönetir; uygulama boyunca tek instance.
        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
        services.AddSingleton<MigrationRunner>();

        // Command tarafı: istek başına tek oturum (bağlantı + transaction)
        services.AddScoped<DbSession>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DbSession>());

        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IBlockRepository, BlockRepository>();
        services.AddScoped<IReportRepository, ReportRepository>();
        services.AddScoped<Moderation.MessageModeration>();
        services.AddScoped<Application.Safety.IReportTargetLookup>(sp => sp.GetRequiredService<Moderation.MessageModeration>());
        services.AddScoped<Application.Safety.IModerationContentRemover>(sp => sp.GetRequiredService<Moderation.MessageModeration>());
        services.AddScoped<IParticipantRepository, ParticipantRepository>();
        services.AddScoped<IRatingRepository, RatingRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
    }

    private static void AddAuth(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => o.SigningKey.Length >= 32, "Jwt:SigningKey en az 32 karakter olmalı.")
            .Validate(o => o.AccessTokenMinutes > 0 && o.RefreshTokenDays > 0, "Jwt süreleri pozitif olmalı.")
            .ValidateOnStart();

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // JwtBearer ayarları JwtOptions'tan (DI üzerinden) okunur.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;

                // "sub", "name" gibi claim adları Microsoft'un uzun URI'lerine çevrilmesin.
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ClaimNames.Name
                };
            });

        services.AddAuthorization();
    }
}
