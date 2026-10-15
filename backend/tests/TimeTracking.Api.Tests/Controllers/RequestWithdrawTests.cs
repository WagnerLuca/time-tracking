using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>A member takes back an own request while it is pending; approvers no longer see it as open.</summary>
public class RequestWithdrawTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public RequestWithdrawTests(TimeTrackingApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Owner, HttpClient Member, string Slug, int Id)> PendingAsync(string prefix)
    {
        var (owner, member, slug, _, _) = await TestHelpers.OrgWithMemberAsync(_factory, prefix, new { initialOvertimeMode = 1 }, "wd.test");
        var created = await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests", new { type = 3, requestData = "2" });
        created.EnsureSuccessStatusCode();
        return (owner, member, slug, (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32());
    }

    private static async Task<string?> CodeOf(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task Owner_Withdraws_AndApproversNoLongerSeeItAsPending()
    {
        var (owner, member, slug, id) = await PendingAsync("wd-ok");
        (await owner.GetFromJsonAsync<JsonElement>("/api/v1/organizations/notifications")).GetProperty("pendingRequests").GetInt32().Should().Be(1);

        var response = await member.PostAsync($"/api/v1/organizations/{slug}/requests/{id}/withdraw", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Withdrawn");

        (await owner.GetFromJsonAsync<JsonElement>("/api/v1/organizations/notifications")).GetProperty("pendingRequests").GetInt32().Should().Be(0);
        var all = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{slug}/requests");
        all.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetInt32() == id)
            .GetProperty("status").GetString().Should().Be("Withdrawn");

        // No "decision" notice for the member, the request shows in their list as withdrawn
        (await member.GetFromJsonAsync<JsonElement>("/api/v1/organizations/user-notifications")).GetProperty("count").GetInt32().Should().Be(0);
        var mine = await member.GetFromJsonAsync<JsonElement>("/api/v1/organizations/my-requests");
        mine.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetInt32() == id)
            .GetProperty("status").GetString().Should().Be("Withdrawn");

        // ...and an approver can no longer answer it
        var accept = await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{id}", new { accept = true });
        accept.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OnlyTheRequester_CanWithdraw()
    {
        var (owner, _, slug, id) = await PendingAsync("wd-owner");
        var response = await owner.PostAsync($"/api/v1/organizations/{slug}/requests/{id}/withdraw", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeOf(response)).Should().Be("request.notFound");
    }

    [Fact]
    public async Task AnsweredRequest_CannotBeWithdrawn()
    {
        var (owner, member, slug, id) = await PendingAsync("wd-answered");
        (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{id}", new { accept = false })).EnsureSuccessStatusCode();

        var response = await member.PostAsync($"/api/v1/organizations/{slug}/requests/{id}/withdraw", null);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("request.notWithdrawable");
    }

    [Fact]
    public async Task UnknownOrganization_IsNotFound()
    {
        var (_, member, _, id) = await PendingAsync("wd-noorg");
        (await member.PostAsync($"/api/v1/organizations/no-such-org/requests/{id}/withdraw", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
