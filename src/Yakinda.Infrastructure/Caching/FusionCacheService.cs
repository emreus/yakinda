using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Yakinda.Application.Abstractions.Caching;
using ZiggyCreatures.Caching.Fusion;

namespace Yakinda.Infrastructure.Caching;

/// <summary>
/// ICacheService'in FusionCache ile gerçeklemesi.
///  - L1: her API instance'ının belleği (mikrosaniye)
///  - L2: Redis (instance'lar arası paylaşılan cache)
///  - Backplane: Redis Pub/Sub; bir instance'taki silme/etiket temizleme diğerlerinin L1'ine anında ulaşır
/// Redis erişilemezse FusionCache hatayı loglar ve L1 + veritabanıyla çalışmaya devam eder;
/// istekler Redis yüzünden düşmez.
/// </summary>
internal sealed class FusionCacheService(IFusionCache cache, ILogger<FusionCacheService> logger) : ICacheService
{
    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan duration,
        IReadOnlyCollection<string> tags,
        CancellationToken cancellationToken)
    {
        try
        {
            return await cache.GetOrSetAsync<T>(
                key,
                (_, ct) => factory(ct),
                options: EntryOptions(duration),
                tags: tags,
                token: cancellationToken);
        }
        catch (Exception ex) when (IsFusionCacheWrapper(ex))
        {
            // Factory'nin fırlattığı asıl hata (NotFound, Forbidden...) API'ye olduğu gibi ulaşsın,
            // böylece 404 gibi durum kodları 500'e dönüşmez.
            ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
            throw;
        }
    }

    public async Task InvalidateTagsAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken)
    {
        foreach (var tag in tags)
        {
            try
            {
                await cache.RemoveByTagAsync(tag, token: cancellationToken);
            }
            catch (Exception ex)
            {
                // Veri zaten kaydedildi; cache temizlenemese bile istek başarılı sayılır.
                // Kayıt en geç süresi dolunca (en fazla birkaç dakika) yenilenir.
                logger.LogWarning(ex, "Cache etiketi temizlenemedi: {Tag}", tag);
            }
        }
    }

    /// <summary>Tüm cache kayıtlarının ortak ayarları; sadece süre çağrıya göre değişir.</summary>
    public static FusionCacheEntryOptions EntryOptions(TimeSpan duration) => new()
    {
        Duration = duration,

        // Fail-safe: veritabanı geçici olarak hata verirse, süresi dolmuş son değer
        // en fazla 2 saat boyunca sunulmaya devam eder (hata yerine biraz eski veri).
        IsFailSafeEnabled = true,
        FailSafeMaxDuration = TimeSpan.FromHours(2),
        FailSafeThrottleDuration = TimeSpan.FromSeconds(30),

        // Redis yavaşlarsa isteği bekletme: 1 sn sonra eski değerle devam et, 2 sn'de vazgeç.
        DistributedCacheSoftTimeout = TimeSpan.FromSeconds(1),
        DistributedCacheHardTimeout = TimeSpan.FromSeconds(2)
    };

    private static bool IsFusionCacheWrapper(Exception ex) =>
        ex.InnerException is not null
        && ex.GetType().Namespace?.StartsWith("ZiggyCreatures", StringComparison.Ordinal) == true;
}
