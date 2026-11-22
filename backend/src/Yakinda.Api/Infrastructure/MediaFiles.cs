using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using FluentValidation;
using FluentValidation.Results;
using Yakinda.Application.Media;
using Yakinda.Infrastructure.Media;

namespace Yakinda.Api.Infrastructure;

public static class MediaFiles
{
    /// <summary>
    /// Görselleri /media/{anahtar} adresinden sunar (giriş gerekmez; profil fotoğrafı ve etkinlik görseli herkese açık).
    /// Dosya adları benzersiz ve hiç değişmediği için tarayıcı/CDN bir yıl önbellekte tutabilir.
    /// </summary>
    public static WebApplication UseMediaFiles(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<MediaOptions>>().Value;
        var root = MediaOptions.ResolveRoot(options, app.Environment.ContentRootPath);
        Directory.CreateDirectory(root);

        // Yalnız sunucunun yazdığı görsel türleri sunulur; klasöre başka türde bir dosya girse bile (ör. .html) yanıt 404 olur.
        var imageTypes = new FileExtensionContentTypeProvider(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp"
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(root),
            ContentTypeProvider = imageTypes,
            ServeUnknownFileTypes = false,
            RequestPath = MediaUrl.Prefix.TrimEnd('/'),
            OnPrepareResponse = context =>
            {
                context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                context.Context.Response.Headers.XContentTypeOptions = "nosniff";
            }
        });

        return app;
    }

    /// <summary>
    /// multipart/form-data isteğindeki "file" alanını okur. Biçim ve meta veri kontrolü command doğrulamasında yapılır.
    /// </summary>
    public static async Task<byte[]> ReadImageUploadAsync(this HttpRequest request, CancellationToken cancellationToken)
    {
        // Bu uç noktalarda istek gövdesi 5 MB'lık görsel + form başlıklarıyla sınırlı.
        var sizeLimit = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
            sizeLimit.MaxRequestBodySize = ImageFile.MaxBytes + 256 * 1024;

        if (!request.HasFormContentType)
            throw Invalid("Görseli multipart/form-data isteğinde \"file\" alanıyla gönder.");

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or BadHttpRequestException or IOException)
        {
            throw Invalid("Görsel en fazla 5 MB olabilir.");
        }

        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
            throw Invalid("Görsel seçilmedi.");
        if (file.Length > ImageFile.MaxBytes)
            throw Invalid("Görsel en fazla 5 MB olabilir.");

        using var buffer = new MemoryStream((int)file.Length);
        await using (var stream = file.OpenReadStream())
            await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static ValidationException Invalid(string message) => new([new ValidationFailure("file", message)]);
}
