using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

/// <summary>
/// A member's vacation figures for one year that differ from the defaults (source data, see
/// <c>Services/Common/VacationBalances.cs</c>): the entitlement of that year and an admin's adjustment (e.g. the
/// opening balance from a previous system). A year without a row (or with <see cref="Entitlement"/> null) uses the
/// latest earlier row's entitlement, else <see cref="UserOrganization.VacationDaysPerYear"/>. RLS like AbsenceDays.
/// </summary>
public class VacationYear
{
    public const int NoteMaxLength = 500;

    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int UserId { get; set; }
    public int Year { get; set; }
    /// <summary>Vacation days of this year; null = inherited (latest earlier row, else the member's allowance).</summary>
    public double? Entitlement { get; set; }
    /// <summary>Extra (+) or fewer (−) days this year, set by an admin.</summary>
    public double Adjustment { get; set; }
    public string? AdjustmentNote { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }

    [JsonIgnore]
    public Organization Organization { get; set; } = null!;
    [JsonIgnore]
    public User User { get; set; } = null!;
}
