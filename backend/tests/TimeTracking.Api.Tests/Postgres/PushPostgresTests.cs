using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Npgsql;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Postgres;

/// <summary>
/// Push devices on real Postgres: owner-only row-level security, the unique endpoint across
/// users (hidden by RLS, refused by the index) and the dispatcher working as system.
/// </summary>
[Collection(PostgresCollection.Name)]
public class PushPostgresTests
{
    private readonly PostgresApiFactory _factory;

    public PushPostgresTests(PostgresApiFactory factory) => _factory = factory;

    private static string Key(int bytes) => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));

    private static async Task<string> SubscribeAsync(HttpClient client)
    {
        var endpoint = $"https://fcm.googleapis.com/fcm/send/{Guid.NewGuid():N}";
        (await client.PostAsJsonAsync("/api/v1/push/subscriptions", new { endpoint, p256dh = Key(65), auth = Key(16), locale = "de" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        return endpoint;
    }

    /// <summary>Runs <paramref name="sql"/> as the app role acting as <paramref name="userId"/> (subject to RLS).</summary>
    private async Task<long> AsUserAsync(int userId, string sql, string endpoint)
    {
        await using var conn = new NpgsqlConnection(_factory.AppConnectionString);
        await conn.OpenAsync();
        await using (var set = new NpgsqlCommand("SELECT set_config('app.user_id', @u, false), set_config('app.bypass_rls', 'off', false)", conn))
        {
            set.Parameters.AddWithValue("u", userId.ToString());
            await set.ExecuteNonQueryAsync();
        }
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("e", endpoint);
        var result = await cmd.ExecuteScalarAsync();
        return result is long n ? n : cmd.Statements[0].RecordsAffected;
    }

    private async Task<long> CountAsync(string endpoint)
    {
        await using var conn = new NpgsqlConnection(
            new NpgsqlConnectionStringBuilder(_factory.SuperuserConnectionString) { Database = "timetracking" }.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""SELECT count(*) FROM "PushSubscriptions" WHERE "Endpoint" = @e""", conn);
        cmd.Parameters.AddWithValue("e", endpoint);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task OtherUsersDevices_AreInvisible_AndTheirEndpointCannotBeTaken()
    {
        var (alice, a) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pushpg-alice@pg.test");
        var (bob, b) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "pushpg-bob@pg.test");
        var alicesDevice = await SubscribeAsync(alice);
        var bobsDevice = await SubscribeAsync(bob);

        (await AsUserAsync(b.User.Id, """SELECT count(*) FROM "PushSubscriptions" WHERE "Endpoint" = @e""", alicesDevice)).Should().Be(0);
        (await AsUserAsync(b.User.Id, """DELETE FROM "PushSubscriptions" WHERE "Endpoint" = @e""", alicesDevice)).Should().Be(0);
        (await AsUserAsync(a.User.Id, """SELECT count(*) FROM "PushSubscriptions" WHERE "Endpoint" = @e""", alicesDevice)).Should().Be(1);
        (await AsUserAsync(a.User.Id, """SELECT count(*) FROM "PushSubscriptions" WHERE "Endpoint" = @e""", bobsDevice)).Should().Be(0);

        // RLS hides Alice's row from the lookup, so the unique index refuses Bob's insert
        var taken = await bob.PostAsJsonAsync("/api/v1/push/subscriptions", new { endpoint = alicesDevice, p256dh = Key(65), auth = Key(16) });
        taken.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await taken.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("push.otherAccount");

        (await bob.PostAsJsonAsync("/api/v1/push/subscriptions/remove", new { endpoint = alicesDevice })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await CountAsync(alicesDevice)).Should().Be(1);
        (await CountAsync(bobsDevice)).Should().Be(1);
    }

    [Fact]
    public async Task Dispatcher_DeliversAsSystem_AndDeletesGoneDevices()
    {
        var (owner, member, slug, _, _) = await TestHelpers.OrgWithMemberAsync(_factory, "pushpg", new { autoPauseEnabled = true }, "pg.test");
        var fine = await SubscribeAsync(member);
        var gone = await SubscribeAsync(member);
        _factory.Push.Answers[gone] = PushSendResult.Gone;

        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { autoPauseEnabled = false })).EnsureSuccessStatusCode();

        (await _factory.Push.WaitForAsync(fine, p => p.Tag.StartsWith("SettingsChanged"))).Should().ContainSingle();
        for (var i = 0; i < 250 && await CountAsync(gone) > 0; i++) await Task.Delay(20);
        (await CountAsync(gone)).Should().Be(0);
        (await CountAsync(fine)).Should().Be(1);
    }
}
