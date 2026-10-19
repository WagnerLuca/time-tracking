namespace TimeTracking.Api.Models.Dtos;

/// <summary>The accounting panel's monthly summary: one row per member (docs/ACCOUNTING_ROLE_PLAN.md).</summary>
public record AccountingSummaryResponse
{
    public int Year { get; init; }
    public int Month { get; init; }
    public bool IsClosed { get; init; }
    public required List<AccountingMemberSummary> Members { get; init; }
}

/// <summary>A member's month: hours like balances and reports (failure entries count 0), absence days, entries needing correction.</summary>
public record AccountingMemberSummary
{
    public int UserId { get; init; }
    public string? PersonnelNumber { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public double TargetHours { get; init; }
    public double WorkedHours { get; init; }
    public double OvertimeHours { get; init; }
    /// <summary>Overtime balance at the end of the month (since the first entry, incl. the initial balance).</summary>
    public double OvertimeBalance { get; init; }
    public double VacationDays { get; init; }
    public double SickDays { get; init; }
    public double OtherAbsenceDays { get; init; }
    /// <summary>Failure entries (timer not stopped) started in the month: "Needs Correction".</summary>
    public int FailureEntryCount { get; init; }
    /// <summary>Left the organization, listed because of time or absences in the month.</summary>
    public bool IsFormerMember { get; init; }
}

/// <summary>The months of one year with their closing state and what still needs attention before closing.</summary>
public record AccountingMonthsResponse
{
    public int Year { get; init; }
    public required List<AccountingMonthStatus> Months { get; init; }
}

public record AccountingMonthStatus
{
    /// <summary>First day of the month.</summary>
    public DateOnly Month { get; init; }
    /// <summary>The month is over in the organization's time zone (only then it can be closed).</summary>
    public bool HasEnded { get; init; }
    public bool IsClosed { get; init; }
    public DateTime? ClosedAt { get; init; }
    public string? ClosedByName { get; init; }
    /// <summary>Failure entries started in the month (still "Needs Correction").</summary>
    public int FailureEntryCount { get; init; }
    /// <summary>Pending requests that would change the month.</summary>
    public int PendingRequestCount { get; init; }
}

/// <summary>Vacation overview of every active member for one year.</summary>
public record VacationOverviewResponse
{
    public int Year { get; init; }
    public required List<VacationOverviewRow> Members { get; init; }
}

public record VacationOverviewRow
{
    public int UserId { get; init; }
    public required string Name { get; init; }
    public string? PersonnelNumber { get; init; }
    /// <summary>The year's entitlement (own row, an earlier year's, else the member's allowance).</summary>
    public double Entitlement { get; init; }
    /// <summary>An admin's adjustment of the year (+/− days).</summary>
    public double Adjustment { get; init; }
    /// <summary>Days carried over from the previous year.</summary>
    public double CarriedIn { get; init; }
    /// <summary>Carried-over days that lapsed unused.</summary>
    public double Expired { get; init; }
    /// <summary>The day after which carried-over days lapse; null without carry-in or when they never lapse.</summary>
    public DateOnly? ExpiresOn { get; init; }
    /// <summary>Vacation days up to today (working days, holidays excluded).</summary>
    public double Taken { get; init; }
    /// <summary>Vacation days after today.</summary>
    public double Planned { get; init; }
    public double Remaining { get; init; }
    public double SickDays { get; init; }
    public double OtherAbsenceDays { get; init; }
}
