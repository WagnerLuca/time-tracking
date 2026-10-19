using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using TimeTracking.Api.Data;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Tests.Infrastructure;

/// <summary>
/// <see cref="PostgresApiFactory"/> for the scaling tests (<c>Perf/</c>): its own container, so the large
/// histories never slow the normal Postgres suite; every app DbContext reports to <see cref="Queries"/>, and
/// the test's async flow reaches the server so a <see cref="QueryCounter.Begin"/> scope sees exactly the SQL of
/// the request it wraps. The background jobs (failure entries + core hours loop, push dispatch, retention) are
/// not started: they would run against the measured database at random moments.
/// <see cref="ForDatabaseAsync"/> gives each history size a fresh database of its own.
/// </summary>
public class PerfApiFactory : PostgresApiFactory
{
    public QueryCounter Queries { get; } = new();

    private static readonly Type[] BackgroundJobs = [typeof(FailureEntryService), typeof(PushDispatcher), typeof(RefreshTokenCleanupService)];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            foreach (var job in services.Where(d => d.ServiceType == typeof(IHostedService) && BackgroundJobs.Contains(d.ImplementationType)).ToList())
                services.Remove(job);
            services.Configure<TestServerOptions>(o => o.PreserveExecutionContext = true);
            services.ConfigureDbContext<TimeTrackingDbContext>(o => o.AddInterceptors(Queries));
        });
    }

    /// <summary>
    /// Creates the empty database <paramref name="name"/> owned by the app role and an app instance on it (the app
    /// migrates it on startup, as in production). Returns the app and the database's superuser and app connection strings.
    /// </summary>
    public async Task<(WebApplicationFactory<Program> App, string Superuser, string AppRole)> ForDatabaseAsync(string name)
    {
        await using (var conn = new NpgsqlConnection(SuperuserConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\" OWNER {AppRole}", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        var app = new NpgsqlConnectionStringBuilder(AppConnectionString) { Database = name }.ConnectionString;
        var factory = WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:DefaultConnection", app));
        _ = factory.Server;   // migrations + demo seed
        return (factory, new NpgsqlConnectionStringBuilder(SuperuserConnectionString) { Database = name }.ConnectionString, app);
    }

    /// <summary>Drops a database of <see cref="ForDatabaseAsync"/> (after its app was disposed).</summary>
    public async Task DropDatabaseAsync(string name)
    {
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(SuperuserConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public class PerfCollection : ICollectionFixture<PerfApiFactory>
{
    public const string Name = "Perf";
}
