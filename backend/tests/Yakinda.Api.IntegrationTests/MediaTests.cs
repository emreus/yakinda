using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Yakinda.Application.Media;

namespace Yakinda.Api.IntegrationTests;

/// <summary>Görsel biçim tespiti ve meta veri temizliği (veritabanı gerekmez).</summary>
public sealed class ImageFileTests
{
    /// <summary>
    /// JPEG'deki meta veri temizlenir ama yön bilgisi korunur: konum içeren EXIF bloğu ve yorum bölümü
    /// çıktıda yoktur; yön etiketi (6) yeniden yazılan küçük EXIF'te durur. Boyut doğru okunur, JFIF başlığı
    /// yerindedir ve dosya bitiş işaretiyle (FF D9) sona erer.
    /// </summary>
    [Fact]
    public void Jpeg_metadata_is_removed_but_orientation_is_kept()
    {
        var image = ImageFile.TryRead(TestImages.Jpeg(orientation: 6));

        Assert.NotNull(image);
        Assert.Equal(ImageFormat.Jpeg, image.Format);
        Assert.Equal((640, 480), (image.Width, image.Height));
        Assert.False(TestImages.Contains(image.Data, TestImages.Secret));          // EXIF/GPS
        Assert.False(TestImages.Contains(image.Data, "gizli yorum"));              // COM
        Assert.True(TestImages.Contains(image.Data, [0x01, 0x12, 0x00, 0x03, 0, 0, 0, 1, 0, 6])); // yön: 6
        Assert.True(TestImages.Contains(image.Data, "JFIF"));
        Assert.Equal(new byte[] { 0xFF, 0xD9 }, image.Data[^2..]);
    }

    /// <summary>Yönü zaten düz olan (1) JPEG'e hiç EXIF bloğu yazılmaz.</summary>
    [Fact]
    public void Upright_jpeg_gets_no_exif_at_all()
    {
        var image = ImageFile.TryRead(TestImages.Jpeg(orientation: 1));
        Assert.NotNull(image);
        Assert.False(TestImages.Contains(image.Data, "Exif"));
    }

    /// <summary>
    /// Bitiş işaretinden sonra eklenmiş veri (hareketli fotoğrafın videosu ya da gizlenmiş veri) atılır;
    /// çıktı tam bitiş işaretinde sona erer.
    /// </summary>
    [Fact]
    public void Jpeg_data_after_the_end_of_image_is_dropped()
    {
        // Bazı telefonlar görüntünün sonuna video ("hareketli fotoğraf") ya da ikinci bir görüntü ekler;
        // aynı yer, dosyaya veri gizlemek için de kullanılabilir.
        byte[] withTrailer = [.. TestImages.Jpeg(), .. Encoding.ASCII.GetBytes("ftypmp4-" + TestImages.Secret)];

        var image = ImageFile.TryRead(withTrailer);

        Assert.NotNull(image);
        Assert.False(TestImages.Contains(image.Data, "ftypmp4"));
        Assert.False(TestImages.Contains(image.Data, TestImages.Secret));
        Assert.Equal(new byte[] { 0xFF, 0xD9 }, image.Data[^2..]);
    }

    /// <summary>
    /// İki tarama arasına sıkıştırılmış EXIF ve yorum bölümleri de temizlenir; taramaların kendi verisi
    /// (dolgulu FF ve yeniden başlatma işareti dahil) olduğu gibi kalır.
    /// </summary>
    [Fact]
    public void Jpeg_metadata_between_scans_is_removed_too()
    {
        var image = ImageFile.TryRead(TestImages.Jpeg(orientation: 1, metadataBetweenScans: true));

        Assert.NotNull(image);
        Assert.False(TestImages.Contains(image.Data, TestImages.Secret));
        Assert.False(TestImages.Contains(image.Data, "Exif"));
        // İki taramanın verisi de yerinde
        Assert.True(TestImages.Contains(image.Data, [10, 11, 12, 13]));
        Assert.True(TestImages.Contains(image.Data, [0xFF, 0x00, 0xFF, 0xD0, 7, 7, 7]));
    }

