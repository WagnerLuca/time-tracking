using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// The CSV import formats: header detection, the lenient value readers, and a preview + confirm
/// of a trimmed, anonymised "Auswertungen" (daily evaluation) export of another tool.
/// </summary>
public class CsvImportFormatTests : IClassFixture<TimeTrackingApiFactory>
{
    private const int Allowed = 2;
    private const string EvaluationHeader =
        "employeeId;firstName;lastName;date;isValid;workStartTime;workStopTime;break;calculatedWorkTime;expectedWorkTime;holidayTime;absenceTime;sickTime;vacationTime;balance;monthlyBalance;notes";
    private const string Employee = "00000000-0000-4000-8000-000000000001;Erika;Muster";

    // BOM, ';', Unicode minus in the balances, empty start/stop on days off, H:mm times without leading zero
    private const string EvaluationCsv = "﻿" + EvaluationHeader + "\r\n" +
        Employee + ";2026-06-01;true;;;;00:00;08:00;00:00;00:00;00:00;00:00;−08:00;−08:00;Saldoübertrag (Neuer Saldo +00:00)\r\n" +
        Employee + ";2026-06-04;true;;;;08:00;08:00;08:00;00:00;00:00;00:00;−24:00;−24:00;Fronleichnam\r\n" +
        Employee + ";2026-06-06;true;;;;00:00;00:00;00:00;00:00;00:00;00:00;−32:00;−32:00;\r\n" +
        Employee + ";2026-07-01;true;7:22;17:00;01:02;08:36;08:00;00:00;00:00;00:00;00:00;67:36;00:36;Neuer Saldo: 67:00 (Übertrag aus Excel Tabellen (können auf Nachfrage ausgegeben werden)), Neuer Urlaubsanspruch: 21,0 Tage\r\n" +
        Employee + ";2026-07-03;true;7:36;13:21;00:00;05:45;08:00;00:00;00:00;00:00;00:00;66:26;−00:34;\r\n" +
        Employee + ";2026-07-15;true;;;;08:00;08:00;00:00;00:00;00:00;08:00;68:08;01:08;Urlaub\r\n" +
        Employee + ";2026-08-21;true;7:34;15:15;00:30;07:11;08:00;00:00;00:00;00:00;00:00;76:25;05:46;Variable Pause angewandt, Home Office\r\n" +
        Employee + ";2026-09-24;true;7:41;12:00;00:41;08:00;08:00;00:00;04:22;00:00;00:00;84:43;06:50;Dienstreise\r\n" +
        Employee + ";2026-09-25;true;;;;08:00;08:00;00:00;08:00;00:00;00:00;84:43;06:50;Dienstreise\r\n" +
        Employee + ";2026-09-28;true;;;;08:00;08:00;00:00;00:00;08:00;00:00;84:43;06:50;Krank\r\n" +
        Employee + ";2026-09-29;false;7:45;17:07;00:30;08:52;08:00;00:00;00:00;00:00;00:00;86:19;08:26;\r\n";

    private readonly TimeTrackingApiFactory _factory;

