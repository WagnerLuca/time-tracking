using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>Session, 2FA and input-limit hardening from issue #36.</summary>
public class SessionHardeningTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public SessionHardeningTests(TimeTrackingApiFactory factory) => _factory = factory;

    private async Task<HttpClient> SecondSessionAsync(string email)
    {
        var client = _factory.CreateClient();
        var login = await TestHelpers.LoginAsync(client, email, "Password123!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    /// <summary>Turns 2FA on and returns the backup codes (each works once).</summary>
    private static async Task<Queue<string>> EnableTwoFactorAsync(HttpClient client)
    {
        var setup = await (await client.PostAsync("/api/v1/Auth/2fa/setup", null)).Content.ReadFromJsonAsync<JsonElement>();
        var totp = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(setup.GetProperty("sharedKey").GetString()!));
        var confirm = await (await client.PostAsJsonAsync("/api/v1/Auth/2fa/confirm", new { code = totp.ComputeTotp() }))
            .Content.ReadFromJsonAsync<JsonElement>();
        return new Queue<string>(confirm.GetProperty("backupCodes").EnumerateArray().Select(c => c.GetString()!));
    }

    private async Task<string> TwoFactorChallengeAsync(string email) =>
        (await (await _factory.CreateClient().PostAsJsonAsync("/api/v1/Auth/login", new { email, password = "Password123!" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("twoFactorToken").GetString()!;

    [Fact]
    public async Task PasswordChange_EndsOtherSessionsAccessTokensImmediately()
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "stamp@harden.test");
        var other = await SecondSessionAsync("stamp@harden.test");
        (await other.GetAsync("/api/v1/Auth/me")).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/v1/Auth/change-password",
            new { currentPassword = "Password123!", newPassword = "Changed-Pass9" })).EnsureSuccessStatusCode();

        (await other.GetAsync("/api/v1/Auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TwoFactorLoginToken_WorksOnlyOnce()
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "once@harden.test");
        var codes = await EnableTwoFactorAsync(client);
        var challenge = await TwoFactorChallengeAsync("once@harden.test");
        var anon = _factory.CreateClient();

        (await anon.PostAsJsonAsync("/api/v1/Auth/2fa/verify", new { twoFactorToken = challenge, code = codes.Dequeue() })).EnsureSuccessStatusCode();
        (await anon.PostAsJsonAsync("/api/v1/Auth/2fa/verify", new { twoFactorToken = challenge, code = codes.Dequeue() }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TwoFactor_LocksAfterFiveWrongCodes_EvenForTheRightCode()
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "lock@harden.test");
        var codes = await EnableTwoFactorAsync(client);
        var challenge = await TwoFactorChallengeAsync("lock@harden.test");
        var anon = _factory.CreateClient();

        for (var i = 0; i < 5; i++)
            (await anon.PostAsJsonAsync("/api/v1/Auth/2fa/verify", new { twoFactorToken = challenge, code = "000000" }))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await anon.PostAsJsonAsync("/api/v1/Auth/2fa/verify", new { twoFactorToken = challenge, code = codes.Dequeue() }))
            .StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task DisablingTwoFactor_NeedsACode()
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "disable@harden.test");
        var codes = await EnableTwoFactorAsync(client);

        (await client.PostAsJsonAsync("/api/v1/Auth/2fa/disable", new { currentPassword = "Password123!", code = "123456" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/v1/Auth/2fa/disable", new { currentPassword = "Password123!", code = codes.Dequeue() }))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ForgotPassword_SendsAtMostOneMailPerCooldown()
    {
        await TestHelpers.CreateAuthenticatedUserAsync(_factory, "cooldown@harden.test");
        var anon = _factory.CreateClient();
        for (var i = 0; i < 3; i++)
            (await anon.PostAsJsonAsync("/api/v1/Auth/forgot-password", new { email = "cooldown@harden.test" })).EnsureSuccessStatusCode();

        _factory.Mail.Sent.Count(m => m.To == "cooldown@harden.test" && m.Body.Contains("/reset-password?token=")).Should().Be(1);
    }

    [Fact]
    public async Task PersonalDataExport_ContainsTheOwnEntries()
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "gdpr@harden.test");
        (await client.PostAsJsonAsync("/api/v1/TimeTracking/start", new { description = "my work" })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/v1/TimeTracking/stop", (object?)null)).EnsureSuccessStatusCode();

        var export = await client.GetAsync("/api/v1/Auth/me/export");
        export.EnsureSuccessStatusCode();
        var json = await export.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("profile").GetProperty("email").GetString().Should().Be("gdpr@harden.test");
        json.GetProperty("timeEntries").EnumerateArray().Should().Contain(e => e.GetProperty("description").GetString() == "my work");
        json.GetProperty("entryChanges").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task InitialOvertime_IsVisibleOnlyToTheMemberAndAdmins()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "ot-owner@harden.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "OT Org", "ot-org");
        var (alice, a) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "ot-a@harden.test");
        var (bob, b) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "ot-b@harden.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, a)).EnsureSuccessStatusCode();
        (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, b)).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/members/{a.User.Id}/initial-overtime",
            new { initialOvertimeHours = 12.5 })).EnsureSuccessStatusCode();

        async Task<double> SeenBy(HttpClient viewer) =>
            (await viewer.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{org.Slug}")).GetProperty("members").EnumerateArray()
                .First(m => m.GetProperty("id").GetInt32() == a.User.Id).GetProperty("initialOvertimeHours").GetDouble();

        (await SeenBy(owner)).Should().Be(12.5);
        (await SeenBy(alice)).Should().Be(12.5);
        (await SeenBy(bob)).Should().Be(0);
    }

    [Fact]
    public async Task Import_RejectsTooManyRowsAndSkipsImplausibleOnes()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "imp-owner@harden.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Import Org", "imp-org");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { csvImportMode = 2 })).EnsureSuccessStatusCode();
        var url = $"/api/v1/ExportImport/import/confirm?orgSlug={org.Slug}";

        var tooMany = Enumerable.Range(0, 1001).Select(_ => new { date = "2026-03-02", startTime = "08:00", endTime = "09:00" });
        (await owner.PostAsJsonAsync(url, tooMany)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await (await owner.PostAsJsonAsync(url, new object[]
        {
            new { date = "1900-01-01", startTime = "08:00", endTime = "09:00" },   // absurd year
            new { date = "2026-03-02", startTime = "08:00", endTime = "09:00" }
        })).Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("importedCount").GetInt32().Should().Be(1);
        result.GetProperty("skippedCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Export_RejectsRangesLongerThanTwoYears()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "exp-owner@harden.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Export Org", "exp-org");

        (await owner.PostAsJsonAsync("/api/v1/ExportImport/export",
            new { type = "daily", organizationSlug = org.Slug, from = "2000-01-01", to = "2026-01-01" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync("/api/v1/ExportImport/export",
            new { type = "daily", organizationSlug = org.Slug, from = "2026-01-01", to = "2026-01-31" }))
            .EnsureSuccessStatusCode();
    }
}
