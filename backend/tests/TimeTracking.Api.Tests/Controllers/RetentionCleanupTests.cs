using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// The retention cleanup of <see cref="RefreshTokenCleanupService"/>: one statement per table on Postgres
/// (<c>ExecuteDelete</c>), load + remove on InMemory. Shared by this InMemory test and <c>PerformancePhase1PostgresTests</c>.
/// </summary>
public static class RetentionScenario
{
    public sealed record Ids(int[] OldTokens, int[] KeptTokens, int[] OldNotifications, int[] KeptNotifications,
        long[] OldPunches, long[] KeptPunches, int[] OldLinks, int[] KeptLinks, int[] OldInvitations, int[] KeptInvitations);

    /// <summary>Old and fresh rows of every table the cleanup touches, for a new user and organization.</summary>
    public static async Task<Ids> SeedAsync(IServiceProvider services, string prefix)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
        var now = DateTime.UtcNow;
        var user = new User { Email = $"{prefix}@t.test", FirstName = "Ret", LastName = "Ention" };
        var org = new Organization { Name = prefix, Slug = prefix };
        db.AddRange(user, org);
        db.UserOrganizations.Add(new UserOrganization { User = user, Organization = org, Role = OrganizationRole.Owner });
        await db.SaveChangesAsync();

        RefreshToken Token(string key, DateTime expires, bool revoked = false) => new()
        {
            UserId = user.Id, Token = $"{prefix}-{key}", ExpiresAt = expires, IsRevoked = revoked, RevokedAt = revoked ? now.AddDays(-1) : null
        };
        var oldTokens = new[] { Token("expired", now.AddMinutes(-1)), Token("expired-revoked", now.AddDays(-3), revoked: true) };
        // Revoked but unexpired: reuse detection still needs it
        var keptTokens = new[] { Token("fresh", now.AddDays(7)), Token("revoked-unexpired", now.AddDays(2), revoked: true) };

        Notification Note(bool read, int ageDays) => new()
        {
            UserId = user.Id, OrganizationId = org.Id, Title = "t", Message = "m", Type = "Test", IsRead = read, CreatedAt = now.AddDays(-ageDays)
        };
        var oldNotes = new[] { Note(true, 181), Note(true, 400) };
        var keptNotes = new[] { Note(false, 400), Note(true, 179), Note(false, 1) };

        TerminalPunch Punch(int ageDays) => new()
        {
            OrganizationId = org.Id, UserId = user.Id, CardHash = "h", CardHint = "1234", Result = PunchResult.Started,
            ScannedAt = now.AddDays(-ageDays), ReceivedAt = now.AddDays(-ageDays)
        };
        var oldPunches = new[] { Punch(401), Punch(800) };
        var keptPunches = new[] { Punch(399), Punch(0) };

        UserToken Link(DateTime expires, DateTime? used) => new()
        {
            UserId = user.Id, Purpose = default, TokenHash = Guid.NewGuid().ToString("N"), ExpiresAt = expires, UsedAt = used
        };
        var oldLinks = new[] { Link(now.AddDays(-31), null), Link(now.AddDays(10), now.AddDays(-31)) };
        var keptLinks = new[] { Link(now.AddDays(-29), null), Link(now.AddDays(1), now.AddDays(-1)) };

        OrgInvitation Invite(DateTime expires, DateTime? accepted = null, DateTime? revoked = null) => new()
        {
            OrganizationId = org.Id, Email = $"{Guid.NewGuid():N}@t.test", TokenHash = Guid.NewGuid().ToString("N"), InvitedByUserId = user.Id,
            ExpiresAt = expires, AcceptedAt = accepted, RevokedAt = revoked
        };
        var oldInvites = new[] { Invite(now.AddDays(-31)), Invite(now.AddDays(5), accepted: now.AddDays(-40)), Invite(now.AddDays(5), revoked: now.AddDays(-35)) };
        var keptInvites = new[] { Invite(now.AddDays(5)), Invite(now.AddDays(5), accepted: now.AddDays(-2)) };

