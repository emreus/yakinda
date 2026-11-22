using System.Text.RegularExpressions;
using FluentValidation;

namespace Yakinda.Application.Auth;

/// <summary>
/// E-posta adresi biçimi. FluentValidation'ın hazır kuralı yalnızca '@' işaretine bakar; "Ad &lt;a@b.com&gt;",
/// boşluklu ya da alan adında nokta olmayan değerleri kabul eder. Burada tek bir düz adres istenir:
/// görünen ad, virgül, boşluk ve satır sonu içeremez (e-posta başlığına ek alıcı ya da satır eklenemez).
/// </summary>
internal static partial class EmailRules
{
    // Yerel kısım: harf, rakam ve yaygın işaretler. Alan adı: en az bir nokta, her parça harf/rakamla başlar ve biter.
    [GeneratedRegex(
        @"^[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <summary>
    /// Değer tek bir düz e-posta adresi mi? Boş olmamalı, 256 karakteri geçmemeli ve yukarıdaki kalıba uymalı.
    /// </summary>
    public static bool IsValid(string? email) =>
        email is not null && email.Length <= 256 && Pattern().IsMatch(email.Trim());

    /// <summary>
    /// IsValid kontrolünü FluentValidation kuralı olarak sunar: RuleFor(...).ValidEmail() biçiminde kullanılır.
    /// </summary>
    public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(IsValid).WithMessage("Geçerli bir e-posta adresi yaz (ör. ad@ornek.com).");
}
