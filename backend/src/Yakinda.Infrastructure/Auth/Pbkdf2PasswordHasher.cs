using System.Globalization;
using System.Security.Cryptography;
using Yakinda.Application.Abstractions.Auth;

namespace Yakinda.Infrastructure.Auth;

/// <summary>
/// .NET'in yerleşik PBKDF2 implementasyonu (ek paket yok).
/// Format: pbkdf2-sha512$iterasyon$salt(base64)$hash(base64)
/// İterasyon sayısı hash'in içinde saklandığı için ileride artırılabilir; eski hash'ler doğrulanmaya devam eder.
/// </summary>
internal sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Prefix = "pbkdf2-sha512";
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 210_000; // OWASP önerisi (PBKDF2-HMAC-SHA512)

    /// <summary>
    /// Şifreyi özetler: 16 baytlık rastgele tuz üretir, PBKDF2-HMAC-SHA512'yi 210.000 iterasyonla çalıştırır
    /// ve sonucu "algoritma$iterasyon$tuz$özet" biçiminde tek metin olarak döner.
    /// </summary>
    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA512, KeySize);
        return string.Join('$',
            Prefix,
            Iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(key));
    }

    /// <summary>Geçerli biçimde ama hiçbir şifreyle eşleşmeyecek bir özet; ilk kullanımda bir kez üretilir.</summary>
    private static readonly Lazy<string> Nothing =
        new(() => new Pbkdf2PasswordHasher().Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))));

    /// <summary>
    /// Hiçbir şifreyle eşleşmeyen, hazır tutulan bir özete karşı doğrulama yapar; sonuç her zaman olumsuzdur ama
    /// gerçek doğrulama kadar süre harcanır.
    /// </summary>
    public void VerifyAgainstNothing(string password) => Verify(password, Nothing.Value);

    /// <summary>
    /// Şifreyi saklanan özetle karşılaştırır: metni parçalarına ayırır, aynı tuz ve iterasyonla özeti yeniden
    /// hesaplar ve sabit süreli karşılaştırma yapar. Biçim bozuksa false döner.
    /// </summary>
    public bool Verify(string password, string hash)
    {
        var parts = hash.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix)
            return false;

        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations))
            return false;

        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
