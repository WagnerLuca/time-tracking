using System.Globalization;

namespace TimeTracking.Api.Services;

/// <summary>
/// German (default) / English texts of the PDF reports and CSV exports. Names are spelled out
/// here instead of coming from CultureInfo so the output does not depend on the host's ICU data.
/// </summary>
public sealed class ReportText
{
    /// <summary><c>en…</c> → English, anything else (incl. null) → German.</summary>
    public static ReportText For(string? lang) =>
        lang != null && lang.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? En : De;

    public bool IsGerman { get; }

    /// <summary>Number format for the PDF (decimal comma in German). CSV numbers stay invariant.</summary>
    public NumberFormatInfo Numbers { get; }

    /// <summary>Date format for the PDF.</summary>
    public string DateFormat => IsGerman ? "dd.MM.yyyy" : "yyyy-MM-dd";

    private readonly Dictionary<string, string> _texts;
    private readonly string[] _days;     // Sunday first, like DayOfWeek
    private readonly string[] _months;

    private ReportText(bool isGerman)
    {
        IsGerman = isGerman;
        Numbers = isGerman
            ? new NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = ".", NegativeSign = "-", PositiveSign = "+" }
            : NumberFormatInfo.InvariantInfo;
        _days = isGerman ? ["So", "Mo", "Di", "Mi", "Do", "Fr", "Sa"] : ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
        _months = isGerman
            ? ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"]
            : ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
        _texts = isGerman ? German : English;
    }

    public string this[string key] => _texts.TryGetValue(key, out var text) ? text : key;

    public string Day(DayOfWeek day) => _days[(int)day];

    public string Month(int month) => _months[month - 1];

    /// <summary>A <see cref="DailyReportRow.Status"/> (English key) in this language; custom absence type names pass through.</summary>
    public string Status(string status) => _texts.TryGetValue("status." + status, out var text) ? text : status;

    /// <summary>The row's status cell: <see cref="Status"/>, plus a core hours note when the day missed them.</summary>
    public string RowStatus(DailyReportRow row) =>
        Status(row.Status) + (row.CoreHoursMissed ? ", " + this["status.CoreHoursMissed"] : "");

    /// <summary>An absence type as shown in reports.</summary>
    public string Absence(Models.AbsenceType type) => this["absence." + type];

    /// <summary>Localizes the failure marker <see cref="DailyReportAggregator"/> puts into entry summaries.</summary>
    public string EntrySummary(string summary) =>
        IsGerman ? summary.Replace(DailyReportAggregator.FailureMarker, " [nicht gestoppt, Korrektur nötig]") : summary;

    private static readonly Dictionary<string, string> English = new()
    {
        ["status.Needs Correction"] = "Needs Correction",
        ["status.Sick"] = "Sick",
        ["status.Vacation"] = "Vacation",
        ["status.Absent"] = "Absent",
        ["status.Half Holiday"] = "Half Holiday",
        ["status.Holiday"] = "Holiday",
        ["status.Weekend"] = "Weekend",
        ["status.Day Off"] = "Day Off",
        ["status.Worked"] = "Worked",
        ["status.Missing"] = "Missing",
        ["status.CoreHoursMissed"] = "Core hours missed",
        ["absence.Vacation"] = "Vacation",
        ["absence.SickDay"] = "Sick day",
        ["absence.Other"] = "Other",

        // CSV column headers (keys are the column ids of the `columns` selection)
        ["col.Date"] = "Date",
        ["col.DayOfWeek"] = "Day",
        ["col.StartTime"] = "Start Time",
        ["col.EndTime"] = "End Time",
        ["col.Duration"] = "Duration (h)",
        ["col.Pause"] = "Pause (min)",
        ["col.NetDuration"] = "Net Duration (h)",
        ["col.Description"] = "Description",
        ["col.Organization"] = "Organization",
        ["col.Status"] = "Status",
        ["col.TargetHours"] = "Target (h)",
        ["col.WorkedHours"] = "Worked (h)",
        ["col.NetWorkedHours"] = "Net Worked (h)",
        ["col.Overtime"] = "Overtime (h)",
        ["col.CumulativeOvertime"] = "Cumulative Overtime (h)",
        ["col.HolidayName"] = "Holiday",
        ["col.AbsenceType"] = "Absence Type",
        ["col.AbsenceNote"] = "Absence Note",

        // PDF
        ["pdf.monthlyTitle"] = "Monthly Time Report",
        ["pdf.yearlyTitle"] = "Yearly Time Report",
        ["pdf.generated"] = "Generated {0} UTC",
        ["pdf.target"] = "Target",
        ["pdf.workedNet"] = "Worked (net)",
        ["pdf.balance"] = "Balance",
        ["pdf.yearBalance"] = "Year Balance",
        ["pdf.overallCumulative"] = "Overall Cumulative",
        ["pdf.vacation"] = "Vacation",
        ["pdf.sick"] = "Sick",
        ["pdf.holidays"] = "Holidays",
        ["pdf.daysSuffix"] = "d",
        ["pdf.page"] = "Page ",
        ["pdf.date"] = "Date",
        ["pdf.day"] = "Day",
        ["pdf.worked"] = "Worked",
        ["pdf.net"] = "Net",
        ["pdf.overtime"] = "Overtime",
        ["pdf.cumul"] = "Cumul.",
        ["pdf.status"] = "Status",
        ["pdf.note"] = "Note",
        ["pdf.month"] = "Month",
        ["pdf.missing"] = "Missing",
        ["pdf.vac"] = "Vac.",
        ["pdf.holiday"] = "Holiday",
        ["pdf.accountingTitle"] = "Monthly Summary (Accounting)",
        ["pdf.member"] = "Member",
        ["pdf.personnelNo"] = "Pers. no.",
        ["pdf.otherAbsences"] = "Other",
        ["pdf.corrections"] = "Correct.",
        ["pdf.former"] = "left",
        ["pdf.total"] = "Total",
        ["pdf.closed"] = "Month closed",
        ["pdf.open"] = "Month open",
    };

    private static readonly Dictionary<string, string> German = new()
    {
        ["status.Needs Correction"] = "Korrektur nötig",
        ["status.Sick"] = "Krank",
        ["status.Vacation"] = "Urlaub",
        ["status.Absent"] = "Abwesend",
        ["status.Half Holiday"] = "Halber Feiertag",
        ["status.Holiday"] = "Feiertag",
        ["status.Weekend"] = "Wochenende",
        ["status.Day Off"] = "Frei",
        ["status.Worked"] = "Gearbeitet",
        ["status.CoreHoursMissed"] = "Kernzeit verletzt",
        ["status.Missing"] = "Fehlt",
        ["absence.Vacation"] = "Urlaub",
        ["absence.SickDay"] = "Krankheitstag",
        ["absence.Other"] = "Sonstige",

        ["col.Date"] = "Datum",
        ["col.DayOfWeek"] = "Tag",
        ["col.StartTime"] = "Beginn",
        ["col.EndTime"] = "Ende",
        ["col.Duration"] = "Dauer (h)",
        ["col.Pause"] = "Pause (min)",
        ["col.NetDuration"] = "Netto (h)",
        ["col.Description"] = "Beschreibung",
        ["col.Organization"] = "Organisation",
        ["col.Status"] = "Status",
        ["col.TargetHours"] = "Soll (h)",
        ["col.WorkedHours"] = "Gearbeitet (h)",
        ["col.NetWorkedHours"] = "Netto gearbeitet (h)",
        ["col.Overtime"] = "Überstunden (h)",
        ["col.CumulativeOvertime"] = "Überstunden kumuliert (h)",
        ["col.HolidayName"] = "Feiertag",
        ["col.AbsenceType"] = "Abwesenheit",
        ["col.AbsenceNote"] = "Notiz zur Abwesenheit",

        ["pdf.monthlyTitle"] = "Monatlicher Arbeitszeitbericht",
        ["pdf.yearlyTitle"] = "Jährlicher Arbeitszeitbericht",
        ["pdf.generated"] = "Erstellt am {0} UTC",
        ["pdf.target"] = "Soll",
        ["pdf.workedNet"] = "Gearbeitet (netto)",
        ["pdf.balance"] = "Saldo",
        ["pdf.yearBalance"] = "Jahressaldo",
        ["pdf.overallCumulative"] = "Gesamtsaldo",
        ["pdf.vacation"] = "Urlaub",
        ["pdf.sick"] = "Krank",
        ["pdf.holidays"] = "Feiertage",
        ["pdf.daysSuffix"] = " T",
        ["pdf.page"] = "Seite ",
        ["pdf.date"] = "Datum",
        ["pdf.day"] = "Tag",
        ["pdf.worked"] = "Gearbeitet",
        ["pdf.net"] = "Netto",
        ["pdf.overtime"] = "Überstd.",
        ["pdf.cumul"] = "Kumuliert",
        ["pdf.status"] = "Status",
        ["pdf.note"] = "Notiz",
        ["pdf.month"] = "Monat",
        ["pdf.missing"] = "Fehlt",
        ["pdf.vac"] = "Url.",
        ["pdf.holiday"] = "Feiert.",
        ["pdf.accountingTitle"] = "Monatsübersicht (Buchhaltung)",
        ["pdf.member"] = "Person",
        ["pdf.personnelNo"] = "Pers.-Nr.",
        ["pdf.otherAbsences"] = "Sonst.",
        ["pdf.corrections"] = "Korr.",
        ["pdf.former"] = "ausgeschieden",
        ["pdf.total"] = "Summe",
        ["pdf.closed"] = "Monat abgeschlossen",
        ["pdf.open"] = "Monat offen",
    };

    // Last: static fields initialize in textual order, and these need the dictionaries above
    public static readonly ReportText De = new(isGerman: true);
    public static readonly ReportText En = new(isGerman: false);
}
