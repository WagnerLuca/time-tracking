using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Tests.Infrastructure;

/// <summary>Records mails instead of sending them, so tests can follow the links inside.</summary>
public class CapturingEmailSender : IEmailSender
{
    public record Mail(string To, string Subject, string Body);

    public ConcurrentQueue<Mail> Sent { get; } = new();

    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        Sent.Enqueue(new Mail(to, subject, body));
        return Task.CompletedTask;
    }

    /// <summary>The token of the newest link to <paramref name="path"/> mailed to <paramref name="to"/>.</summary>
    public string LinkToken(string to, string path)
    {
        var mail = Sent.Reverse().First(m => m.To.Equals(to, StringComparison.OrdinalIgnoreCase) && m.Body.Contains($"/{path}?token="));
        return Uri.UnescapeDataString(Regex.Match(mail.Body, $@"/{path}\?token=([^\s]+)").Groups[1].Value);
    }
}
