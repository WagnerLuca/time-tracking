using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TimeTracking.Api.Data;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Tests.Infrastructure;

/// <summary>
/// Custom WebApplicationFactory that uses a unique InMemory database per test class
/// and seeds fresh data for each test run.
/// </summary>
public class TimeTrackingApiFactory : WebApplicationFactory<Program>, IMailCapturingFactory
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    /// <summary>Mails the app "sent" (verification, password reset, ...).</summary>
    public CapturingEmailSender Mail { get; } = new();

    /// <summary>Web pushes the app "sent" (delivered in the background by PushDispatcher).</summary>
    public FakePushSender Push { get; } = new();

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
            // (Program.cs creates and seeds it on startup)
            services.AddDbContext<TimeTrackingDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Mail);
            services.RemoveAll<IPushSender>();
            services.AddSingleton<IPushSender>(Push);
        });
    }
}
