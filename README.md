# Yakında 📍

Kullanıcının yakınındaki etkinlikleri gösteren, etkinlik oluşturmayı ve katılımcıların etkinlikleri 5 üzerinden puanlamasını sağlayan mobil uygulama.
"Okeye 4. aranıyor"dan 200 kişilik partiye, "sinemaya gidelim"den halı saha maçına kadar her ölçekte etkinlik.

| Katman | Teknoloji |
|---|---|
| API | .NET 10, Minimal API, **CQRS + MediatR**, FluentValidation, JWT + refresh token |
| Veri | **Dapper** + Npgsql, PostgreSQL 17 + **PostGIS** (konum sorguları) |
| Cache | **Redis 8** + FusionCache (bellek + Redis, instance'lar arası senkronizasyon) |
| Mobil | **React Native (Expo SDK 58)**, Expo Router, TanStack Query, react-native-maps |

---

## Özellikler

- **Yakınımda**: konuma göre yarıçap (1–50 km) içindeki etkinlikler; kategori, "sadece yeri olanlar", yakınlık/zaman sıralaması, sonsuz kaydırma
- **Harita**: etkinlikler pin olarak, "bu bölgede ara"
- **Etkinlik oluşturma**: hızlı şablonlar (🃏 Okeye 4., 🎬 Sinema, ⚽ Halı saha, 🎉 Parti…), haritadan konum seçme, otomatik adres, kontenjan (ya da sınırsız), anında katılım ya da organizatör onayı
- **Katılım**: katıl / istek gönder / ayrıl; organizatör onaylar-reddeder; kontenjan yarışına karşı satır kilidi
- **Puanlama**: sadece onaylı katılımcılar, 1–5 yıldız + yorum, sonradan güncellenebilir
- **Organizatör puanı**: kullanıcının düzenlediği etkinliklere verilen *her oy* ortalamasını etkiler; profilde ortalama + yıldız dağılımı
- **Bekleme listesi**: kontenjan doluysa katılmak isteyen sıraya girer; biri ayrılınca, çıkarılınca ya da kontenjan artınca sıradaki otomatik alınır (onaylı etkinlikte isteğe dönüşür) ve bildirim alır
- **Etkinlik düzenleme**: organizatör başlamadan önce saat, yer, kontenjan vb. değiştirebilir; saat ya da yer değişirse katılımcılara bildirim gider, hatırlatma yeni saate göre kurulur
- **Tekrarlayan etkinlik**: "her salı okey" için 4, 8 ya da 12 hafta boyunca ayrı etkinlikler
- **Arama ve sıralama**: başlık/açıklama/mekan araması; yakınlık, zaman ya da organizatör puanına göre (Bayesian ortalama: tek oyla 5,0 alan, 200 oyla 4,8 alanın önüne geçmez)
- **Profil fotoğrafı ve etkinlik kapak görseli**: galeriden ya da kameradan; telefonda kırpılıp küçültülür, sunucuda konum (GPS) gibi meta veriler silinir. Profil adı ve "hakkımda" düzenlenebilir
- **Etkinlik sohbeti**: organizatör ve onaylı katılımcılar arasında ("taşları kim getiriyor?"); okunana kadar etkinlik başına tek bildirim, uzun basınca sil/şikayet et
- **Paylaşım bağlantısı**: `https://<api>/e/{id}` (WhatsApp'ta önizlemeli kart, "uygulamada aç" butonu)
- **Bildirimler**: katılım isteği / yeni katılımcı, onay / ret, iptal, başlamadan 1 saat önce hatırlatma, bitince "puanla"; uygulama içi liste + push

---

## Mimari

```
 Mobil (Expo)  ──HTTP/JSON──►  Yakinda.Api  (Minimal API endpoint'leri)
                                    │  ISender.Send(...)
                                    ▼
                    ┌──────── MediatR pipeline ────────┐
                    │ LoggingBehavior                  │
                    │ ValidationBehavior (FluentVal.)  │
                    │ TransactionBehavior (ICommand)   │
                    └──────────────┬───────────────────┘
                 ┌─────────────────┴─────────────────┐
           COMMAND (yazma)                       QUERY (okuma)
   Handler → Domain nesnesi (kurallar)     Handler → Dapper ile SQL
           → Repository (Dapper)                  → doğrudan DTO
           → tek transaction                      (join'li, optimize)
                 └─────────────────┬─────────────────┘
                                   ▼
                        PostgreSQL + PostGIS
```

**Neden bu ayrım?** Yazma tarafında iş kuralları `Domain` nesnelerinde (ör. `Event.Admit`, `Event.EnsureCanBeRatedBy`) toplanır ve
her command tek bir transaction'da çalışır. Okuma tarafında ise ekranın ihtiyacı olan veri tek bir SQL ile, join'lerle doğrudan DTO'ya okunur.
Repository/ORM katmanı okuma performansını etkilemez.

### Proje yapısı

```
backend/
  Yakinda.slnx
  Directory.Packages.props          ← tüm NuGet sürümleri burada
  src/
    Yakinda.Domain/                 ← saf C#, paket bağımlılığı yok
      Events/  Event, EventParticipant, EventRating, EventRules, Enums
      Users/   User
    Yakinda.Application/            ← CQRS: her özellik tek dosyada (Command + Validator + Handler)
      Abstractions/  ICommand/IQuery, repository arayüzleri, ICurrentUser…
      Behaviors/     Logging, Validation, Transaction
      Auth/          Register, Login, RefreshToken
      Events/        Create, Cancel, Join, Leave, Participants(Review), Rate,
                     GetNearby, GetDetail, GetParticipants, GetRatings
      Users/         GetProfile, GetMyEvents
      Categories/    GetCategories
    Yakinda.Infrastructure/         ← Dapper repository'leri, DbSession (UoW), JWT, PBKDF2, migration runner
      Persistence/Migrations/*.sql  ← gömülü SQL migration'lar
    Yakinda.Api/                    ← Program.cs, endpoint'ler, hata yönetimi (ProblemDetails)
mobile/
  app/                              ← Expo Router ekranları (dosya tabanlı yönlendirme)
    (auth)/login, register
    (tabs)/index (Yakınımda), map, create, profile
    event/[id], user/[id]
  src/
    api/        client (token yenileme), endpoints, types
    auth/       AuthContext (refresh token → SecureStore)
    components/ EventCard, StarRating, CategoryChips, DateTimeField, ui…
    hooks/      TanStack Query hook'ları, useCurrentLocation
docker-compose.yml                  ← PostgreSQL + PostGIS ve Redis
.github/workflows/ci.yml            ← her push/PR'da backend derlemesi + mobil tip kontrolü
```

---

## Kurulum

### Gereksinimler
- .NET 10 SDK
- Docker (veritabanı için)
- Node.js 22.13+ ya da 24.3+ (Expo SDK 58 tek sayılı Node sürümlerini desteklemiyor)
- Telefonda **Expo Go (SDK 58)** ya da bir development build

```bash
git clone https://github.com/emreus/yakinda.git
cd yakinda
```

### 1) Veritabanı ve Redis
```bash
docker compose up -d          # PostgreSQL + PostGIS (5432) ve Redis (6379)
```
Redis çalışmıyorsa API yine açılır: cache sadece bellekte tutulur ve log'a uyarı yazılır.
Redis'i tamamen kapatmak için `ConnectionStrings:Redis` değerini boş bırakın.

### 2) API
```bash
cd backend
dotnet run --project src/Yakinda.Api
```
Development ortamında migration'lar açılışta otomatik çalışır (`Database:MigrateOnStartup`).
API `http://0.0.0.0:5080` adresinde dinler; OpenAPI dokümanı: `http://localhost:5080/openapi/v1.json`.
Örnek istekler için `src/Yakinda.Api/Yakinda.Api.http` dosyasına bakın.

### 3) Mobil
```bash
cd mobile
npm install
npm run setup                 # paket sürümlerini Expo SDK 58'e göre hizalar (değişiklik yoksa bir şey yapmaz)
npx expo start                # a: Android emülatör, i: iOS simülatör, QR: telefondaki Expo Go
```
> API adresi için ayar gerekmez: uygulama, Expo'nun çalıştığı bilgisayarın adresini kullanır
> (`http://<bilgisayarın IP'si>:5080`). Farklı bir sunucu için `.env.example` dosyasını `.env` olarak
> kopyalayıp `EXPO_PUBLIC_API_URL` yazın. macOS "dotnet gelen bağlantıları kabul etsin mi?" diye
> sorarsa izin verin.

---

## İş kuralları

| Kural | Nerede |
|---|---|
| `capacity` = organizatör **hariç** katılabilecek kişi sayısı (`null` = sınırsız). "Okeye 4. aranıyor" → `capacity = 1` | `Event` |
| Katılım değişiklikleri (katıl, ayrıl, onayla, reddet, iptal) sadece etkinlik başlamadan önce yapılabilir | `Event.EnsureParticipationCanChange` |
| Son boş yere aynı anda iki kişi katılmaya çalışırsa: `SELECT … FOR UPDATE` ile biri bekler, sonra "Kontenjan dolu" alır. Ayrıca DB'de `CHECK (participant_count <= capacity)` var | `JoinEventCommand` |
| Oy verebilmek için **onaylı katılımcı** olmak gerekir; organizatör kendi etkinliğini oylayamaz | `Event.EnsureCanBeRatedBy` |
| Oylama etkinlik **başladığında** açılır, **bitişten 14 gün sonra** kapanır (bitiş yoksa başlangıç + 3 saat) | `EventRules` |
| Kullanıcı oyunu güncelleyebilir; toplama sadece fark (yeni − eski) yansır | `Event.ApplyRating` → `RatingDelta` |
| Organizatör puanı = düzenlediği tüm etkinliklere verilen **tüm oyların** ortalaması (etkinlik ortalamalarının ortalaması değil) | `users.rating_sum / rating_count` |

Puanlar `users` ve `events` tablolarında `rating_sum`/`rating_count` olarak **denormalize** tutulur ve oyla **aynı transaction**'da güncellenir.
Böylece listelerde organizatör puanı için ek sorgu ya da `AVG()` gerekmez.

---

## Cache (Redis)

Cache iki seviyelidir: her API instance'ının belleği (L1) ve Redis (L2). Bir instance bir kaydı geçersiz kıldığında
Redis Pub/Sub (backplane) diğer instance'ların belleğine de haber verir; API'yi birden fazla kopya çalıştırınca eski veri görülmez.
Kütüphane olarak [FusionCache](https://github.com/ZiggyCreatures/FusionCache) kullanılır; Application katmanı sadece
`ICacheService` arayüzünü bilir.

**Ne cache'leniyor**

| Veri | Anahtar | Süre | Ne zaman temizlenir (etiket) |
|---|---|---|---|
| Kategoriler | `categories:v1` | 1 saat | Kategori migration'ı eklenince anahtardaki sürümü artırın |
| Kullanıcı profili + organizatör puanı | `user:{id}:profile` | 10 dk | `user:{id}`: etkinliğine oy gelince, etkinlik oluşturup iptal edince, bir etkinliğe katılıp ayrılınca |
| Etkinlik detayının herkes için aynı kısmı | `event:{id}:snapshot` | 5 dk | `event:{id}`: katılım, onay/red, oy, iptal |
| Etkinlik yorumları (sayfa sayfa) | `event:{id}:ratings:{sayfa}:{boyut}` | 10 dk | `event:{id}` |

Etkinlik detayında kullanıcıya özel alanlar (katılım durumum, puanım, bekleyen istekler) cache'lenmez, her istekte taze okunur.

**Ne cache'lenmiyor ve neden**
- *Yakınımdaki etkinlikler:* her kullanıcının konumu ve filtresi farklı, kontenjanlar sık değişiyor; isabet oranı düşük olur.
  Mobil uygulama aynı listeyi zaten 30 saniye kendi belleğinde tutuyor.
- *Etkinliklerim, katılımcı listesi:* kişiye özel ve sık değişiyor.

**Nasıl çalışır:** `ICachedQuery` işaretli sorguları `QueryCachingBehavior` cache'ten döner.
Command handler'lar `ICacheInvalidator.InvalidateAfterCommit(...)` ile etkiledikleri etiketleri bildirir;
`CacheInvalidationBehavior` bu etiketleri transaction **commit edildikten sonra** temizler.
Veritabanı geçici olarak hata verirse son değer en fazla 2 saat sunulmaya devam eder (fail-safe);
Redis 1 saniyeden fazla gecikirse istek beklemez.

---

## Güvenlik ve mağaza zorunlulukları

App Store (1.2 kullanıcı içeriği, 5.1.1(v) hesap silme) ve Google Play (hesap silme) kuralları için:

| Özellik | Nerede |
|---|---|
| Kayıtta kullanım koşulları onayı (18+, sıfır tolerans) | Kayıt ekranı; `acceptTerms` zorunlu |
| İçerik filtresi | Etkinlik başlığı/açıklaması/mekan, görünen ad, yorum. Liste: `backend/src/Yakinda.Application/Moderation/blocked-words.txt` |
| Şikayet | Etkinlik detayı ve kullanıcı profilinde "🚩 Şikayet et" → `POST /api/reports` |
| Engelleme | "🚫 Engelle"; iki yönlü: birbirinin etkinliklerini görmez, katılamaz. Profil → Engellenen kullanıcılar |
| Moderasyon | `GET /api/admin/reports`, `POST /api/admin/reports/{id}/resolve` (Dismiss / RemoveContent / BanUser) |
| Hesap silme | Profil → Hesabımı sil (şifreyle); web: `/legal/delete-account` |
| Gizlilik politikası, koşullar | `/legal/privacy`, `/legal/terms` (mağaza formlarına bu adresler girilir) |
| İletişim | Profil → Destek (`app.json` → `extra.supportEmail`) |

**Yönetici olmak için** (şimdilik yönetim ekranı yok, `Yakinda.Api.http` içindeki isteklerle):
```sql
UPDATE users SET is_admin = true WHERE email = 'senin@epostan.com';
```
Apple şikayetlere 24 saat içinde yanıt verilmesini bekliyor; açık şikayetleri düzenli kontrol et.

**Yayınlamadan önce:** `appsettings.json` → `Legal` (işletmeci adı, iletişim e-postası) ve `mobile/app.json` →
`extra.supportEmail` değerlerini kendi bilgilerinle değiştir. `/legal/*` metinleri bir başlangıç taslağıdır;
KVKK aydınlatma metni dahil bir hukukçuya inceletmeni öneririm.

**Hesap silinince** e-posta, ad, şifre, oturumlar, cihaz token'ları, bildirimler, engellemeler ve yorum metinleri silinir;
yaklaşan katılımlar boşalır, düzenlenen yaklaşan etkinlikler iptal edilir. Satır "Silinmiş kullanıcı" olarak kalır ki
geçmiş etkinlikler ve diğer kullanıcıların puan ortalamaları bozulmasın.

---

## Şifre sıfırlama ve e-posta

Giriş ekranı → **Şifremi unuttum** → e-postaya 6 haneli kod → kod + yeni şifre → kullanıcı giriş yapmış olur, diğer
cihazlardaki oturumlar kapanır. Kod 15 dakika geçerli, en fazla 5 yanlış deneme, aynı hesaba dakikada bir kod.
Kayıtlı olmayan e-posta için de aynı yanıt döner (hangi adreslerin kayıtlı olduğu anlaşılmasın).

**Geliştirmede** e-postalar gerçekten gönderilmez: `docker compose up -d` ile gelen **Mailpit** hepsini yakalar,
<http://localhost:8025> adresinde görürsün (`appsettings.Development.json` → `Email:Smtp` zaten buna ayarlı).
SMTP ayarı hiç yoksa e-posta içeriği backend konsoluna yazılır.

**Canlıda** herhangi bir SMTP sağlayıcısı (Resend, Brevo, Amazon SES, Mailgun…) kullanılabilir:

```bash
Email__From=no-reply@alanadin.com
Email__Smtp__Host=smtp.saglayici.com
Email__Smtp__Port=587
Email__Smtp__Username=...
Email__Smtp__Password=...
```

---

## Görseller

Profil fotoğrafı (`PUT /api/users/me/avatar`) ve etkinlik kapağı (`PUT /api/events/{id}/image`, yalnız organizatör)
`multipart/form-data` isteğinde `file` alanıyla yüklenir.

- **Kabul edilen**: JPEG, PNG, WebP; en fazla 5 MB ve 8000×8000 piksel. Biçim dosyanın kendi baytlarından anlaşılır,
  dosya adına ya da `Content-Type`'a bakılmaz.
- **Gizlilik**: kaydetmeden önce EXIF (çekildiği yerin GPS koordinatı, cihaz modeli), XMP, IPTC ve yorumlar silinir.
  JPEG'in yön bilgisi korunur. Mobil uygulama da fotoğrafı yeniden kodlayıp küçülterek gönderir (kare 800 px, kapak 1600 px).
- **Saklama**: dosyalar `Media:RootPath` klasörüne (varsayılan `data/media`) yazılır ve `/media/...` adresinden
  sunulur. Dosya adları benzersiz olduğu için bir yıl önbelleğe alınabilirler. Bir görsel değiştirildiğinde ya da
  kaldırıldığında eski dosya, işlem veritabanına kaydedildikten sonra silinir; işlem başarısız olursa yeni dosya silinir.
  **Sunucuda bu klasör kalıcı bir diske (Docker volume) bağlanmalı.**
- **Moderasyon**: profil şikayetinde *İçeriği kaldır*, fotoğrafı ve "hakkımda" yazısını siler. Kaldırılan etkinliğin
  kapağı da silinir. Hesap silindiğinde fotoğraf silinir.

## Paylaşım bağlantısı ve deep link

Etkinlik detayındaki **Paylaş**, `https://<api-adresi>/e/{id}` bağlantısını gönderir. Bu sayfa herkese açıktır
(adres/koordinat göstermez), sosyal medyada önizleme kartı çıkarır ve **Uygulamada aç** butonu `yakinda://event/{id}` ile
uygulamayı açar. Bağlantının doğrudan uygulamada açılması için (Universal Links / App Links) API'ye şu ayarları ver;
gereken `/.well-known/apple-app-site-association` ve `/.well-known/assetlinks.json` dosyaları kendiliğinden sunulur:

```bash
App__AppleAppId=TEAMID.com.emreus.yakinda          # Apple Developer → Membership → Team ID
App__AndroidPackage=com.emreus.yakinda
App__AndroidSha256Fingerprints__0=AA:BB:...        # eas credentials → Android → SHA-256
```

Uygulama tarafında `mobile/app.config.js`, derlemede `APP_LINK_DOMAIN` verilirse (bkz. `mobile/eas.json`) iOS
`associatedDomains` ve Android `intentFilters` (autoVerify) ayarlarını ekler. Sunucuda bu değerler `deploy/.env`'deki
`APPLE_APP_ID` ve `ANDROID_SHA256_FINGERPRINT` alanlarıdır.

---

## Yayına alma

Sırayla: **1) sunucu** (API'nin herkese açık bir https adresi olsun), **2) mobil derleme** (uygulama bu adresle derlenir),
**3) mağaza başvurusu**. Hesap açma ve ödemeler (sunucu, alan adı, Apple Developer, Google Play Console) senin adına yapılamaz.

### 1) Sunucu (Docker + Caddy)

`deploy/docker-compose.prod.yml` dört servis kurar: PostGIS, Redis, API (`backend/Dockerfile`) ve Caddy. Caddy alan adın için
Let's Encrypt sertifikasını kendisi alır ve yeniler. Dışarıya yalnız 80/443 portları açılır; veritabanı ve API iç ağda kalır.
Kurulum her `git push` sonrası CI'da da denenir: imaj derlenir, veritabanıyla açılır ve `/health` kontrol edilir.

**Gerekenler:** Docker kurulu bir Linux sunucu (x86/amd64, en az 2 GB RAM; resmi PostGIS imajı ARM için yok), bir alan adı ve
`api.alanadin.com` için sunucunun IP'sini gösteren bir DNS **A** kaydı.

```bash
git clone https://github.com/emreus/yakinda.git && cd yakinda/deploy
cp .env.example .env
openssl rand -hex 24    # → POSTGRES_PASSWORD
openssl rand -hex 32    # → JWT_SIGNING_KEY
nano .env               # API_DOMAIN, şifreler, SMTP, destek e-postası
docker compose -f docker-compose.prod.yml up -d --build
curl https://api.alanadin.com/health          # {"status":"ok"}
```

- **Güncelleme:** `git pull && docker compose -f docker-compose.prod.yml up -d --build`. Migration'lar açılışta kendiliğinden çalışır.
- **Loglar:** `docker compose -f docker-compose.prod.yml logs -f api`. Eksik ayar varsa (SMTP, örnek destek adresi) açılışta uyarı yazılır.
- **Yedek:** `./backup.sh` veritabanını ve görselleri `deploy/backups/` altına alır (dosyanın başında cron örneği var).
  Yedekleri sunucu dışına da kopyala. Geri yükleme:
  `docker compose -f docker-compose.prod.yml exec -T db pg_restore -U yakinda -d yakinda --clean < backups/db-....dump`
- **Yönetici yapmak** (şikayet kuyruğu için):
  `docker compose -f docker-compose.prod.yml exec db psql -U yakinda -c "UPDATE users SET is_admin = true WHERE email = 'sen@ornek.com'"`
- **E-posta:** şifre sıfırlama için herhangi bir SMTP sağlayıcısının bilgilerini `.env`'e yaz. Boş kalırsa kodlar yalnız loga yazılır.
- API proxy arkasında olduğu için `ForwardedHeaders__Enabled=true`. Böylece istek sınırı her kullanıcının kendi IP'sine göre
  çalışır ve paylaşım sayfasındaki adresler https olur.

### 2) Mobil derleme (EAS)

```bash
npm install -g eas-cli
cd mobile
eas login                  # expo.dev hesabı
eas init                   # projeyi bağlar, app.json'a extra.eas.projectId yazar (push bildirimleri de bunu kullanır)
```

1. `mobile/eas.json` içindeki `api.ornek.com` değerlerini kendi alan adınla değiştir. `EXPO_PUBLIC_API_URL` uygulamanın
   bağlandığı adrestir, `APP_LINK_DOMAIN` paylaşım bağlantılarının uygulamada açılmasını sağlar.
2. Android haritası için Google Cloud'da **Maps SDK for Android** anahtarı oluştur ve paket adı `com.emreus.yakinda`
   ile kısıtla. Anahtarı expo.dev → proje → **Environment variables** bölümüne `GOOGLE_MAPS_API_KEY` adıyla
   *preview* ve *production* ortamları için *Sensitive* olarak ekle (`eas env:create` komutu da aynı işi yapar).
   Anahtar olmadan Android mağaza derlemesinde harita boş görünür. iOS Apple Haritalar'ı kullanır, anahtar gerekmez.
3. **Telefonda deneme (Android APK):** `eas build -p android --profile preview` komutunu çalıştır, verilen bağlantıdan APK'yı telefona kur.
4. **Mağaza derlemesi:** `eas build --profile production` (iOS + Android). Sürüm numarasını EAS her derlemede artırır.
   iOS sertifikalarını ve push (APNs) anahtarını EAS kendisi oluşturmayı önerir. Android push için Firebase'de
   bir proje açıp FCM V1 servis hesabı anahtarını `eas credentials -p android` ile yükle.
5. Paylaşım bağlantısı için: Apple Team ID'yi `deploy/.env` → `APPLE_APP_ID=TEAMID.com.emreus.yakinda` alanına yaz.
   `eas credentials -p android` çıktısındaki SHA-256 parmak izini `ANDROID_SHA256_FINGERPRINT` alanına yaz. Ardından API'yi yeniden başlat.

Uygulama kimliği iki platformda da `com.emreus.yakinda`. Mağazada yayınlandıktan sonra bu kimlik değiştirilemez.

### 3) Mağaza başvurusu

`eas submit -p ios` / `eas submit -p android` derlemeyi App Store Connect'e / Google Play Console'a yükler. Formlarda istenenler:

| Alan | Değer |
|---|---|
| Gizlilik politikası | `https://api.alanadin.com/legal/privacy` |
| Kullanım koşulları | `https://api.alanadin.com/legal/terms` |
| Hesap silme (Google Play) | `https://api.alanadin.com/legal/delete-account` (uygulama içinde: Profil → Hesabımı sil) |
| Destek e-postası | `deploy/.env` → `SUPPORT_EMAIL` ve `mobile/app.json` → `extra.supportEmail` (ikisi aynı olmalı) |
| İnceleme hesabı | Apple ve Google için, içinde birkaç etkinlik olan bir deneme hesabı oluştur |
| Şifreleme (iOS) | Standart dışı şifreleme yok (`usesNonExemptEncryption: false` ayarlı) |
| Veri güvenliği / gizlilik etiketleri | E-posta, ad, konum (yalnız uygulama açıkken, yakındaki etkinlikler için), fotoğraflar (profil/etkinlik), kullanıcı içeriği (sohbet, yorum), push bildirimi için cihaz token'ı (Expo üzerinden). Kod reklam ya da analiz SDK'sı içermez; formu kendi işletme kararlarına göre doldur. |

Kullanıcı içeriği olan uygulamalar için mağazaların istediği şikayet, engelleme, içerik filtresi ve hesap silme özellikleri
uygulamada var (bkz. "Güvenlik ve mağaza zorunlulukları").

---

## Testler

```bash
cd backend
dotnet test
```

| Proje | Ne test eder | Gereksinim |
|---|---|---|
| `tests/Yakinda.Domain.Tests` | Etkinlik/katılım kuralları: kontenjan, iptal, oylama penceresi, puan farkı, durum geçişleri | Yok (milisaniyeler) |
| `tests/Yakinda.Api.IntegrationTests` | Gerçek HTTP + gerçek PostGIS: kayıt/giriş, yakındakiler, katılım + bildirim, onay akışı, iptal, oylama ortalamaları, zamanlayıcı, rate limit | Docker çalışıyor olmalı |

Entegrasyon testleri Testcontainers ile kendi PostGIS konteynerini açıp kapatır; geliştirme veritabanına dokunmaz.
Apple Silicon Mac'te PostGIS imajı yalnızca amd64 olduğu için önce bir kez `docker compose up -d` (ya da
`docker pull --platform linux/amd64 postgis/postgis:17-3.5`) çalıştırılmış olmalı. CI her PR'da ikisini de çalıştırır.

**Rate limiting:** `/api/auth/*` uçları IP başına dakikada 20 istekle sınırlı (`RateLimiting` ayarları); aşılınca
`429` + `"code": "rate_limited"` + `Retry-After` döner.

---

## Bildirimler

| Olay | Kime | Nasıl üretilir |
|---|---|---|
| Katılım isteği / yeni katılımcı | Organizatör | `JoinEventCommand` |
| İstek onaylandı / reddedildi, etkinlikten çıkarıldın | Katılımcı | `ReviewParticipantCommand` |
| Etkinlik iptal edildi | Onaylı + bekleyen katılımcılar | `CancelEventCommand` |
| 1 saat içinde başlıyor | Onaylı katılımcılar + organizatör | `ScheduledNotificationService` (dakikada bir) |
| Etkinlik bitti, puanla | Henüz oy vermemiş onaylı katılımcılar | `ScheduledNotificationService` |

**Nasıl çalışıyor?** Bildirim, ona yol açan işlemle **aynı transaction'da** `notifications` tablosuna yazılır; işlem geri alınırsa
bildirim de oluşmaz. Bu tablo aynı zamanda push için *outbox*: `PushDispatcher` (arka plan servisi) birkaç saniyede bir
işlenmemiş kayıtları alır, kullanıcının kayıtlı cihazlarına **Expo Push** servisiyle gönderir (Expo, Android'de FCM'e, iOS'ta
APNs'e iletir). Expo'ya ulaşılamazsa artan aralıklarla tekrar dener; geçersiz token'ları siler. Zamanlanmış bildirimler her
etkinlik için bir kez gider (`events.reminder_sent_at`, `rating_prompt_sent_at`). Birden fazla API instance'ı çalışsa da
`FOR UPDATE SKIP LOCKED` sayesinde aynı bildirim iki kez üretilmez ya da gönderilmez.

**Uygulama içi liste** her yerde çalışır (Expo Go dahil): Bildirimler sekmesi, okunmamış sayısını 30 saniyede bir sorar.

**Push (telefonun bildirim çubuğu)** Expo Go'da çalışmaz (SDK 53'ten beri); bir *development build* gerekir. Expo Go'da
uygulama push'u sessizce atlar. Push'u açmak için:

1. Expo hesabı açın ve projeyi bağlayın: `cd mobile && npx eas-cli@latest login && npx eas-cli@latest init`
   (`app.json` içine `extra.eas.projectId` yazılır; uygulama push token'ını bununla alır).
2. `app.json` içindeki `ios.bundleIdentifier` ve `android.package` değerlerini kendinize göre değiştirin (`com.example.*` olmasın).
3. **Android**: Firebase'de proje açıp Android uygulaması ekleyin (paket adı 2. adımdaki ile aynı), `google-services.json`
   dosyasını `mobile/` altına koyup `app.json` → `android.googleServicesFile: "./google-services.json"` ekleyin. FCM V1
   servis hesabı anahtarını `npx eas-cli@latest credentials` ile Expo'ya yükleyin.
4. **iOS**: Apple Developer üyeliği gerekir; push anahtarını `eas credentials` (ya da ilk build) sizin için oluşturur.
5. Development build alın ve kurun: `npx eas-cli@latest build --profile development --platform android` (ya da `ios`),
   sonra `npx expo start --dev-client`.

Backend tarafında ek ayar gerekmez. Expo hesabında *Enhanced security for push notifications* açıksa erişim anahtarını
`Push:ExpoAccessToken` ayarına (ya da `Push__ExpoAccessToken` ortam değişkenine) koyun. Push göndermeyi tamamen kapatmak için
`Push:Enabled = false`.

---

## API

Tüm uç noktalar (auth ve kategoriler hariç) `Authorization: Bearer <accessToken>` ister. Hatalar RFC 7807 `ProblemDetails`
formatındadır ve makine tarafından okunabilir bir `code` alanı içerir (ör. `event.full`, `rating.not_participant`).

| Metot | Yol | Açıklama |
|---|---|---|
| POST | `/api/auth/register` | Kayıt → access + refresh token |
| POST | `/api/auth/login` | Giriş |
| POST | `/api/auth/refresh` | Refresh token rotation |
| GET | `/api/categories` | Kategoriler |
| GET | `/api/events/nearby?lat&lng&radiusKm&categoryId&from&to&onlyAvailable&sort=distance\|time&page&pageSize` | Yakındaki etkinlikler |
| POST | `/api/events` | Etkinlik oluştur |
| GET | `/api/events/{id}` | Detay (+ `canJoin`, `canRate`… gibi kullanıcıya özel yetkiler) |
| POST | `/api/events/{id}/cancel` | İptal (organizatör) |
| POST | `/api/events/{id}/join` · `/leave` | Katıl / ayrıl |
| GET | `/api/events/{id}/participants` | Katılımcılar (bekleyenleri sadece organizatör görür) |
| POST | `/api/events/{id}/participants/{userId}/approve` · `/reject` | Onayla / reddet |
| PUT | `/api/events/{id}/rating` | `{ score: 1-5, comment? }` puan ver / güncelle |
| GET | `/api/events/{id}/ratings` | Yorumlar |
| GET | `/api/users/me` · `/api/users/{id}` | Profil + organizatör puanı + dağılım |
| GET | `/api/users/me/events?role=organizing\|attending` | Etkinliklerim |
| PUT | `/api/events/{id}` | Etkinliği düzenle (organizatör, başlamadan önce) |
| GET | `/api/notifications?page&pageSize` | Bildirimlerim (en yeni önce) |
| GET | `/api/notifications/unread-count` | `{ count }` okunmamış sayısı |
| POST | `/api/notifications/{id}/read` · `/api/notifications/read-all` | Okundu yap |
| POST | `/api/devices` | `{ token, platform }` Expo push token'ını kaydet |
| DELETE | `/api/devices?token=...` | Token'ı sil (çıkış yaparken) |

---

## ⚠️ Önemli notlar

**MediatR lisansı.** MediatR 13 ve sonrası çift lisanslıdır (RPL-1.5 / ticari). Yıllık geliri 5M$'ın altındaki şirketler için ücretsiz
*Community* lisansı vardır, ama lisans anahtarı almanız gerekir (mediatr.io). Anahtarı `MediatR:LicenseKey` ayarına ya da
`MEDIATR_LICENSE_KEY` ortam değişkenine koyun; anahtar olmadan da çalışır, sadece log'a uyarı yazar.
Tamamen ücretsiz kalmak isterseniz MIT lisanslı son sürüm olan 12.5.0'a sabitleyebilir ya da source-generator tabanlı `Mediator`
kütüphanesine geçebilirsiniz. Mimari aynı kalır.

**Production öncesi:** adımlar "Yayına alma" bölümünde. Yönetilen bir PostgreSQL (Azure, AWS RDS, Supabase…) kullanacaksan
PostGIS eklentisinin açık olduğundan emin ol. Görselleri birden çok sunucuya dağıtacaksan `IMediaStorage`'ın bir nesne deposu
(S3/R2) uygulamasını yazman yeterli; geri kalan kod değişmez.

---

## Sonraki adımlar için öneriler

1. **Bildirim tercihleri**: kullanıcı hangi bildirimleri almak istediğini seçebilsin; eski bildirimleri temizleyen bir iş.
2. **Güven**: telefon doğrulama; uygulama içi yönetici ekranı (şikayet kuyruğu).
3. Görseller için nesne deposu (S3/R2) ve CDN; sunucu sayısı artınca.
