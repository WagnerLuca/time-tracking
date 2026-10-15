using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Web Push (issue #67): device registration, categories, and that every in-app notification
/// type (plus new requests for approvers) reaches the user's devices in their language.
/// </summary>
public class PushTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public PushTests(TimeTrackingApiFactory factory) => _factory = factory;

    // ── Helpers ─────────────────────────────────────────────────────────

    private static string Key(int bytes) => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));

    private static string NewEndpoint(string host = "fcm.googleapis.com") => $"https://{host}/fcm/send/{Guid.NewGuid():N}";

    private static object Body(string endpoint, string? locale = "de", string? p256dh = null, string? auth = null) =>
        new { endpoint, p256dh = p256dh ?? Key(65), auth = auth ?? Key(16), locale };

    /// <summary>Registers a new device for the client's user; returns its endpoint.</summary>
    private static async Task<string> SubscribeAsync(HttpClient client, string locale = "de")
    {
        var endpoint = NewEndpoint();
        (await client.PostAsJsonAsync("/api/v1/push/subscriptions", Body(endpoint, locale))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return endpoint;
    }

    private static async Task<string?> CodeOf(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private async Task WithDbAsync(Func<TimeTrackingDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>());
    }

    private async Task<List<PushSubscription>> DevicesAsync(int userId)
    {
        List<PushSubscription> list = [];
        await WithDbAsync(async db => list = await db.PushSubscriptions.AsNoTracking().Where(s => s.UserId == userId).ToListAsync());
        return list;
    }

    /// <summary>
    /// Pushes a marker to <paramref name="userId"/> and waits for it on <paramref name="endpoint"/>: the
    /// queue is FIFO, so everything enqueued before has been handled once it arrives.
    /// </summary>
    private async Task FlushAsync(int userId, string endpoint)
    {
        var tag = $"flush-{Guid.NewGuid():N}";
        _factory.Services.GetRequiredService<PushQueue>().Enqueue([new PushMessage(userId, PushCategory.None, tag, "/", "t", "b", "t", "b")]);
        (await _factory.Push.WaitForAsync(endpoint, p => p.Tag == tag)).Should().ContainSingle();
    }

    /// <summary>Pushes to <paramref name="endpoint"/> other than <see cref="FlushAsync"/> markers.</summary>
    private List<FakePushSender.Push> Real(string endpoint) => _factory.Push.To(endpoint).Where(p => !p.Tag.StartsWith("flush-")).ToList();

    // ── Registration ────────────────────────────────────────────────────

    [Fact]
    public async Task Endpoints_NeedAuthentication()
    {
        var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/push/public-key")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/v1/push/subscriptions", Body(NewEndpoint()))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/v1/push/subscriptions/remove", new { endpoint = NewEndpoint() })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/push/settings")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PutAsJsonAsync("/api/v1/push/settings", new { reminders = true })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PublicKey_IsTheSendersKey()
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "push-key@push.test");
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/push/public-key")).GetProperty("publicKey").GetString()
            .Should().Be(_factory.Push.PublicKey);
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/fcm/send/x")]
    [InlineData("https://evil.example.com/fcm/send/x")]
    [InlineData("https://fcm.googleapis.com.evil.example.com/x")]
    [InlineData("https://fcm.googleapis.com:8443/fcm/send/x")]
    [InlineData("https://push.services.mozilla.com.evil.test/x")]
    [InlineData("not a url")]
    [InlineData("")]
    public async Task Subscribe_RefusesOtherHosts(string endpoint)
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"push-host-{Guid.NewGuid():N}@push.test");
        var response = await client.PostAsJsonAsync("/api/v1/push/subscriptions", Body(endpoint));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("push.unsupportedService");
    }

    [Theory]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/x")]
    [InlineData("https://wns2-db5p.notify.windows.com/w/?token=x")]
    [InlineData("https://web.push.apple.com/x")]
    [InlineData("https://android.googleapis.com/gcm/send/x")]
    public void SupportedPushServices_AreAccepted(string endpoint) =>
        PushService.IsPushServiceEndpoint(endpoint).Should().BeTrue();

    [Fact]
    public void OverlongEndpoint_IsRefused() =>
        PushService.IsPushServiceEndpoint("https://fcm.googleapis.com/" + new string('x', 1000)).Should().BeFalse();

    [Theory]
    [InlineData(64, 16)]
    [InlineData(65, 15)]
    [InlineData(-1, 16)]   // not base64url
    [InlineData(65, -1)]
    [InlineData(-2, 16)]   // overlong
    public async Task Subscribe_RefusesBadKeys(int p256dhBytes, int authBytes)
    {
        string K(int n) => n switch { -1 => "!!not base64!!", -2 => new string('A', 201), _ => Key(n) };
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"push-keys-{Guid.NewGuid():N}@push.test");
        var response = await client.PostAsJsonAsync("/api/v1/push/subscriptions", Body(NewEndpoint(), p256dh: K(p256dhBytes), auth: K(authBytes)));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("push.invalidSubscription");
    }

    [Fact]
    public async Task Subscribe_EndpointOfAnotherUser_Conflicts()
    {
        var (alice, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "push-alice@push.test");
        var (bob, b) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "push-bob@push.test");
        var endpoint = await SubscribeAsync(alice);

        var response = await bob.PostAsJsonAsync("/api/v1/push/subscriptions", Body(endpoint));
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("push.otherAccount");
        (await DevicesAsync(b.User.Id)).Should().BeEmpty();

        // Bob cannot remove Alice's device either (no error, nothing happens)
        (await bob.PostAsJsonAsync("/api/v1/push/subscriptions/remove", new { endpoint })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await WithDbAsync(async db => (await db.PushSubscriptions.CountAsync(s => s.Endpoint == endpoint)).Should().Be(1));
    }

    [Fact]
    public async Task Resubscribe_UpdatesTheDevice_AndRemoveDeletesIt()
    {
        var (client, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "push-re@push.test");
        var endpoint = await SubscribeAsync(client);
        var p256dh = Key(65);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (" + new string('x', 400) + ")");
        (await client.PostAsJsonAsync("/api/v1/push/subscriptions", Body(endpoint, "en", p256dh))).EnsureSuccessStatusCode();

        var device = (await DevicesAsync(login.User.Id)).Should().ContainSingle().Subject;
        device.Locale.Should().Be("en");
        device.P256dh.Should().Be(p256dh);
        device.UserAgent.Should().HaveLength(300);

        // Unknown locales fall back to German
        (await client.PostAsJsonAsync("/api/v1/push/subscriptions", Body(endpoint, "fr"))).EnsureSuccessStatusCode();
        (await DevicesAsync(login.User.Id)).Single().Locale.Should().Be("de");

        (await client.PostAsJsonAsync("/api/v1/push/subscriptions/remove", new { endpoint })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DevicesAsync(login.User.Id)).Should().BeEmpty();
        (await client.PostAsJsonAsync("/api/v1/push/subscriptions/remove", new { endpoint })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task EleventhDevice_EvictsTheLeastRecentlyUsed()
    {
        var (client, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "push-many@push.test");
        var endpoints = new List<string>();
        for (var i = 0; i < PushService.MaxDevicesPerUser; i++) endpoints.Add(await SubscribeAsync(client));
        await WithDbAsync(async db =>
        {
            (await db.PushSubscriptions.SingleAsync(s => s.Endpoint == endpoints[3])).LastUsedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        });

        // Refreshing a known device at the limit evicts nothing
        (await client.PostAsJsonAsync("/api/v1/push/subscriptions", Body(endpoints[0]))).EnsureSuccessStatusCode();
        (await DevicesAsync(login.User.Id)).Should().HaveCount(10);

        var eleventh = await SubscribeAsync(client);
        (await DevicesAsync(login.User.Id)).Select(d => d.Endpoint).Should().HaveCount(10)
            .And.Contain(eleventh).And.NotContain(endpoints[3]);
    }

    [Fact]
    public async Task Settings_RoundTrip()
    {
        var (client, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "push-settings@push.test");
        var initial = await client.GetFromJsonAsync<JsonElement>("/api/v1/push/settings");
        foreach (var name in new[] { "reminders", "requests", "coreHours", "organization" })
            initial.GetProperty(name).GetBoolean().Should().BeTrue();

        var put = await client.PutAsJsonAsync("/api/v1/push/settings", new { reminders = false, requests = true, coreHours = false, organization = true });
        put.EnsureSuccessStatusCode();
        (await put.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("coreHours").GetBoolean().Should().BeFalse();

        var saved = await client.GetFromJsonAsync<JsonElement>("/api/v1/push/settings");
        saved.GetProperty("reminders").GetBoolean().Should().BeFalse();
        saved.GetProperty("requests").GetBoolean().Should().BeTrue();
        saved.GetProperty("coreHours").GetBoolean().Should().BeFalse();
        saved.GetProperty("organization").GetBoolean().Should().BeTrue();
        await WithDbAsync(async db => (await db.Users.SingleAsync(u => u.Id == login.User.Id)).PushMutedCategories
            .Should().Be(PushCategory.Reminders | PushCategory.CoreHours));
    }

    [Fact]
    public async Task Service_WithoutKeys_RefusesSubscriptions_AndUnknownUserHasNoSettings()
    {
        await WithDbAsync(async db =>
        {
            var off = new PushService(db, new FakePushSender { PublicKey = null });
            off.GetPublicKey().Data!.PublicKey.Should().BeNull();
            var result = await off.SubscribeAsync(1, new PushSubscribeRequest(NewEndpoint(), Key(65), Key(16), "de"), null);
            result.ErrorMessage.Should().Be("Push notifications are not configured on this server.");

            (await new PushService(db, _factory.Push).UpdateSettingsAsync(int.MaxValue, new PushSettingsDto(true, true, true, true)))
                .ErrorType.Should().Be(ServiceErrorType.NotFound);
        });
    }

    // ── Every notification type is pushed ──────────────────────────────

    private async Task<(HttpClient Client, int UserId, int OrgId)> DayEndOrgAsync(string prefix)
    {
        var (client, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}@push.test");
        var org = await TestHelpers.CreateOrganizationAsync(client, $"{prefix} Org", $"{prefix}-org");
        (await client.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { businessDayEnd = "00:00", timeZone = "Europe/Berlin" }))
            .EnsureSuccessStatusCode();
        return (client, login.User.Id, org.Id);
    }

    private async Task<int> RunningEntryAsync(int userId, int orgId, DateTime startUtc)
    {
        var id = 0;
        await WithDbAsync(async db =>
        {
            var entry = new TimeEntry { UserId = userId, OrganizationId = orgId, StartTime = startUtc, IsRunning = true, CreatedAt = startUtc, UpdatedAt = startUtc };
            db.TimeEntries.Add(entry);
            await db.SaveChangesAsync();
            id = entry.Id;
        });
        return id;
    }

    [Fact]
    public async Task TimerReminderAndFailureEntry_ArePushed_InTheDevicesLanguage_WithDeepLinks()
    {
        var (client, userId, orgId) = await DayEndOrgAsync("push-dayend");
        var german = await SubscribeAsync(client);
        var english = await SubscribeAsync(client, "en");
        var entryId = await RunningEntryAsync(userId, orgId, new DateTime(2026, 3, 2, 8, 0, 0, DateTimeKind.Utc));   // 09:00 Berlin

        await WithDbAsync(db => FailureEntryService.SendDayEndRemindersAsync(db, new DateTime(2026, 3, 2, 22, 40, 0, DateTimeKind.Utc)));
        var reminder = (await _factory.Push.WaitForAsync(german, p => p.Tag == $"TimerReminder-{entryId}")).Single();
        reminder.Title.Should().Be("Dein Timer läuft noch");
        reminder.Body.Should().Contain("um 00:00 automatisch gestoppt");
        reminder.Url.Should().Be("/time");
        var reminderEn = (await _factory.Push.WaitForAsync(english, p => p.Tag == $"TimerReminder-{entryId}")).Single();
        reminderEn.Title.Should().Be("Your timer is still running");
        reminderEn.Body.Should().Contain("at 00:00");

        await WithDbAsync(db => FailureEntryService.CloseOverdueEntriesAsync(db, new DateTime(2026, 3, 2, 23, 1, 0, DateTimeKind.Utc)));
        var failure = (await _factory.Push.WaitForAsync(german, p => p.Tag == $"FailureEntry-{entryId}")).Single();
        failure.Title.Should().Be("Timer automatisch gestoppt");
        failure.Url.Should().Be($"/history?date=2026-03-02&entry={entryId}");
        (await _factory.Push.WaitForAsync(english, p => p.Tag == $"FailureEntry-{entryId}")).Single().Title.Should().Be("Timer stopped automatically");

        // The device was used just now
        (await DevicesAsync(userId)).Single(d => d.Endpoint == german).LastUsedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task MutedCategory_IsNotPushed()
    {
        var (client, userId, orgId) = await DayEndOrgAsync("push-muted");
        var endpoint = await SubscribeAsync(client);
        (await client.PutAsJsonAsync("/api/v1/push/settings", new { reminders = false, requests = true, coreHours = true, organization = true }))
            .EnsureSuccessStatusCode();
        await RunningEntryAsync(userId, orgId, new DateTime(2026, 3, 2, 8, 0, 0, DateTimeKind.Utc));

        await WithDbAsync(db => FailureEntryService.SendDayEndRemindersAsync(db, new DateTime(2026, 3, 2, 22, 40, 0, DateTimeKind.Utc)));
        await WithDbAsync(db => FailureEntryService.CloseOverdueEntriesAsync(db, new DateTime(2026, 3, 2, 23, 1, 0, DateTimeKind.Utc)));

        await FlushAsync(userId, endpoint);
        Real(endpoint).Should().BeEmpty();
        // The in-app notifications are still there
        (await client.GetStringAsync("/api/v1/Notifications")).Should().Contain("FailureEntry").And.Contain("TimerReminder");
    }

    [Fact]
    public async Task CoreHoursViolation_IsPushedToTheMemberAndTheirAdmin()
    {
        var day = new DateOnly(2026, 3, 3);
        var (owner, member, _, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "push-core",
            new { coreHoursEnabled = true, coreHoursStart = "09:00", coreHoursEnd = "15:00", coreHoursToleranceMinutes = 5, timeZone = "Europe/Berlin", memberTimeEntryVisibility = true },
            "push.test");
        var memberDevice = await SubscribeAsync(member);
        var ownerDevice = await SubscribeAsync(owner, "en");
        var berlin = OrgClock.For("Europe/Berlin");
        await WithDbAsync(async db =>
        {
            db.TimeEntries.Add(new TimeEntry
            {
                UserId = memberId, OrganizationId = orgId, StartTime = berlin.ToUtc(day.ToDateTime(new TimeOnly(9, 30))),
                EndTime = berlin.ToUtc(day.ToDateTime(new TimeOnly(15, 0))), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
            (await db.Organizations.SingleAsync(o => o.Id == orgId)).CoreHoursChangedAt = null;
            foreach (var uo in await db.UserOrganizations.Where(uo => uo.OrganizationId == orgId).ToListAsync())
                uo.JoinedAt = berlin.ToUtc(day.ToDateTime(TimeOnly.MinValue));
            await db.SaveChangesAsync();
        });

        await WithDbAsync(db => CoreHours.CheckDueAsync(db, berlin.ToUtc(day.ToDateTime(new TimeOnly(15, 30)))));

        var own = (await _factory.Push.WaitForAsync(memberDevice, p => p.Tag == $"CoreHoursViolation-{memberId}")).Single();
        own.Title.Should().Be("Kernarbeitszeit nicht eingehalten");
        own.Body.Should().Be("Am 03.03.2026 wurde die Kernarbeitszeit nicht eingehalten.");
        own.Url.Should().Be("/history?date=2026-03-03");

        var lead = (await _factory.Push.WaitForAsync(ownerDevice, p => p.Tag == $"CoreHoursViolation-{memberId}")).Single();
        lead.Title.Should().Be("Core working hours missed");
        lead.Body.Should().Be("Test User did not keep the core working hours on 03.03.2026.");
        lead.Url.Should().Be("/inbox");
    }

    private async Task<int> RequestInitialOvertimeAsync(HttpClient member, string slug)
    {
        var created = await member.PostAsJsonAsync($"/api/v1/organizations/{slug}/requests", new { type = 3, requestData = "2" });
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Requests_NewOnesArePushedToApprovers_DecisionsToTheRequester()
    {
        var (owner, member, slug, _, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "push-req", new { initialOvertimeMode = 1 }, "push.test");
        var ownerDevice = await SubscribeAsync(owner);
        var ownerEn = await SubscribeAsync(owner, "en");
        var memberDevice = await SubscribeAsync(member);

        var first = await RequestInitialOvertimeAsync(member, slug);
        var created = (await _factory.Push.WaitForAsync(ownerDevice, p => p.Tag == $"NewRequest-{first}")).Single();
        created.Title.Should().Be("Neue Anfrage");
        created.Body.Should().Be("Test User: Überstunden-Startsaldo");
        created.Url.Should().Be("/inbox");
        (await _factory.Push.WaitForAsync(ownerEn, p => p.Tag == $"NewRequest-{first}")).Single().Body.Should().Be("Test User: initial overtime");

        (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{first}", new { accept = true })).EnsureSuccessStatusCode();
        var accepted = (await _factory.Push.WaitForAsync(memberDevice, p => p.Tag == $"RequestAccepted-{first}")).Single();
        accepted.Title.Should().Be("Anfrage angenommen");
        accepted.Url.Should().Be("/inbox");

        var second = await RequestInitialOvertimeAsync(member, slug);
        (await owner.PutAsJsonAsync($"/api/v1/organizations/{slug}/requests/{second}", new { accept = false })).EnsureSuccessStatusCode();
        (await _factory.Push.WaitForAsync(memberDevice, p => p.Tag == $"RequestDeclined-{second}")).Single().Title.Should().Be("Anfrage abgelehnt");

        // The requester is not pushed about their own new requests
        await FlushAsync(memberId, memberDevice);
        Real(memberDevice).Select(p => p.Tag).Should().BeEquivalentTo([$"RequestAccepted-{first}", $"RequestDeclined-{second}"]);
    }

    [Fact]
    public async Task WithdrawingARequest_PushesNothing()
    {
        var (owner, member, slug, _, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "push-wd", new { initialOvertimeMode = 1 }, "push.test");
        var ownerDevice = await SubscribeAsync(owner);
        var memberDevice = await SubscribeAsync(member);
        var ownerId = await UserIdAsync("push-wd-owner@push.test");

        var id = await RequestInitialOvertimeAsync(member, slug);
        (await _factory.Push.WaitForAsync(ownerDevice, p => p.Tag == $"NewRequest-{id}")).Should().ContainSingle();
        (await member.PostAsync($"/api/v1/organizations/{slug}/requests/{id}/withdraw", null)).EnsureSuccessStatusCode();

        await FlushAsync(memberId, memberDevice);
        await FlushAsync(ownerId, ownerDevice);
        Real(memberDevice).Should().BeEmpty();
        Real(ownerDevice).Should().ContainSingle();
    }

    private async Task<int> UserIdAsync(string email)
    {
        var id = 0;
        await WithDbAsync(async db => id = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        return id;
    }

    [Fact]
    public async Task SettingsChanged_IsPushedToMembers_ButNotToTheAdminWhoChangedThem()
    {
        var (owner, member, slug, _, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "push-rules", new { autoPauseEnabled = true }, "push.test");
        var ownerDevice = await SubscribeAsync(owner);
        var memberDevice = await SubscribeAsync(member, "en");

        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { autoPauseEnabled = false })).EnsureSuccessStatusCode();

        var push = (await _factory.Push.WaitForAsync(memberDevice, p => p.Tag.StartsWith("SettingsChanged"))).Single();
        push.Title.Should().Be("Rules updated");
        push.Url.Should().Be("/inbox");
        await FlushAsync(await UserIdAsync("push-rules-owner@push.test"), ownerDevice);
        Real(ownerDevice).Should().BeEmpty();
    }

    [Fact]
    public async Task DeactivatedUser_GetsNoPush()
    {
        var (owner, member, slug, _, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "push-inactive", new { autoPauseEnabled = true }, "push.test");
        var ownerDevice = await SubscribeAsync(owner);
        var memberDevice = await SubscribeAsync(member);
        await WithDbAsync(async db =>
        {
            (await db.Users.SingleAsync(u => u.Id == memberId)).IsActive = false;
            await db.SaveChangesAsync();
        });

        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { autoPauseEnabled = false })).EnsureSuccessStatusCode();

        await FlushAsync(await UserIdAsync("push-inactive-owner@push.test"), ownerDevice);   // the same queue: the member's push was handled
        _factory.Push.To(memberDevice).Should().BeEmpty();
    }

    [Fact]
    public async Task GoneDevice_IsDeleted_FailedDeviceKept()
    {
        var (owner, member, slug, _, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "push-gone", new { autoPauseEnabled = true }, "push.test");
        var gone = await SubscribeAsync(member);
        var failing = await SubscribeAsync(member);
        var fine = await SubscribeAsync(member);
        _factory.Push.Answers[gone] = PushSendResult.Gone;
        _factory.Push.Answers[failing] = PushSendResult.Failed;

        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { autoPauseEnabled = false })).EnsureSuccessStatusCode();

        await FlushAsync(memberId, fine);
        (await DevicesAsync(memberId)).Select(d => d.Endpoint).Should().BeEquivalentTo([failing, fine]);
    }

    // ── Personal data ───────────────────────────────────────────────────

    [Fact]
    public async Task Export_ListsThePushServiceHost_NeverTheKeys()
    {
        var (client, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "push-export@push.test");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TestBrowser/1.0");
        var p256dh = Key(65);
        var auth = Key(16);
        var endpoint = NewEndpoint();
        (await client.PostAsJsonAsync("/api/v1/push/subscriptions", Body(endpoint, "en", p256dh, auth))).EnsureSuccessStatusCode();

        var raw = await client.GetStringAsync("/api/v1/Auth/me/export");
        raw.Should().NotContain(p256dh).And.NotContain(auth).And.NotContain(endpoint);
        var device = JsonDocument.Parse(raw).RootElement.GetProperty("pushDevices").EnumerateArray().Should().ContainSingle().Subject;
        device.GetProperty("pushService").GetString().Should().Be("fcm.googleapis.com");
        device.GetProperty("userAgent").GetString().Should().Be("TestBrowser/1.0");
        device.GetProperty("locale").GetString().Should().Be("en");
    }

    [Fact]
    public async Task DeletingTheAccount_RemovesItsDevices()
    {
        var (client, login) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "push-delete@push.test");
        await SubscribeAsync(client);
        await SubscribeAsync(client);

        (await client.PostAsJsonAsync("/api/v1/Auth/account/delete", new { password = "Password123!" })).EnsureSuccessStatusCode();
        (await DevicesAsync(login.User.Id)).Should().BeEmpty();
    }

    // ── PushTexts ───────────────────────────────────────────────────────

    private static Notification N(string type, string message, int? related = 7, int userId = 1) =>
        new() { UserId = userId, Type = type, Title = "T", Message = message, RelatedEntityId = related };

    [Fact]
    public void Texts_FailureEntry()
    {
        var m = PushTexts.From(N("FailureEntry", "Your timer started 02.03.2026 09:00 ran past the end of the business day and was stopped."));
        m.Should().Be(new PushMessage(1, PushCategory.Reminders, "FailureEntry-7", "/history?date=2026-03-02&entry=7",
            "Timer automatisch gestoppt", "Dein Timer lief über das Ende des Geschäftstags und zählt erst nach einer Korrektur.",
            "Timer stopped automatically", "Your timer ran past the end of the business day and does not count until you correct it."));
        PushTexts.From(N("FailureEntry", "no date")).Url.Should().Be("/history");
    }

    [Fact]
    public void Texts_TimerReminder()
    {
        var m = PushTexts.From(N("TimerReminder", "Your timer is still running. It will be stopped automatically at 04:00 (end of the business day)."));
        m.Category.Should().Be(PushCategory.Reminders);
        m.Url.Should().Be("/time");
        m.BodyDe.Should().Be("Er wird um 04:00 automatisch gestoppt (Ende des Geschäftstags).");
        m.BodyEn.Should().Be("It will be stopped automatically at 04:00 (end of the business day).");
        var noTime = PushTexts.From(N("TimerReminder", "Your timer is still running."));
        noTime.BodyDe.Should().Be("Er wird zum Ende des Geschäftstags automatisch gestoppt.");
        noTime.BodyEn.Should().Be("It will be stopped automatically at the end of the business day.");
    }

    [Fact]
    public void Texts_CoreHoursViolation_OwnAndSomeoneElses()
    {
        const string msg = "Mia Muster did not keep the core working hours on 03.03.2026 (09:00-15:00): late start, 20 min not covered.";
        var own = PushTexts.From(N("CoreHoursViolation", msg, related: 1));
        own.Category.Should().Be(PushCategory.CoreHours);
        own.Url.Should().Be("/history?date=2026-03-03");
        own.BodyEn.Should().Be("The core working hours on 03.03.2026 were not kept.");
        PushTexts.From(N("CoreHoursViolation", msg, related: null)).Url.Should().Be("/history?date=2026-03-03");
        PushTexts.From(N("CoreHoursViolation", "no date", related: 1)).Url.Should().Be("/history");

        var other = PushTexts.From(N("CoreHoursViolation", msg, related: 9));
        other.Url.Should().Be("/inbox");
        other.Tag.Should().Be("CoreHoursViolation-9");
        other.BodyDe.Should().Be("Mia Muster hat am 03.03.2026 die Kernarbeitszeit nicht eingehalten.");
        other.BodyEn.Should().Be("Mia Muster did not keep the core working hours on 03.03.2026.");
    }

    [Theory]
    [InlineData("RequestAccepted", PushCategory.Requests, "Anfrage angenommen", "Request accepted")]
    [InlineData("RequestDeclined", PushCategory.Requests, "Anfrage abgelehnt", "Request declined")]
    [InlineData("SettingsChanged", PushCategory.Organization, "Regeln aktualisiert", "Rules updated")]
    public void Texts_InboxTypes(string type, PushCategory category, string titleDe, string titleEn)
    {
        var m = PushTexts.From(N(type, "whatever"));
        (m.Category, m.Url, m.TitleDe, m.TitleEn).Should().Be((category, "/inbox", titleDe, titleEn));
    }

    [Fact]
    public void Texts_UnknownType_UsesTheNotificationItself()
    {
        var m = PushTexts.From(N("MemberJoined", "Someone joined", related: null));
        m.Should().Be(new PushMessage(1, PushCategory.Organization, "MemberJoined-", "/inbox", "T", "Someone joined", "T", "Someone joined"));
    }

    [Fact]
    public void Texts_NewRequest()
    {
        PushTexts.NewRequest(5, 42, "Mia Muster", RequestType.RequestAbsence).Should().Be(new PushMessage(5, PushCategory.Requests, "NewRequest-42", "/inbox",
            "Neue Anfrage", "Mia Muster: Abwesenheit", "New request", "Mia Muster: absence"));
    }

    // ── Dispatcher ──────────────────────────────────────────────────────

    private static PushMessage Msg(int userId, PushCategory category = PushCategory.Requests, string tag = "x") =>
        new(userId, category, tag, "/inbox", "De", "Body de", "En", "Body en");

    [Fact]
    public async Task Deliver_SkipsMutedInactiveAndUnknownUsers_DropsGoneDevicesOnce()
    {
        var sender = new FakePushSender();
        await WithDbAsync(async db =>
        {
            var on = new User { Email = $"deliver-on-{Guid.NewGuid():N}@push.test", FirstName = "On", LastName = "U" };
            var muted = new User { Email = $"deliver-mute-{Guid.NewGuid():N}@push.test", FirstName = "M", LastName = "U", PushMutedCategories = PushCategory.Requests };
            var inactive = new User { Email = $"deliver-off-{Guid.NewGuid():N}@push.test", FirstName = "I", LastName = "U", IsActive = false };
            db.Users.AddRange(on, muted, inactive);
            await db.SaveChangesAsync();
            PushSubscription Device(User u, string locale = "de") => new() { UserId = u.Id, Endpoint = NewEndpoint(), P256dh = "p", Auth = "a", Locale = locale };
            var (onDe, onEn, onGone, mutedDevice, inactiveDevice) = (Device(on), Device(on, "en"), Device(on), Device(muted), Device(inactive));
            db.PushSubscriptions.AddRange(onDe, onEn, onGone, mutedDevice, inactiveDevice);
            await db.SaveChangesAsync();
            sender.Answers[onGone.Endpoint] = PushSendResult.Gone;

            var sent = await PushDispatcher.DeliverAsync(db, sender,
                [Msg(on.Id, tag: "a"), Msg(on.Id, tag: "b"), Msg(muted.Id), Msg(muted.Id, PushCategory.Reminders, "m"), Msg(inactive.Id), Msg(int.MaxValue)]);

            sent.Should().Be(5);   // on: 2 × (de + en), muted: only the unmuted category
            sender.To(onDe.Endpoint).Select(p => (p.Tag, p.Title, p.Body, p.Url)).Should().Equal(("a", "De", "Body de", "/inbox"), ("b", "De", "Body de", "/inbox"));
            sender.To(onEn.Endpoint).Select(p => p.Title).Should().Equal("En", "En");
            sender.To(mutedDevice.Endpoint).Select(p => p.Tag).Should().Equal("m");
            sender.To(inactiveDevice.Endpoint).Should().BeEmpty();
            (await db.PushSubscriptions.AsNoTracking().AnyAsync(s => s.Id == onGone.Id)).Should().BeFalse();
        });
    }

    [Fact]
    public async Task Deliver_WithoutKeysOrMessages_SendsNothing()
    {
        await WithDbAsync(async db =>
        {
            (await PushDispatcher.DeliverAsync(db, new FakePushSender { PublicKey = null }, [Msg(1)])).Should().Be(0);
            (await PushDispatcher.DeliverAsync(db, new FakePushSender(), [])).Should().Be(0);
        });
    }

    /// <summary>Throws on its first push, then records like the fake.</summary>
    private sealed class ThrowOnceSender : FakePushSender, IPushSender
    {
        private int _calls;
        public int Calls => _calls;

        Task<PushSendResult> IPushSender.SendAsync(PushSubscription subscription, string payload, CancellationToken ct) =>
            Interlocked.Increment(ref _calls) == 1 ? throw new HttpRequestException("push service down") : SendAsync(subscription, payload, ct);
    }

    [Fact]
    public async Task Dispatcher_LogsAFailingBatch_AndKeepsSending()
    {
        var sender = new ThrowOnceSender();
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<TimeTrackingDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IPushSender>(sender);
        await using var provider = services.BuildServiceProvider();
        var endpoint = NewEndpoint();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            var user = new User { Email = "dispatch@push.test", FirstName = "D", LastName = "U" };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.PushSubscriptions.Add(new PushSubscription { UserId = user.Id, Endpoint = endpoint, P256dh = "p", Auth = "a" });
            await db.SaveChangesAsync();
        }
        var userId = provider.CreateScope().ServiceProvider.GetRequiredService<TimeTrackingDbContext>().Users.Single().Id;

        var queue = new PushQueue();
        using var dispatcher = new PushDispatcher(queue, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<PushDispatcher>.Instance);
        await dispatcher.StartAsync(CancellationToken.None);
        queue.Enqueue([Msg(userId, tag: "lost")]);
        for (var i = 0; i < 250 && sender.Calls == 0; i++) await Task.Delay(20);
        queue.Enqueue([Msg(userId, tag: "next")]);

        (await sender.WaitForAsync(endpoint)).Select(p => p.Tag).Should().Equal("next");
        await dispatcher.StopAsync(CancellationToken.None);
    }
}
