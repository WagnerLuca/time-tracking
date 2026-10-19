using Microsoft.AspNetCore.Mvc.Testing;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>The invalidation triggers on InMemory (<c>BalanceInvalidation.ApplyTrackedAsync</c>), see <see cref="BalanceInvalidationScenarios"/>.</summary>
public class BalanceInvalidationTests(NoBalanceJobsApiFactory factory) : BalanceInvalidationScenarios, IClassFixture<NoBalanceJobsApiFactory>
{
    protected override WebApplicationFactory<Program> App => factory;
}
