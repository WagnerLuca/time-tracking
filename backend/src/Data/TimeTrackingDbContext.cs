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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure User entity
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
            
            // Create unique index on Email
            entity.HasIndex(e => e.Email).IsUnique();
        });
    }
}
