using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Data;

public class TimeTrackingDbContext : DbContext
{
    public TimeTrackingDbContext(DbContextOptions<TimeTrackingDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Organization> Organizations { get; set; }
    public DbSet<UserOrganization> UserOrganizations { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<TimeEntry> TimeEntries { get; set; }
    public DbSet<PauseRule> PauseRules { get; set; }
    public DbSet<OrgRequest> OrgRequests { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var isInMemory = Database.IsInMemory();

        // Configure User entity
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
            
            if (isInMemory)
                entity.Property(e => e.CreatedAt).IsRequired();
            else
                entity.Property(e => e.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            // Create unique indexes
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.GitHubId).IsUnique();
        });

        // Configure Organization entity
        modelBuilder.Entity<Organization>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Slug).IsRequired().HasMaxLength(100);
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
            
            if (isInMemory)
                entity.Property(e => e.CreatedAt).IsRequired();
            else
                entity.Property(e => e.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            // Create unique index on Slug
            entity.HasIndex(e => e.Slug).IsUnique();
        });

        // Configure UserOrganization entity
        modelBuilder.Entity<UserOrganization>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Role).IsRequired();
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
            
            if (isInMemory)
                entity.Property(e => e.JoinedAt).IsRequired();
            else
                entity.Property(e => e.JoinedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            // Create unique index on UserId and OrganizationId combination
            entity.HasIndex(e => new { e.UserId, e.OrganizationId }).IsUnique();
            
            // Configure relationships
            entity.HasOne(e => e.User)
                .WithMany(u => u.UserOrganizations)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
                
            entity.HasOne(e => e.Organization)
                .WithMany(o => o.UserOrganizations)
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure RefreshToken entity
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Token).IsRequired();
            entity.Property(e => e.ExpiresAt).IsRequired();
            entity.Property(e => e.IsRevoked).IsRequired().HasDefaultValue(false);
            
            if (isInMemory)
                entity.Property(e => e.CreatedAt).IsRequired();
            else
                entity.Property(e => e.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            // Create unique index on Token
            entity.HasIndex(e => e.Token).IsUnique();
            
            // Configure relationship
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure TimeEntry entity
        modelBuilder.Entity<TimeEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.StartTime).IsRequired();
            entity.Property(e => e.IsRunning).IsRequired().HasDefaultValue(false);

            if (isInMemory)
                entity.Property(e => e.CreatedAt).IsRequired();
            else
                entity.Property(e => e.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Index for quick lookup of running entries per user
            entity.HasIndex(e => new { e.UserId, e.IsRunning });

            // Configure relationships
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure PauseRule entity
        modelBuilder.Entity<PauseRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MinHours).IsRequired();
            entity.Property(e => e.PauseMinutes).IsRequired();

            if (isInMemory)
                entity.Property(e => e.CreatedAt).IsRequired();
            else
                entity.Property(e => e.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.OrganizationId, e.MinHours }).IsUnique();

            entity.HasOne(e => e.Organization)
                .WithMany(o => o.PauseRules)
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure OrgRequest entity
        modelBuilder.Entity<OrgRequest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Message).HasMaxLength(500);
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.Type).IsRequired();
            entity.Property(e => e.RequestData).HasMaxLength(2000);

            if (isInMemory)
                entity.Property(e => e.CreatedAt).IsRequired();
            else
                entity.Property(e => e.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Index: user+org+type+status for fast lookups
            entity.HasIndex(e => new { e.UserId, e.OrganizationId, e.Type, e.Status });

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.RespondedByUser)
                .WithMany()
                .HasForeignKey(e => e.RespondedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
