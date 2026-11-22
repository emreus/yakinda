using Yakinda.Application.Media;

namespace Yakinda.Application.Abstractions.Media;

/// <summary>
/// Görsellerin saklandığı yer (şimdilik sunucu diski; ileride S3/R2 gibi bir nesne deposu).
/// Anahtar "klasör/benzersiz-ad.uzantı" biçimindedir ve herkese açık "/media/{anahtar}" adresinden sunulur.
/// </summary>
public interface IMediaStorage
{
    /// <summary>
    /// Doğrulanmış ve temizlenmiş görseli verilen klasöre kaydeder; veritabanına yazılacak anahtarı döner.
    /// </summary>
    Task<string> SaveAsync(string folder, ImageFile image, CancellationToken cancellationToken);

    /// <summary>Dosya yoksa sessizce geçer.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

public static class MediaFolders
{
    public const string Avatars = "avatars";
    public const string Events = "events";
}
