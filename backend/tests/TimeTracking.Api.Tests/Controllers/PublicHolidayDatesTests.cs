using FluentAssertions;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Every holiday region's dates against an independent list: Nager.Date
/// (date.nager.at, fetched 2026-10-27: DE holidays that are global or name the state, AT/CH
/// global ones), cross-checked with feiertage-api.de — both agree for every German state, except
/// that feiertage-api.de only notes Berlin's one-off 08.05.2025 (Berlin law makes it a holiday).
/// </summary>
public class PublicHolidayDatesTests
{
    private const string Expected = """
        2025 DE    01-01 04-18 04-21 05-01 05-29 06-09 10-03 12-25 12-26
        2025 DE-BW 01-01 01-06 04-18 04-21 05-01 05-29 06-09 06-19 10-03 11-01 12-25 12-26
        2025 DE-BY 01-01 01-06 04-18 04-21 05-01 05-29 06-09 06-19 10-03 11-01 12-25 12-26
        2025 DE-BE 01-01 03-08 04-18 04-21 05-01 05-08 05-29 06-09 10-03 12-25 12-26
        2025 DE-BB 01-01 04-18 04-20 04-21 05-01 05-29 06-08 06-09 10-03 10-31 12-25 12-26
        2025 DE-HB 01-01 04-18 04-21 05-01 05-29 06-09 10-03 10-31 12-25 12-26
        2025 DE-HH 01-01 04-18 04-21 05-01 05-29 06-09 10-03 10-31 12-25 12-26
        2025 DE-HE 01-01 04-18 04-21 05-01 05-29 06-09 06-19 10-03 12-25 12-26
        2025 DE-MV 01-01 03-08 04-18 04-21 05-01 05-29 06-09 10-03 10-31 12-25 12-26
        2025 DE-NI 01-01 04-18 04-21 05-01 05-29 06-09 10-03 10-31 12-25 12-26
        2025 DE-NW 01-01 04-18 04-21 05-01 05-29 06-09 06-19 10-03 11-01 12-25 12-26
        2025 DE-RP 01-01 04-18 04-21 05-01 05-29 06-09 06-19 10-03 11-01 12-25 12-26
        2025 DE-SL 01-01 04-18 04-21 05-01 05-29 06-09 06-19 08-15 10-03 11-01 12-25 12-26
        2025 DE-SN 01-01 04-18 04-21 05-01 05-29 06-09 10-03 10-31 11-19 12-25 12-26
        2025 DE-ST 01-01 01-06 04-18 04-21 05-01 05-29 06-09 10-03 10-31 12-25 12-26
        2025 DE-SH 01-01 04-18 04-21 05-01 05-29 06-09 10-03 10-31 12-25 12-26
        2025 DE-TH 01-01 04-18 04-21 05-01 05-29 06-09 09-20 10-03 10-31 12-25 12-26
        2025 AT    01-01 01-06 04-20 04-21 05-01 05-29 06-08 06-09 06-19 08-15 10-26 11-01 12-08 12-25 12-26
        2025 CH    01-01 05-29 08-01 12-25
        2026 DE    01-01 04-03 04-06 05-01 05-14 05-25 10-03 12-25 12-26
        2026 DE-BW 01-01 01-06 04-03 04-06 05-01 05-14 05-25 06-04 10-03 11-01 12-25 12-26
        2026 DE-BY 01-01 01-06 04-03 04-06 05-01 05-14 05-25 06-04 10-03 11-01 12-25 12-26
        2026 DE-BE 01-01 03-08 04-03 04-06 05-01 05-14 05-25 10-03 12-25 12-26
        2026 DE-BB 01-01 04-03 04-05 04-06 05-01 05-14 05-24 05-25 10-03 10-31 12-25 12-26
        2026 DE-HB 01-01 04-03 04-06 05-01 05-14 05-25 10-03 10-31 12-25 12-26
        2026 DE-HH 01-01 04-03 04-06 05-01 05-14 05-25 10-03 10-31 12-25 12-26
        2026 DE-HE 01-01 04-03 04-06 05-01 05-14 05-25 06-04 10-03 12-25 12-26
        2026 DE-MV 01-01 03-08 04-03 04-06 05-01 05-14 05-25 10-03 10-31 12-25 12-26
        2026 DE-NI 01-01 04-03 04-06 05-01 05-14 05-25 10-03 10-31 12-25 12-26
        2026 DE-NW 01-01 04-03 04-06 05-01 05-14 05-25 06-04 10-03 11-01 12-25 12-26
        2026 DE-RP 01-01 04-03 04-06 05-01 05-14 05-25 06-04 10-03 11-01 12-25 12-26
        2026 DE-SL 01-01 04-03 04-06 05-01 05-14 05-25 06-04 08-15 10-03 11-01 12-25 12-26
        2026 DE-SN 01-01 04-03 04-06 05-01 05-14 05-25 10-03 10-31 11-18 12-25 12-26
        2026 DE-ST 01-01 01-06 04-03 04-06 05-01 05-14 05-25 10-03 10-31 12-25 12-26
        2026 DE-SH 01-01 04-03 04-06 05-01 05-14 05-25 10-03 10-31 12-25 12-26
        2026 DE-TH 01-01 04-03 04-06 05-01 05-14 05-25 09-20 10-03 10-31 12-25 12-26
        2026 AT    01-01 01-06 04-05 04-06 05-01 05-14 05-24 05-25 06-04 08-15 10-26 11-01 12-08 12-25 12-26
        2026 CH    01-01 05-14 08-01 12-25
        2027 DE    01-01 03-26 03-29 05-01 05-06 05-17 10-03 12-25 12-26
        2027 DE-BW 01-01 01-06 03-26 03-29 05-01 05-06 05-17 05-27 10-03 11-01 12-25 12-26
        2027 DE-BY 01-01 01-06 03-26 03-29 05-01 05-06 05-17 05-27 10-03 11-01 12-25 12-26
        2027 DE-BE 01-01 03-08 03-26 03-29 05-01 05-06 05-17 10-03 12-25 12-26
        2027 DE-BB 01-01 03-26 03-28 03-29 05-01 05-06 05-16 05-17 10-03 10-31 12-25 12-26
        2027 DE-HB 01-01 03-26 03-29 05-01 05-06 05-17 10-03 10-31 12-25 12-26
        2027 DE-HH 01-01 03-26 03-29 05-01 05-06 05-17 10-03 10-31 12-25 12-26
        2027 DE-HE 01-01 03-26 03-29 05-01 05-06 05-17 05-27 10-03 12-25 12-26
        2027 DE-MV 01-01 03-08 03-26 03-29 05-01 05-06 05-17 10-03 10-31 12-25 12-26
        2027 DE-NI 01-01 03-26 03-29 05-01 05-06 05-17 10-03 10-31 12-25 12-26
        2027 DE-NW 01-01 03-26 03-29 05-01 05-06 05-17 05-27 10-03 11-01 12-25 12-26
        2027 DE-RP 01-01 03-26 03-29 05-01 05-06 05-17 05-27 10-03 11-01 12-25 12-26
        2027 DE-SL 01-01 03-26 03-29 05-01 05-06 05-17 05-27 08-15 10-03 11-01 12-25 12-26
        2027 DE-SN 01-01 03-26 03-29 05-01 05-06 05-17 10-03 10-31 11-17 12-25 12-26
        2027 DE-ST 01-01 01-06 03-26 03-29 05-01 05-06 05-17 10-03 10-31 12-25 12-26
        2027 DE-SH 01-01 03-26 03-29 05-01 05-06 05-17 10-03 10-31 12-25 12-26
        2027 DE-TH 01-01 03-26 03-29 05-01 05-06 05-17 09-20 10-03 10-31 12-25 12-26
        2027 AT    01-01 01-06 03-28 03-29 05-01 05-06 05-16 05-17 05-27 08-15 10-26 11-01 12-08 12-25 12-26
        2027 CH    01-01 05-06 08-01 12-25
        """;

