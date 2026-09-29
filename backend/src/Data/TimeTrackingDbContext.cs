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
    public DbSet<Holiday> Holidays { get; set; }
    public DbSet<AbsenceDay> AbsenceDays { get; set; }
    public DbSet<AbsenceTypeDefinition> AbsenceTypeDefinitions { get; set; }
    public DbSet<WorkSchedule> WorkSchedules { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<Group> Groups { get; set; }
    public DbSet<GroupMember> GroupMembers { get; set; }

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

            // Global query filter: automatically exclude soft-deleted organizations
            entity.HasQueryFilter(e => e.IsActive);
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

            // Global query filter: automatically exclude soft-deleted memberships
            entity.HasQueryFilter(e => e.IsActive);
            
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

            // Hide entries of soft-deleted organizations; personal entries (no org) stay visible
            entity.HasQueryFilter(e => e.OrganizationId == null || e.Organization!.IsActive);
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

            // Keep dependent rows hidden when the parent organization is soft-deleted.
            entity.HasQueryFilter(e => e.Organization.IsActive);

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
            entity.Property(e => e.RequestData).HasMaxLength(50000);

            if (isInMemory)
                entity.Property(e => e.CreatedAt).IsRequired();
            else
                entity.Property(e => e.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Index: user+org+type+status for fast lookups
            entity.HasIndex(e => new { e.UserId, e.OrganizationId, e.Type, e.Status });

            // Keep dependent rows hidden when the parent organization is soft-deleted.
            entity.HasQueryFilter(e => e.Organization.IsActive);

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

            entity.HasOne(e => e.Group)
                .WithMany()
                .HasForeignKey(e => e.GroupId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure Holiday entity
        modelBuilder.Entity<Holiday>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Date).IsRequired();

            if (isInMemory)
                entity.Property(e => e.CreatedAt).IsRequired();
            else
                entity.Property(e => e.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.OrganizationId, e.Date }).IsUnique();

            // Keep dependent rows hidden when the parent organization is soft-deleted.
            entity.HasQueryFilter(e => e.Organization.IsActive);

            entity.HasOne(e => e.Organization)
                .WithMany(o => o.Holidays)
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure AbsenceDay entity
        modelBuilder.Entity<AbsenceDay>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Note).HasMaxLength(500);
            entity.Property(e => e.Date).IsRequired();
            entity.Property(e => e.Type).IsRequired();

            if (isInMemory)
                entity.Property(e => e.CreatedAt).IsRequired();
            else
                entity.Property(e => e.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.UserId, e.OrganizationId, e.Date }).IsUnique();

            // Keep dependent rows hidden when the parent organization is soft-deleted.
            entity.HasQueryFilter(e => e.Organization.IsActive);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CustomType)
                .WithMany()
                .HasForeignKey(e => e.CustomTypeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure AbsenceTypeDefinition entity (org-defined custom absence types)
        modelBuilder.Entity<AbsenceTypeDefinition>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Color).HasMaxLength(20);
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);

            if (isInMemory)
                entity.Property(e => e.CreatedAt).IsRequired();
            else
                entity.Property(e => e.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.OrganizationId, e.Name }).IsUnique();

            // Keep dependent rows hidden when the parent organization is soft-deleted.
            entity.HasQueryFilter(e => e.Organization.IsActive);

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure WorkSchedule entity
        modelBuilder.Entity<WorkSchedule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ValidFrom).IsRequired();

            if (isInMemory)
                entity.Property(e => e.CreatedAt).IsRequired();
            else
                entity.Property(e => e.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.UserId, e.OrganizationId, e.ValidFrom }).IsUnique();

            // Keep dependent rows hidden when the parent organization is soft-deleted.
            entity.HasQueryFilter(e => e.Organization.IsActive);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Notification entity
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Message).IsRequired().HasMaxLength(2000);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(100);
            entity.Property(e => e.IsRead).IsRequired().HasDefaultValue(false);

            if (isInMemory)
                entity.Property(e => e.CreatedAt).IsRequired();
            else
                entity.Property(e => e.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.UserId, e.IsRead });

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure Group entity
        modelBuilder.Entity<Group>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);

            if (isInMemory)
            {
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();
            }
            else
            {
                entity.Property(e => e.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.UpdatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
            }

            entity.HasQueryFilter(e => e.IsActive && e.Organization.IsActive);

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure GroupMember entity
        modelBuilder.Entity<GroupMember>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Role).IsRequired();

            if (isInMemory)
                entity.Property(e => e.JoinedAt).IsRequired();
            else
                entity.Property(e => e.JoinedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

            // A user can only be in a group once
            entity.HasIndex(e => new { e.GroupId, e.UserId }).IsUnique();

            entity.HasOne(e => e.Group)
                .WithMany(g => g.Members)
                .HasForeignKey(e => e.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
