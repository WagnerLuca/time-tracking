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

        var expiredTokens = await context.RefreshTokens
            // Revoked-but-unexpired tokens are kept: reuse detection needs them to recognise a replay
            .Where(rt => rt.ExpiresAt < cutoff)
            .ToListAsync(cancellationToken);

        if (expiredTokens.Count > 0)
        {
            context.RefreshTokens.RemoveRange(expiredTokens);
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Cleaned up {Count} expired/revoked refresh tokens.", expiredTokens.Count);
        }

        // Data minimisation: spent links and old read notifications have no further use
        var linkCutoff = cutoff.AddDays(-30);
        context.UserTokens.RemoveRange(await context.UserTokens
            .Where(t => t.ExpiresAt < linkCutoff || t.UsedAt < linkCutoff)
            .ToListAsync(cancellationToken));
        context.OrgInvitations.RemoveRange(await context.OrgInvitations.IgnoreQueryFilters()
            .Where(i => i.ExpiresAt < linkCutoff || i.AcceptedAt < linkCutoff || i.RevokedAt < linkCutoff)
            .ToListAsync(cancellationToken));
        var notificationCutoff = cutoff.AddDays(-180);
        context.Notifications.RemoveRange(await context.Notifications
            .Where(n => n.IsRead && n.CreatedAt < notificationCutoff)
            .ToListAsync(cancellationToken));
        var removed = await context.SaveChangesAsync(cancellationToken);
        if (removed > 0)
            _logger.LogInformation("Retention cleanup removed {Count} old links and notifications.", removed);
    }
}
