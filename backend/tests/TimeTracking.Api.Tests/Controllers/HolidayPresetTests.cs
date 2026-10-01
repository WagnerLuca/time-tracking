using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TimeTracking.Api.Services;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>Bundesland holiday presets, Easter computation and the default pause rules of new organizations.</summary>
public class HolidayPresetTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public HolidayPresetTests(TimeTrackingApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData(2025, "2025-04-20")]
    [InlineData(2026, "2026-04-05")]
    [InlineData(2027, "2027-03-28")]
    public void Easter_IsComputed(int year, string expected) =>
        HolidayPresets.Easter(year).Should().Be(DateOnly.Parse(expected));

    [Fact]
    public void RepentanceDay_IsWednesdayBefore23November() =>
        HolidayPresets.RepentanceDay(2026).Should().Be(new DateOnly(2026, 11, 18));

    [Fact]
    public void StatePresets_HaveTheirOwnHolidays()
    {
        var bavaria = HolidayPresets.Get("de-by", 2026, 2026)!.Select(h => h.Name).ToList();
        bavaria.Should().Contain(["Heilige Drei Könige", "Fronleichnam", "Allerheiligen"]).And.NotContain("Reformationstag");

        var saxony = HolidayPresets.Get("DE-SN", 2026, 2026)!;
        saxony.Should().Contain(h => h.Name == "Buß- und Bettag" && h.Date == new DateOnly(2026, 11, 18) && !h.IsRecurring);
        saxony.Should().Contain(h => h.Name == "Reformationstag" && h.IsRecurring);

        HolidayPresets.Get("DE", 2026, 2026)!.Should().HaveCount(9);
        HolidayPresets.Get("DE-XX", 2026, 2026).Should().BeNull();
        HolidayPresets.All().Should().HaveCount(19);
    }

    [Fact]
    public void YearRange_CreatesMovableHolidaysPerYear_FixedOnesOnce()
    {
        var holidays = HolidayPresets.Get("DE", 2025, 2027)!;
        holidays.Count(h => h.Name == "Neujahr").Should().Be(1);
        holidays.Where(h => h.Name == "Ostermontag").Select(h => h.Date)
            .Should().Equal(new DateOnly(2025, 4, 21), new DateOnly(2026, 4, 6), new DateOnly(2027, 3, 29));
    }

    [Fact]
    public async Task ImportPreset_ForStateAndYearRange()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "preset-range@test.com");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Preset Range", "preset-range");

        var response = await owner.PostAsync($"/api/v1/organizations/{org.Slug}/holidays/import-preset?preset=DE-BW&year=2026&toYear=2027", null);
        response.EnsureSuccessStatusCode();
        var holidays = await response.Content.ReadFromJsonAsync<List<HolidayResponseDto>>(TestHelpers.JsonOptions);
        holidays!.Where(h => h.Name == "Fronleichnam").Select(h => h.Date).Should().Equal("2026-06-04", "2027-05-27");

        (await owner.PostAsync($"/api/v1/organizations/{org.Slug}/holidays/import-preset?preset=DE&year=2026&toYear=2040", null))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task NewOrganization_GetsStatutoryPauseRules()
    {
        var (owner, _) = await TestHelpers.CreateAuthenticatedUserAsync(_factory, "default-pause@test.com");
        var org = await TestHelpers.CreateOrganizationAsync(owner, "Default Pause", "default-pause");

        var detail = await owner.GetFromJsonAsync<OrganizationDetailResponseDto>($"/api/v1/organizations/{org.Slug}", TestHelpers.JsonOptions);
        detail!.PauseRules!.OrderBy(r => r.MinHours).Select(r => (r.MinHours, r.PauseMinutes))
            .Should().Equal((6.0, 30), (9.0, 45));
    }
}
