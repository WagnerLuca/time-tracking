using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// Closed months on real Postgres under row-level security: the guard reads the (RLS-free)
/// closed months inside a member's request, refuses the member, and lets the admin's corrected
/// entry, its flagged history row and the audit event commit in one transaction.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ClosedPeriodPostgresTests
{
    private readonly PostgresApiFactory _factory;

    public ClosedPeriodPostgresTests(PostgresApiFactory factory) => _factory = factory;

    [Fact]
    public async Task MemberRefused_AdminCorrectsWithReason_HistoryAndAuditCommitUnderRls()
    {
        var (owner, member, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "pg-closed",
            new { memberTimeEntryVisibility = true }, "pg.test");
        var lastMonth = ClosedPeriods.MonthOf(OrgClock.For((string?)null).Today()).AddMonths(-1);
        var start = lastMonth.AddDays(13).ToDateTime(new TimeOnly(6, 0), DateTimeKind.Utc);

        var manual = await member.PostAsJsonAsync("/api/v1/TimeTracking/manual", new { startTime = start, endTime = start.AddHours(8), organizationId = orgId });
        manual.StatusCode.Should().Be(HttpStatusCode.Created);
        var entryId = (await manual.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("entry").GetProperty("id").GetInt32();
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{slug}/closed-periods", new { from = lastMonth, to = lastMonth })).EnsureSuccessStatusCode();

        var refused = await member.PutAsJsonAsync($"/api/v1/TimeTracking/{entryId}", new { endTime = start.AddHours(7) });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("period.closed");

        var correction = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/organizations/{slug}/members/{memberId}/entries/{entryId}")
        {
            Content = JsonContent.Create(new { endTime = start.AddHours(9) })
        };
        correction.Headers.Add("X-Change-Reason", Uri.EscapeDataString("Nachtrag für Lohnlauf"));
        (await owner.SendAsync(correction)).EnsureSuccessStatusCode();

        // The member (owner of the entry) sees the flagged history row through RLS
        var history = await member.GetFromJsonAsync<JsonElement>($"/api/v1/TimeTracking/{entryId}/history");
        var last = history.EnumerateArray().Last();
        last.GetProperty("inClosedPeriod").GetBoolean().Should().BeTrue();
        last.GetProperty("reason").GetString().Should().Be("Nachtrag für Lohnlauf");
        var events = (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/closed-periods")).GetProperty("events");
        events[0].GetProperty("kind").GetString().Should().Be("AdminChange");
    }

    private TimeTrackingDbContext Db(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<TimeTrackingDbContext>().UseNpgsql(
            new NpgsqlConnectionStringBuilder(_factory.SuperuserConnectionString) { Database = "timetracking" }.ConnectionString);
        if (interceptor != null) options.AddInterceptors(interceptor);
        return new TimeTrackingDbContext(options.Options);
    }

    /// <summary>Lets another admin close <paramref name="month"/> just before the first save goes out.</summary>
    private sealed class ConcurrentClose(Func<TimeTrackingDbContext> db, int orgId, DateOnly month) : SaveChangesInterceptor
    {
        private bool _done;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!_done)
            {
                _done = true;
                await using var other = db();
                other.ClosedPeriods.Add(new ClosedPeriod { OrganizationId = orgId, Month = month, ClosedAt = DateTime.UtcNow });
                await other.SaveChangesAsync(cancellationToken);
            }
            return result;
        }
    }

    [Fact]
    public async Task OverlappingCloses_StillCloseEveryMonth()
    {
        var (_, _, slug, orgId, _) = await TestHelpers.OrgWithMemberAsync(_factory, "pg-closed-race", new { }, "pg.test");
        int ownerId;
        await using (var setup = Db())
            ownerId = await setup.UserOrganizations.Where(uo => uo.OrganizationId == orgId && uo.Role == OrganizationRole.Owner)
                .Select(uo => uo.UserId).SingleAsync();
        var last = ClosedPeriods.MonthOf(OrgClock.For((string?)null).Today()).AddMonths(-1);

        await using var db = Db(new ConcurrentClose(() => Db(), orgId, last.AddMonths(-1)));
        var result = await new ClosedPeriodService(db).CloseAsync(slug, ownerId, new CloseMonthsRequest { From = last.AddMonths(-2), To = last });

        result.Data!.Months.Should().Equal(last.AddMonths(-2), last.AddMonths(-1), last);
        result.Data.Events.Where(e => e.Kind == "Closed").Select(e => e.Month).Should().BeEquivalentTo([last.AddMonths(-2), last],
            "this close added only the months still open");
    }
}
