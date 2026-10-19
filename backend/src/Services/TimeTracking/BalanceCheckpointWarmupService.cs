using TimeTracking.Api.Data;

namespace TimeTracking.Api.Services;

/// <summary>
/// Pre-computes the overtime balance checkpoints (<see cref="BalanceCheckpoints.WarmAsync"/>) once per org-local month,
/// so balance reads (team overview, payroll, …) never pay for a whole history. Its own hosted service, not part of the
/// <see cref="FailureEntryService"/> minute loop: the first pass after a deploy computes every organization's full
/// history, which must not delay the auto-stop or push core-hours checks into "checked late". One organization at a time;
/// a pass with nothing due costs one small query. System work (no HttpContext = RLS bypass).
/// </summary>
public class BalanceCheckpointWarmupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BalanceCheckpointWarmupService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(10);

    public BalanceCheckpointWarmupService(IServiceScopeFactory scopeFactory, ILogger<BalanceCheckpointWarmupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish starting (migrations, the other jobs' first passes) before the first, possibly long pass
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
                var warmed = await BalanceCheckpoints.WarmAsync(context, DateTime.UtcNow, stoppingToken, _logger);
                if (warmed > 0)
                    _logger.LogInformation("Pre-computed the balance checkpoints of {Count} organization(s).", warmed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error while pre-computing balance checkpoints.");
            }
            await Task.Delay(_interval, stoppingToken);
        }
    }
}