    public CsvImportFormatTests(TimeTrackingApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Member, string Slug, int OrgId)> SetupAsync(string prefix, string firstName = "Erika", string lastName = "Muster")
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-owner@test.com");
        var org = await TestHelpers.CreateOrganizationAsync(owner, $"{prefix} Org", $"{prefix}-org");
        var (member, m) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, $"{prefix}-m@test.com", firstName, lastName);
        (await TestHelpers.AddMemberAsync(_factory, owner, org.Slug, m)).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{org.Slug}/settings", new { csvImportMode = Allowed })).EnsureSuccessStatusCode();
        return (member, org.Slug, org.Id);
    }

    private static Task<HttpResponseMessage> PreviewAsync(HttpClient client, string slug, string csv, string? format = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "import.csv");
        var query = format == null ? "" : $"&format={format}";
        return client.PostAsync($"/api/v1/ExportImport/import/preview?orgSlug={slug}{query}", form);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var c) ? c.GetString() : null;

    private static CsvTable Table(string csv) => CsvTable.Parse(Encoding.UTF8.GetBytes(csv))!;

    // ── Pure parsing ──

    [Fact]
    public void Table_DropsTheBom_AndDetectsTheSeparator()
    {
        var table = Table(EvaluationCsv);
        table.Delimiter.Should().Be(';');
        table.Headers[0].Should().Be("employeeId");
        table.Rows.Should().HaveCount(11);
        table.Rows[0].RowNumber.Should().Be(1);

        Table("Date,Start Time,End Time\n2026-03-02,08:00,16:30\n").Delimiter.Should().Be(',');
        Table("Date\tStart\tEnd\n2026-03-02\t08:00\t16:30\n").Delimiter.Should().Be('\t');
        // Separators inside quotes do not count
        Table("\"a;b;c\",Date,Start,End\n").Delimiter.Should().Be(',');
        CsvTable.ParseLine("1;\"x;\"\"y\"\"\";3", ';').Should().Equal("1", "x;\"y\"", "3");
        CsvTable.Parse(Encoding.UTF8.GetBytes("﻿\r\n  \r\n")).Should().BeNull();
    }

    [Theory]
    [InlineData("01:02", 62)]
    [InlineData("104:00", 6240)]
    [InlineData("−08:00", -480)]   // Unicode minus
    [InlineData("-00:34", -34)]
    [InlineData("+00:30", 30)]
    [InlineData("", null)]
    [InlineData("8", null)]
    [InlineData("8:5", null)]
    [InlineData("8:75", null)]
    [InlineData("a:00", null)]
    public void ParseDuration_ReadsSignedHoursAndMinutes(string value, int? minutes) =>
        ImportValues.ParseDuration(value).Should().Be(minutes);

    [Theory]
    [InlineData("2026-03-02", "2026-03-02")]
    [InlineData("2.3.2026", "2026-03-02")]
    [InlineData("02.03.2026", "2026-03-02")]
    [InlineData("02.03.26", "2026-03-02")]
    [InlineData("2026/03/02", "2026-03-02")]
    [InlineData("someday", "someday")]
    public void NormalizeDate_UnderstandsIsoAndGermanDates(string value, string expected) =>
        ImportValues.NormalizeDate(value).Should().Be(expected);

    [Theory]
    [InlineData("7:22", "07:22")]
    [InlineData("07:22:00", "07:22")]
    [InlineData("17:00", "17:00")]
    [InlineData("nonsense", "nonsense")]
    public void NormalizeTime_PadsAndDropsSeconds(string value, string expected) =>
        ImportValues.NormalizeTime(value).Should().Be(expected);

    [Theory]
    [InlineData("30", 30)]
    [InlineData("0:45", 45)]
    [InlineData("", 0)]
    [InlineData("x", 0)]
    public void ParsePause_TakesMinutesOrHoursAndMinutes(string value, int expected) =>
        ImportValues.ParsePause(value).Should().Be(expected);

    [Fact]
    public void Detect_PicksTheFormatFromTheHeader()
    {
        CsvImportFormats.Detect(Table(EvaluationCsv))!.Id.Should().Be("evaluation");
        CsvImportFormats.Detect(Table("Datum,Tag,Beginn,Ende,Dauer (h),Pause (min),Netto (h),Beschreibung\n2026-03-02,Mo,08:00,16:30,8.5,30,8,x\n"))!.Id.Should().Be("standard");
        CsvImportFormats.Detect(Table("Datum;Von;Bis;Pause\n02.03.2026;8:00;16:30;0:30\n"))!.Id.Should().Be("standard");
        CsvImportFormats.Detect(Table("Foo,Bar\n1,2\n")).Should().BeNull();
        CsvImportFormats.Find("EVALUATION")!.Id.Should().Be("evaluation");
        CsvImportFormats.Find("nope").Should().BeNull();
    }

    [Theory]
    [InlineData("Variable Pause angewandt", null)]
    [InlineData("Variable Pause angewandt, Home Office", "Home Office")]
    [InlineData("Neuer Saldo: 67:00 (Übertrag aus Excel Tabellen (können, auf Nachfrage)), Neuer Urlaubsanspruch: 21,0 Tage", null)]
    [InlineData("Variable Pause angewandt, Saldoübertrag (Neuer Saldo +77:53)", null)]
    [InlineData("Dienstreise", "Dienstreise")]
    [InlineData("", null)]
    public void CleanNotes_DropsTheToolsBookkeeping(string notes, string? expected) =>
        EvaluationCsvFormat.CleanNotes(notes).Should().Be(expected);

    [Fact]
    public void Evaluation_ParsesWorkDays_AndCountsWhatItSkips()
    {
        var parsed = new EvaluationCsvFormat().Parse(Table(EvaluationCsv));

        parsed.Employees.Should().Equal("Erika Muster");
        parsed.Rows.Select(r => r.Date).Should().Equal("2026-07-01", "2026-07-03", "2026-08-21", "2026-09-24", "2026-09-29");
        parsed.Rows[0].Should().Be(new ParsedImportRow(4, "2026-07-01", "07:22", "17:00", 62, null));
        parsed.Rows[2].Description.Should().Be("Home Office");
        parsed.Rows[3].PauseMinutes.Should().Be(41);
        parsed.Rows[4].Warning.Should().Be("Marked as invalid in the source file.");
        parsed.SkippedRowCount.Should().Be(6);
        parsed.VacationDays.Should().Be(1);
        parsed.SickDays.Should().Be(1);
        parsed.OtherAbsenceDays.Should().Be(2);   // business trip: half day with work + full day
    }

    [Fact]
    public void Evaluation_FlagsRowsWithHalfATimeOrABadBreak()
    {
        var parsed = new EvaluationCsvFormat().Parse(Table(EvaluationHeader + "\n" +
            Employee + ";2026-07-01;true;7:00;;;;;;;;;;;;\n" +
            Employee + ";2026-07-02;true;7:00;15:00;lunch;;;;;;;;;;\n"));
        parsed.Rows.Select(r => r.Warning).Should().Equal("Start or end time is missing.", "Invalid break duration.");
    }

    // ── Through the API ──

    [Fact]
    public async Task Evaluation_PreviewThenConfirm_CreatesImportedEntriesWithManualPauses()
    {
        var (member, slug, orgId) = await SetupAsync("fmt-eval");

        var preview = await PreviewAsync(member, slug, EvaluationCsv);
        preview.EnsureSuccessStatusCode();
        var body = await preview.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("format").GetString().Should().Be("evaluation");
        body.GetProperty("totalRows").GetInt32().Should().Be(5);
        body.GetProperty("skippedRowCount").GetInt32().Should().Be(6);
        body.GetProperty("skippedVacationDays").GetInt32().Should().Be(1);
        body.GetProperty("skippedSickDays").GetInt32().Should().Be(1);
        body.GetProperty("skippedOtherAbsenceDays").GetInt32().Should().Be(2);
        body.GetProperty("employeeName").GetString().Should().Be("Erika Muster");
        body.GetProperty("employeeMatchesUser").GetBoolean().Should().BeTrue();

        var rows = body.GetProperty("rows").EnumerateArray().ToList();
        rows[0].GetProperty("startTime").GetString().Should().Be("07:22");
        rows[0].GetProperty("pauseMinutes").GetInt32().Should().Be(62);
        rows[4].GetProperty("warning").GetString().Should().NotBeNullOrEmpty();

        // The client sends back the rows without a warning, as the UI selects them by default
        var entries = rows.Where(r => r.GetProperty("warning").ValueKind == JsonValueKind.Null).Select(r => new
        {
            date = r.GetProperty("date").GetString(),
            startTime = r.GetProperty("startTime").GetString(),
            endTime = r.GetProperty("endTime").GetString(),
            pauseMinutes = r.GetProperty("pauseMinutes").GetInt32(),
            description = r.GetProperty("description").GetString()
        }).ToList();
        entries.Should().HaveCount(4);
        var confirm = await member.PostAsJsonAsync($"/api/v1/ExportImport/import/confirm?orgSlug={slug}", entries);
        confirm.EnsureSuccessStatusCode();
        (await confirm.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("importedCount").GetInt32().Should().Be(4);

        var history = await member.GetFromJsonAsync<JsonElement>($"/api/v1/TimeTracking?organizationId={orgId}");
        var items = history.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(4).And.OnlyContain(e => e.GetProperty("source").GetString() == "Import");
        var first = items.Single(e => e.GetProperty("startTime").GetDateTime().Date == new DateTime(2026, 7, 1));
        first.GetProperty("startTime").GetDateTime().Should().Be(new DateTime(2026, 7, 1, 5, 22, 0));   // 07:22 Berlin (CEST)
        first.GetProperty("pauseDurationMinutes").GetInt32().Should().Be(62);
        items.Should().Contain(e => e.GetProperty("description").GetString() == "Home Office");

        // Importing the same file again flags the imported days as duplicates
        var again = await (await PreviewAsync(member, slug, EvaluationCsv)).Content.ReadFromJsonAsync<JsonElement>();
        again.GetProperty("duplicateCount").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task Evaluation_OfSomeoneElse_IsFlagged_AndSeveralEmployeesAreRejected()
    {
        var (member, slug, _) = await SetupAsync("fmt-who", "Max", "Beispiel");

        var other = await (await PreviewAsync(member, slug, EvaluationCsv)).Content.ReadFromJsonAsync<JsonElement>();
        other.GetProperty("employeeMatchesUser").GetBoolean().Should().BeFalse();

        var mixed = EvaluationCsv + "00000000-0000-4000-8000-000000000002;Max;Beispiel;2026-07-01;true;8:00;16:00;00:30;;;;;;;;;\n";
        var response = await PreviewAsync(member, slug, mixed);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("import.severalEmployees");
    }

    [Fact]
    public async Task Standard_WithSemicolonsGermanDatesAndHourPauses_IsNormalized()
    {
        var (member, slug, _) = await SetupAsync("fmt-std");
        var response = await PreviewAsync(member, slug, "Datum;Beginn;Ende;Pause;Beschreibung\n02.03.2026;8:00;16:30;0:30;\"Review; Planung\"\n");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("format").GetString().Should().Be("standard");
        body.TryGetProperty("employeeName", out var name).Should().BeTrue();
        name.ValueKind.Should().Be(JsonValueKind.Null);
        var row = body.GetProperty("rows")[0];
        row.GetProperty("date").GetString().Should().Be("2026-03-02");
        row.GetProperty("startTime").GetString().Should().Be("08:00");
        row.GetProperty("pauseMinutes").GetInt32().Should().Be(30);
        row.GetProperty("description").GetString().Should().Be("Review; Planung");
        row.GetProperty("warning").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ChosenFormat_MustFitTheFile()
    {
        var (member, slug, _) = await SetupAsync("fmt-pick");
        const string standard = "Date,Start Time,End Time\n2026-03-02,08:00,16:30\n";

        (await PreviewAsync(member, slug, standard, "standard")).EnsureSuccessStatusCode();
        (await PreviewAsync(member, slug, EvaluationCsv, "auto")).EnsureSuccessStatusCode();

        var mismatch = await PreviewAsync(member, slug, standard, "evaluation");
        mismatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(mismatch)).Should().Be("import.formatMismatch");

        var unknown = await PreviewAsync(member, slug, standard, "excel");
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(unknown)).Should().Be("import.unknownFormat");
    }

    [Fact]
    public async Task RowLimit_CountsEntries_NotDaysOff()
    {
        var (member, slug, _) = await SetupAsync("fmt-limit");

        // 1200 days, of which only 3 have work: fits
        var sb = new StringBuilder(EvaluationHeader).Append('\n');
        var day = new DateOnly(2023, 1, 1);
        for (var i = 0; i < 1200; i++, day = day.AddDays(1))
            sb.Append(Employee).Append(';').Append(day.ToString("yyyy-MM-dd")).Append(i % 400 == 0 ? ";true;8:00;16:00;00:30;" : ";true;;;;").Append(";;;;;;;;\n");
        var fits = await PreviewAsync(member, slug, sb.ToString());
        fits.EnsureSuccessStatusCode();
        var body = await fits.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("totalRows").GetInt32().Should().Be(3);
        body.GetProperty("skippedRowCount").GetInt32().Should().Be(1197);

        // 1001 entries: too many
        var tooMany = new StringBuilder("Date,Start Time,End Time\n");
        for (var i = 0; i < 1001; i++) tooMany.Append("2025-01-01,08:00,09:00\n");
        var response = await PreviewAsync(member, slug, tooMany.ToString());
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("import.tooManyRows");
    }

    [Fact]
    public async Task OnlyDaysOff_GivesAnEmptyPreview_AndAHeaderAloneIsRejected()
    {
        var (member, slug, _) = await SetupAsync("fmt-empty");
        var daysOff = EvaluationHeader + "\n" + Employee + ";2026-06-06;true;;;;00:00;00:00;00:00;00:00;00:00;00:00;−32:00;−32:00;\n";
        var body = await (await PreviewAsync(member, slug, daysOff)).Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("totalRows").GetInt32().Should().Be(0);
        body.GetProperty("skippedRowCount").GetInt32().Should().Be(1);

        var headerOnly = await PreviewAsync(member, slug, "﻿" + EvaluationHeader + "\n");
        headerOnly.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(headerOnly)).Should().Be("import.empty");
    }
}
