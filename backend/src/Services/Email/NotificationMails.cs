using Microsoft.Extensions.Logging.Abstractions;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

/// <summary>
/// Short notification mails (German first, then English) that accompany in-app notifications.
/// A mail that cannot be sent is logged, never fails the action that triggered it.
/// </summary>
public static class NotificationMails
{
    public static async Task TrySendAsync(IEmailSender email, string to, string subject, string body,
        ILogger? logger = null, CancellationToken ct = default)
    {
        try
        {
            await email.SendAsync(to, subject, body, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            (logger ?? NullLogger.Instance).LogWarning(ex, "Notification mail '{Subject}' could not be sent", subject);
        }
    }

    /// <summary>German / English name of a request type.</summary>
    public static (string De, string En) Label(RequestType type) => type switch
    {
        RequestType.JoinOrganization => ("Beitritt", "joining"),
        RequestType.EditPastEntry => ("Änderung eines Zeiteintrags", "time entry change"),
        RequestType.EditPause => ("Pausenänderung", "pause change"),
        RequestType.SetInitialOvertime => ("Überstunden-Startsaldo", "initial overtime"),
        RequestType.WorkScheduleChange => ("Arbeitszeitmodell", "work schedule change"),
        RequestType.CsvImport => ("CSV-Import", "CSV import"),
        RequestType.RequestAbsence => ("Abwesenheit", "absence"),
        RequestType.AddManualEntry => ("Nachtrag eines Zeiteintrags", "manual time entry"),
        _ => (type.ToString(), type.ToString())
    };
}
