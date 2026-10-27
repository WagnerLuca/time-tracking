using System.Net;
using System.Net.Mail;

namespace TimeTracking.Api.Services;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}

/// <summary>SMTP settings (section "Email").</summary>
public class EmailSettings
{
    public string From { get; set; } = "Time Tracking <no-reply@localhost>";
    /// <summary>When SMTP is unavailable, log mail contents for development; otherwise discard them.</summary>
    public bool LogWhenSmtpUnconfigured { get; set; } = true;
    /// <summary>Base URL of the frontend, used for links in mails (e.g. https://time.example.com).</summary>
    public string FrontendBaseUrl { get; set; } = "http://localhost:5179";
    public SmtpSettings Smtp { get; set; } = new();

    public class SmtpSettings
    {
        public string? Host { get; set; }
        public int Port { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;
        public string? Username { get; set; }
        public string? Password { get; set; }
    }
}

/// <summary>Sends plain-text mail over SMTP.</summary>
public class SmtpEmailSender(EmailSettings settings, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        using var client = new SmtpClient(settings.Smtp.Host, settings.Smtp.Port) { EnableSsl = settings.Smtp.EnableSsl };
        if (!string.IsNullOrEmpty(settings.Smtp.Username))
            client.Credentials = new NetworkCredential(settings.Smtp.Username, settings.Smtp.Password);

        using var message = new MailMessage { From = new MailAddress(settings.From), Subject = subject, Body = body };
        message.To.Add(to);
        await client.SendMailAsync(message, ct);
        logger.LogInformation("Sent email '{Subject}'", subject);
    }
}

/// <summary>Development stand-in: writes the whole mail (including its link) to the log.</summary>
public class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        logger.LogWarning("Email not sent (no Email:Smtp:Host configured). To: {To} | {Subject}\n{Body}", to, subject, body);
        return Task.CompletedTask;
    }
}

/// <summary>Discards mail when SMTP is unavailable without logging recipients or links.</summary>
public class DiscardEmailSender(ILogger<DiscardEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        logger.LogWarning("Email not sent because SMTP is unconfigured and email logging is disabled.");
        return Task.CompletedTask;
    }
}
