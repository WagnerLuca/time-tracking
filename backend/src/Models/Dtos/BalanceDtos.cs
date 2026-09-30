namespace TimeTracking.Api.Models.Dtos;

/// <summary>Worked vs. target hours for a period (holidays and absences already credited).</summary>
public record PeriodBalance
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public double TargetHours { get; init; }
    public double WorkedHours { get; init; }
    public double OvertimeHours { get; init; }
}

/// <summary>
/// A member's working-time balance, computed by the same day-by-day rules as the reports.
/// Days are the organization's local days; periods end today.
/// </summary>
public record BalanceResponse
{
    /// <summary>Running overtime since the first tracked entry, including the initial overtime.</summary>
    public double CumulativeOvertimeHours { get; init; }
    public double InitialOvertimeHours { get; init; }
    public required PeriodBalance Today { get; init; }
    public required PeriodBalance Week { get; init; }
    public required PeriodBalance Month { get; init; }
    /// <summary>Uncorrected failure entries (not counted in any figure above).</summary>
    public int FailureEntryCount { get; init; }
}
