using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yakinda.Application.Abstractions.Email;

namespace Yakinda.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string From { get; set; } = "no-reply@ornek.com";
    public string FromName { get; set; } = "Yakında";
    public SmtpOptions Smtp { get; set; } = new();

    public sealed class SmtpOptions
    {
        /// <summary>Boşsa e-postalar gönderilmez, sadece loga yazılır (geliştirme).</summary>
        public string? Host { get; set; }
        public int Port { get; set; } = 587;
        public string? Username { get; set; }
        public string? Password { get; set; }
        public bool EnableSsl { get; set; } = true;
    }
}

/// <summary>
/// Standart SMTP ile gönderir. Geliştirmede docker-compose'daki Mailpit (localhost:1025, arayüz:
/// http://localhost:8025); canlıda herhangi bir SMTP sağlayıcısının bilgileri Email:Smtp ayarlarına yazılır.
/// </summary>
internal sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    /// <summary>
    /// E-postayı SMTP ile gönderir. Gönderen ve alıcı adreslerini kurar; HTML gövde varsa düz metni
    /// alternatif görünüm olarak ekler. Sunucu, port, şifreleme ve kullanıcı bilgisi ayarlardan gelir.
    /// Gönderimden sonra yalnızca alıcıyı loglar.
    /// </summary>
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var mail = new MailMessage
        {
            From = new MailAddress(settings.From, settings.FromName),
            Subject = message.Subject,
            Body = message.HtmlBody ?? message.TextBody,
            IsBodyHtml = message.HtmlBody is not null
        };
        // Tek adres olarak ayrıştırılır (To.Add(string) virgülle ayrılmış birden çok alıcıyı kabul ederdi).
        mail.To.Add(new MailAddress(message.To));
        if (message.HtmlBody is not null)
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.TextBody, null, "text/plain"));

        using var client = new SmtpClient(settings.Smtp.Host, settings.Smtp.Port)
        {
            EnableSsl = settings.Smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        if (!string.IsNullOrEmpty(settings.Smtp.Username))
            client.Credentials = new NetworkCredential(settings.Smtp.Username, settings.Smtp.Password);

        await client.SendMailAsync(mail, cancellationToken);
        // Konu ve içerik loglanmaz (sıfırlama kodu gibi gizli bilgiler taşıyabilir).
        logger.LogInformation("E-posta gönderildi → {To}", message.To);
    }
}

/// <summary>SMTP ayarlanmamışsa: e-postayı göndermek yerine loga yazar (kod konsolda görünür).</summary>
internal sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    /// <summary>
    /// E-postayı göndermez; alıcıyı, konuyu ve düz metin gövdeyi uyarı olarak loga yazar (SMTP ayarlanmamış
    /// geliştirme ortamı için).
    /// </summary>
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "SMTP ayarlı değil, e-posta gönderilmedi. Alıcı: {To} | Konu: {Subject}\n{Body}",
            message.To,
            message.Subject,
            message.TextBody);
        return Task.CompletedTask;
    }
}
