using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Npgsql;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>Planned appointments (issue #83) on real Postgres: row-level security like TimeEntries (migration <c>Appointments</c>).</summary>
[Collection(PostgresCollection.Name)]
public class AppointmentPostgresTests(PostgresApiFactory factory)
{
    private async Task<NpgsqlConnection> OpenAsAsync(int userId)
    {
        var conn = new NpgsqlConnection(factory.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.user_id', @u, false), set_config('app.bypass_rls', 'off', false)";
        cmd.Parameters.AddWithValue("u", userId.ToString());
        await cmd.ExecuteNonQueryAsync();
        return conn;
    }

    private static async Task<long> CountAsync(NpgsqlConnection conn, int orgId)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM \"Appointments\" WHERE \"OrganizationId\" = @org";
        cmd.Parameters.AddWithValue("org", orgId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Appointments_AreInvisibleAndUnwritable_ForOtherOrganizations()
    {
        var a = await TestHelpers.OrgWithMemberAsync(factory, "pg-appt-a", new { }, "pg.test");
        var b = await TestHelpers.OrgWithMemberAsync(factory, "pg-appt-b", new { }, "pg.test");
        foreach (var (member, slug) in new[] { (a.Member, a.Slug), (b.Member, b.Slug) })
            (await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/appointments", new { date = "2026-03-03", startTime = "10:00", endTime = "12:00", note = "Arzt" }))
                .StatusCode.Should().Be(HttpStatusCode.Created);

        await using (var asA = await OpenAsAsync(a.MemberId))
        {
            (await CountAsync(asA, a.OrgId)).Should().Be(1);
            (await CountAsync(asA, b.OrgId)).Should().Be(0);

            await using var insert = asA.CreateCommand();
            insert.CommandText = """
                INSERT INTO "Appointments" ("OrganizationId", "UserId", "Date", "CreatedAt")
                VALUES (@org, @user, DATE '2026-03-04', now())
                """;
            insert.Parameters.AddWithValue("org", b.OrgId);
            insert.Parameters.AddWithValue("user", b.MemberId);
            var act = async () => await insert.ExecuteNonQueryAsync();
            (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        }

        // The API reads through RLS as the colleague in the same organization (owner of A sees the member's slot and note)
        var list = await a.Owner.GetFromJsonAsync<List<System.Text.Json.JsonElement>>($"/api/v1/organizations/{a.Slug}/appointments?from=2026-03-01&to=2026-03-31");
        list.Should().ContainSingle().Which.GetProperty("note").GetString().Should().Be("Arzt");
    }
}
