using MediatR;
using Yakinda.Application.Abstractions.Caching;

namespace Yakinda.Application.Behaviors;

/// <summary>
/// ICachedQuery işaretli sorguların sonucunu cache'ten döner; cache'te yoksa handler'ı
/// çalıştırıp sonucu saklar. Aynı anahtara aynı anda gelen istekler tek bir veritabanı
/// sorgusunu paylaşır (cache stampede koruması FusionCache'ten gelir).
/// Handler hata fırlatırsa (ör. NotFound) hiçbir şey cache'lenmez.
/// </summary>
internal sealed class QueryCachingBehavior<TRequest, TResponse>(ICacheService cache)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ICachedQuery cached)
            return await next();

        return await cache.GetOrCreateAsync(
            cached.CacheKey,
            _ => next(),
            cached.CacheDuration,
            cached.CacheTags,
            cancellationToken);
    }
}
