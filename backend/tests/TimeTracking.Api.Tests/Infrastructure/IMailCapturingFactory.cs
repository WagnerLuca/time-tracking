namespace TimeTracking.Api.Tests.Infrastructure;

/// <summary>A test factory that captures the mails the app sends.</summary>
public interface IMailCapturingFactory
{
    CapturingEmailSender Mail { get; }
}
