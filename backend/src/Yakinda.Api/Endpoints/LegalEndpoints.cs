using System.Net;
using System.Text;

namespace Yakinda.Api.Endpoints;

/// <summary>
/// Mağaza başvurusu için herkese açık sayfalar:
///   /legal/privacy          gizlilik politikası (App Store ve Google Play zorunlu)
///   /legal/terms            kullanım koşulları ve topluluk kuralları (App Store 1.2: kullanıcı içeriği)
///   /legal/delete-account   hesap silme talebi (Google Play: uygulama dışından erişilebilir bağlantı)
/// İçerik bir başlangıç taslağıdır; yayınlamadan önce bir hukukçuya inceletin (KVKK aydınlatma metni dahil).
/// Uygulama adı, işletmeci ve iletişim adresi "Legal" ayarlarından gelir.
/// </summary>
public static class LegalEndpoints
{
    public static IEndpointRouteBuilder MapLegalPages(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/legal").AllowAnonymous().ExcludeFromDescription();

        group.MapGet("/privacy", (IConfiguration config) => Page(config, "Gizlilik Politikası", Privacy));
        group.MapGet("/terms", (IConfiguration config) => Page(config, "Kullanım Koşulları ve Topluluk Kuralları", Terms));
        group.MapGet("/delete-account", (IConfiguration config) => Page(config, "Hesap Silme", DeleteAccount));

        return app;
    }

    private sealed record LegalInfo(string AppName, string Operator, string Email, string LastUpdated);

