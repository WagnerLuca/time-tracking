using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// Regression tests for bugs that only surface on real Postgres (EF InMemory ignores
/// unique indexes and DateTime kinds): each of these returned 500 or corrupted data before.
/// </summary>
[Collection(PostgresCollection.Name)]
public class PostgresIntegrityTests
{
    private readonly PostgresApiFactory _factory;

    public PostgresIntegrityTests(PostgresApiFactory factory) => _factory = factory;

    [Fact]
    public async Task DateTimesWithoutTimeZone_AreAcceptedAsUtc()
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-utc@pg.test");
        (await client.PostAsJsonAsync("/api/v1/TimeTracking/start", new { description = "utc" })).EnsureSuccessStatusCode();
        var stopped = await (await client.PostAsJsonAsync("/api/v1/TimeTracking/stop", (object?)null)).Content.ReadFromJsonAsync<JsonElement>();
        var id = stopped.GetProperty("id").GetInt32();

        // No "Z": Kind=Unspecified, which Npgsql refuses for timestamptz unless normalized
        var update = await client.PutAsync($"/api/v1/TimeTracking/{id}",
            new StringContent("{\"startTime\":\"2026-03-03T08:00:00\",\"endTime\":\"2026-03-03T12:00:00\"}", Encoding.UTF8, "application/json"));
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        (await update.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("durationMinutes").GetDouble().Should().Be(240);

        // Date-only query parameters are Unspecified as well
        var history = await client.GetAsync("/api/v1/TimeTracking?from=2026-03-01&to=2026-03-31");
        history.StatusCode.Should().Be(HttpStatusCode.OK);
        (await history.Content.ReadAsStringAsync()).Should().Contain("utc");
    }