    /// <summary>Bitiş işareti olmayan (sonu kesilmiş) JPEG kabul edilmez.</summary>
    [Fact]
    public void Jpeg_without_an_end_marker_is_rejected()
    {
        var jpeg = TestImages.Jpeg();
        Assert.Null(ImageFile.TryRead(jpeg[..^2]));
    }

    /// <summary>
    /// PNG'deki metin parçası (tEXt) silinir; boyut doğru okunur, görüntü verisi (IDAT) ve bitiş parçası
    /// (IEND) yerinde kalır.
    /// </summary>
    [Fact]
    public void Png_text_chunks_are_removed()
    {
        var image = ImageFile.TryRead(TestImages.Png(300, 200));

        Assert.NotNull(image);
        Assert.Equal((300, 200), (image.Width, image.Height));
        Assert.False(TestImages.Contains(image.Data, TestImages.Secret));
        Assert.True(TestImages.Contains(image.Data, "IDAT"));
        Assert.True(TestImages.Contains(image.Data, "IEND"));
    }

    /// <summary>
    /// WebP'deki EXIF ve XMP parçaları silinir, başlıktaki "EXIF var / XMP var" bayrakları kapatılır ve
    /// RIFF başlığındaki dosya boyutu yeni uzunluğa göre düzeltilir.
    /// </summary>
    [Fact]
    public void WebP_exif_and_xmp_are_removed_and_flags_cleared()
    {
        var image = ImageFile.TryRead(TestImages.WebP(800, 600));

        Assert.NotNull(image);
        Assert.Equal((800, 600), (image.Width, image.Height));
        Assert.False(TestImages.Contains(image.Data, TestImages.Secret));
        Assert.Equal(0, image.Data[20] & 0x0C);                                      // VP8X: EXIF/XMP bayrakları
        Assert.Equal(image.Data.Length - 8, (int)BinaryPrimitives.ReadUInt32LittleEndian(image.Data.AsSpan(4)));
    }