    public static TheoryData<string, int, string> Cases()
    {
        var data = new TheoryData<string, int, string>();
        foreach (var line in Expected.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            data.Add(parts[1], int.Parse(parts[0]), parts[2].Trim());
            // Bavaria with Mariä Himmelfahrt = Bavaria + 15.08.
            if (parts[1] == "DE-BY")
                data.Add("DE-BY-MH", int.Parse(parts[0]), string.Join(' ', parts[2].Trim().Split(' ').Append("08-15").Order()));
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Region_HasTheOfficialDates(string region, int year, string expected) =>
        string.Join(' ', HolidayPresets.For(region, year)!.Select(h => h.Date.ToString("MM-dd")))
            .Should().Be(expected);

    [Fact]
    public void EveryRegion_IsCovered() =>
        Cases().Select(c => (string)c[0]).Distinct().Order()
            .Should().Equal(HolidayPresets.All().Select(p => p.Key).Order());

    [Theory]
    [InlineData("DE-BE", 2018, "03-08", false)]
    [InlineData("DE-BE", 2019, "03-08", true)]
    [InlineData("DE-MV", 2022, "03-08", false)]
    [InlineData("DE-MV", 2023, "03-08", true)]
    [InlineData("DE-TH", 2018, "09-20", false)]
    [InlineData("DE-TH", 2019, "09-20", true)]
    [InlineData("DE-NI", 2016, "10-31", false)]
    [InlineData("DE-NI", 2018, "10-31", true)]
    [InlineData("DE-BY", 2017, "10-31", true)]   // 500 Jahre Reformation, nationwide once
    [InlineData("DE", 2017, "10-31", true)]
    [InlineData("DE-BY", 2018, "10-31", false)]
    [InlineData("DE-BE", 2020, "05-08", true)]   // Tag der Befreiung, one-offs
    [InlineData("DE-BE", 2025, "05-08", true)]
    [InlineData("DE-BE", 2026, "05-08", false)]
    public void Holiday_ExistsOnlyFromItsStartYear(string region, int year, string day, bool exists) =>
        HolidayPresets.For(region, year)!.Any(h => h.Date.ToString("MM-dd") == day).Should().Be(exists);

    [Fact]
    public void HolidaysWithAStartYear_AreNeverRecurringRows()
    {
        // A recurring row counts in every year, also before the holiday existed
        var imported = HolidayPresets.Get("DE-BE", 2025, 2027)!;
        imported.Where(h => h.Name == "Internationaler Frauentag").Should().HaveCount(3).And.OnlyContain(h => !h.IsRecurring);
        HolidayPresets.Get("DE-NI", 2026, 2026)!.Single(h => h.Name == "Reformationstag").IsRecurring.Should().BeFalse();
        HolidayPresets.Get("DE-SN", 2026, 2026)!.Single(h => h.Name == "Reformationstag").IsRecurring.Should().BeTrue();
    }
}
