using System.Globalization;
using System.Net;
using System.Text.Json;
using MediatR;
using Yakinda.Application.Events.GetPublic;

namespace Yakinda.Api.Endpoints;

/// <summary>
/// Paylaşım bağlantısı: https://&lt;api&gt;/e/{id}
///  * WhatsApp gibi uygulamalarda tıklanabilir (özel şema yakinda:// tıklanamaz) ve önizleme kartı çıkar (Open Graph).
///  * Sayfadaki buton uygulamayı yakinda://event/{id} ile açar.
///  * Universal Links / App Links ayarlıysa (App:AppleAppId, App:AndroidPackage) telefon bağlantıyı doğrudan uygulamada açar;
///    gereken doğrulama dosyaları /.well-known altında sunulur.
/// </summary>
public static class ShareEndpoints
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static IEndpointRouteBuilder MapSharePages(this IEndpointRouteBuilder app)
    {
        app.MapGet("/e/{id:guid}", async (Guid id, HttpRequest request, ISender sender, IConfiguration config, CancellationToken ct) =>
            {
                var ev = await sender.Send(new GetPublicEventQuery(id), ct);
                // Önizleme kartındaki görsel tam adres olmalı (WhatsApp/Telegram göreli adresi açmaz).
                var baseUrl = $"{request.Scheme}://{request.Host}{request.PathBase}";
                return Results.Content(ev is null ? NotFoundPage() : EventPage(ev, config, baseUrl), "text/html; charset=utf-8");
            })
            .AllowAnonymous()
            .ExcludeFromDescription();

        // iOS Universal Links
        app.MapGet("/.well-known/apple-app-site-association", (IConfiguration config) =>
            {
                var appId = config["App:AppleAppId"]; // TEAMID.com.emreus.yakinda
                if (string.IsNullOrWhiteSpace(appId))
                    return Results.NotFound();

                var body = new { applinks = new { details = new[] { new { appIDs = new[] { appId }, components = new[] { new Dictionary<string, string> { ["/"] = "/e/*" } } } } } };
                return Results.Json(body, contentType: "application/json");
            })
            .AllowAnonymous()
            .ExcludeFromDescription();

        // Android App Links
        app.MapGet("/.well-known/assetlinks.json", (IConfiguration config) =>
            {
                var package = config["App:AndroidPackage"];
                // Ortam değişkeni boş bırakılınca dizi [""] olur; boşlar atılır.
                var fingerprints = (config.GetSection("App:AndroidSha256Fingerprints").Get<string[]>() ?? [])
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .ToArray();
                if (string.IsNullOrWhiteSpace(package) || fingerprints.Length == 0)
                    return Results.NotFound();

                var body = new[]
                {
                    new
                    {
                        relation = new[] { "delegate_permission/common.handle_all_urls" },
                        target = new { @namespace = "android_app", package_name = package, sha256_cert_fingerprints = fingerprints }
                    }
                };
                return Results.Json(body, new JsonSerializerOptions());
            })
            .AllowAnonymous()
            .ExcludeFromDescription();

        return app;
    }

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static TimeZoneInfo TimeZone(IConfiguration config)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(config["App:TimeZone"] ?? "Europe/Istanbul");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("TR", TimeSpan.FromHours(3), "Türkiye", "Türkiye");
        }
    }

    private static string EventPage(PublicEventDto ev, IConfiguration config, string baseUrl)
    {
        var image = ev.ImageUrl is null ? null : baseUrl + ev.ImageUrl;
        var zone = TimeZone(config);
        var starts = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(ev.StartsAt, DateTimeKind.Utc), zone);
        var when = starts.ToString("d MMMM yyyy dddd, HH:mm", Turkish);
        var where = string.IsNullOrWhiteSpace(ev.PlaceName) ? "" : $" · {ev.PlaceName}";
        var seats = ev.Capacity is null
            ? $"{ev.ParticipantCount} katılımcı"
            : ev.ParticipantCount >= ev.Capacity
                ? "Kontenjan dolu, bekleme listesine girilebilir"
                : $"{ev.Capacity - ev.ParticipantCount} kişilik yer var";
        var description = $"{when}{where} · {ev.CategoryName} · Organizatör: {ev.OrganizerName}";
        var deepLink = $"yakinda://event/{ev.Id}";
        var iosStore = config["App:IosStoreUrl"];
        var androidStore = config["App:AndroidStoreUrl"];

        return $$"""
            <!DOCTYPE html>
            <html lang="tr"><head>
            <meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{Encode(ev.Title)}} · Yakında</title>
            <meta property="og:type" content="website">
            <meta property="og:title" content="{{Encode(ev.CategoryEmoji + " " + ev.Title)}}">
            <meta property="og:description" content="{{Encode(description)}}">
            {{(image is null ? "" : $"<meta property=\"og:image\" content=\"{Encode(image)}\">")}}
            <meta name="description" content="{{Encode(description)}}">
            <style>
              body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;margin:0;background:#f6f5fb;color:#1f1d2b}
              main{max-width:480px;margin:0 auto;padding:40px 20px}
              .card{background:#fff;border-radius:20px;padding:28px;box-shadow:0 6px 24px rgba(31,29,43,.08)}
              .cover{width:100%;aspect-ratio:16/9;object-fit:cover;border-radius:14px;margin-bottom:12px}
              .emoji{font-size:48px} h1{font-size:1.5rem;margin:12px 0 8px} p{margin:6px 0;color:#4b4860}
              .btn{display:block;text-align:center;background:#5b3fd9;color:#fff;text-decoration:none;font-weight:600;padding:14px;border-radius:12px;margin-top:24px}
              .stores{display:flex;gap:12px;justify-content:center;margin-top:16px} .stores a{color:#5b3fd9}
              small{display:block;text-align:center;color:#8a879c;margin-top:20px}
            </style>
            </head><body><main><div class="card">
              {{(image is null ? "" : $"<img class=\"cover\" src=\"{Encode(image)}\" alt=\"\">")}}
              <div class="emoji">{{Encode(ev.CategoryEmoji)}}</div>
              <h1>{{Encode(ev.Title)}}</h1>
              <p>📅 {{Encode(when)}}</p>
              {{(string.IsNullOrWhiteSpace(ev.PlaceName) ? "" : $"<p>📍 {Encode(ev.PlaceName)}</p>")}}
              <p>👥 {{Encode(seats)}}</p>
              <p>🙋 Organizatör: {{Encode(ev.OrganizerName)}}</p>
              <a class="btn" href="{{deepLink}}">Yakında uygulamasında aç</a>
              <div class="stores">
                {{(string.IsNullOrWhiteSpace(iosStore) ? "" : $"<a href=\"{Encode(iosStore)}\">App Store</a>")}}
                {{(string.IsNullOrWhiteSpace(androidStore) ? "" : $"<a href=\"{Encode(androidStore)}\">Google Play</a>")}}
              </div>
            </div>
            <small>Katılmak için uygulamada giriş yapman gerekir.</small>
            </main></body></html>
            """;
    }

    private static string NotFoundPage() => """
        <!DOCTYPE html><html lang="tr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Etkinlik bulunamadı · Yakında</title>
        <style>body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;text-align:center;padding:64px 20px;color:#1f1d2b}</style>
        </head><body><div style="font-size:48px">🤷</div><h1>Etkinlik bulunamadı</h1>
        <p>Bu etkinlik iptal edilmiş ya da kaldırılmış olabilir.</p></body></html>
        """;
}