    /// <summary>
    /// Görsel olmayan ya da sınırı aşan girdiler reddedilir: boş veri, HTML metni, tarama başlamadan
    /// kesilmiş JPEG, 9000 x 9000 piksellik PNG ve izin verilen bayt sınırından büyük dosya.
    /// </summary>
    [Fact]
    public void Invalid_or_oversized_images_are_rejected()
    {
        Assert.Null(ImageFile.TryRead(null));
        Assert.Null(ImageFile.TryRead([]));
        Assert.Null(ImageFile.TryRead(Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>")));

        var jpeg = TestImages.Jpeg();
        Assert.Null(ImageFile.TryRead(jpeg[..(jpeg.Length / 3)]));                   // SOS'tan önce kesilmiş
        Assert.Null(ImageFile.TryRead(TestImages.Png(9000, 9000)));                  // açılınca telefonu kilitler
        Assert.Null(ImageFile.TryRead(new byte[ImageFile.MaxBytes + 1]));
    }
}

[Collection(ApiCollection.Name)]
public sealed class MediaTests(ApiFixture api)
{
    /// <summary>
    /// Profil fotoğrafının yaşam döngüsü: yüklenen dosya rastgele adlı bir adresten oturumsuz, doğru türle
    /// ve "değişmez" (immutable) önbellek başlığıyla sunulur; içinde konum verisi yoktur. Yeni fotoğraf yüklenince
    /// eskisi diskten silinir; fotoğraf kaldırılınca profildeki adres boşalır ve dosya 404 döner.
    /// </summary>
    [Fact]
    public async Task Avatar_upload_replace_and_delete()
    {
        var user = await api.RegisterAsync();
        var anonymous = api.Factory.CreateClient();

        var first = await UploadAvatar(user, TestImages.Jpeg());
        Assert.Matches("^/media/avatars/[0-9a-f]{32}\\.jpg$", first);

        var file = await anonymous.GetAsync(first);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("image/jpeg", file.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", file.Headers.CacheControl?.ToString());
        Assert.False(TestImages.Contains(await file.Content.ReadAsByteArrayAsync(), TestImages.Secret));

        Assert.Equal(first, await AvatarOf(user));

        var second = await UploadAvatar(user, TestImages.Png(400, 400));
        Assert.EndsWith(".png", second);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(first)).StatusCode); // eski dosya silindi
        Assert.Equal(second, await AvatarOf(user));

        await (await user.Client.DeleteAsync("/api/users/me/avatar")).ExpectAsync(HttpStatusCode.NoContent);
        Assert.Null(await AvatarOf(user));
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(second)).StatusCode);
    }

    /// <summary>
    /// Görsel olmayan yüklemeler 400 alır: türü image/jpeg diye bildirilmiş metin içerik (validation_failed),
    /// dosyasız form ve JSON gövde.
    /// </summary>
    [Fact]
    public async Task Non_images_are_rejected()
    {
        var user = await api.RegisterAsync();

        var response = await user.Client.PutAsync("/api/users/me/avatar",
            Form(Encoding.UTF8.GetBytes("<svg onload=alert(1)>"), "image/jpeg", "foto.jpg"));
        Assert.Equal("validation_failed", await response.ProblemCodeAsync(HttpStatusCode.BadRequest));

        var noFile = await user.Client.PutAsync("/api/users/me/avatar", new MultipartFormDataContent());
        Assert.Equal(HttpStatusCode.BadRequest, noFile.StatusCode);

        var json = await user.Client.PutAsJsonAsync("/api/users/me/avatar", new { file = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, json.StatusCode);
    }

    /// <summary>
    /// Kapak görselini yalnızca organizatör yükleyebilir (başkası 403 event.not_organizer). Yüklenen adres
    /// etkinlik detayında, organizatörün etkinlik listesinde ve paylaşım sayfasının önizleme etiketinde
    /// görünür; görsel kaldırılınca detaydaki adres boşalır.
    /// </summary>
    [Fact]
    public async Task Only_the_organizer_sets_the_event_image_and_it_shows_everywhere()
    {
        var organizer = await api.RegisterAsync("Kapakçı");
        var other = await api.RegisterAsync();
        var eventId = await organizer.CreateEventAsync("Kapaklı etkinlik");

        Assert.Equal("event.not_organizer", await (await other.Client.PutAsync($"/api/events/{eventId}/image", Form(TestImages.WebP(800, 450), "image/webp", "a.webp")))
            .ProblemCodeAsync(HttpStatusCode.Forbidden));

        var upload = await (await organizer.Client.PutAsync($"/api/events/{eventId}/image", Form(TestImages.WebP(800, 450), "image/webp", "a.webp")))
            .ReadJsonAsync(HttpStatusCode.OK);
        var url = upload["url"]!.GetValue<string>();
        Assert.Matches("^/media/events/[0-9a-f]{32}\\.webp$", url);

        var detail = await (await other.Client.GetAsync($"/api/events/{eventId}")).ReadJsonAsync(HttpStatusCode.OK);
        Assert.Equal(url, detail["imageUrl"]!.GetValue<string>());

        var mine = (await (await organizer.Client.GetAsync("/api/users/me/events?role=organizing")).ReadJsonAsync(HttpStatusCode.OK)).AsArray();
        Assert.Equal(url, mine.Single(e => e!["id"]!.GetValue<Guid>() == eventId)!["imageUrl"]!.GetValue<string>());

        var html = await (await api.Factory.CreateClient().GetAsync($"/e/{eventId}")).Content.ReadAsStringAsync();
        Assert.Contains($"og:image\" content=\"http://localhost{url}\"", html);

        await (await organizer.Client.DeleteAsync($"/api/events/{eventId}/image")).ExpectAsync(HttpStatusCode.NoContent);
        Assert.Null((await (await other.Client.GetAsync($"/api/events/{eventId}")).ReadJsonAsync(HttpStatusCode.OK))["imageUrl"]);
    }

    /// <summary>
    /// Görünen ad ve tanıtım yazısı güncellenebilir; yazının başındaki ve sonundaki boşluklar atılır.
    /// Küfür içeren yazı içerik filtresine takılır ve 400 döner.
    /// </summary>
    [Fact]
    public async Task Profile_can_be_edited_and_is_filtered()
    {
        var user = await api.RegisterAsync("Eski Ad");

        await (await user.Client.PutAsJsonAsync("/api/users/me", new { displayName = "Yeni Ad", bio = "  Okey ve tavla severim.  " }))
            .ExpectAsync(HttpStatusCode.NoContent);
        var me = await (await user.Client.GetAsync("/api/users/me")).ReadJsonAsync(HttpStatusCode.OK);
        Assert.Equal("Yeni Ad", me["displayName"]!.GetValue<string>());
        Assert.Equal("Okey ve tavla severim.", me["bio"]!.GetValue<string>());

        var bad = await user.Client.PutAsJsonAsync("/api/users/me", new { displayName = "Yeni Ad", bio = "amk" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    /// <summary>
    /// Şikayet edilen profil için yönetici "içeriği kaldır" kararı verince fotoğraf ve tanıtım yazısı
    /// profilden silinir, fotoğraf dosyası da artık sunulmaz (404).
    /// </summary>
    [Fact]
    public async Task Moderators_remove_a_reported_profile_photo()
    {
        var target = await api.RegisterAsync("Uygunsuz Foto");
        var reporter = await api.RegisterAsync();
        var avatar = await UploadAvatar(target, TestImages.Jpeg());
        await target.Client.PutAsJsonAsync("/api/users/me", new { displayName = "Uygunsuz Foto", bio = "Kural dışı yazı" });

        await (await reporter.Client.PostAsJsonAsync("/api/reports", new { targetType = "User", targetId = target.Id, reason = "Inappropriate" }))
            .ExpectAsync(HttpStatusCode.NoContent);

        var admin = await api.RegisterAsync("Yönetici");
        await api.ExecuteSqlAsync("UPDATE users SET is_admin = true WHERE id = @id", new { id = admin.Id });
        var report = (await (await admin.Client.GetAsync("/api/admin/reports")).ReadJsonAsync(HttpStatusCode.OK)).AsArray()
            .Single(r => r!["targetId"]!.GetValue<Guid>() == target.Id)!;
        await (await admin.Client.PostAsJsonAsync($"/api/admin/reports/{report["id"]}/resolve", new { action = "RemoveContent" }))
            .ExpectAsync(HttpStatusCode.NoContent);

        var profile = await (await reporter.Client.GetAsync($"/api/users/{target.Id}")).ReadJsonAsync(HttpStatusCode.OK);
        Assert.Null(profile["avatarUrl"]);
        Assert.Null(profile["bio"]);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Factory.CreateClient().GetAsync(avatar)).StatusCode);
    }

    /// <summary>Hesap silinince profil fotoğrafının dosyası da silinir (404).</summary>
    [Fact]
    public async Task Deleting_the_account_deletes_the_photo()
    {
        var user = await api.RegisterAsync();
        var avatar = await UploadAvatar(user, TestImages.Jpeg());

        await (await user.Client.PostAsJsonAsync("/api/users/me/delete", new { password = user.Password })).ExpectAsync(HttpStatusCode.NoContent);

        Assert.Equal(HttpStatusCode.NotFound, (await api.Factory.CreateClient().GetAsync(avatar)).StatusCode);
    }

    // ------------------------------------------------------------------ yardımcılar

    /// <summary>
    /// Verilen baytları "file" alanında, istenen içerik türü ve dosya adıyla taşıyan çok parçalı form üretir.
    /// </summary>
    private static MultipartFormDataContent Form(byte[] data, string contentType, string fileName)
    {
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    /// <summary>
    /// Kullanıcı adına profil fotoğrafı yükler (200 bekler) ve dosyanın adresini döner. Tür hep image/jpeg
    /// diye bildirilir; sunucu gerçek biçimi içerikten anlar.
    /// </summary>
    private static async Task<string> UploadAvatar(TestUser user, byte[] data)
    {
        var json = await (await user.Client.PutAsync("/api/users/me/avatar", Form(data, "image/jpeg", "foto.jpg"))).ReadJsonAsync(HttpStatusCode.OK);
        return json["url"]!.GetValue<string>();
    }

    /// <summary>Kullanıcının kendi profilindeki fotoğraf adresini döner (yoksa null).</summary>
    private static async Task<string?> AvatarOf(TestUser user) =>
        (await (await user.Client.GetAsync("/api/users/me")).ReadJsonAsync(HttpStatusCode.OK))["avatarUrl"]?.GetValue<string>();
}

/// <summary>
/// Elle kurulan küçük görsel dosyaları. Sunucu görüntüyü çözmez (yalnız yapıyı okur), bu yüzden piksel verisinin
/// gerçek olması gerekmez; meta veri parçaları ise gerçek biçimdedir.
/// </summary>
internal static class TestImages
{
    public const string Secret = "GPS-41.0082N-28.9784E";

    /// <summary>Baytların içinde verilen ASCII metin geçiyor mu?</summary>
    public static bool Contains(byte[] data, string text) => Contains(data, Encoding.ASCII.GetBytes(text));

    /// <summary>Baytların içinde verilen bayt dizisi geçiyor mu?</summary>
    public static bool Contains(byte[] data, byte[] part) => data.AsSpan().IndexOf(part) >= 0;

    /// <summary>
    /// Elle kurulmuş küçük bir JPEG: başlangıç işareti, JFIF başlığı, EXIF bloğu (yön etiketi ve "konum"
    /// verisi), yorum bölümü, verilen boyutları taşıyan çerçeve başlığı ve sahte veriyle bir tarama.
    /// metadataBetweenScans true ise araya EXIF ve yorum sıkıştırılmış ikinci bir tarama eklenir.
    /// </summary>
    public static byte[] Jpeg(int width = 640, int height = 480, ushort orientation = 6, bool metadataBetweenScans = false)
    {
        var o = new MemoryStream();
        o.Write([0xFF, 0xD8]);
        o.Write([0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0]);

        // APP1: little-endian TIFF, IFD0 = Orientation + GPS IFD işaretçisi, ardından "konum" verisi
        var tiff = new MemoryStream();
        tiff.Write("II*\0"u8);
        tiff.Write(Le32(8));
        tiff.Write(Le16(2));
        tiff.Write([.. Le16(0x0112), .. Le16(3), .. Le32(1), .. Le16(orientation), 0, 0]);
        tiff.Write([.. Le16(0x8825), .. Le16(4), .. Le32(1), .. Le32(38)]);
        tiff.Write(Le32(0));
        tiff.Write(Encoding.ASCII.GetBytes(Secret));
        Segment(o, 0xE1, [.. "Exif\0\0"u8, .. tiff.ToArray()]);

        Segment(o, 0xFE, Encoding.ASCII.GetBytes("gizli yorum"));
        Segment(o, 0xC0, [8, .. Be16(height), .. Be16(width), 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1]);
        Segment(o, 0xDA, [3, 1, 0, 2, 0x11, 3, 0x11, 0, 0x3F, 0]);
        o.Write(Enumerable.Range(0, 200).Select(i => (byte)(i % 200)).ToArray()); // "sıkıştırılmış" veri (0xFF yok)

        if (metadataBetweenScans)
        {
            // Aşamalı JPEG gibi ikinci bir tarama; araya EXIF ve yorum sıkıştırılmış
            Segment(o, 0xE1, [.. "Exif\0\0"u8, .. Encoding.ASCII.GetBytes(Secret)]);
            Segment(o, 0xFE, Encoding.ASCII.GetBytes(Secret));
            Segment(o, 0xDA, [1, 1, 0, 0, 0x3F, 0]);
            o.Write([0xFF, 0x00, 0xFF, 0xD0, 7, 7, 7]); // dolgulu 0xFF ve RST0: veri sayılır, bölüm başı değil
        }

        o.Write([0xFF, 0xD9]);
        return o.ToArray();
    }

    /// <summary>
    /// Elle kurulmuş küçük bir PNG: imza, boyutları taşıyan IHDR, gizli veriyi taşıyan tEXt, IDAT ve IEND.
    /// </summary>
    public static byte[] Png(int width, int height)
    {
        var o = new MemoryStream();
        o.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(o, "IHDR", [.. Be32(width), .. Be32(height), 8, 2, 0, 0, 0]);
        Chunk(o, "tEXt", Encoding.ASCII.GetBytes("Comment\0" + Secret));
        Chunk(o, "IDAT", [0x78, 0x9C, 1, 2, 3, 4]);
        Chunk(o, "IEND", []);
        return o.ToArray();

        // PNG parçası yazar: uzunluk (4 bayt, büyük uçlu), tür, veri ve sıfırlarla doldurulmuş CRC.
        static void Chunk(Stream s, string type, byte[] data)
        {
            s.Write(Be32(data.Length));
            s.Write(Encoding.ASCII.GetBytes(type));
            s.Write(data);
            s.Write([0, 0, 0, 0]); // CRC (sunucu kontrol etmez)
        }
    }

    /// <summary>
    /// Elle kurulmuş küçük bir WebP: RIFF kabı, EXIF ve XMP bayrakları açık VP8X başlığı, boyutları taşıyan
    /// VP8L başlığı ve gizli veriyi taşıyan EXIF ile XMP parçaları.
    /// </summary>
    public static byte[] WebP(int width, int height)
    {
        var body = new MemoryStream();
        body.Write("WEBP"u8);
        Chunk(body, "VP8X", [0x08 | 0x04, 0, 0, 0, .. Le24(width - 1), .. Le24(height - 1)]);
        var bits = (uint)(width - 1) | ((uint)(height - 1) << 14);
        Chunk(body, "VP8L", [0x2F, .. Le32((int)bits), 0, 0, 0]);
        Chunk(body, "EXIF", Encoding.ASCII.GetBytes(Secret)); // tek uzunluk: dolgu baytı da test edilir
        Chunk(body, "XMP ", Encoding.ASCII.GetBytes("<x:xmpmeta>" + Secret + "</x:xmpmeta>"));

        var o = new MemoryStream();
        o.Write("RIFF"u8);
        o.Write(Le32((int)body.Length));
        o.Write(body.ToArray());
        return o.ToArray();

        // RIFF parçası yazar: dört harfli tür, uzunluk (4 bayt, küçük uçlu), veri; uzunluk tekse bir dolgu baytı.
        static void Chunk(Stream s, string fourCc, byte[] data)
        {
            s.Write(Encoding.ASCII.GetBytes(fourCc));
            s.Write(Le32(data.Length));
            s.Write(data);
            if (data.Length % 2 == 1)
                s.WriteByte(0);
        }
    }

    /// <summary>
    /// JPEG bölümü yazar: FF ve işaret baytı, iki baytlık uzunluk (uzunluk alanı dahil), ardından içerik.
    /// </summary>
    private static void Segment(Stream s, byte marker, byte[] payload)
    {
        s.Write([0xFF, marker]);
        s.Write(Be16(payload.Length + 2));
        s.Write(payload);
    }

    /// <summary>Sayıyı 2 bayta çevirir, küçük uçlu (düşük bayt önce).</summary>
    private static byte[] Le16(int v) => [(byte)v, (byte)(v >> 8)];
    /// <summary>Sayıyı 3 bayta çevirir, küçük uçlu.</summary>
    private static byte[] Le24(int v) => [(byte)v, (byte)(v >> 8), (byte)(v >> 16)];
    /// <summary>Sayıyı 4 bayta çevirir, küçük uçlu.</summary>
    private static byte[] Le32(int v) => [(byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24)];
    /// <summary>Sayıyı 2 bayta çevirir, büyük uçlu (yüksek bayt önce).</summary>
    private static byte[] Be16(int v) => [(byte)(v >> 8), (byte)v];
    /// <summary>Sayıyı 4 bayta çevirir, büyük uçlu.</summary>
    private static byte[] Be32(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
}
