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

/// <summary>One day of the balance breakdown (organization-local date).</summary>
public record DayBalance
{
    public DateOnly Date { get; init; }
    /// <summary>Planned target (holidays and absences credited), also for days still to come.</summary>
    public double TargetHours { get; init; }
    /// <summary>Net worked hours (failure entries excluded).</summary>
    public double WorkedHours { get; init; }
    /// <summary>The day's overtime; 0 for days not counted.</summary>
    public double OvertimeHours { get; init; }
    /// <summary>The day is in the balance: not in the future and not before the first tracked entry.</summary>
    public bool IsCounted { get; init; }
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
    /// <summary>
    /// The current week Monday–Sunday, one entry per day. Target is the planned target of every day
    /// (upcoming ones too); overtime only counts days that are in the balance (see <see cref="DayBalance.IsCounted"/>).
    /// </summary>
    public List<DayBalance> WeekDays { get; init; } = [];
    /// <summary>
    /// The current month split into Monday-based weeks (clipped to the month), with the same
    /// rules as <see cref="WeekDays"/>: planned target of the whole segment, overtime of its counted days.
    /// </summary>
    public List<PeriodBalance> MonthWeeks { get; init; } = [];
    /// <summary>Uncorrected failure entries (not counted in any figure above).</summary>
    public int FailureEntryCount { get; init; }
}
