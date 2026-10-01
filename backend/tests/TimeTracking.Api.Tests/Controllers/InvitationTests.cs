using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>Members join only by invitation: no adding by id, no leaking other accounts.</summary>
public class InvitationTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public InvitationTests(TimeTrackingApiFactory factory) => _factory = factory;

    [Fact]
    public async Task AddingByUserId_NoLongerExists()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "inv-byid@inv.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Inv ById", "inv-byid");
        var response = await owner.PostAsJsonAsync($"/api/v1/Organizations/{org.Slug}/members", new { userId = 1 });
        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.Content.ReadAsStringAsync()).Should().NotContain("@example.com");
    }

    [Fact]
    public async Task OnlyTheInvitedAddress_CanAccept_AndTheLinkIsSingleUse()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "inv-owner@inv.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Inv Org", "inv-org");
        var (invited, invitedLogin) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "inv-target@inv.test");
        var (stranger, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "inv-stranger@inv.test");

        var invite = await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/invitations", new { email = "INV-target@inv.test" });
        invite.StatusCode.Should().Be(HttpStatusCode.Created);
        (await invite.Content.ReadAsStringAsync()).Should().NotContain("token");
        var token = _factory.Mail.LinkToken("INV-target@inv.test", "invite");

        (await stranger.PostAsJsonAsync("/api/v1/invitations/accept", new { token })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await invited.PostAsJsonAsync("/api/v1/invitations/accept", new { token })).EnsureSuccessStatusCode();
        (await invited.PostAsJsonAsync("/api/v1/invitations/accept", new { token })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var mine = await invited.GetFromJsonAsync<List<JsonElement>>("/api/v1/Organizations/mine");
        mine.Should().Contain(o => o.GetProperty("slug").GetString() == org.Slug);

        // Already a member → no second invitation
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/invitations", new { email = invitedLogin.User.Email }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RevokedInvitation_CannotBeAccepted_AndPendingListShowsOnlyOpenOnes()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "inv-rev-owner@inv.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Inv Rev", "inv-rev");
        var (invited, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "inv-rev-target@inv.test");

        var id = (await (await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/invitations", new { email = "inv-rev-target@inv.test" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var token = _factory.Mail.LinkToken("inv-rev-target@inv.test", "invite");
        (await owner.GetFromJsonAsync<List<JsonElement>>($"/api/v1/organizations/{org.Slug}/invitations")).Should().ContainSingle();

        (await owner.DeleteAsync($"/api/v1/organizations/{org.Slug}/invitations/{id}")).EnsureSuccessStatusCode();
        (await owner.GetFromJsonAsync<List<JsonElement>>($"/api/v1/organizations/{org.Slug}/invitations")).Should().BeEmpty();
        (await invited.PostAsJsonAsync("/api/v1/invitations/accept", new { token })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OnlyOwnersInviteAdmins_MembersCannotInvite_NoOwnerInvites()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "inv-roles-owner@inv.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Inv Roles", "inv-roles");
        var (admin, adminLogin) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "inv-roles-admin@inv.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, adminLogin, role: 1)).EnsureSuccessStatusCode();
        var (member, memberLogin) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "inv-roles-m@inv.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, memberLogin)).EnsureSuccessStatusCode();

        (await admin.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/invitations", new { email = "x1@inv.test", role = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/invitations", new { email = "x2@inv.test" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await member.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/invitations", new { email = "x3@inv.test" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/invitations", new { email = "x4@inv.test", role = 2 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UndefinedEnumValues_AreRejected()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "inv-enum-owner@inv.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Inv Enum", "inv-enum");
        var (_, memberLogin) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "inv-enum-m@inv.test");
        (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, memberLogin)).EnsureSuccessStatusCode();

        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/members/{memberLogin.User.Id}", new { role = 3 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { editPastEntriesMode = 7 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/invitations", new { email = "y@inv.test", role = 9 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
