using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TimeTracking.Api.Data;

/// <summary>
/// Factory used by EF Core design-time tools (migrations add/remove/script)
/// to create a DbContext with a real database provider (Npgsql).
/// At runtime the provider is configured in Program.cs instead.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TimeTrackingDbContext>
{
    public TimeTrackingDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=timetracking;Username=user;Password=pass";

        var optionsBuilder = new DbContextOptionsBuilder<TimeTrackingDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new TimeTrackingDbContext(optionsBuilder.Options);
    }
}
