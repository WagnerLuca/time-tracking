using Microsoft.AspNetCore.Mvc.Testing;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// The invalidation triggers on Postgres (<c>BalanceInvalidation.ApplySqlAsync</c>: the bump and the carry as SQL in the
/// save's transaction), see <see cref="BalanceInvalidationScenarios"/>. The orgs are marked pre-warmed so the hosted
/// minute loop of the shared app leaves their rows alone.
/// </summary>
[Collection(PostgresCollection.Name)]
public class BalanceInvalidationPostgresTests(PostgresApiFactory factory) : BalanceInvalidationScenarios
{
    protected override WebApplicationFactory<Program> App => factory;
}
