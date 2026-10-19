using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;
using Xunit.Abstractions;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// The balance checkpoint property test on InMemory (tracked invalidation path, <c>BalanceInvalidation.ApplyTrackedAsync</c>):
/// a fixed seed × 120 random balance-input changes, every valid checkpoint and every consumer compared with the from-scratch
/// computation after each change (see <see cref="BalancePropertyRunner"/>). One class per seed, so the seeds run in parallel.
/// </summary>
public abstract class BalanceCheckpointPropertyTests(NoBalanceJobsApiFactory factory, ITestOutputHelper output, int seed)
    : IClassFixture<NoBalanceJobsApiFactory>
{
    [Fact]
    public async Task RandomChanges_CheckpointedBalancesEqualTheFullRecompute()
    {
        var runner = new BalancePropertyRunner(factory, seed, $"bcp-mem-{seed}", output);
        await runner.SetupAsync();
        await runner.RunAsync(120);

        runner.MaxValidRows.Should().BeGreaterThan(20, "the reads store checkpoints, so later reads start from them");
        runner.Bumps.Should().BeGreaterThan(30, "most changes touch an ended month");
    }
}

public class BalanceCheckpointPropertySeed11Tests(NoBalanceJobsApiFactory f, ITestOutputHelper o) : BalanceCheckpointPropertyTests(f, o, 11);
public class BalanceCheckpointPropertySeed2026Tests(NoBalanceJobsApiFactory f, ITestOutputHelper o) : BalanceCheckpointPropertyTests(f, o, 2026);
public class BalanceCheckpointPropertySeed424242Tests(NoBalanceJobsApiFactory f, ITestOutputHelper o) : BalanceCheckpointPropertyTests(f, o, 424242);
