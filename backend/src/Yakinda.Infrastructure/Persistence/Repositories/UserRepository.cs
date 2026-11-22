using Yakinda.Application.Abstractions.Persistence;
using Yakinda.Domain.Events;
using Yakinda.Domain.Users;

namespace Yakinda.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(DbSession session) : RepositoryBase(session), IUserRepository
{
    private const string SelectColumns =
        "id, email, password_hash, display_name, bio, rating_sum, rating_count, created_at, " +
        "terms_accepted_at, is_admin, banned_at, deleted_at, token_version";

    /// <summary>
    /// Kullanıcıyı kimliğiyle okur (silinmiş ya da yasaklı olsa da döner; durumu çağıran denetler).
    /// </summary>
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        QuerySingleOrDefaultAsync<User>(
            $"SELECT {SelectColumns} FROM users WHERE id = @Id",
            new { Id = id },
            cancellationToken);

    /// <summary>
    /// Kullanıcıyı e-postasıyla okur. E-postanın çağırandan normalize edilmiş (küçük harfli) gelmesi beklenir.
    /// </summary>
    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        QuerySingleOrDefaultAsync<User>(
            $"SELECT {SelectColumns} FROM users WHERE email = @Email",
            new { Email = normalizedEmail },
            cancellationToken);

    /// <summary>SELECT EXISTS ile bu e-postayla bir hesap olup olmadığına bakar.</summary>
    public async Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        await ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM users WHERE email = @Email)",
            new { Email = normalizedEmail },
            cancellationToken);

    /// <summary>
    /// Yeni kullanıcıyı ekler. Aynı e-posta aynı anda iki kez kaydedilmeye çalışılırsa benzersizlik indeksi
    /// ikincisini reddeder (API'de 409).
    /// </summary>
    public Task AddAsync(User user, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            INSERT INTO users (id, email, password_hash, display_name, bio, rating_sum, rating_count, created_at, terms_accepted_at)
            VALUES (@Id, @Email, @PasswordHash, @DisplayName, @Bio, @RatingSum, @RatingCount, @CreatedAt, @TermsAcceptedAt)
            """, user, cancellationToken);

    /// <summary>
    /// Organizatörün puan toplamına ve oy adedine farkı ekler. Önce okuyup sonra yazmak yerine tek atomik
    /// UPDATE kullanıldığı için, farklı etkinliklerine aynı anda gelen oylarda değer kaybolmaz.
    /// </summary>
    public Task ApplyRatingDeltaAsync(Guid userId, RatingDelta delta, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            UPDATE users
               SET rating_sum   = rating_sum + @SumDelta,
                   rating_count = rating_count + @CountDelta
             WHERE id = @UserId
            """, new { UserId = userId, delta.SumDelta, delta.CountDelta }, cancellationToken);

    /// <summary>
    /// Hesap silme: tek komutta art arda çalışan SQL'lerle kullanıcının e-postasını, adını, şifre özetini,
    /// tanıtım yazısını ve fotoğraf anahtarını temizler, deleted_at alanını doldurur ve oturum sürümünü
    /// artırır; oturumlarını, cihaz token'larını, bildirimlerini ve engellemelerini siler; oy yorumlarını,
    /// sohbet mesajlarının metnini ve düzenlediği etkinliklerin açıklama ve adreslerini boşaltır.
    /// </summary>
    public Task AnonymizeAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            UPDATE users
               SET email         = 'deleted+' || id::text || '@yakinda.invalid',
                   display_name  = 'Silinmiş kullanıcı',
                   bio           = NULL,
                   avatar_key    = NULL,
                   password_hash = '',
                   is_admin      = false,
                   token_version = token_version + 1,
                   deleted_at    = @Now
             WHERE id = @Id;
            DELETE FROM refresh_tokens     WHERE user_id = @Id;
            DELETE FROM device_push_tokens WHERE user_id = @Id;
            DELETE FROM notifications      WHERE user_id = @Id;
            -- Başkalarına gitmiş, bu kişinin adını ya da mesaj önizlemesini taşıyan bildirimler
            DELETE FROM notifications      WHERE actor_id = @Id;
            DELETE FROM user_blocks        WHERE blocker_id = @Id OR blocked_id = @Id;
            UPDATE event_ratings SET comment = NULL WHERE rater_id = @Id;
            -- Sohbet mesajlarının metni silinir ("Mesaj silindi" olarak görünür)
            UPDATE event_messages SET body = '', deleted_at = COALESCE(deleted_at, @Now) WHERE author_id = @Id;
            -- Düzenlediği etkinliklerin serbest metinleri (ev adresi, açıklama) silinir; başlık, zaman ve sayılar kalır
            UPDATE events SET description = NULL, address = NULL WHERE organizer_id = @Id;
            """, new { Id = userId, Now = nowUtc }, cancellationToken);

    /// <summary>
    /// Kullanıcıyı yasaklar: banned_at alanını doldurur, oturum sürümünü artırır (eldeki erişim token'ları
    /// geçersiz olur), refresh token'larını ve cihaz token'larını siler.
    /// </summary>
    public Task BanAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            UPDATE users SET banned_at = COALESCE(banned_at, @Now), token_version = token_version + 1 WHERE id = @Id;
            DELETE FROM refresh_tokens     WHERE user_id = @Id;
            DELETE FROM device_push_tokens WHERE user_id = @Id;
            """, new { Id = userId, Now = nowUtc }, cancellationToken);

    /// <summary>Şifre özetini yazar, oturum sürümünü bir artırır ve RETURNING ile yeni sürümü döner.</summary>
    public Task<int> UpdatePasswordAsync(Guid userId, string passwordHash, CancellationToken cancellationToken) =>
        ExecuteScalarAsync<int>(
            "UPDATE users SET password_hash = @Hash, token_version = token_version + 1 WHERE id = @Id RETURNING token_version",
            new { Id = userId, Hash = passwordHash },
            cancellationToken);

    /// <summary>Görünen adı ve tanıtım yazısını günceller.</summary>
    public Task UpdateProfileAsync(Guid userId, string displayName, string? bio, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE users SET display_name = @DisplayName, bio = @Bio WHERE id = @Id",
            new { Id = userId, DisplayName = displayName, Bio = bio },
            cancellationToken);

    /// <summary>Tanıtım yazısını NULL yapar.</summary>
    public Task ClearBioAsync(Guid userId, CancellationToken cancellationToken) =>
        ExecuteAsync("UPDATE users SET bio = NULL WHERE id = @Id", new { Id = userId }, cancellationToken);
}

