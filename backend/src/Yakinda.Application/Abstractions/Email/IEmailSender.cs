namespace Yakinda.Application.Abstractions.Email;

public sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody = null);

/// <summary>
/// E-posta gönderimi. Geliştirmede Mailpit'e (http://localhost:8025), SMTP ayarı yoksa
/// sadece loga yazılır; canlıda herhangi bir SMTP sağlayıcısı (Resend, Brevo, SES, Gmail…) kullanılabilir.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
