using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace TimeTracking.Api.Tests.Infrastructure;

/// <summary>
/// Runs the API against a real PostgreSQL (Docker via Testcontainers) instead of EF InMemory,
/// which enforces no unique indexes, transactions, DateTime kinds or row-level security.
/// The app connects as a normal (non-superuser) role that owns the database — exactly like
/// production must — because superusers would silently bypass the RLS policies.
/// Startup runs the real migrations and the demo seed.
/// </summary>
public class PostgresApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AppRole = "tt_app";
    public const string AppPassword = "tt_app_pw";

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    /// <summary>Connection string of the app role (subject to RLS).</summary>
    public string AppConnectionString { get; private set; } = "";

    /// <summary>Connection string of the container superuser (bypasses RLS) for test setup/inspection.</summary>
    public string SuperuserConnectionString => _db.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _db.StartAsync();

        await using (var conn = new NpgsqlConnection(_db.GetConnectionString()))
        {
            await conn.OpenAsync();
            // Separate commands: CREATE DATABASE refuses to run inside a multi-statement batch
            foreach (var sql in new[]
                     {
                         $"CREATE ROLE {AppRole} LOGIN PASSWORD '{AppPassword}' NOSUPERUSER NOBYPASSRLS",
                         $"CREATE DATABASE timetracking OWNER {AppRole}"
                     })
            {
                await using var cmd = new NpgsqlCommand(sql, conn);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        AppConnectionString = new NpgsqlConnectionStringBuilder(_db.GetConnectionString())
        {
            Database = "timetracking",
            Username = AppRole,
            Password = AppPassword
        }.ConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("UseInMemoryDatabase", "false");
        builder.UseSetting("SeedDemoData", "true");
        builder.UseSetting("ConnectionStrings:DefaultConnection", AppConnectionString);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresApiFactory>
{
    public const string Name = "Postgres";
}