    [Fact]
    public async Task ConcurrentStarts_LeaveExactlyOneRunningTimer()
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-race@pg.test");

        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => client.PostAsJsonAsync("/api/v1/TimeTracking/start", new { description = "race" })));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.Conflict);
        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/TimeTracking?limit=50");
        page.GetProperty("items").EnumerateArray().Count(e => e.GetProperty("isRunning").GetBoolean()).Should().Be(1);
    }

    [Fact]
    public async Task MemberWhoLeft_CanJoinAgain()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-rejoin-owner@pg.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Rejoin Org", "pg-rejoin-org");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { joinPolicy = 2 /* Allowed */ })).EnsureSuccessStatusCode();

        var (member, m) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-rejoin-m@pg.test");
        (await member.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/requests", new { type = 0 })).EnsureSuccessStatusCode();
        (await owner.DeleteAsync($"/api/v1/Organizations/{org.Slug}/members/{m.User.Id}")).EnsureSuccessStatusCode();

        // The soft-deleted membership row still holds the unique (UserId, OrganizationId) index
        var again = await member.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/requests", new { type = 0 });
        again.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        again.IsSuccessStatusCode.Should().BeTrue();
        (await member.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{org.Slug}")).GetProperty("slug").GetString().Should().Be(org.Slug);
    }

    [Fact]
    public async Task SlugOfDeletedOrganization_CanBeReused()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-slug@pg.test");
        await TestHelpers.CreateOrganizationAsync(owner, "First", "pg-reused-slug");
        (await owner.DeleteAsync("/api/v1/Organizations/pg-reused-slug")).EnsureSuccessStatusCode();

        var second = await owner.PostAsJsonAsync("/api/v1/Organizations", new { name = "Second", slug = "pg-reused-slug" });
        second.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Email_IsUniqueRegardlessOfCase()
    {
        var client = _factory.CreateClient();
        await TestHelpers.RegisterAsync(client, "Case@PG.test", "StrongPass1!", "A", "B");
        var dup = await client.PostAsJsonAsync("/api/v1/Auth/register",
            new { email = "case@pg.TEST", password = "StrongPass1!", firstName = "C", lastName = "D" });
        dup.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConcurrentApprovals_ApplyOnlyOnce()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-approve-owner@pg.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Approve Org", "pg-approve-org");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { csvImportMode = 1 /* RequiresApproval */ })).EnsureSuccessStatusCode();
        var (member, m) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-approve-m@pg.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, m)).EnsureSuccessStatusCode();

        var data = JsonSerializer.Serialize(new
        {
            entries = new[] { new { date = "2026-03-02", startTime = "08:00", endTime = "12:00", pauseMinutes = 0, description = "once" } }
        });
        var id = (await (await member.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/requests", new { type = 5, requestData = data }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        // Status is a concurrency token: only one of several simultaneous approvals may apply the import
        var results = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => owner.PutAsJsonAsync($"/api/v1/organizations/{org.Slug}/requests/{id}", new { accept = true })));
        results.Count(r => r.IsSuccessStatusCode).Should().Be(1);
        results.Should().OnlyContain(r => r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.Conflict || r.StatusCode == HttpStatusCode.BadRequest);

        var history = await member.GetFromJsonAsync<JsonElement>("/api/v1/TimeTracking?limit=50");
        history.GetProperty("items").EnumerateArray().Count(e => e.GetProperty("description").GetString() == "once").Should().Be(1);
    }

    [Fact]
    public async Task PersonnelNumber_IsFreedWhenTheMemberIsRemoved()
    {
        var (owner, _, slug, _, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "pg-pn", new { }, "pg.test");
        string Url(int id) => $"/api/v1/organizations/{slug}/members/{id}/personnel-number";
        (await owner.PutAsJsonAsync(Url(memberId), new { personnelNumber = "P-7" })).EnsureSuccessStatusCode();
        (await owner.DeleteAsync($"/api/v1/Organizations/{slug}/members/{memberId}")).EnsureSuccessStatusCode();

        // The unique index (org, number) no longer holds it on the inactive membership row
        var (_, next) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pg-pn-next@pg.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, slug, next)).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync(Url(next.User.Id), new { personnelNumber = "P-7" })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Gives <paramref name="number"/> to another member just before the first save goes out.</summary>
    private sealed class ConcurrentNumber(string connection, int orgId, int otherUserId, string number) : SaveChangesInterceptor
    {
        private bool _done;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!_done)
            {
                _done = true;
                await using var other = new TimeTrackingDbContext(new DbContextOptionsBuilder<TimeTrackingDbContext>().UseNpgsql(connection).Options);
                (await other.UserOrganizations.SingleAsync(uo => uo.OrganizationId == orgId && uo.UserId == otherUserId, cancellationToken))
                    .PersonnelNumber = number;
                await other.SaveChangesAsync(cancellationToken);
            }
            return result;
        }
    }

    [Fact]
    public async Task PersonnelNumber_TakenAtTheSameTime_IsAConflictNotAnError()
    {
        var (_, _, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "pg-pn-race", new { }, "pg.test");
        var connection = new NpgsqlConnectionStringBuilder(_factory.SuperuserConnectionString) { Database = "timetracking" }.ConnectionString;
        int ownerId;
        await using (var setup = new TimeTrackingDbContext(new DbContextOptionsBuilder<TimeTrackingDbContext>().UseNpgsql(connection).Options))
            ownerId = await setup.UserOrganizations.Where(uo => uo.OrganizationId == orgId && uo.UserId != memberId).Select(uo => uo.UserId).SingleAsync();

        await using var db = new TimeTrackingDbContext(new DbContextOptionsBuilder<TimeTrackingDbContext>().UseNpgsql(connection)
            .AddInterceptors(new ConcurrentNumber(connection, orgId, ownerId, "P-9")).Options);
        var result = await new OrganizationService(db, NullLogger<OrganizationService>.Instance)
            .SetMemberPersonnelNumberAsync(slug, ownerId, memberId, new SetPersonnelNumberRequest { PersonnelNumber = "P-9" });
        result.ErrorType.Should().Be(ServiceErrorType.Conflict);
        ErrorCodes.For(result.ErrorMessage!).Should().Be("org.personnelNumberTaken");
    }

    [Fact]
    public async Task WithdrawRacingApprovalAndDoubleClick_OnlyOneWins()
    {
        var (owner, member, slug, _, _) = await TestHelpers.OrgWithMemberAsync(_factory, "pg-withdraw", new { initialOvertimeMode = 1 }, "pg.test");
        var id = (await (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests", new { type = 3, requestData = "2" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        // A double click on "withdraw" plus an approver accepting at the same moment: Status is the concurrency token
        var results = await Task.WhenAll(
            member.PostAsync($"/api/v1/organizations/{slug}/requests/{id}/withdraw", null),
            member.PostAsync($"/api/v1/organizations/{slug}/requests/{id}/withdraw", null),
            owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{id}", new { accept = true }));
        results.Count(r => r.IsSuccessStatusCode).Should().Be(1);
        results.Should().OnlyContain(r => r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.Conflict || r.StatusCode == HttpStatusCode.BadRequest);
    }
}
