namespace Yakinda.Infrastructure.Auth;

/// <summary>Token içindeki claim adları (MapInboundClaims=false olduğu için olduğu gibi okunur).</summary>
public static class ClaimNames
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string TokenId = "jti";
}

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "yakinda-api";
    public string Audience { get; set; } = "yakinda-mobile";

    /// <summary>
    /// HMAC-SHA256 imzalama anahtarı, en az 32 karakter.
    /// Production'da appsettings'e değil, ortam değişkenine / secret store'a koyun (Jwt__SigningKey).
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 30;
}
