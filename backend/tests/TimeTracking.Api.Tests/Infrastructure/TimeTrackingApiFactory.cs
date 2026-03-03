using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimeTracking.Api.Data;

namespace TimeTracking.Api.Tests.Infrastructure;

/// <summary>
/// Custom WebApplicationFactory that uses a unique InMemory database per test class
/// and seeds fresh data for each test run.
/// </summary>
public class TimeTrackingApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Tell Program.cs to use InMemory instead of Npgsql
        builder.UseSetting("UseInMemoryDatabase", "true");

        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext registration from Program.cs
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<TimeTrackingDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            // Re-add with a unique InMemory database per factory instance
            services.AddDbContext<TimeTrackingDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            // Build a service provider to seed the database
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            db.Database.EnsureCreated();
            DbSeeder.SeedAsync(db).GetAwaiter().GetResult();
        });
    }
}
