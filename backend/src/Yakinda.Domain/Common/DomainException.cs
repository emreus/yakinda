namespace Yakinda.Domain.Common;

/// <summary>
/// İş kuralı ihlali. <see cref="Code"/> mobil uygulamanın hatayı tanıyabilmesi için
/// sabit bir anahtardır (örn. "event.full"), mesaj ise kullanıcıya gösterilecek metindir.
/// </summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
