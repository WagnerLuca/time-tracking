using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;
using Xunit.Abstractions;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// The balance checkpoint property test on Postgres as the app role: changes and reads act as the owner or the member
/// (row-level security on), the bump and carry run as SQL in the save's transaction (<c>ApplySqlAsync</c>), checkpoints are
/// stored by the upsert. See <see cref="BalancePropertyRunner"/>.
/// </summary>
[Collection(PostgresCollection.Name)]
public class BalanceCheckpointPropertyPostgresTests(PostgresApiFactory factory, ITestOutputHelper output)
{
    [Theory]
    [InlineData(7)]
    public async Task RandomChanges_CheckpointedBalancesEqualTheFullRecompute(int seed)
    {
        var runner = new BalancePropertyRunner(factory, seed, $"bcp-pg-{seed}", output);
        await runner.SetupAsync();
        await runner.RunAsync(130);

        runner.MaxValidRows.Should().BeGreaterThan(20, "the reads store checkpoints, so later reads start from them");
        runner.Bumps.Should().BeGreaterThan(20, "most changes touch an ended month");
    }
}
