using MediatR;
using Yakinda.Application.Abstractions.Caching;
using Yakinda.Application.Abstractions.Messaging;

namespace Yakinda.Application.Behaviors;

/// <summary>
/// Command'ın handler'da biriktirdiği cache etiketlerini, işlem başarıyla bittikten
/// (yani TransactionBehavior commit ettikten) sonra temizler. Pipeline'da
/// TransactionBehavior'dan ÖNCE kaydedilir ki onu sarmalasın.
/// Command hata fırlatırsa etiketler hiç temizlenmez; veri değişmediği için gerek yoktur.
/// </summary>
internal sealed class CacheInvalidationBehavior<TRequest, TResponse>(
    CacheInvalidationQueue queue,
    ICacheService cache)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// Önce sonraki adımı (transaction ve handler) bitirir. İstek bir command ise kuyrukta biriken
    /// etiketleri alır ve cache'ten temizler. Hata fırlarsa bu satırlara hiç gelinmez, cache'e dokunulmaz.
    /// </summary>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next();

        if (request is ITransactionalRequest)
        {
            var tags = queue.Drain();
            if (tags.Count > 0)
            {
                // Veri zaten kaydedildi; istemci bağlantıyı kapatsa bile temizlik yapılsın.
                await cache.InvalidateTagsAsync(tags, CancellationToken.None);
            }
        }

        return response;
    }
}

/// <summary>İstek başına (scoped) temizlenecek etiket listesi.</summary>
internal sealed class CacheInvalidationQueue : ICacheInvalidator
{
    private readonly HashSet<string> _tags = new(StringComparer.Ordinal);

    /// <summary>Etiketleri kümeye ekler; aynı etiket iki kez eklenirse tek kayıt olarak kalır.</summary>
    public void InvalidateAfterCommit(params string[] tags)
    {
        foreach (var tag in tags)
            _tags.Add(tag);
    }

    /// <summary>
    /// Biriken etiketlerin kopyasını döner ve kümeyi boşaltır (aynı etiketler ikinci kez temizlenmesin).
    /// </summary>
    public IReadOnlyCollection<string> Drain()
    {
        var copy = _tags.ToArray();
        _tags.Clear();
        return copy;
    }
}
