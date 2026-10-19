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

        // Revoked-but-unexpired tokens are kept: reuse detection needs them to recognise a replay
        var expiredTokens = await DeleteAsync(context, context.RefreshTokens.Where(rt => rt.ExpiresAt < cutoff), cancellationToken);
        if (expiredTokens > 0)
            _logger.LogInformation("Cleaned up {Count} expired/revoked refresh tokens.", expiredTokens);

        // Data minimisation: spent links and old read notifications have no further use
        var linkCutoff = cutoff.AddDays(-30);
        var removed = await DeleteAsync(context, context.UserTokens
            .Where(t => t.ExpiresAt < linkCutoff || t.UsedAt < linkCutoff), cancellationToken);
        removed += await DeleteAsync(context, context.OrgInvitations.IgnoreQueryFilters()
            .Where(i => i.ExpiresAt < linkCutoff || i.AcceptedAt < linkCutoff || i.RevokedAt < linkCutoff), cancellationToken);
        var notificationCutoff = cutoff.AddDays(-180);
        removed += await DeleteAsync(context, context.Notifications
            .Where(n => n.IsRead && n.CreatedAt < notificationCutoff), cancellationToken);
        // Punch protocol: kept 400 days (a year of payroll questions plus slack)
        var punchCutoff = cutoff.AddDays(-400);
        removed += await DeleteAsync(context, context.TerminalPunches.IgnoreQueryFilters()
            .Where(p => p.ReceivedAt < punchCutoff), cancellationToken);
        if (removed > 0)
            _logger.LogInformation("Retention cleanup removed {Count} old links, notifications and punch protocol rows.", removed);
    }

    /// <summary>
    /// Deletes the matching rows in one statement, without loading them. None of these tables has a
    /// <c>SaveChangesAsync</c> hook (closed periods, entry history,
    /// push) and nothing references them with a cascade the database does not handle itself. The InMemory provider
    /// (development, tests) cannot run <c>ExecuteDelete</c>, so it loads and removes them as before.
    /// </summary>
    private static async Task<int> DeleteAsync<T>(TimeTrackingDbContext context, IQueryable<T> rows, CancellationToken ct) where T : class
    {
        if (!context.Database.IsInMemory())
            return await rows.ExecuteDeleteAsync(ct);
        var loaded = await rows.ToListAsync(ct);
        if (loaded.Count == 0) return 0;
        context.RemoveRange(loaded);
        await context.SaveChangesAsync(ct);
        return loaded.Count;
    }
}