    private static IResult Page(IConfiguration config, string title, Func<LegalInfo, string> body)
    {
        var section = config.GetSection("Legal");
        var info = new LegalInfo(
            Encode(section["AppName"] ?? "Yakında"),
            Encode(section["OperatorName"] ?? "Yakında uygulaması"),
            Encode(section["ContactEmail"] ?? "destek@ornek.com"),
            Encode(section["LastUpdated"] ?? ""));

        var html = new StringBuilder()
            .Append("<!DOCTYPE html><html lang=\"tr\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append("<title>").Append(Encode(title)).Append(" · ").Append(info.AppName).Append("</title>")
            .Append("<style>")
            .Append("body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;max-width:720px;margin:0 auto;padding:24px 20px 64px;line-height:1.6;color:#1f1d2b}")
            .Append("h1{font-size:1.6rem;margin-bottom:4px}h2{font-size:1.15rem;margin-top:28px}")
            .Append("p.meta{color:#6b6880;margin-top:0}a{color:#5b3fd9}li{margin:4px 0}")
            .Append("</style></head><body>")
            .Append("<h1>").Append(Encode(title)).Append("</h1>")
            .Append("<p class=\"meta\">").Append(info.AppName)
            .Append(info.LastUpdated.Length > 0 ? " · Son güncelleme: " + info.LastUpdated : "")
            .Append("</p>")
            .Append(body(info))
            .Append("</body></html>");

        return Results.Content(html.ToString(), "text/html; charset=utf-8");
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Mail(LegalInfo i) => $"<a href=\"mailto:{i.Email}\">{i.Email}</a>";

    private static string Privacy(LegalInfo i) => $"""
        <p>Bu metin, {i.AppName} uygulamasını kullanırken hangi kişisel verilerin neden işlendiğini ve
        haklarını açıklar. Veri sorumlusu: {i.Operator}. İletişim: {Mail(i)}.</p>

        <h2>Hangi verileri işliyoruz?</h2>
        <ul>
          <li><b>Hesap bilgileri:</b> e-posta adresi, görünen ad, şifre (yalnızca geri döndürülemez özet olarak saklanır).</li>
          <li><b>Oluşturduğun içerik:</b> etkinlikler (başlık, açıklama, zaman, konum, mekan, görsel), profil fotoğrafın
              ve "hakkımda" yazın, katılımların, verdiğin puanlar ve yorumlar, etkinlik sohbetlerindeki mesajların.
              Yüklediğin fotoğraflardaki konum (GPS) ve cihaz bilgileri kaydedilmeden önce silinir.</li>
          <li><b>Konum:</b> yakındaki etkinlikleri göstermek için cihazının konumu, sen izin verdiğinde ve yalnızca
              arama sırasında kullanılır; konum geçmişin saklanmaz. Etkinlik oluştururken seçtiğin yer etkinlikle birlikte saklanır.</li>
          <li><b>Bildirimler:</b> bildirim göndermek için cihazına ait bildirim anahtarı (push token).</li>
          <li><b>Güvenlik kayıtları:</b> şikayetler, engellemeler, kötüye kullanımı önlemek için sınırlı süre tutulan teknik kayıtlar (IP adresi, hata günlükleri).</li>
        </ul>

        <h2>Neden işliyoruz?</h2>
        <ul>
          <li>Hizmeti sunmak: etkinlik oluşturma, yakındaki etkinlikleri gösterme, katılım ve puanlama.</li>
          <li>Sana bildirim göndermek (katılım istekleri, onaylar, hatırlatmalar).</li>
          <li>Topluluğu korumak: şikayetleri incelemek, kurallara aykırı içeriği kaldırmak, kötüye kullanımı engellemek.</li>
          <li>Yasal yükümlülükleri yerine getirmek.</li>
        </ul>

        <h2>Kimler görebilir, kimlerle paylaşılır?</h2>
        <p>Görünen adın, düzenlediğin etkinlikler, organizatör puanın ve yorumların diğer kullanıcılara görünür.
        E-posta adresin diğer kullanıcılarla paylaşılmaz. Verilerin, hizmeti çalıştırmak için kullandığımız sağlayıcılarla
        (sunucu barındırma, e-posta gönderimi, Expo ve Apple/Google bildirim servisleri) yalnızca bu amaçla paylaşılır.
        Verilerini satmayız ve reklam amacıyla kullanmayız. Yasal bir zorunluluk olduğunda yetkili makamlarla paylaşabiliriz.</p>

        <h2>Ne kadar süre saklıyoruz?</h2>
        <p>Hesabın açık olduğu sürece. Hesabını sildiğinde e-posta adresin, adın, profil fotoğrafın, şifren, oturumların,
        cihaz anahtarların, bildirimlerin, yorum metinlerin, sohbet mesajlarının metni ve düzenlediğin etkinliklerin
        açıklaması, adresi ve görseli silinir. Diğer kullanıcıların puan ortalamaları ve etkinlik geçmişleri bozulmasın diye
        geçmiş etkinliklerin başlığı, zamanı, haritadaki yeri ve oy <i>sayıları</i> "Silinmiş kullanıcı" adıyla kalır.</p>

        <h2>Hakların</h2>
        <p>6698 sayılı Kişisel Verilerin Korunması Kanunu'nun 11. maddesi kapsamında verilerinin işlenip işlenmediğini
        öğrenme, bilgi isteme, düzeltilmesini ya da silinmesini isteme ve işlemeye itiraz etme haklarına sahipsin.
        Başvurularını {Mail(i)} adresine gönderebilirsin. Hesabını uygulama içinden de silebilirsin
        (<a href="/legal/delete-account">nasıl?</a>).</p>

        <h2>Yaş sınırı</h2>
        <p>{i.AppName} yabancılarla buluşmayı içerdiği için 18 yaşından küçükler tarafından kullanılamaz.</p>

        <h2>Değişiklikler</h2>
        <p>Bu metni güncellediğimizde yukarıdaki tarihi değiştiririz; önemli değişiklikleri uygulama içinde duyururuz.</p>
        """;

    private static string Terms(LegalInfo i) => $"""
        <p>{i.AppName}'ı kullanarak bu koşulları kabul etmiş olursun. Sorular için: {Mail(i)}.</p>

        <h2>Hesabın</h2>
        <ul>
          <li>18 yaşından büyük olmalısın.</li>
          <li>Hesap bilgilerin doğru olmalı; hesabının güvenliğinden sen sorumlusun.</li>
          <li>Hesabını istediğin zaman uygulama içinden silebilirsin.</li>
        </ul>

        <h2>Topluluk kuralları</h2>
        <p>Uygunsuz içeriğe ve kötü niyetli kullanıcılara <b>sıfır tolerans</b> gösteriyoruz. Şunlar yasaktır:</p>
        <ul>
          <li>Hakaret, taciz, tehdit, zorbalık ve nefret söylemi.</li>
          <li>Müstehcen, şiddet içeren ya da yasa dışı içerik ve etkinlikler.</li>
          <li>Sahte etkinlikler, dolandırıcılık, spam ve reklam.</li>
          <li>Başkasının kişisel bilgilerini izinsiz paylaşmak.</li>
          <li>Puanlama sistemini manipüle etmek.</li>
        </ul>

        <h2>Şikayet, engelleme ve moderasyon</h2>
        <p>Kurallara aykırı bir etkinliği, mesajı ya da kullanıcıyı uygulama içinden şikayet edebilir, bir kullanıcıyı
        engelleyebilirsin; engellediğin kişiyle birbirinizin etkinliklerini görmez ve etkileşime giremezsiniz.
        Şikayetleri en geç 24 saat içinde inceleriz. Kurallara aykırı içeriği kaldırır, ihlal eden hesapları kapatırız.</p>

        <h2>Buluşma güvenliği</h2>
        <p>{i.AppName} kullanıcıları bir araya getiren bir platformdur; etkinlikleri kullanıcılar düzenler.
        İlk buluşmaları kalabalık ve herkese açık yerlerde yap, yakınlarına nerede olacağını söyle, kendini güvende
        hissetmediğin durumdan ayrıl. Acil durumda 112'yi ara. Etkinliklerde yaşanan olaylardan etkinliği düzenleyen ve
        katılan kullanıcılar sorumludur.</p>

        <h2>Hizmet</h2>
        <p>Hizmeti olduğu gibi sunarız; kesintisiz ya da hatasız olacağını garanti etmeyiz. Bu koşulları
        güncelleyebiliriz; güncel hali her zaman bu sayfadadır.</p>
        """;

    private static string DeleteAccount(LegalInfo i) => $"""
        <h2>Uygulama içinden</h2>
        <ol>
          <li>{i.AppName} uygulamasını aç ve <b>Profil</b> sekmesine git.</li>
          <li><b>Hesabımı sil</b>'e dokun, şifreni gir ve onayla.</li>
        </ol>
        <p>Hesabın hemen silinir.</p>

        <h2>Uygulamaya erişemiyorsan</h2>
        <p>Hesabına kayıtlı e-posta adresinden {Mail(i)} adresine <b>"Hesap silme"</b> konulu bir e-posta gönder.
        Talebini doğrulayıp en geç 30 gün içinde hesabını sileriz.</p>

        <h2>Neler silinir?</h2>
        <ul>
          <li>E-posta adresin, görünen adın, profil fotoğrafın, şifren, oturumların ve cihaz bildirim anahtarların.</li>
          <li>Bildirimlerin, engellemelerin, yazdığın yorum metinleri ve sohbet mesajlarının metni.</li>
          <li>Düzenlediğin etkinliklerin açıklaması, adresi ve görseli.</li>
          <li>Yaklaşan etkinliklerdeki katılımın; düzenlediğin yaklaşan etkinlikler iptal edilir ve katılımcılara haber verilir.</li>
        </ul>

        <h2>Neler kalır?</h2>
        <p>Diğer kullanıcıların puan ortalamaları ve etkinlik geçmişleri bozulmasın diye geçmiş etkinliklerin başlığı,
        zamanı, haritadaki yeri ve verdiğin oyların sayıları "Silinmiş kullanıcı" adıyla kalır.</p>
        """;
}
