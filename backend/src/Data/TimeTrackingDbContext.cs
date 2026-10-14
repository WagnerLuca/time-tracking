using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Data;

public class TimeTrackingDbContext : DbContext
{
    private readonly RlsContext? _rls;

    public TimeTrackingDbContext(DbContextOptions<TimeTrackingDbContext> options)
        : base(options)
    {
    }

    /// <summary>Used by DI: the acting user is recorded in the time entry history.</summary>
    public TimeTrackingDbContext(DbContextOptions<TimeTrackingDbContext> options, RlsContext rls)
        : base(options)
    {
        _rls = rls;
    }

    /// <summary>
    /// Kind recorded for the time entry changes of the next save instead of created/updated/deleted
    /// (set by request approvals to <see cref="TimeEntryChangeKind.ApprovedRequest"/>).
    /// </summary>
    public TimeEntryChangeKind? EntryChangeKindOverride { get; set; }

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
    public DbSet<ApiKey> ApiKeys { get; set; }
    public DbSet<BadgeCard> BadgeCards { get; set; }
    public DbSet<TerminalPunch> TerminalPunches { get; set; }
    public DbSet<UserToken> UserTokens { get; set; }
    public DbSet<OrgInvitation> OrgInvitations { get; set; }
    public DbSet<TimeEntryChange> TimeEntryChanges { get; set; }
    public DbSet<CoreHoursCheck> CoreHoursChecks { get; set; }

