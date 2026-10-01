using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TimeTracking.Api.Data;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Account lifecycle and session security: password reset, email verification, anonymizing
/// account deletion, refresh-token rotation/reuse detection, TOTP replay and "Require 2FA".
/// </summary>
public class AccountSecurityTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public AccountSecurityTests(TimeTrackingApiFactory factory) => _factory = factory;

    [Fact]
    public async Task PasswordReset_ViaMailedLink_ChangesPasswordAndEndsSessions()
    {
        var (client, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "reset@sec.test");
        var anon = _factory.CreateClient();

        // Same answer for known and unknown addresses (no enumeration)
        var known = await anon.PostAsJsonAsync("/api/v1/Auth/forgot-password", new { email = "reset@sec.test" });
        var unknown = await anon.PostAsJsonAsync("/api/v1/Auth/forgot-password", new { email = "nobody@sec.test" });
        (await known.Content.ReadAsStringAsync()).Should().Be(await unknown.Content.ReadAsStringAsync());

        var token = _factory.Mail.LinkToken("reset@sec.test", "reset-password");
        (await anon.PostAsJsonAsync("/api/v1/Auth/reset-password", new { token, newPassword = "Weak" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await anon.PostAsJsonAsync("/api/v1/Auth/reset-password", new { token, newPassword = "Brand-New-Pass1" })).EnsureSuccessStatusCode();

        // Link is single use, old password and old refresh token are dead, new password works
        (await anon.PostAsJsonAsync("/api/v1/Auth/reset-password", new { token, newPassword = "Another-Pass2" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await anon.PostAsJsonAsync("/api/v1/Auth/refresh", new { refreshToken = login.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.PostAsJsonAsync("/api/v1/Auth/login", new { email = "reset@sec.test", password = "Password123!" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.PostAsJsonAsync("/api/v1/Auth/login", new { email = "reset@sec.test", password = "Brand-New-Pass1" }))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Registration_SendsVerificationLink_ThatConfirmsTheEmail()
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "verify@sec.test");
        (await client.GetFromJsonAsync<UserInfoDto>("/api/v1/Auth/me", TestHelpers.JsonOptions))!.EmailConfirmed.Should().BeFalse();

        var token = _factory.Mail.LinkToken("verify@sec.test", "verify-email");
        (await _factory.CreateClient().PostAsJsonAsync("/api/v1/Auth/verify-email", new { token })).EnsureSuccessStatusCode();

        (await client.GetFromJsonAsync<UserInfoDto>("/api/v1/Auth/me", TestHelpers.JsonOptions))!.EmailConfirmed.Should().BeTrue();
        (await client.PostAsync("/api/v1/Auth/verify-email/resend", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteAccount_AnonymizesButKeepsTimeEntriesForTheOrganization()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "anon-owner@sec.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Anon Org", "anon-org");
        var (member, m) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "anon-member@sec.test", "Erika", "Muster");
        (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, m)).EnsureSuccessStatusCode();
        (await member.PostAsJsonAsync("/api/v1/TimeTracking/start", new { organizationId = org.Id })).EnsureSuccessStatusCode();
        (await member.PostAsJsonAsync("/api/v1/TimeTracking/stop", (object?)null)).EnsureSuccessStatusCode();

        (await member.PostAsJsonAsync("/api/v1/Auth/account/delete", new { password = "Password123!" })).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
        var user = await db.Users.FindAsync(m.User.Id);
        user!.Email.Should().NotContain("anon-member");
        user.FirstName.Should().Be("Deleted");
        user.PasswordHash.Should().BeNull();
        user.IsActive.Should().BeFalse();
        db.TimeEntries.Count(e => e.UserId == m.User.Id && e.OrganizationId == org.Id).Should().Be(1);

        // The address is free again
        (await _factory.CreateClient().PostAsJsonAsync("/api/v1/Auth/register",
            new { email = "anon-member@sec.test", password = "StrongPass1!", firstName = "New", lastName = "Person" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task RefreshToken_ReuseOfRotatedToken_RevokesAllSessions()
    {
        var (_, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "reuse@sec.test");
        var anon = _factory.CreateClient();

        var rotated = await (await anon.PostAsJsonAsync("/api/v1/Auth/refresh", new { refreshToken = login.RefreshToken }))
            .Content.ReadFromJsonAsync<RefreshTokenResponseDto>(TestHelpers.JsonOptions);

        // An attacker replays the stolen, already rotated token...
        (await anon.PostAsJsonAsync("/api/v1/Auth/refresh", new { refreshToken = login.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // ...which also kills the legitimate successor
        (await anon.PostAsJsonAsync("/api/v1/Auth/refresh", new { refreshToken = rotated!.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshTokens_AreStoredHashed()
    {
        var (_, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "hashed@sec.test");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
        db.RefreshTokens.Any(rt => rt.Token == login.RefreshToken).Should().BeFalse();
    }

    [Fact]
    public async Task TotpCode_CannotBeReplayed()
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "replay@sec.test");
        var setup = await (await client.PostAsync("/api/v1/Auth/2fa/setup", null)).Content.ReadFromJsonAsync<JsonElement>();
        var totp = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(setup.GetProperty("sharedKey").GetString()!));
        var usedCode = totp.ComputeTotp();
        (await client.PostAsJsonAsync("/api/v1/Auth/2fa/confirm", new { code = usedCode })).EnsureSuccessStatusCode();

        var anon = _factory.CreateClient();
        async Task<string> ChallengeAsync() => (await (await anon.PostAsJsonAsync("/api/v1/Auth/login",
            new { email = "replay@sec.test", password = "Password123!" })).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("twoFactorToken").GetString()!;

        // The exact code used to confirm setup is spent and cannot log in afterwards
        (await anon.PostAsJsonAsync("/api/v1/Auth/2fa/verify", new { twoFactorToken = await ChallengeAsync(), code = usedCode }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Require2fa_BlocksOrgAccessUntilEnabled_AndAdminsNeed2faToTurnItOn()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "req2fa-owner@sec.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Strict Org", "strict-org");

        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { require2fa = true }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var setup = await (await owner.PostAsync("/api/v1/Auth/2fa/setup", null)).Content.ReadFromJsonAsync<JsonElement>();
        var totp = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(setup.GetProperty("sharedKey").GetString()!));
        var backupCode = (await (await owner.PostAsJsonAsync("/api/v1/Auth/2fa/confirm", new { code = totp.ComputeTotp() }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("backupCodes")[0].GetString();
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { require2fa = true })).EnsureSuccessStatusCode();

        var (member, m) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "req2fa-m@sec.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, m)).EnsureSuccessStatusCode();

        var blocked = await member.GetAsync($"/api/v1/organizations/{org.Slug}/holidays");
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await blocked.Content.ReadAsStringAsync()).Should().Contain("TwoFactorRequired");
        (await member.PostAsJsonAsync("/api/v1/TimeTracking/start", new { organizationId = org.Id }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Owner cannot switch their own 2FA off while the org requires it
        var disable = await owner.PostAsJsonAsync("/api/v1/Auth/2fa/disable", new { currentPassword = "Password123!", code = backupCode });
        disable.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await disable.Content.ReadAsStringAsync()).Should().Contain("Strict Org");
    }

    [Fact]
    public async Task Login_DoesNotRevealDeactivatedAccountsWithoutThePassword()
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "deact@sec.test");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            var u = db.Users.Single(x => x.Email == "deact@sec.test");
            u.IsActive = false;
            await db.SaveChangesAsync();
        }

        var wrong = await _factory.CreateClient().PostAsJsonAsync("/api/v1/Auth/login", new { email = "deact@sec.test", password = "nope" });
        (await wrong.Content.ReadAsStringAsync()).Should().Contain("Invalid email or password").And.NotContain("deactivated");
    }
}
