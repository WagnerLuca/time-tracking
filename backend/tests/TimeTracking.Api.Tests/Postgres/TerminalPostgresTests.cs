using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TimeTracking.Api.Data;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// The terminal on real Postgres: the anonymous punch writes the protocol and the member's
/// entries under row-level security, the unique indexes behind idempotency and cards, and the
/// migration that turned the old single badge IDs into hashed cards.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TerminalPostgresTests
{
    private readonly PostgresApiFactory _factory;

    public TerminalPostgresTests(PostgresApiFactory factory) => _factory = factory;

    [Fact]
    public async Task UnknownCard_Adopted_ThenOfflineQueue_DoubleScan_AndReplay_UnderRls()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-term-owner@pg.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Terminal Org", "pg-term-org");
        var (member, m) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-term-m@pg.test", "Pia", "Punch");
        (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, m)).EnsureSuccessStatusCode();
        var key = (await (await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/api-keys", new { name = "Tor" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString();
        var terminal = _factory.CreateClient();
        terminal.DefaultRequestHeaders.Add("X-Api-Key", key);
        Task<HttpResponseMessage> Punch(string badge, DateTime? scannedAt = null, string? id = null) =>
            terminal.PostAsJsonAsync("/api/v1/terminal/punch", new { badgeId = badge, scannedAt, clientPunchId = id });

        (await Punch("aa:bb:cc:dd")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var unknown = await owner.GetFromJsonAsync<List<JsonElement>>($"/api/v1/organizations/{org.Slug}/terminal/unknown-cards");
        var punchId = unknown!.Single().GetProperty("punchId").GetInt64();
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/members/{m.User.Id}/cards", new { fromPunchId = punchId }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        // The same card typed in differently is still the same card
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/members/{m.User.Id}/cards", new { cardId = "AABBCCDD" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var start = DateTime.UtcNow.AddHours(-3);
        (await Punch("AABBCCDD", start, "pg-1")).StatusCode.Should().Be(HttpStatusCode.OK);
        var repeat = await (await Punch("AABBCCDD", start.AddSeconds(20), "pg-2")).Content.ReadFromJsonAsync<JsonElement>();
        repeat.GetProperty("repeated").GetBoolean().Should().BeTrue();
        var stop = await (await Punch("AABBCCDD", start.AddHours(2), "pg-3")).Content.ReadFromJsonAsync<JsonElement>();
        stop.GetProperty("action").GetString().Should().Be("Stopped");
        var replay = await Punch("AABBCCDD", start.AddHours(2), "pg-3");
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("action").GetString().Should().Be("Stopped");

        var history = await member.GetFromJsonAsync<JsonElement>($"/api/v1/TimeTracking?organizationId={org.Id}");
        var entry = history.GetProperty("items").EnumerateArray().Single();
        entry.GetProperty("source").GetString().Should().Be("Terminal");
        entry.GetProperty("terminalStartName").GetString().Should().Be("Tor");
        entry.GetProperty("terminalStopName").GetString().Should().Be("Tor");
        entry.GetProperty("durationMinutes").GetDouble().Should().BeApproximately(120, 0.1);

        var protocol = await owner.GetFromJsonAsync<List<JsonElement>>($"/api/v1/organizations/{org.Slug}/terminal/punches");
        protocol!.Select(p => p.GetProperty("result").GetString()).Should().Equal("UnknownCard", "Stopped", "Repeated", "Started");   // by scan time
    }

    [Fact]
    public async Task Migration_MovesOldBadgeIds_IntoHashedCards()
    {
        const string database = "tt_badge_migration";
        await using (var admin = new NpgsqlConnection(_factory.SuperuserConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }
        var connection = new NpgsqlConnectionStringBuilder(_factory.SuperuserConnectionString) { Database = database }.ConnectionString;
        await using var db = new TimeTrackingDbContext(new DbContextOptionsBuilder<TimeTrackingDbContext>().UseNpgsql(connection).Options);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync("20261001192104_PauseIsManual");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Users" ("Id", "Email", "EmailConfirmed", "FirstName", "LastName", "SecurityStamp", "TwoFactorEnabled", "TwoFactorFailedAttempts", "UpdatedAt")
            VALUES (1, 'a@m.test', true, 'A', 'A', 's', false, 0, now()), (2, 'b@m.test', true, 'B', 'B', 's', false, 0, now()),
                   (3, 'c@m.test', true, 'C', 'C', 's', false, 0, now());
            INSERT INTO "Organizations" ("Id", "Name", "Slug", "AbsenceSelfServiceMode", "AutoPauseEnabled", "BusinessDayEnd", "CsvImportMode",
                "DefaultVacationDays", "EditPastEntriesMode", "EditPauseMode", "InitialOvertimeMode", "JoinPolicy", "MemberTimeEntryVisibility",
                "Require2fa", "SickDayVisibility", "UpdatedAt", "VacationVisibility", "WorkScheduleChangeMode")
            VALUES (1, 'Org', 'org', 0, true, '00:00', 0, 30, 0, 0, 0, 0, false, false, 0, now(), 0, 0);
            INSERT INTO "UserOrganizations" ("UserId", "OrganizationId", "Role", "InitialOvertimeHours", "VacationDaysPerYear", "BadgeId", "IsActive")
            VALUES (1, 1, 2, 0, 30, ' 04:a2 3b ', true), (2, 1, 0, 0, 30, NULL, true), (3, 1, 0, 0, 30, 'GONE', false);
            """);

        await migrator.MigrateAsync();

        var cards = await db.BadgeCards.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        cards.Should().ContainSingle();
        cards[0].UserId.Should().Be(1);
        cards[0].CardHash.Should().Be(CardIds.Hash("04A23B"));
        cards[0].CardHint.Should().Be("A23B");
        cards[0].IsBlocked.Should().BeFalse();
    }
}
