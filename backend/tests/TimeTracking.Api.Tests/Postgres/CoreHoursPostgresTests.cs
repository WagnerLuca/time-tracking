using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// The core hours background check on real Postgres: it runs outside a request (RLS bypass) and
/// writes its rows, admins read them back under RLS, other organizations see none.
/// </summary>
[Collection(PostgresCollection.Name)]
public class CoreHoursPostgresTests
{
    private readonly PostgresApiFactory _factory;

    public CoreHoursPostgresTests(PostgresApiFactory factory) => _factory = factory;

    [Fact]
    public async Task BackgroundCheck_WritesUnderBypass_VisibleToOrgOnly()
    {
        var (owner, _, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "pg-core",
            new { coreHoursEnabled = true, coreHoursStart = "09:00", coreHoursEnd = "15:00", memberTimeEntryVisibility = true }, "pg.test");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            db.TimeEntries.Add(new TimeEntry
            {
                UserId = memberId, OrganizationId = orgId,
                StartTime = new DateTime(2026, 9, 15, 7, 0, 0, DateTimeKind.Utc), EndTime = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc)   // Tue 15.09., 09:00-14:00 Berlin (UTC+2)
            });
            await db.SaveChangesAsync();

            var now = new DateTime(2026, 9, 15, 14, 0, 0, DateTimeKind.Utc);   // 16:00 local
            (await CoreHours.CheckDueAsync(db, now)).Should().Be(2);   // the member left early, the owner tracked nothing
            await CoreHours.CheckDueAsync(db, now.AddMinutes(1));
            (await db.CoreHoursChecks.CountAsync(c => c.OrganizationId == orgId)).Should().Be(2);
            (await db.Notifications.CountAsync(n => n.Type == CoreHours.NotificationType && n.RelatedEntityId == memberId)).Should().Be(2);
        }

        var days = await owner.GetFromJsonAsync<List<JsonElement>>($"/api/v1/organizations/{slug}/members/{memberId}/compliance?from=2026-09-01&to=2026-09-30");
        days!.Single().GetProperty("coreHoursIssues").EnumerateArray().Select(i => i.GetString()).Should().Equal("early");

        var (_, outsider) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-core-out@pg.test");
        (await CountAsUserAsync(outsider.User.Id, orgId)).Should().Be(0);
        (await CountAsUserAsync(memberId, orgId)).Should().Be(2);   // own row + the org's
    }

    private async Task<long> CountAsUserAsync(int userId, int orgId)
    {
        await using var conn = new NpgsqlConnection(_factory.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT set_config('app.user_id', @u, false), set_config('app.bypass_rls', 'off', false);
            SELECT count(*) FROM "CoreHoursChecks" WHERE "OrganizationId" = @org;
            """;
        cmd.Parameters.AddWithValue("u", userId.ToString());
        cmd.Parameters.AddWithValue("org", orgId);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.NextResultAsync();
        await reader.ReadAsync();
        return reader.GetInt64(0);
    }
}
