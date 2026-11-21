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
