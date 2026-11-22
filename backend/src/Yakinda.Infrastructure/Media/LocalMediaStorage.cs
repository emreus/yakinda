using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Yakinda.Application.Abstractions.Media;
using Yakinda.Application.Media;

namespace Yakinda.Infrastructure.Media;

public sealed class MediaOptions
{
    public const string SectionName = "Media";

    /// <summary>
    /// Görsellerin yazılacağı klasör. Göreli yol, uygulamanın içerik köküne göre çözülür.
    /// Sunucuda kalıcı bir diske (Docker volume) bağlanmalı; yoksa her yeniden kurulumda görseller kaybolur.
    /// </summary>
    public string RootPath { get; set; } = "data/media";

    public static string ResolveRoot(MediaOptions options, string contentRootPath) =>
        Path.GetFullPath(options.RootPath, contentRootPath);
}

/// <summary>
/// Görselleri sunucu diskine yazar. Anahtarlar sunucuda üretilir ("avatars/0193...jpg"); istemciden gelen hiçbir
/// ad dosya yoluna girmez. Dosya önce gizli (nokta ile başlayan) geçici adla yazılıp sonra taşınır; böylece
/// yarım yazılmış bir dosya hiçbir zaman sunulmaz.
/// </summary>
internal sealed partial class LocalMediaStorage(IOptions<MediaOptions> options, IHostEnvironment environment) : IMediaStorage
{
    private string Root => MediaOptions.ResolveRoot(options.Value, environment.ContentRootPath);

    public async Task<string> SaveAsync(string folder, ImageFile image, CancellationToken cancellationToken)
    {
        if (!FolderPattern().IsMatch(folder))
            throw new ArgumentException("Geçersiz klasör adı.", nameof(folder));

        var directory = Path.Combine(Root, folder);
        Directory.CreateDirectory(directory);

        var name = $"{Guid.CreateVersion7():N}.{image.Extension}";
        var temporary = Path.Combine(directory, $".{name}.tmp");
        await File.WriteAllBytesAsync(temporary, image.Data, cancellationToken);
        File.Move(temporary, Path.Combine(directory, name));

        return $"{folder}/{name}";
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        if (KeyPattern().IsMatch(key))
            File.Delete(Path.Combine(Root, key)); // dosya yoksa hata vermez
        return Task.CompletedTask;
    }

    [GeneratedRegex("^[a-z]+$")]
    private static partial Regex FolderPattern();

    [GeneratedRegex("^[a-z]+/[0-9a-f]{32}\\.(jpg|png|webp)$")]
    private static partial Regex KeyPattern();
}
