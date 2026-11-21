using Yakinda.Application.Abstractions.Messaging;

namespace Yakinda.Application.Abstractions.Caching;

/// <summary>
/// Uygulamanın cache'e bakışı. Application hangi kütüphanenin (FusionCache + Redis)
/// kullanıldığını bilmez; Infrastructure bu arayüzü uygular.
/// </summary>
public interface ICacheService
{
    /// <summary>Anahtar cache'te varsa onu döner; yoksa factory'yi bir kez çalıştırıp sonucu etiketleriyle saklar.</summary>
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan duration,
        IReadOnlyCollection<string> tags,
        CancellationToken cancellationToken);

    /// <summary>Bu etiketleri taşıyan tüm kayıtları geçersiz kılar (tüm API instance'larında).</summary>
    Task InvalidateTagsAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken);
}

/// <summary>
/// Command handler'ları değiştirdikleri verinin etiketlerini buraya bildirir.
/// Etiketler transaction commit edildikten SONRA temizlenir (CacheInvalidationBehavior);
/// böylece geri alınan bir işlem cache'i boş yere silmez ve commit'ten önce eski veri yeniden cache'lenmez.
/// </summary>
public interface ICacheInvalidator
{
    void InvalidateAfterCommit(params string[] tags);
}

/// <summary>Sonucu cache'lenecek sorgu. QueryCachingBehavior bu bilgilere göre çalışır.</summary>
public interface ICachedQuery
{
    string CacheKey { get; }
    TimeSpan CacheDuration { get; }
    IReadOnlyCollection<string> CacheTags { get; }
}

public interface ICachedQuery<TResponse> : IQuery<TResponse>, ICachedQuery
{
}

/// <summary>
/// Cache etiketleri tek yerde. Bir etkinlikte ya da kullanıcıda değişiklik olduğunda
/// ilgili etiket temizlenir ve o etkinliğe/kullanıcıya ait tüm cache kayıtları geçersizleşir.
/// </summary>
public static class CacheTag
{
    public const string Categories = "categories";

    /// <summary>Etkinlik detayı (paylaşılan kısım) ve yorum sayfaları.</summary>
    public static string Event(Guid eventId) => $"event:{eventId}";

    /// <summary>Kullanıcı profili (organizatör puanı, sayılar, dağılım).</summary>
    public static string User(Guid userId) => $"user:{userId}";
}