        db.RefreshTokens.AddRange(oldTokens.Concat(keptTokens));
        db.Notifications.AddRange(oldNotes.Concat(keptNotes));
        db.TerminalPunches.AddRange(oldPunches.Concat(keptPunches));
        db.UserTokens.AddRange(oldLinks.Concat(keptLinks));
        db.OrgInvitations.AddRange(oldInvites.Concat(keptInvites));
        await db.SaveChangesAsync();
        return new Ids(oldTokens.Select(t => t.Id).ToArray(), keptTokens.Select(t => t.Id).ToArray(),
            oldNotes.Select(n => n.Id).ToArray(), keptNotes.Select(n => n.Id).ToArray(),
            oldPunches.Select(p => p.Id).ToArray(), keptPunches.Select(p => p.Id).ToArray(),
            oldLinks.Select(l => l.Id).ToArray(), keptLinks.Select(l => l.Id).ToArray(),
            oldInvites.Select(i => i.Id).ToArray(), keptInvites.Select(i => i.Id).ToArray());
    }

    /// <summary>
    /// One cleanup pass, as the hosted service runs it every 6 hours (the service would only run it on start), with the
    /// app's own scope factory (system work, no HttpContext).
    /// </summary>
    public static async Task RunAsync(IServiceProvider services)
    {
        var service = new RefreshTokenCleanupService(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RefreshTokenCleanupService>.Instance);
        await service.CleanupTokensAsync(CancellationToken.None);
    }

    public static async Task AssertAsync(IServiceProvider services, Ids ids)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
        var allTokens = ids.OldTokens.Concat(ids.KeptTokens).ToArray();
        (await db.RefreshTokens.Where(t => allTokens.Contains(t.Id)).Select(t => t.Id).ToListAsync())
            .Should().BeEquivalentTo(ids.KeptTokens, "expired tokens go, unexpired (also revoked) ones stay");
        var allNotes = ids.OldNotifications.Concat(ids.KeptNotifications).ToArray();
        (await db.Notifications.IgnoreQueryFilters().Where(n => allNotes.Contains(n.Id)).Select(n => n.Id).ToListAsync())
            .Should().BeEquivalentTo(ids.KeptNotifications, "read notifications older than 180 days go, unread ones stay");
        var allPunches = ids.OldPunches.Concat(ids.KeptPunches).ToArray();
        (await db.TerminalPunches.IgnoreQueryFilters().Where(p => allPunches.Contains(p.Id)).Select(p => p.Id).ToListAsync())
            .Should().BeEquivalentTo(ids.KeptPunches, "punches older than 400 days go");
        var allLinks = ids.OldLinks.Concat(ids.KeptLinks).ToArray();
        (await db.UserTokens.Where(l => allLinks.Contains(l.Id)).Select(l => l.Id).ToListAsync())
            .Should().BeEquivalentTo(ids.KeptLinks, "links expired or used more than 30 days ago go");
        var allInvites = ids.OldInvitations.Concat(ids.KeptInvitations).ToArray();
        (await db.OrgInvitations.IgnoreQueryFilters().Where(i => allInvites.Contains(i.Id)).Select(i => i.Id).ToListAsync())
            .Should().BeEquivalentTo(ids.KeptInvitations, "invitations expired, accepted or revoked more than 30 days ago go");
    }
}

public class RetentionCleanupTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public RetentionCleanupTests(TimeTrackingApiFactory factory) => _factory = factory;

    [Fact]
    public async Task InMemory_OldRowsAreDeleted_FreshAndUnreadAreKept_AndASecondPassIsANoOp()
    {
        var ids = await RetentionScenario.SeedAsync(_factory.Services, "ret-mem");
        await RetentionScenario.RunAsync(_factory.Services);
        await RetentionScenario.AssertAsync(_factory.Services, ids);
        await RetentionScenario.RunAsync(_factory.Services);
        await RetentionScenario.AssertAsync(_factory.Services, ids);
    }
}
