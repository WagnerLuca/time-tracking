using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TimeTracking.Api.Infrastructure;
using TimeTracking.Api.Models;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>Undefined enum values are rejected; a negative nullable enum means "no value".</summary>
public class StrictEnumConverterTests : IClassFixture<TimeTrackingApiFactory>
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new StrictEnumConverterFactory() } };

    private readonly TimeTrackingApiFactory _factory;

    public StrictEnumConverterTests(TimeTrackingApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("2", RuleMode.Allowed)]
    [InlineData("\"RequiresApproval\"", RuleMode.RequiresApproval)]
    [InlineData("\"allowed\"", RuleMode.Allowed)]
    public void Reads_DefinedNumbersAndNames(string json, RuleMode expected) =>
        JsonSerializer.Deserialize<RuleMode>(json, Options).Should().Be(expected);

    [Theory]
    [InlineData("3")]
    [InlineData("-1")]
    [InlineData("\"2\"")]        // numeric strings would bypass the name check
    [InlineData("\"Sometimes\"")]
    [InlineData("true")]
    public void Rejects_UndefinedValues(string json) =>
        FluentActions.Invoking(() => JsonSerializer.Deserialize<RuleMode>(json, Options)).Should().Throw<JsonException>();

    [Theory]
    [InlineData("null", null)]
    [InlineData("-1", null)]
    [InlineData("1", RuleMode.RequiresApproval)]
    public void Nullable_TreatsNullAndNegativeAsNoValue(string json, RuleMode? expected) =>
        JsonSerializer.Deserialize<RuleMode?>(json, Options).Should().Be(expected);

    [Fact]
    public void Nullable_StillRejectsUndefinedPositiveValues() =>
        FluentActions.Invoking(() => JsonSerializer.Deserialize<RuleMode?>("7", Options)).Should().Throw<JsonException>();

    [Fact]
    public void Writes_Numbers()
    {
        JsonSerializer.Serialize(RuleMode.Allowed, Options).Should().Be("2");
        JsonSerializer.Serialize<RuleMode?>(RuleMode.Disabled, Options).Should().Be("0");
        JsonSerializer.Serialize<RuleMode?>(null, Options).Should().Be("null");
    }

    [Fact]
    public async Task Api_Returns400ForAnUndefinedRole_AndTreatsMinusOneAsInherit()
    {
        var (owner, _, slug, _, memberId) = await TestHelpers.OrgWithMemberAsync(_factory, "enum", new { }, "enum.test");

        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/members/{memberId}", new { role = 3 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/settings", new { editPauseMode = 5 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The UI's "inherit" sends -1: it clears just that override, fields not sent stay as they are
        var groupId = await TestHelpers.CreateGroupAsync(owner, slug, "G", new { editPauseMode = 2, vacationVisibility = 1 });
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/permissions", new { editPauseMode = -1 })).EnsureSuccessStatusCode();
        var perms = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/Organizations/{slug}/groups/{groupId}/permissions");
        perms.GetProperty("editPauseMode").ValueKind.Should().Be(JsonValueKind.Null);
        perms.GetProperty("vacationVisibility").GetString().Should().Be("AdminOnly");
        (await owner.PutAsJsonAsync($"/api/v1/Organizations/{slug}/groups/{groupId}/permissions", new { vacationVisibility = 9 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void OverrideConverter_KeepsNegativeAsClearMarker_AndValidatesTheRest()
    {
        var options = new JsonSerializerOptions { Converters = { new EnumOverrideConverter<RuleMode>() } };
        JsonSerializer.Deserialize<RuleMode?>("-1", options).Should().Be((RuleMode)(-1));
        JsonSerializer.Deserialize<RuleMode?>("null", options).Should().BeNull();
        JsonSerializer.Deserialize<RuleMode?>("\"Allowed\"", options).Should().Be(RuleMode.Allowed);
        FluentActions.Invoking(() => JsonSerializer.Deserialize<RuleMode?>("4", options)).Should().Throw<JsonException>();
        JsonSerializer.Serialize<RuleMode?>(RuleMode.Allowed, options).Should().Be("2");
        JsonSerializer.Serialize<RuleMode?>(null, options).Should().Be("null");
    }
}
