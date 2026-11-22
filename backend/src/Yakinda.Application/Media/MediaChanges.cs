using MediatR;
using Microsoft.Extensions.Logging;
using Yakinda.Application.Abstractions.Media;
using Yakinda.Application.Abstractions.Messaging;

namespace Yakinda.Application.Media;

/// <summary>
/// Dosya işlemleri veritabanı transaction'ına dahil olamaz; bu sınıf ikisini uyumlu tutar (istek başına bir tane):
///  * Command başarıyla commit edilirse, yerine yenisi konan ya da kaldırılan eski dosyalar silinir.
///  * Command hata verirse (rollback), bu istekte kaydedilen yeni dosyalar silinir; ortada sahipsiz dosya kalmaz.
/// </summary>
internal sealed class MediaChanges(IMediaStorage storage, ILogger<MediaChanges> logger)
{
    private readonly List<string> _saved = [];
    private readonly List<string> _obsolete = [];

    public async Task<string> SaveAsync(string folder, ImageFile image, CancellationToken cancellationToken)
    {
        var key = await storage.SaveAsync(folder, image, cancellationToken);
        _saved.Add(key);
        return key;
    }

    public void DeleteAfterCommit(string? key)
    {
        if (!string.IsNullOrEmpty(key))
            _obsolete.Add(key);
    }

    public async Task CompleteAsync(bool committed)
    {
        var toDelete = (committed ? _obsolete : _saved).ToArray();
        _saved.Clear();
        _obsolete.Clear();

        foreach (var key in toDelete)
        {
            try
            {
                // Veri zaten kaydedildi; istemci bağlantıyı kapatsa bile temizlik yapılsın.
                await storage.DeleteAsync(key, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Görsel silinemedi: {Key}", key);
            }
        }
    }
}

/// <summary>TransactionBehavior'ı sarmalar: commit/rollback sonrasında MediaChanges'i tamamlar.</summary>
internal sealed class MediaChangesBehavior<TRequest, TResponse>(MediaChanges changes)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ITransactionalRequest)
            return await next();

        TResponse response;
        try
        {
            response = await next();
        }
        catch
        {
            await changes.CompleteAsync(committed: false);
            throw;
        }

        await changes.CompleteAsync(committed: true);
        return response;
    }
}
