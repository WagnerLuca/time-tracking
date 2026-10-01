using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Several open requests per type (but no duplicates), decline reasons, and the mails +
/// linked in-app notifications for requests, failure entries and the day-end reminder.
/// </summary>
public class RequestNotificationTests : IClassFixture<TimeTrackingApiFactory>
{
    private const int RequiresApproval = 1;
    private const int EditPastEntry = 1;
    private const int RequestAbsence = 6;
    private readonly TimeTrackingApiFactory _factory;

    public RequestNotificationTests(TimeTrackingApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Owner, HttpClient Member, LoginResponseDto MemberLogin, string Slug, int OrgId)> SetupAsync(string prefix)
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-owner@notify.test");
        var org = await TestHelpers.CreateOrganizationAsync(owner, $"{prefix} Org", $"{prefix}-org");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings",
            new { editPastEntriesMode = RequiresApproval, absenceSelfServiceMode = RequiresApproval })).EnsureSuccessStatusCode();
        var (member, m) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-m@notify.test", "Mia", "Member");
        (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, m)).EnsureSuccessStatusCode();
        return (owner, member, m, org.Slug, org.Id);
    }

    private static Task<HttpResponseMessage> RequestAsync(HttpClient member, string slug, int type, object data, int? relatedEntityId = null) =>
        member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests",
            new { type, relatedEntityId, requestData = JsonSerializer.Serialize(data) });

    private static async Task<int> FinishedEntryAsync(HttpClient member, int orgId)
    {
        (await member.PostAsJsonAsync("/api/v1/TimeTracking/start", new { organizationId = orgId })).EnsureSuccessStatusCode();
        return (await (await member.PostAsJsonAsync("/api/v1/TimeTracking/stop", (object?)null)).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task SeveralOpenRequestsOfOneType_AreAllowed_ButNotDuplicates()
    {
        var (_, member, _, slug, orgId) = await SetupAsync("multi");

        (await RequestAsync(member, slug, RequestAbsence, new { dates = new[] { "2026-11-02", "2026-11-03" }, type = 1 })).EnsureSuccessStatusCode();
        (await RequestAsync(member, slug, RequestAbsence, new { dates = new[] { "2026-12-01" }, type = 1 })).EnsureSuccessStatusCode();
        (await RequestAsync(member, slug, RequestAbsence, new { dates = new[] { "2026-11-03", "2026-11-04" }, type = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var first = await FinishedEntryAsync(member, orgId);
        var second = await FinishedEntryAsync(member, orgId);
        (await RequestAsync(member, slug, EditPastEntry, new { description = "a" }, first)).EnsureSuccessStatusCode();
        (await RequestAsync(member, slug, EditPastEntry, new { description = "b" }, second)).EnsureSuccessStatusCode();
        (await RequestAsync(member, slug, EditPastEntry, new { description = "c" }, first))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Decline_WithReason_IsStoredNotifiedAndMailed_ApproversAreMailedOnCreation()
    {
        var (owner, member, m, slug, _) = await SetupAsync("reason");

        var created = await RequestAsync(member, slug, RequestAbsence, new { dates = new[] { "2026-11-09" }, type = 1 });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        _factory.Mail.Sent.Should().Contain(mail => mail.To == "reason-owner@notify.test" && mail.Body.Contains("Mia Member"));

        (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{id}", new { accept = false, reason = new string('x', 1001) }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var declined = await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{id}",
            new { accept = false, reason = "Team is short-staffed that week" });
        declined.EnsureSuccessStatusCode();
        (await declined.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("responseReason").GetString()
            .Should().Be("Team is short-staffed that week");

        var mine = await member.GetFromJsonAsync<JsonElement>("/api/v1/organizations/my-requests");
        mine.GetProperty("items")[0].GetProperty("responseReason").GetString().Should().Be("Team is short-staffed that week");

        var notifications = await member.GetFromJsonAsync<JsonElement>("/api/v1/Notifications");
        var n = notifications.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("type").GetString() == "RequestDeclined");
        n.GetProperty("relatedEntityId").GetInt32().Should().Be(id);
        n.GetProperty("message").GetString().Should().Contain("short-staffed");

        _factory.Mail.Sent.Should().Contain(mail => mail.To == m.User.Email && mail.Subject.Contains("abgelehnt") && mail.Body.Contains("short-staffed"));
    }

    [Fact]
    public async Task FailureEntryAndDayEndReminder_AreNotifiedWithEntryLink_AndMailedOnce()
    {
        var (_, member, m, _, orgId) = await SetupAsync("dayend");
        var start = new DateTime(2026, 3, 2, 8, 0, 0, DateTimeKind.Utc);   // Berlin 09:00; day end 00:00 local = 23:00 UTC
        int entryId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            var entry = new TimeEntry { UserId = m.User.Id, OrganizationId = orgId, StartTime = start, IsRunning = true, CreatedAt = start, UpdatedAt = start };
            db.TimeEntries.Add(entry);
            await db.SaveChangesAsync();
            entryId = entry.Id;

            (await FailureEntryService.SendDayEndRemindersAsync(db, new DateTime(2026, 3, 2, 22, 0, 0, DateTimeKind.Utc), _factory.Mail)).Should().Be(0);
            (await FailureEntryService.SendDayEndRemindersAsync(db, new DateTime(2026, 3, 2, 22, 40, 0, DateTimeKind.Utc), _factory.Mail)).Should().Be(1);
            (await FailureEntryService.SendDayEndRemindersAsync(db, new DateTime(2026, 3, 2, 22, 50, 0, DateTimeKind.Utc), _factory.Mail)).Should().Be(0);
            (await FailureEntryService.CloseOverdueEntriesAsync(db, new DateTime(2026, 3, 2, 23, 1, 0, DateTimeKind.Utc), default, _factory.Mail, "http://app")).Should().Be(1);
        }

        _factory.Mail.Sent.Count(mail => mail.To == m.User.Email && mail.Subject.StartsWith("Dein Timer")).Should().Be(1);
        _factory.Mail.Sent.Should().Contain(mail => mail.To == m.User.Email && mail.Body.Contains($"/history?date=2026-03-02&entry={entryId}"));

        var notifications = await member.GetFromJsonAsync<JsonElement>("/api/v1/Notifications");
        notifications.GetProperty("items").EnumerateArray()
            .Where(x => x.GetProperty("type").GetString() is "FailureEntry" or "TimerReminder")
            .Select(x => x.GetProperty("relatedEntityId").GetInt32())
            .Should().Equal(entryId, entryId);
    }
}
