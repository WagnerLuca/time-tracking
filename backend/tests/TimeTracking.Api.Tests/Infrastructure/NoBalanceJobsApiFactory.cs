using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Tests.Infrastructure;

/// <summary>
/// InMemory app without the background jobs that compute or delete balance checkpoints: the failure-entry minute loop and
/// the checkpoint pre-warm (matched by type name, wherever it is hosted), so tests that count or inspect checkpoint rows
/// see only what they did themselves. Tests call <c>FailureEntryService.CloseOverdueEntriesAsync</c> /
/// <c>BalanceCheckpoints.WarmAsync</c> directly.
/// </summary>
public class NoBalanceJobsApiFactory : TimeTrackingApiFactory
{
    public static bool IsBalanceJob(Type? type) =>
        type != null && (type == typeof(FailureEntryService) || type == typeof(RefreshTokenCleanupService)
                         || type.Name.Contains("Balance") || type.Name.Contains("Warm") || type.Name.Contains("Checkpoint"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            foreach (var job in services.Where(d => d.ServiceType == typeof(IHostedService) && IsBalanceJob(d.ImplementationType)).ToList())
                services.Remove(job);
        });
    }
}
