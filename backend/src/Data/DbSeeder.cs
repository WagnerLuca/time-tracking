using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(TimeTrackingDbContext context)
    {
        // Check if database is already seeded
        if (context.Users.Any())
        {
            return; // Database already has data
        }

        // Create test users
        var user1 = new User
        {
            Email = "test.user1@example.com",
            FirstName = "John",
            LastName = "Doe",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123"), // Hash the password
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };

        var user2 = new User
        {
            Email = "test.user2@example.com",
            FirstName = "Jane",
            LastName = "Smith",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123"), // Hash the password  
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };

        context.Users.AddRange(user1, user2);
        await context.SaveChangesAsync();

        // Create test organizations
        var org1 = new Organization
        {
            Name = "Acme Corporation",
            Description = "A leading software development company",
            Slug = "acme-corp",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true,
            Website = "https://acme-corp.example.com",
            AllowEditPause = true,
            AllowEditPastEntries = true
        };

        var org2 = new Organization
        {
            Name = "Tech Startup Inc",
            Description = "An innovative tech startup",
            Slug = "tech-startup",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };

        context.Organizations.AddRange(org1, org2);
        await context.SaveChangesAsync();

        // Create user-organization relationships
        var userOrg1 = new UserOrganization
        {
            UserId = user1.Id,
            OrganizationId = org1.Id,
            Role = OrganizationRole.Owner,
            JoinedAt = DateTime.UtcNow,
            IsActive = true
        };

        var userOrg2 = new UserOrganization
        {
            UserId = user1.Id,
            OrganizationId = org2.Id,
            Role = OrganizationRole.Member,
            JoinedAt = DateTime.UtcNow,
            IsActive = true
        };

        var userOrg3 = new UserOrganization
        {
            UserId = user2.Id,
            OrganizationId = org1.Id,
            Role = OrganizationRole.Admin,
            JoinedAt = DateTime.UtcNow,
            IsActive = true
        };

        var userOrg4 = new UserOrganization
        {
            UserId = user2.Id,
            OrganizationId = org2.Id,
            Role = OrganizationRole.Owner,
            JoinedAt = DateTime.UtcNow,
            IsActive = true
        };

        context.UserOrganizations.AddRange(userOrg1, userOrg2, userOrg3, userOrg4);
        await context.SaveChangesAsync();

        Console.WriteLine("✅ Database seeded successfully!");
        Console.WriteLine($"   - Created {context.Users.Count()} users");
        Console.WriteLine($"   - Created {context.Organizations.Count()} organizations");
        Console.WriteLine($"   - Created {context.UserOrganizations.Count()} user-organization relationships");
    }
}