internal sealed class MediaRepository(DbSession session) : RepositoryBase(session), IMediaRepository
{
    /// <summary>
    /// Profil fotoğrafı anahtarını yazar ve ESKİ anahtarı döner (eski dosya silinsin diye). UPDATE ...
    /// RETURNING yeni değeri verdiği için eski değer, aynı satırı kilitleyen bir alt sorguyla okunur.
    /// </summary>
    public Task<string?> SetUserAvatarAsync(Guid userId, string? key, CancellationToken cancellationToken) =>
        ExecuteScalarAsync<string?>("""
            UPDATE users u SET avatar_key = @Key
              FROM (SELECT id, avatar_key FROM users WHERE id = @Id FOR UPDATE) old
             WHERE u.id = old.id
            RETURNING old.avatar_key
            """, new { Id = userId, Key = key }, cancellationToken);

    /// <summary>
    /// Etkinliğin kapak görseli anahtarını yazar ve eski anahtarı döner; yöntem SetUserAvatarAsync ile aynıdır.
    /// </summary>
    public Task<string?> SetEventImageAsync(Guid eventId, string? key, CancellationToken cancellationToken) =>
        ExecuteScalarAsync<string?>("""
            UPDATE events e SET image_key = @Key
              FROM (SELECT id, image_key FROM events WHERE id = @Id FOR UPDATE) old
             WHERE e.id = old.id
            RETURNING old.image_key
            """, new { Id = eventId, Key = key }, cancellationToken);

    /// <summary>
    /// Organizatörün görseli olan bütün etkinliklerinde anahtarı boşaltır ve silinecek dosya anahtarlarını döner.
    /// </summary>
    public Task<IReadOnlyList<string>> ClearEventImagesByOrganizerAsync(Guid organizerId, CancellationToken cancellationToken) =>
        QueryAsync<string>("""
            UPDATE events e SET image_key = NULL
              FROM (SELECT id, image_key FROM events WHERE organizer_id = @Id AND image_key IS NOT NULL FOR UPDATE) old
             WHERE e.id = old.id
            RETURNING old.image_key
            """, new { Id = organizerId }, cancellationToken);
}
