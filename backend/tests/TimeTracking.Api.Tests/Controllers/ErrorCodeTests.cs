using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>Errors carry a stable <c>code</c> the frontend translates.</summary>
public class ErrorCodeTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public ErrorCodeTests(TimeTrackingApiFactory factory) => _factory = factory;

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var c) ? c.GetString() : null;

    [Fact]
    public void Catalog_CodesAreDottedLowerCamel_AndEveryMessageStillExistsInTheSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Services"))) dir = dir.Parent;
        dir.Should().NotBeNull("the backend source must be reachable from the test output");

        var source = string.Concat(Directory.EnumerateFiles(Path.Combine(dir!.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("ErrorCodes.cs") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText));

        foreach (var (message, code) in ErrorCodes.ByMessage)
        {
            code.Should().MatchRegex(@"^[a-z][a-zA-Z]*(\.[a-z][a-zA-Z]*)+$");
            if (message is "Forbidden" or "Unauthorized") continue;   // default arguments
            source.Should().Contain($"\"{message}\"", $"the message of {code} was reworded; update ErrorCodes");
        }
    }

    [Fact]
    public async Task ServiceErrors_And_FilterErrors_CarryTheirCode()
    {
        var anonymous = _factory.CreateClient();
        (await CodeOf(await anonymous.PostAsJsonAsync("/api/v1/Auth/login", new { email = "nobody@codes.test", password = "wrong" })))
            .Should().Be("auth.invalidCredentials");

        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "codes@codes.test");
        (await CodeOf(await client.PostAsJsonAsync("/api/v1/TimeTracking/stop", new { }))).Should().Be("timer.notRunning");

        (await CodeOf(await client.PostAsJsonAsync("/api/v1/Auth/change-password", new { currentPassword = "Password123!", newPassword = "alllowercase1!" })))
            .Should().Be("auth.passwordNeedsUppercase");
        (await CodeOf(await client.PostAsJsonAsync("/api/v1/Auth/change-password", new { currentPassword = "Wrong123!", newPassword = "Better123!" })))
            .Should().Be("auth.wrongCurrentPassword");

        // RequireOrgRole filter
        (await CodeOf(await client.GetAsync($"/api/v1/organizations/{TestHelpers.SeedOrgSlug}/holidays"))).Should().Be("org.notMember");

        // Variable message, matched by prefix
        var org = await TestHelpers.CreateOrganizationAsync(client, "Codes Org", "codes-org");
        var badZone = await client.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { timeZone = "Mars/Olympus" });
        badZone.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(badZone)).Should().Be("org.unknownTimeZone");
    }

    [Fact]
    public void For_MapsRuleMessagesWithAFeatureName()
    {
        ErrorCodes.For("Editing pause duration is disabled in this organization.").Should().Be("org.featureDisabled");
        ErrorCodes.For("Schedule changes require admin approval. Please submit a WorkScheduleChange request via the organization requests endpoint.")
            .Should().Be("org.featureRequiresApproval");
        ErrorCodes.For("Editing past entries is disabled in this organization.").Should().Be("entry.editDisabled");
        ErrorCodes.For("Something nobody coded").Should().BeNull();
    }
}
