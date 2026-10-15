using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Integration tests for organization API keys, member cards, the punch protocol and the
/// terminal punch endpoint (double scan, offline queue, idempotency, unknown/blocked cards).
/// </summary>
public class ApiKeysControllerTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public ApiKeysControllerTests(TimeTrackingApiFactory factory) => _factory = factory;

    private sealed record Setup(HttpClient Owner, HttpClient Member, string Slug, int OrgId, int MemberId, string Key, int KeyId);

    /// <summary>Owner with an org, one member with a card, and a fresh API key named "Entrance".</summary>
    private async Task<Setup> SetupAsync(string prefix, string card = "RFID-1")
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-owner@test.com");
        var org = await TestHelpers.CreateOrganizationAsync(owner, $"{prefix} Org", $"{prefix}-org");
        var (member, m) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-m@test.com", "Mia", "Member");
        (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, m)).EnsureSuccessStatusCode();
        (await AddCardAsync(owner, org.Slug, m.User.Id, new { cardId = card })).StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await owner.PostAsJsonAsync($"/api/v1/organizations/{org.Slug}/api-keys", new { name = "Entrance" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        return new Setup(owner, member, org.Slug, org.Id, m.User.Id, body.GetProperty("key").GetString()!, body.GetProperty("id").GetInt32());
    }

    private static Task<HttpResponseMessage> AddCardAsync(HttpClient admin, string slug, int memberId, object body) =>
        admin.PostAsJsonAsync($"/api/v1/organizations/{slug}/members/{memberId}/cards", body);

    private Task<HttpResponseMessage> PunchAsync(string? key, string badge, DateTime? scannedAt = null, string? clientPunchId = null)
    {
        var client = _factory.CreateClient();
        if (key != null) client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client.PostAsJsonAsync("/api/v1/terminal/punch", new { badgeId = badge, scannedAt, clientPunchId });
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).TryGetProperty("code", out var code) ? code.GetString() : null;

    private static DateTime Minutes(int offset) => DateTime.UtcNow.AddMinutes(offset);

    // ── Toggle, keys ────────────────────────────────────────────────────

    [Fact]
    public async Task Punch_TogglesMembersTimer_StartThenStop()
    {
        var s = await SetupAsync("punch");

        var first = await PunchAsync(s.Key, "RFID-1", scannedAt: Minutes(-10));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var started = await JsonAsync(first);
        started.GetProperty("action").GetString().Should().Be("Started");
        started.GetProperty("repeated").GetBoolean().Should().BeFalse();
        started.GetProperty("firstName").GetString().Should().Be("Mia");

        var current = await s.Member.GetFromJsonAsync<JsonElement>("/api/v1/TimeTracking/current");
        current.GetProperty("isRunning").GetBoolean().Should().BeTrue();
        current.GetProperty("source").GetString().Should().Be("Terminal");

        var second = await PunchAsync(s.Key, "RFID-1");
        (await JsonAsync(second)).GetProperty("action").GetString().Should().Be("Stopped");
    }

    [Fact]
    public async Task Punch_RejectsMissingWrongOrRevokedKey_AndUnknownCard()
    {
        var s = await SetupAsync("punch-deny");

        (await PunchAsync(null, "RFID-1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PunchAsync("tt_not-a-real-key", "RFID-1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var unknown = await PunchAsync(s.Key, "NOPE");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(unknown)).Should().Be("terminal.unknownCard");

        (await s.Owner.DeleteAsync($"/api/v1/organizations/{s.Slug}/api-keys/{s.KeyId}")).EnsureSuccessStatusCode();
        (await PunchAsync(s.Key, "RFID-1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // The protocol keeps the revoked key's rows
        (await s.Owner.GetFromJsonAsync<List<JsonElement>>($"/api/v1/organizations/{s.Slug}/terminal/punches")).Should().ContainSingle();
    }

    [Fact]
    public async Task Key_OnlyReachesItsOwnOrganizationsCards()
    {
        var a = await SetupAsync("iso-a", card: "SHARED");
        await SetupAsync("iso-b", card: "OTHER");

        (await PunchAsync(a.Key, "OTHER")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Key_IsNotAUserCredential()
    {
        var s = await SetupAsync("not-user");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", s.Key);

        (await client.GetAsync("/api/v1/Auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_NeverReturnsSecret_AndMembersCannotManageKeysOrCards()
    {
        var s = await SetupAsync("list");

        var list = await s.Owner.GetStringAsync($"/api/v1/organizations/{s.Slug}/api-keys");
        list.Should().Contain("Entrance").And.NotContain(s.Key);

        (await s.Member.GetAsync($"/api/v1/organizations/{s.Slug}/api-keys")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.Member.PostAsJsonAsync($"/api/v1/organizations/{s.Slug}/api-keys", new { name = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.Member.PutAsJsonAsync($"/api/v1/organizations/{s.Slug}/api-keys/{s.KeyId}", new { name = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AddCardAsync(s.Member, s.Slug, s.MemberId, new { cardId = "MINE" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.Member.GetAsync($"/api/v1/organizations/{s.Slug}/members/{s.MemberId}/cards")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.Member.GetAsync($"/api/v1/organizations/{s.Slug}/terminal/unknown-cards")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.Member.GetAsync($"/api/v1/organizations/{s.Slug}/terminal/punches")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RenamedKey_IsTheTerminalNameInProtocolAndEntries()
    {
        var s = await SetupAsync("rename");
        var renamed = await s.Owner.PutAsJsonAsync($"/api/v1/organizations/{s.Slug}/api-keys/{s.KeyId}", new { name = "Eingang" });
        (await JsonAsync(renamed)).GetProperty("name").GetString().Should().Be("Eingang");

        var punch = await JsonAsync(await PunchAsync(s.Key, "RFID-1"));
        punch.GetProperty("entry").GetProperty("terminalStartName").GetString().Should().Be("Eingang");

        var protocol = await s.Owner.GetFromJsonAsync<List<JsonElement>>($"/api/v1/organizations/{s.Slug}/terminal/punches");
        protocol!.Single().GetProperty("terminalName").GetString().Should().Be("Eingang");
        protocol!.Single().GetProperty("memberName").GetString().Should().Be("Mia Member");
        protocol!.Single().GetProperty("result").GetString().Should().Be("Started");
    }

    // ── Double scan, idempotency, offline queue ─────────────────────────

    [Fact]
    public async Task SecondScanWithinAMinute_TogglesNothing_AndReturnsThePreviousResult()
    {
        var s = await SetupAsync("double");

        var first = await JsonAsync(await PunchAsync(s.Key, "RFID-1"));
        var again = await PunchAsync(s.Key, "rfid-1");
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        var repeated = await JsonAsync(again);
        repeated.GetProperty("repeated").GetBoolean().Should().BeTrue();
        repeated.GetProperty("action").GetString().Should().Be("Started");
        repeated.GetProperty("entry").GetProperty("id").GetInt32().Should().Be(first.GetProperty("entry").GetProperty("id").GetInt32());

        (await s.Member.GetFromJsonAsync<JsonElement>("/api/v1/TimeTracking/current")).GetProperty("isRunning").GetBoolean().Should().BeTrue();
        var results = (await s.Owner.GetFromJsonAsync<List<JsonElement>>($"/api/v1/organizations/{s.Slug}/terminal/punches"))!
            .Select(p => p.GetProperty("result").GetString());
        results.Should().Equal("Repeated", "Started");
    }

    [Fact]
    public async Task ReplayedClientPunchId_ReturnsStoredResult_AndChangesNothing()
    {
        var s = await SetupAsync("replay");

        var first = await JsonAsync(await PunchAsync(s.Key, "RFID-1", Minutes(-30), "p-1"));
        first.GetProperty("action").GetString().Should().Be("Started");
        // Replayed after the stop: still the stored "Started", the timer stays stopped
        (await JsonAsync(await PunchAsync(s.Key, "RFID-1", Minutes(-20), "p-2"))).GetProperty("action").GetString().Should().Be("Stopped");
        var replay = await PunchAsync(s.Key, "RFID-1", Minutes(-30), "p-1");
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await JsonAsync(replay);
        body.GetProperty("action").GetString().Should().Be("Started");
        body.GetProperty("entry").GetProperty("id").GetInt32().Should().Be(first.GetProperty("entry").GetProperty("id").GetInt32());

        (await s.Member.GetAsync("/api/v1/TimeTracking/current")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Owner.GetFromJsonAsync<List<JsonElement>>($"/api/v1/organizations/{s.Slug}/terminal/punches")).Should().HaveCount(2);
    }

    [Fact]
    public async Task QueuedScans_UseScannedAt_ForStartAndEnd()
    {
        var s = await SetupAsync("offline");
        var start = DateTime.UtcNow.AddHours(-5).AddTicks(-(DateTime.UtcNow.Ticks % TimeSpan.TicksPerSecond));
        var end = start.AddHours(4);

        (await PunchAsync(s.Key, "RFID-1", start, "q-1")).EnsureSuccessStatusCode();
        var stop = await JsonAsync(await PunchAsync(s.Key, "RFID-1", end, "q-2"));
        stop.GetProperty("action").GetString().Should().Be("Stopped");
        var entry = stop.GetProperty("entry");
        entry.GetProperty("startTime").GetDateTime().ToUniversalTime().Should().BeCloseTo(start, TimeSpan.FromMilliseconds(1));
        entry.GetProperty("endTime").GetDateTime().ToUniversalTime().Should().BeCloseTo(end, TimeSpan.FromMilliseconds(1));
        // Pause rules apply as on any stop (new orgs: 30 min from 6 h; 4 h → none)
        entry.GetProperty("pauseDurationMinutes").GetInt32().Should().Be(0);
        entry.GetProperty("terminalStopName").GetString().Should().Be("Entrance");
    }

    [Fact]
    public async Task QueuedScans_OutsideTheWindowOrOutOfOrder_AreRejected()
    {
        var s = await SetupAsync("offline-bad");

        var tooOld = await PunchAsync(s.Key, "RFID-1", DateTime.UtcNow.AddHours(-73));
        tooOld.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(tooOld)).Should().Be("terminal.scannedAtOutOfRange");
        var future = await PunchAsync(s.Key, "RFID-1", Minutes(5));
        (await CodeAsync(future)).Should().Be("terminal.scannedAtOutOfRange");

        (await PunchAsync(s.Key, "RFID-1", Minutes(-60))).EnsureSuccessStatusCode();
        var earlier = await PunchAsync(s.Key, "RFID-1", Minutes(-120));
        earlier.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(earlier)).Should().Be("terminal.outOfOrder");

        // Still running, and the refusal is in the protocol
        (await s.Member.GetFromJsonAsync<JsonElement>("/api/v1/TimeTracking/current")).GetProperty("isRunning").GetBoolean().Should().BeTrue();
        var results = (await s.Owner.GetFromJsonAsync<List<JsonElement>>($"/api/v1/organizations/{s.Slug}/terminal/punches"))!
            .Select(p => p.GetProperty("result").GetString());
        results.Should().Contain("Rejected");
    }

    [Fact]
    public async Task QueuedScan_ClosesTheFailureEntry_InsteadOfStartingATimer()
    {
        var s = await SetupAsync("close-failure");
        var start = DateTime.UtcNow.AddHours(-10);
        int failureId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
            var failure = new TimeEntry
            {
                UserId = s.MemberId, OrganizationId = s.OrgId, StartTime = start, EndTime = DateTime.UtcNow.AddHours(-1),
                IsFailure = true, Source = TimeEntrySource.Terminal, CreatedAt = start, UpdatedAt = start
            };
            db.TimeEntries.Add(failure);
            await db.SaveChangesAsync();
            failureId = failure.Id;
        }

        var leftAt = start.AddHours(7);
        var punch = await JsonAsync(await PunchAsync(s.Key, "RFID-1", leftAt, "late-1"));
        punch.GetProperty("action").GetString().Should().Be("ClosedFailure");
        var entry = punch.GetProperty("entry");
        entry.GetProperty("id").GetInt32().Should().Be(failureId);
        entry.GetProperty("isFailure").GetBoolean().Should().BeFalse();
        entry.GetProperty("endTime").GetDateTime().ToUniversalTime().Should().BeCloseTo(leftAt, TimeSpan.FromMilliseconds(1));
        // 7 h → the 6 h rule's 30 min pause
        entry.GetProperty("pauseDurationMinutes").GetInt32().Should().Be(30);

        (await s.Member.GetAsync("/api/v1/TimeTracking/current")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Member.GetFromJsonAsync<List<JsonElement>>("/api/v1/TimeTracking/failures")).Should().BeEmpty();
    }

    // ── Cards: unknown, adopted, blocked, several ───────────────────────

    [Fact]
    public async Task UnknownCard_IsListed_Adopted_AndThenPunches()
    {
        var s = await SetupAsync("enrol");

        (await PunchAsync(s.Key, "04:a1:b2:c3")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var second = await PunchAsync(s.Key, "04 A1 B2 C3");
        (await second.Content.ReadAsStringAsync()).Should().NotContain("04A1B2C3");

        var unknown = await s.Owner.GetStringAsync($"/api/v1/organizations/{s.Slug}/terminal/unknown-cards");
        unknown.Should().NotContain("04A1B2C3").And.NotContain("04:a1");
        var list = JsonSerializer.Deserialize<List<JsonElement>>(unknown)!;
        list.Should().ContainSingle();
        list[0].GetProperty("cardHint").GetString().Should().Be("B2C3");
        list[0].GetProperty("count").GetInt32().Should().Be(2);
        list[0].GetProperty("terminalName").GetString().Should().Be("Entrance");

        var adopted = await AddCardAsync(s.Owner, s.Slug, s.MemberId, new { fromPunchId = list[0].GetProperty("punchId").GetInt64(), label = "Spare" });
        adopted.StatusCode.Should().Be(HttpStatusCode.Created);
        (await adopted.Content.ReadAsStringAsync()).Should().NotContain("04A1B2C3");
        (await s.Owner.GetFromJsonAsync<List<JsonElement>>($"/api/v1/organizations/{s.Slug}/terminal/unknown-cards")).Should().BeEmpty();

        var punch = await PunchAsync(s.Key, "04A1B2C3");
        punch.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(punch)).GetProperty("action").GetString().Should().Be("Started");

        var cards = await s.Owner.GetFromJsonAsync<List<JsonElement>>($"/api/v1/organizations/{s.Slug}/members/{s.MemberId}/cards");
        cards!.Select(c => c.GetProperty("cardHint").GetString()).Should().BeEquivalentTo("ID-1", "B2C3");
        cards!.Single(c => c.GetProperty("cardHint").GetString() == "B2C3").GetProperty("lastUsedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task BlockedCard_Is403_Logged_AndTogglesNothing()
    {
        var s = await SetupAsync("blocked");
        var cardId = (await s.Owner.GetFromJsonAsync<List<JsonElement>>($"/api/v1/organizations/{s.Slug}/members/{s.MemberId}/cards"))!
            .Single().GetProperty("id").GetInt32();
        (await s.Owner.PutAsJsonAsync($"/api/v1/organizations/{s.Slug}/members/{s.MemberId}/cards/{cardId}", new { isBlocked = true, label = "Lost" }))
            .EnsureSuccessStatusCode();

        var blocked = await PunchAsync(s.Key, "RFID-1");
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(blocked)).Should().Be("terminal.cardBlocked");
        (await s.Member.GetAsync("/api/v1/TimeTracking/current")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var row = (await s.Owner.GetFromJsonAsync<List<JsonElement>>($"/api/v1/organizations/{s.Slug}/terminal/punches"))!.Single();
        row.GetProperty("result").GetString().Should().Be("Blocked");
        row.GetProperty("cardLabel").GetString().Should().Be("Lost");

        (await s.Owner.PutAsJsonAsync($"/api/v1/organizations/{s.Slug}/members/{s.MemberId}/cards/{cardId}", new { isBlocked = false }))
            .EnsureSuccessStatusCode();
        (await PunchAsync(s.Key, "RFID-1")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SeveralCards_EachToggleTheSameMember_AndCardsAreUniquePerOrganization()
    {
        var s = await SetupAsync("multi");
        (await AddCardAsync(s.Owner, s.Slug, s.MemberId, new { cardId = "KEYFOB-2", label = "Fob" })).StatusCode.Should().Be(HttpStatusCode.Created);

        // A different card right after is a real toggle, not a double scan
        (await JsonAsync(await PunchAsync(s.Key, "RFID-1"))).GetProperty("action").GetString().Should().Be("Started");
        (await JsonAsync(await PunchAsync(s.Key, "KEYFOB-2"))).GetProperty("action").GetString().Should().Be("Stopped");

        var (_, other) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "multi-2@test.com");
        (await TestHelpers.AddMemberAsync(_factory, s.Owner, s.Slug, other)).EnsureSuccessStatusCode();
        var dup = await AddCardAsync(s.Owner, s.Slug, other.User.Id, new { cardId = "rfid-1" });
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(dup)).Should().Be("terminal.cardTaken");
        (await CodeAsync(await AddCardAsync(s.Owner, s.Slug, other.User.Id, new { cardId = " : " }))).Should().Be("terminal.cardRequired");

        var org = await s.Owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{s.Slug}");
        org.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("id").GetInt32() == s.MemberId)
            .GetProperty("cardCount").GetInt32().Should().Be(2);

        var cardId = (await s.Owner.GetFromJsonAsync<List<JsonElement>>($"/api/v1/organizations/{s.Slug}/members/{s.MemberId}/cards"))!
            .Single(c => c.GetProperty("label").GetString() == "Fob").GetProperty("id").GetInt32();
        (await s.Owner.DeleteAsync($"/api/v1/organizations/{s.Slug}/members/{s.MemberId}/cards/{cardId}")).EnsureSuccessStatusCode();
        (await PunchAsync(s.Key, "KEYFOB-2")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RemovedMember_LosesTheirCards()
    {
        var s = await SetupAsync("removed");
        (await s.Owner.DeleteAsync($"/api/v1/Organizations/{s.Slug}/members/{s.MemberId}")).EnsureSuccessStatusCode();

        (await PunchAsync(s.Key, "RFID-1")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PersonalDataExport_ListsCardsByHintOnly_AndThePunchLog()
    {
        var s = await SetupAsync("gdpr", card: "SECRET-UID-9876");

        (await PunchAsync(s.Key, "SECRET-UID-9876")).StatusCode.Should().Be(HttpStatusCode.OK);

        var export = await s.Member.GetStringAsync("/api/v1/Auth/me/export");
        export.Should().Contain("9876").And.NotContain("SECRET-UID");
        // The punch log is part of it (GDPR Art. 15): time, terminal, result
        var punch = JsonDocument.Parse(export).RootElement.GetProperty("terminalPunches").EnumerateArray().Single();
        (punch.GetProperty("terminal").GetString(), punch.GetProperty("result").GetString(), punch.GetProperty("cardHint").GetString())
            .Should().Be(("Entrance", "Started", "9876"));
        punch.GetProperty("scannedAt").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        (await s.Owner.GetStringAsync("/api/v1/Auth/me/export")).Should().Contain("\"terminalPunches\": []");
    }

    [Fact]
    public async Task UnknownScans_BeyondTheBudget_Get429_WhileKnownCardsKeepWorking()
    {
        var s = await SetupAsync("brute");

        for (var i = 0; i < 20; i++)
            (await PunchAsync(s.Key, $"GUESS-{i}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var limited = await PunchAsync(s.Key, "GUESS-X");
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await CodeAsync(limited)).Should().Be("terminal.tooManyUnknown");

        (await PunchAsync(s.Key, "RFID-1")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Terminal settings per organization ──────────────────────────────

    private static Task<HttpResponseMessage> SetTerminalAsync(Setup s, object settings) =>
        s.Owner.PutAsJsonAsync($"/api/v1/Organizations/{s.Slug}/settings", settings);

    [Fact]
    public async Task TerminalSettings_DefaultToTheOldConstants_AndAreValidated()
    {
        var s = await SetupAsync("tset-validate");
        var detail = await s.Owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{s.Slug}");
        detail.GetProperty("terminalDoubleScanSeconds").GetInt32().Should().Be(60);
        detail.GetProperty("terminalOfflineHours").GetInt32().Should().Be(72);
        detail.GetProperty("terminalUnknownCardLimit").GetInt32().Should().Be(20);

        foreach (var (body, code) in new (object, string)[]
                 {
                     (new { terminalDoubleScanSeconds = 4 }, "terminal.doubleScanRange"),
                     (new { terminalDoubleScanSeconds = 601 }, "terminal.doubleScanRange"),
                     (new { terminalOfflineHours = 0 }, "terminal.offlineRange"),
                     (new { terminalOfflineHours = 169 }, "terminal.offlineRange"),
                     (new { terminalUnknownCardLimit = 4 }, "terminal.unknownLimitRange"),
                     (new { terminalUnknownCardLimit = 201 }, "terminal.unknownLimitRange")
                 })
        {
            var response = await SetTerminalAsync(s, body);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await CodeAsync(response)).Should().Be(code);
        }

        var ok = await JsonAsync(await SetTerminalAsync(s, new { terminalDoubleScanSeconds = 5, terminalOfflineHours = 168, terminalUnknownCardLimit = 200 }));
        ok.GetProperty("terminalDoubleScanSeconds").GetInt32().Should().Be(5);
        ok.GetProperty("terminalOfflineHours").GetInt32().Should().Be(168);
        ok.GetProperty("terminalUnknownCardLimit").GetInt32().Should().Be(200);
    }

    [Fact]
    public async Task DoubleScanWindow_IsTheOrganizationsSetting()
    {
        var s = await SetupAsync("tset-double");
        (await SetTerminalAsync(s, new { terminalDoubleScanSeconds = 300 })).EnsureSuccessStatusCode();

        // 2 minutes apart: a repeat under 300 s (it would toggle with the default 60 s)
        (await PunchAsync(s.Key, "RFID-1", Minutes(-3))).EnsureSuccessStatusCode();
        (await JsonAsync(await PunchAsync(s.Key, "RFID-1", Minutes(-1)))).GetProperty("repeated").GetBoolean().Should().BeTrue();

        (await SetTerminalAsync(s, new { terminalDoubleScanSeconds = 5 })).EnsureSuccessStatusCode();
        (await JsonAsync(await PunchAsync(s.Key, "RFID-1"))).GetProperty("action").GetString().Should().Be("Stopped");
    }

    [Fact]
    public async Task OfflineWindow_IsTheOrganizationsSetting()
    {
        var s = await SetupAsync("tset-offline");
        (await SetTerminalAsync(s, new { terminalOfflineHours = 2 })).EnsureSuccessStatusCode();

        var tooOld = await PunchAsync(s.Key, "RFID-1", DateTime.UtcNow.AddHours(-3));
        tooOld.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await JsonAsync(tooOld);
        problem.GetProperty("code").GetString().Should().Be("terminal.scannedAtOutOfRange");
        problem.GetProperty("detail").GetString().Should().Contain("2 hours");

        (await SetTerminalAsync(s, new { terminalOfflineHours = 100 })).EnsureSuccessStatusCode();
        (await PunchAsync(s.Key, "RFID-1", DateTime.UtcNow.AddHours(-80))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnknownCardBudget_IsTheOrganizationsSetting()
    {
        var s = await SetupAsync("tset-budget");
        (await SetTerminalAsync(s, new { terminalUnknownCardLimit = 5 })).EnsureSuccessStatusCode();

        for (var i = 0; i < 5; i++)
            (await PunchAsync(s.Key, $"GUESS-{i}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PunchAsync(s.Key, "GUESS-X")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        (await SetTerminalAsync(s, new { terminalUnknownCardLimit = 7 })).EnsureSuccessStatusCode();
        (await PunchAsync(s.Key, "GUESS-Y")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Entry source per path ───────────────────────────────────────────

    [Fact]
    public async Task Source_IsSetOnEveryPathThatCreatesEntries()
    {
        var s = await SetupAsync("source");
        (await s.Owner.PutAsJsonAsync($"/api/v1/Organizations/{s.Slug}/settings", new { memberTimeEntryVisibility = true, csvImportMode = 2 }))
            .EnsureSuccessStatusCode();
        var day = DateTime.UtcNow.Date.AddDays(-3);

        // App timer
        (await s.Member.PostAsJsonAsync("/api/v1/TimeTracking/start", new { organizationId = s.OrgId })).EnsureSuccessStatusCode();
        (await s.Member.PostAsJsonAsync("/api/v1/TimeTracking/stop", new { })).EnsureSuccessStatusCode();
        // Manual
        (await s.Member.PostAsJsonAsync("/api/v1/TimeTracking/manual",
            new { startTime = day.AddHours(6), endTime = day.AddHours(8), organizationId = s.OrgId })).StatusCode.Should().Be(HttpStatusCode.Created);
        // Admin
        (await s.Owner.PostAsJsonAsync($"/api/v1/organizations/{s.Slug}/members/{s.MemberId}/entries",
            new { startTime = day.AddHours(9), endTime = day.AddHours(10) })).StatusCode.Should().Be(HttpStatusCode.Created);
        // CSV import (local time, a day earlier)
        var importDay = DateOnly.FromDateTime(day.AddDays(-1)).ToString("yyyy-MM-dd");
        (await s.Member.PostAsJsonAsync($"/api/v1/ExportImport/import/confirm?orgSlug={s.Slug}",
            new[] { new { date = importDay, startTime = "08:00", endTime = "09:00", pauseMinutes = 0, description = "Imported" } })).EnsureSuccessStatusCode();
        // Terminal (live; the app timer above is stopped)
        (await PunchAsync(s.Key, "RFID-1")).EnsureSuccessStatusCode();

        var history = await s.Member.GetFromJsonAsync<JsonElement>($"/api/v1/TimeTracking?organizationId={s.OrgId}");
        var sources = history.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("source").GetString()).ToList();
        sources.Should().BeEquivalentTo("Terminal", "App", "Manual", "Admin", "Import");

        var memberView = await s.Owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/Organizations/{s.Slug}/member-entries/{s.MemberId}?from={DateTime.UtcNow.AddDays(-1):o}&to={DateTime.UtcNow.AddDays(1):o}");
        var terminalEntry = memberView.GetProperty("items").EnumerateArray().Single(e => e.GetProperty("source").GetString() == "Terminal");
        terminalEntry.GetProperty("terminalStartName").GetString().Should().Be("Entrance");
    }
}
