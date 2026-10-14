using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>The login limit may only be raised for local development (e2e runs), never in production.</summary>
public class RateLimitConfigTests
{
    private static int AuthStrict(params string[] files)
    {
        var builder = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory);
        foreach (var f in files) builder.AddJsonFile(f, optional: false);
        return builder.Build().GetValue("RateLimiting:AuthStrictPerMinute", Program.DefaultAuthStrictPerMinute);
    }

    [Fact]
    public void Production_keeps_ten_logins_per_minute()
    {
        Program.DefaultAuthStrictPerMinute.Should().Be(10);
        AuthStrict("appsettings.json").Should().Be(10);
    }

    [Fact]
    public void Development_raises_the_login_limit_for_e2e_runs()
    {
        AuthStrict("appsettings.json", "appsettings.Development.json").Should().BeGreaterThan(10);
    }
}