    /// <summary>
    /// Saves, and records every change a user made to a finished time entry in
    /// <see cref="TimeEntryChanges"/> (see <see cref="TimeEntryHistory"/>). Trusted system work
    /// (seed, background jobs) is not recorded. The history rows need the ids of new entries, so
    /// they are a second save inside the same transaction.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var pending = _rls is { IsSystem: false } ? TimeEntryHistory.Capture(this, EntryChangeKindOverride) : [];
        if (pending.Count == 0) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        await using var transaction = Database.IsRelational() && Database.CurrentTransaction == null
            ? await Database.BeginTransactionAsync(cancellationToken)
            : null;
        var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        TimeEntryChanges.AddRange(TimeEntryHistory.Rows(pending, _rls!.UserId));
        await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    /// <summary>
    /// Every DateTime is stored and compared as UTC. Values arriving without a kind (e.g.
    /// "2026-03-03T10:00" from a client, or a date-only query parameter) are taken as UTC,
    /// local ones are converted. Without this, Postgres rejects them for timestamptz while
    /// the InMemory provider silently accepts them. Applies to query parameters too.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();
    }

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
        });

        // Configure Organization entity
        modelBuilder.Entity<Organization>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Slug).IsRequired().HasMaxLength(100);
            entity.Property(e => e.TimeZone).IsRequired().HasMaxLength(64).HasDefaultValue("Europe/Berlin");
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);

            if (isInMemory)
                entity.Property(e => e.CreatedAt).IsRequired();
            else
                entity.Property(e => e.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Create unique index on Slug
            // Only live organizations reserve their slug, so a deleted org's slug can be reused
            entity.HasIndex(e => e.Slug).IsUnique().HasFilter("\"IsActive\"");

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

            // Defense in depth for the API's strict enum parsing: no role outside Member..Owner
            entity.ToTable(t => t.HasCheckConstraint("CK_UserOrganizations_Role", "\"Role\" BETWEEN 0 AND 2"));

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
            // Concurrency token: two refreshes racing with the same token cannot both rotate it
            entity.Property(e => e.IsRevoked).IsRequired().HasDefaultValue(false).IsConcurrencyToken();

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

            // At most one running timer per user — enforced by the database, not just by the
            // check-then-insert in StartAsync (races, double-read RFID badges)
            entity.HasIndex(e => e.UserId).IsUnique().HasFilter("\"IsRunning\"");
            entity.HasIndex(e => new { e.UserId, e.StartTime });
            entity.HasIndex(e => new { e.OrganizationId, e.StartTime });

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
            // Concurrency token: two admins answering the same request cannot both apply it
            entity.Property(e => e.Status).IsRequired().IsConcurrencyToken();
            entity.Property(e => e.Type).IsRequired();
            entity.Property(e => e.RequestData).HasMaxLength(50000);
            entity.Property(e => e.ResponseReason).HasMaxLength(1000);

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
            entity.HasIndex(e => new { e.OrganizationId, e.Date });

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

            // Deleted (inactive) types free their name for reuse
            entity.HasIndex(e => new { e.OrganizationId, e.Name }).IsUnique().HasFilter("\"IsActive\"");

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
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });

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
            entity.ToTable(t => t.HasCheckConstraint("CK_GroupMembers_Role", "\"Role\" BETWEEN 0 AND 1"));

            entity.HasOne(e => e.Group)
                .WithMany(g => g.Members)
                .HasForeignKey(e => e.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure OrgInvitation entity (joining an organization by invitation)
        modelBuilder.Entity<OrgInvitation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
            entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(64);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => new { e.OrganizationId, e.Email });
            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(e => e.Organization.IsActive);
        });

        // Configure TimeEntryChange entity (edit history of time entries)
        modelBuilder.Entity<TimeEntryChange>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Changes).IsRequired();
            entity.HasIndex(e => new { e.TimeEntryId, e.ChangedAt });
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ChangedByUser)
                .WithMany()
                .HasForeignKey(e => e.ChangedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure UserToken entity (email verification / password reset links)
        modelBuilder.Entity<UserToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.NewEmail).HasMaxLength(256);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure ApiKey entity (organization terminal keys)
        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.KeyPrefix).IsRequired().HasMaxLength(16);
            entity.Property(e => e.KeyHash).IsRequired().HasMaxLength(64);
            entity.HasIndex(e => e.KeyHash).IsUnique();

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(e => e.Organization.IsActive);
        });

        // Configure CoreHoursCheck entity (daily core hours check per member; RLS like TimeEntries)
        modelBuilder.Entity<CoreHoursCheck>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.OrganizationId, e.UserId, e.Date }).IsUnique();
            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(e => e.Organization.IsActive);
        });

        // Configure BadgeCard entity (a member's terminal cards; only hash + hint are stored)
        modelBuilder.Entity<BadgeCard>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CardHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.CardHint).IsRequired().HasMaxLength(8);
            entity.Property(e => e.Label).HasMaxLength(100);
            entity.HasIndex(e => new { e.OrganizationId, e.CardHash }).IsUnique();
            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(e => e.Organization.IsActive);
        });

        // Configure TerminalPunch entity (punch protocol of the terminals)
        modelBuilder.Entity<TerminalPunch>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CardHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.CardHint).IsRequired().HasMaxLength(8);
            entity.Property(e => e.ClientPunchId).HasMaxLength(64);
            // Last accepted punch of a member (double scan, order check), protocol, unknown-scan budget
            entity.HasIndex(e => new { e.OrganizationId, e.UserId, e.ScannedAt });
            entity.HasIndex(e => new { e.OrganizationId, e.ScannedAt });
            entity.HasIndex(e => new { e.ApiKeyId, e.ReceivedAt });
            entity.HasIndex(e => e.TimeEntryId);
            // A terminal's punch id is applied once (idempotent offline replays)
            entity.HasIndex(e => new { e.ApiKeyId, e.ClientPunchId }).IsUnique().HasFilter("\"ClientPunchId\" IS NOT NULL");

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ApiKey)
                .WithMany()
                .HasForeignKey(e => e.ApiKeyId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.BadgeCard)
                .WithMany()
                .HasForeignKey(e => e.BadgeCardId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.TimeEntry)
                .WithMany()
                .HasForeignKey(e => e.TimeEntryId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(e => e.Organization.IsActive);
        });
    }
}
