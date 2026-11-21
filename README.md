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
cp .env.example .env          # EXPO_PUBLIC_API_URL = bilgisayarınızın yerel IP'si
npm install
npm run setup                 # paket sürümlerini Expo SDK 58'e göre hizalar (değişiklik yoksa bir şey yapmaz)
npx expo start
```
> Telefonda `localhost` bilgisayarınızı göstermez. `.env` içine bilgisayarın Wi-Fi IP'sini yazın (Android emülatör: `10.0.2.2`).

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

---

## ⚠️ Önemli notlar

**MediatR lisansı.** MediatR 13 ve sonrası çift lisanslıdır (RPL-1.5 / ticari). Yıllık geliri 5M$'ın altındaki şirketler için ücretsiz
*Community* lisansı vardır, ama lisans anahtarı almanız gerekir (mediatr.io). Anahtarı `MediatR:LicenseKey` ayarına ya da
`MEDIATR_LICENSE_KEY` ortam değişkenine koyun; anahtar olmadan da çalışır, sadece log'a uyarı yazar.
Tamamen ücretsiz kalmak isterseniz MIT lisanslı son sürüm olan 12.5.0'a sabitleyebilir ya da source-generator tabanlı `Mediator`
kütüphanesine geçebilirsiniz. Mimari aynı kalır.

**Production öncesi:**
- `Jwt:SigningKey` değerini appsettings'e değil ortam değişkenine / secret store'a koyun (`Jwt__SigningKey`), en az 32 karakter.
- API'yi HTTPS arkasında yayınlayın (iOS ATS ve Android cleartext kısıtları production'da HTTP'ye izin vermez).
- Android'de Google Maps için API anahtarı gerekir: `app.json` → `plugins` içine
  `["react-native-maps", { "androidGoogleMapsApiKey": "..." }]` ekleyin. iOS varsayılan olarak Apple Maps kullanır, anahtar gerekmez.
- Yönetilen PostgreSQL (Azure, AWS RDS, Supabase…) kullanacaksanız PostGIS eklentisinin açık olduğundan emin olun.
- Mağaza yayını için `app.json` içindeki `bundleIdentifier` / `package` değerlerini değiştirin, EAS Build kullanın.

---

## Sonraki adımlar için öneriler

1. **Bildirimler** (expo-notifications): katılım isteği geldi, onaylandın, etkinlik yarın, "oy vermeyi unutma".
2. **Etkinlik düzenleme** ve organizatör ile katılımcılar arası basit **mesajlaşma**.
3. **Güven & güvenlik**: kullanıcı engelleme/şikayet, telefon doğrulama, rate limiting (`AddRateLimiter`).
4. **Sıralamada Bayesian ortalama**: tek oyla 5,0 alan organizatörün 200 oyla 4,8 alanın önüne geçmemesi için
   `(C·m + Σpuan) / (C + n)` gibi ağırlıklı skor (gösterimde ham ortalama kalabilir).
5. **Testler**: Domain kuralları için birim testleri, Testcontainers (PostGIS) ile entegrasyon testleri.
6. Profil fotoğrafı / etkinlik görseli (S3/Blob + ön imzalı URL).
