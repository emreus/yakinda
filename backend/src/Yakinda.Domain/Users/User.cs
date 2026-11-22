namespace Yakinda.Domain.Users;

public sealed class User
{
    // Dapper için
    private User() { }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string? Bio { get; private set; }

    /// <summary>Organize ettiği etkinliklere verilen tüm puanların toplamı.</summary>
    public long RatingSum { get; private set; }

    /// <summary>Organize ettiği etkinliklere verilen puan adedi.</summary>
    public int RatingCount { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>Kayıtta kullanım koşulları ve gizlilik politikası kabul edildi.</summary>
    public DateTime? TermsAcceptedAt { get; private set; }

    /// <summary>Şikayetleri inceleyebilir (veritabanından elle verilir).</summary>
    public bool IsAdmin { get; private set; }

    /// <summary>Kurallara aykırılık nedeniyle yasaklandı; giriş yapamaz.</summary>
    public DateTime? BannedAt { get; private set; }

    /// <summary>Hesap silindi; kişisel veriler temizlendi.</summary>
    public DateTime? DeletedAt { get; private set; }

    /// <summary>
    /// Oturum sürümü. Erişim token'ı bu değeri taşır; şifre sıfırlama, yasaklama ve hesap silmede artar
    /// ve eski token'lar o anda geçersiz olur.
    /// </summary>
    public int TokenVersion { get; private set; }

    public bool IsBanned => BannedAt is not null;

    /// <summary>Veritabanında oturum sürümü artırıldıktan sonra (yeni token doğru sürümle üretilsin diye).</summary>
    public void SessionsRevoked(int newTokenVersion) => TokenVersion = newTokenVersion;

    /// <summary>
    /// Organizatör puanı: her bir oy eşit ağırlıktadır (etkinlik ortalamalarının ortalaması değil).
    /// Böylece 50 kişinin oyladığı parti, 1 kişinin oyladığı okey masasından daha fazla etki eder.
    /// </summary>
    public double? AverageRating =>
        RatingCount == 0 ? null : Math.Round((double)RatingSum / RatingCount, 2);

    /// <summary>E-postayı kırpıp küçük harfe çevirir; kayıt ve girişte aynı biçim karşılaştırılsın diye.</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>
    /// Yeni kullanıcı üretir: zaman sıralı kimlik (UUIDv7) verir, e-postayı normalize eder, adı kırpar,
    /// puan toplamlarını sıfırdan başlatır ve koşulların kabul edildiği anı kayıt anı olarak işaretler.
    /// </summary>
    public static User Register(string email, string passwordHash, string displayName, DateTime nowUtc) => new()
    {
        Id = Guid.CreateVersion7(),
        Email = NormalizeEmail(email),
        PasswordHash = passwordHash,
        DisplayName = displayName.Trim(),
        RatingSum = 0,
        RatingCount = 0,
        CreatedAt = nowUtc,
        TermsAcceptedAt = nowUtc
    };
}
