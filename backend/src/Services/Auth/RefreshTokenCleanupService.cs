using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;

namespace TimeTracking.Api.Services;

/// <summary>
/// Background service that periodically purges expired and revoked refresh tokens
/// to prevent unbounded table growth.
/// </summary>
public class RefreshTokenCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RefreshTokenCleanupService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(6);

    public RefreshTokenCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<RefreshTokenCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RefreshTokenCleanupService started. Running every {Interval}.", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupTokensAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during refresh token cleanup.");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task CleanupTokensAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();

        var cutoff = DateTime.UtcNow;

        var deleted = await context.RefreshTokens
            .Where(rt => rt.ExpiresAt < cutoff || rt.IsRevoked)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired/revoked refresh tokens.", deleted);
        }
    }
}
