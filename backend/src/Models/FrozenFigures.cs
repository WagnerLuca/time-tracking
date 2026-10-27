using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

/// <summary>
/// A member's figures of one closed month, stored when the month was closed (issue #81, <c>Services/Common/FrozenFigures.cs</c>):
/// for a closed month these are the truth — later changes of holidays, absence-type credit modes, the time zone, schedules or
/// the initial overtime do not change them. Totals as the payroll CSV shows them (days counted in the balance only, like
/// <c>DailyReportAggregator.TotalsAsync</c>); <see cref="OvertimeDelta"/> is where the running balance of later months
/// continues. Reopening the month deletes the rows; an admin change inside it re-freezes the member's month. RLS like TimeEntries.
/// </summary>
public class FrozenMonth
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int UserId { get; set; }
    /// <summary>First day of the org-local month.</summary>
    public DateOnly Month { get; set; }
    /// <summary>The member's first tracked day the running balance started from (null: no balance yet at the month's end).</summary>
    public DateOnly? FirstDay { get; set; }
    /// <summary>Overtime from <see cref="FirstDay"/> to the month's last day, without the initial overtime (like <see cref="BalanceCheckpoint.OvertimeDelta"/>).</summary>
    public double OvertimeDelta { get; set; }
    /// <summary>The member's initial overtime when the month was frozen (part of every frozen cumulative figure).</summary>
    public double InitialOvertimeHours { get; set; }
    public double TargetHours { get; set; }
    public double WorkedHours { get; set; }
    public double OvertimeHours { get; set; }
    /// <summary>The overtime balance at the month's end (initial overtime + <see cref="OvertimeDelta"/>, or the initial overtime before the first day).</summary>
    public double Balance { get; set; }
    public double VacationDays { get; set; }
    public double SickDays { get; set; }
    public double OtherAbsenceDays { get; set; }
    public int FailureEntryCount { get; set; }
    /// <summary>The organization's time zone the days were computed in.</summary>
    public string? TimeZone { get; set; }
    public DateTime FrozenAt { get; set; }

    [JsonIgnore]
    public Organization Organization { get; set; } = null!;
    [JsonIgnore]
    public User User { get; set; } = null!;
}

/// <summary>
/// One day of a <see cref="FrozenMonth"/>: the <c>DailyReportRow</c> as reports showed it when the month was frozen, plus what
/// the calendar, the vacation balance and the compliance check read. Every day of the month has a row.
/// </summary>
public class FrozenDay
{
    public long Id { get; set; }
    public int OrganizationId { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    /// <summary>The schedule's target less the holiday credit (the calendar's day target).</summary>
    public double DayTargetHours { get; set; }
    /// <summary>Hours the absence took off the target.</summary>
    public double CreditHours { get; set; }
    /// <summary>The effective target (<see cref="DayTargetHours"/> − <see cref="CreditHours"/>, at least 0).</summary>
    public double TargetHours { get; set; }
    public double WorkedHours { get; set; }
    public int PauseMinutes { get; set; }
    public double NetWorkedHours { get; set; }
    public double Overtime { get; set; }
    /// <summary>The running overtime sum without the initial overtime (null: before the balance started).</summary>
    public double? OvertimeDelta { get; set; }
    public double CumulativeOvertime { get; set; }
    public required string Status { get; set; }
    public string? HolidayName { get; set; }
    public AbsenceType? AbsenceType { get; set; }
    public bool AbsenceHalfDay { get; set; }
    public string? AbsenceNote { get; set; }
    public bool CountsAsVacation { get; set; }
    /// <summary>Days used from the vacation budget (1, 0.5 or 0: vacation on a working day that is no holiday).</summary>
    public double VacationUsed { get; set; }
    public bool CoreHoursMissed { get; set; }
    public int FailureCount { get; set; }
    public string EntrySummary { get; set; } = "";
    /// <summary>The day's compliance finding as JSON (<c>ComplianceDayResponse</c>), null when there was none.</summary>
    public string? Compliance { get; set; }
}
