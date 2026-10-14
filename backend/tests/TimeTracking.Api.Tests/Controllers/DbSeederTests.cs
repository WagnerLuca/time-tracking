using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// SeedDemoData on a live test instance: the demo org is added next to real data, once.
/// </summary>
public class DbSeederTests
{
    private static TimeTrackingDbContext NewContext() =>
        new(new DbContextOptionsBuilder<TimeTrackingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Seed_AddsDemoOrg_NextToExistingUsers_AndOnlyOnce()
    {
        await using var db = NewContext();
        db.Users.Add(new User { Email = "real.user@desktop-tools.dzg", FirstName = "Real", LastName = "User", PasswordHash = "x", IsActive = true });
        await db.SaveChangesAsync();

        await DbSeeder.SeedAsync(db);
        var users = await db.Users.CountAsync();
        var entries = await db.TimeEntries.CountAsync();

        users.Should().BeGreaterThan(1);
        entries.Should().BeGreaterThan(0);
        (await db.Organizations.SingleAsync()).Slug.Should().Be("mueller-software");
        (await db.Users.AnyAsync(u => u.Email == "real.user@desktop-tools.dzg")).Should().BeTrue();

        await DbSeeder.SeedAsync(db);

        (await db.Users.CountAsync()).Should().Be(users);
        (await db.Organizations.CountAsync()).Should().Be(1);
        (await db.TimeEntries.CountAsync()).Should().Be(entries);
    }
}
