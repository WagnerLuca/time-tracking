using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Phase 1 of docs/PERFORMANCE_PLAN.md: the import preview loads the member's existing entries only for the file's date
/// range (± 1 day). Duplicates must still be found at the edges of that range and for every date spelling the formats read.
/// </summary>
public class ImportDuplicateTests : IClassFixture<TimeTrackingApiFactory>
{
    private static readonly OrgClock Berlin = OrgClock.For("Europe/Berlin");
    private readonly TimeTrackingApiFactory _factory;

    public ImportDuplicateTests(TimeTrackingApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Member, string Slug)> SetupAsync(string prefix)
    {
        var (_, member, slug, orgId, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, prefix,
            new { csvImportMode = 2, timeZone = "Europe/Berlin" });
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimeTrackingDbContext>();
        void Add(DateTime fromLocal, DateTime toLocal, bool running = false)
        {
            var start = Berlin.ToUtc(fromLocal);
            db.TimeEntries.Add(new TimeEntry
            {
                UserId = memberId, OrganizationId = orgId, StartTime = start, EndTime = running ? null : Berlin.ToUtc(toLocal),
                IsRunning = running, CreatedAt = start, UpdatedAt = start
            });
        }
        // Night shift starting the day before the file's other dates, crossing midnight
        Add(new DateTime(2026, 3, 1, 22, 0, 0), new DateTime(2026, 3, 2, 2, 0, 0));
        Add(new DateTime(2026, 3, 2, 8, 0, 0), new DateTime(2026, 3, 2, 12, 0, 0));
        // Late evening on the file's last day (UTC is still the same day; Berlin +1)
        Add(new DateTime(2026, 3, 5, 23, 30, 0), new DateTime(2026, 3, 6, 1, 0, 0));
        // Far before the file: never a duplicate of anything in it
        Add(new DateTime(2025, 3, 2, 8, 0, 0), new DateTime(2025, 3, 2, 12, 0, 0));
        await db.SaveChangesAsync();
        return (member, slug);
    }

    private static async Task<JsonElement> PreviewAsync(HttpClient client, string slug, string csv)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "entries.csv");
        var response = await client.PostAsync($"/api/v1/ExportImport/import/preview?orgSlug={slug}", form);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<(string Date, string Start, bool Duplicate)> Rows(JsonElement preview) =>
        preview.GetProperty("rows").EnumerateArray()
            .Select(r => (r.GetProperty("date").GetString()!, r.GetProperty("startTime").GetString()!, r.GetProperty("isDuplicate").GetBoolean()))
            .ToList();

    [Fact]
    public async Task IsoDates_DuplicatesAtTheEdgesOfTheFileRange_AreFound()
    {
        var (member, slug) = await SetupAsync("dup-iso");
        var preview = await PreviewAsync(member, slug,
            "Date,Start Time,End Time\n" +
            "2026-03-01,22:00,02:00\n" +     // the night shift (its own start day)
            "2026-03-02,08:00,12:00\n" +
            "2026-03-03,08:00,12:00\n" +     // new
            "2026-03-05,23:30,23:59\n" +     // late evening on the last day
            "2026-03-02,13:00,15:00\n");     // same day, other time: new
        preview.GetProperty("duplicateCount").GetInt32().Should().Be(3);
        Rows(preview).Where(r => r.Duplicate).Select(r => $"{r.Date} {r.Start}")
            .Should().BeEquivalentTo("2026-03-01 22:00", "2026-03-02 08:00", "2026-03-05 23:30");
    }

    [Fact]
    public async Task IsoDates_OneFileDay_FindsTheEntryThatStartedTheDayBefore_OnlyIfTheFileHasThatDay()
    {
        var (member, slug) = await SetupAsync("dup-edge");
        // Only 2 March in the file: the night shift starts on 1 March, so a 2 March row at 22:00 is not it
        var preview = await PreviewAsync(member, slug, "Date,Start Time,End Time\n2026-03-02,08:00,12:00\n2026-03-02,22:00,23:00\n");
        Rows(preview).Should().Equal(("2026-03-02", "08:00", true), ("2026-03-02", "22:00", false));
    }

    [Theory]
    [InlineData("dup-de1", "Datum;Beginn;Ende\n01.03.2026;22:00;02:00\n02.03.2026;8:00;12:00\n03.03.2026;08:00;12:00\n05.03.2026;23:30;23:59\n")]
    [InlineData("dup-de2", "Datum;Beginn;Ende\n1.3.2026;22:00;02:00\n2.3.26;08:00:00;12:00\n03.03.26;08:00;12:00\n5.3.2026;23:30;23:59\n")]
    [InlineData("dup-slash", "Date\tStart\tEnd\n2026/03/01\t22:00\t02:00\n2026/03/02\t08:00\t12:00\n2026/03/03\t08:00\t12:00\n2026-3-5\t23:30\t23:59\n")]
    public async Task GermanAndOtherDateSpellings_StillFindDuplicates(string prefix, string csv)
    {
        var (member, slug) = await SetupAsync(prefix);
        var preview = await PreviewAsync(member, slug, csv);
        preview.GetProperty("format").GetString().Should().Be("standard");
        preview.GetProperty("duplicateCount").GetInt32().Should().Be(3,
            "rows of non-ISO files are normalised to yyyy-MM-dd before the existing entries of the file's range are loaded; " +
            "if this fails, the date-range filter in PreviewImportAsync no longer sees the file's dates");
    }

    [Fact]
    public async Task EvaluationFormat_GermanDates_StillFindDuplicates()
    {
        var (member, slug) = await SetupAsync("dup-eval");
        var csv =
            "employeeId;firstName;lastName;date;isValid;workStartTime;workStopTime;break;absenceTime;sickTime;vacationTime;notes\n" +
            "7;Test;User;01.03.2026;true;22:00;02:00;0:00;;;;\n" +
            "7;Test;User;02.03.2026;true;08:00;12:00;0:00;;;;\n" +
            "7;Test;User;04.03.2026;true;;;;;;8:00;\n" +          // vacation day, no entry
            "7;Test;User;05.03.2026;true;23:30;23:59;0:00;;;;\n";
        var preview = await PreviewAsync(member, slug, csv);
        preview.GetProperty("format").GetString().Should().Be("evaluation");
        preview.GetProperty("duplicateCount").GetInt32().Should().Be(3,
            "the evaluation format's German dates must reach the duplicate check's date range");
    }

    [Fact]
    public async Task UnreadableDatesNextToValidOnes_DoNotHideDuplicates()
    {
        var (member, slug) = await SetupAsync("dup-bad");
        var preview = await PreviewAsync(member, slug,
            "Date,Start Time,End Time\nsomeday,08:00,12:00\n2026-03-02,08:00,12:00\n0001-01-01,08:00,12:00\n");
        preview.GetProperty("duplicateCount").GetInt32().Should().Be(1);
        Rows(preview)[1].Duplicate.Should().BeTrue();
    }
}
