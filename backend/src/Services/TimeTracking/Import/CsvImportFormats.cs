using System.Globalization;

namespace TimeTracking.Api.Services;

/// <summary>
/// A CSV layout the import understands. Every format turns its rows into the same
/// <see cref="ParsedImportRow"/>s (organization-local <c>yyyy-MM-dd</c> / <c>HH:mm</c> strings where the
/// value could be read), so the preview, duplicate check and confirm step stay format-agnostic.
/// </summary>
public interface ICsvImportFormat
{
    /// <summary>Stable id for the API (<c>format</c> query parameter, preview response).</summary>
    string Id { get; }

    /// <summary>Whether the header row has the columns this format needs.</summary>
    bool Matches(CsvTable table);

    /// <summary>Reads the rows of a table this format <see cref="Matches"/>.</summary>
    ParsedImport Parse(CsvTable table);
}

/// <summary>A row ready for the preview; <see cref="Warning"/> is a problem the format already found.</summary>
public sealed record ParsedImportRow(
    int RowNumber, string Date, string StartTime, string EndTime, int PauseMinutes, string? Description, string? Warning = null);

/// <summary>What a format read from a file, including what it deliberately left out.</summary>
public sealed class ParsedImport
{
    public List<ParsedImportRow> Rows { get; } = [];
    public List<string> Warnings { get; } = [];

    /// <summary>Data lines that hold no time entry (days without work), not imported.</summary>
    public int SkippedRowCount { get; set; }
    public int VacationDays { get; set; }
    public int SickDays { get; set; }
    public int OtherAbsenceDays { get; set; }

    /// <summary>Distinct employees ("First Last") when the file names them; empty for formats without names.</summary>
    public List<string> Employees { get; } = [];
}

/// <summary>The known formats, most specific first (auto-detection takes the first that matches).</summary>
public static class CsvImportFormats
{
    public const string Auto = "auto";

    public static readonly IReadOnlyList<ICsvImportFormat> All = [new EvaluationCsvFormat(), new StandardCsvFormat()];

    /// <summary>The format with this id (case-insensitive), or null.</summary>
    public static ICsvImportFormat? Find(string id) =>
        All.FirstOrDefault(f => f.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The first format whose columns the table has, or null.</summary>
    public static ICsvImportFormat? Detect(CsvTable table) => All.FirstOrDefault(f => f.Matches(table));
}

/// <summary>Lenient readers for the date, time and duration spellings of the supported formats.</summary>
public static class ImportValues
{
    private static readonly string[] DateFormats =
        ["yyyy-MM-dd", "yyyy-M-d", "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "d.M.yy", "yyyy/MM/dd"];

    private static readonly string[] TimeFormats = ["H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss"];

    /// <summary><c>yyyy-MM-dd</c> for ISO or German dates; anything else is returned unchanged (the row check reports it).</summary>
    public static string NormalizeDate(string value) =>
        DateOnly.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : value;

    /// <summary><c>HH:mm</c> for <c>7:05</c> or <c>07:05:00</c>; anything else is returned unchanged.</summary>
    public static string NormalizeTime(string value) =>
        TimeOnly.TryParseExact(value, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t.ToString("HH:mm", CultureInfo.InvariantCulture)
            : value;

    /// <summary>
    /// Minutes of a <c>[±]H:mm</c> duration (hours may exceed 24; the Unicode minus <c>−</c> counts as a sign),
    /// or null when it is no such duration.
    /// </summary>
    public static int? ParseDuration(string value)
    {
        var s = value.Trim().Replace('−', '-');
        if (s.Length == 0) return null;
        var sign = 1;
        if (s[0] is '-' or '+')
        {
            if (s[0] == '-') sign = -1;
            s = s[1..];
        }
        var parts = s.Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h)
            || parts[1].Length != 2
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m)
            || m > 59)
            return null;
        return sign * (h * 60 + m);
    }

    /// <summary>Pause in minutes: a whole number of minutes or a <c>H:mm</c> duration; 0 when empty or unreadable.</summary>
    public static int ParsePause(string value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)) return minutes;
        return ParseDuration(value) ?? 0;
    }
}

/// <summary>
/// One entry per row with Date, Start, End and optional Pause / Description columns: this app's own
/// entries export (English or German headers) and simple hand-made files. <c>,</c>, <c>;</c> or tab
/// separated; ISO or German dates; pause in minutes or <c>H:mm</c>.
/// </summary>
public sealed class StandardCsvFormat : ICsvImportFormat
{
    public string Id => "standard";

    private static int DateCol(CsvTable t) => t.Column("date", "datum");
    private static int StartCol(CsvTable t) => t.Column("start time", "starttime", "start", "beginn", "von");
    private static int EndCol(CsvTable t) => t.Column("end time", "endtime", "end", "ende", "bis");

    public bool Matches(CsvTable table) => DateCol(table) >= 0 && StartCol(table) >= 0 && EndCol(table) >= 0;

    public ParsedImport Parse(CsvTable table)
    {
        int dateIdx = DateCol(table), startIdx = StartCol(table), endIdx = EndCol(table);
        var pauseIdx = table.Column("pause", "pause (min)", "pause minutes", "break");
        var descIdx = table.Column("description", "notes", "desc", "beschreibung", "notiz");
        var result = new ParsedImport();

        foreach (var row in table.Rows)
        {
            if (row.Fields.Count <= Math.Max(dateIdx, Math.Max(startIdx, endIdx)))
            {
                result.Warnings.Add($"Row {row.RowNumber}: insufficient columns, skipped.");
                result.SkippedRowCount++;
                continue;
            }

            var desc = row[descIdx];
            result.Rows.Add(new ParsedImportRow(
                row.RowNumber,
                ImportValues.NormalizeDate(row[dateIdx]),
                ImportValues.NormalizeTime(row[startIdx]),
                ImportValues.NormalizeTime(row[endIdx]),
                pauseIdx >= 0 ? ImportValues.ParsePause(row[pauseIdx]) : 0,
                desc));
        }
        return result;
    }
}

/// <summary>
/// "Auswertungen" export of another time-tracking tool: one row per employee and calendar day with
/// <c>employeeId;firstName;lastName;date;isValid;workStartTime;workStopTime;break;…;sickTime;vacationTime;balance;monthlyBalance;notes</c>.
/// <list type="bullet">
/// <item>A day with start and stop becomes one entry; <c>break</c> (<c>H:mm</c>) is its manual pause.</item>
/// <item>Days without start and stop (weekends, holidays, absences) are skipped and counted. Vacation, sick and
/// other absence hours are counted per day but not imported; target, balance and holiday columns are informational.</item>
/// <item>Notes become the description, minus the tool's bookkeeping remarks (variable pause, balance carry-over).</item>
/// <item><c>isValid = false</c> rows are listed with a warning, so they are not selected by default.</item>
/// </list>
/// </summary>
public sealed class EvaluationCsvFormat : ICsvImportFormat
{
    public string Id => "evaluation";

    // Remarks the tool writes itself; they say nothing about the work done
    private static readonly string[] BookkeepingNotes =
        ["Variable Pause angewandt", "Saldoübertrag", "Neuer Saldo", "Neuer Urlaubsanspruch"];

    public bool Matches(CsvTable table) =>
        table.Column("date") >= 0 && table.Column("workstarttime") >= 0 && table.Column("workstoptime") >= 0;

    public ParsedImport Parse(CsvTable table)
    {
        int idIdx = table.Column("employeeid"), firstIdx = table.Column("firstname"), lastIdx = table.Column("lastname");
        int dateIdx = table.Column("date"), validIdx = table.Column("isvalid");
        int startIdx = table.Column("workstarttime"), stopIdx = table.Column("workstoptime"), breakIdx = table.Column("break");
        int absenceIdx = table.Column("absencetime"), sickIdx = table.Column("sicktime"), vacationIdx = table.Column("vacationtime");
        var notesIdx = table.Column("notes");

        var result = new ParsedImport();
        var employeeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in table.Rows)
        {
            var name = $"{row[firstIdx]} {row[lastIdx]}".Trim();
            var key = row[idIdx] is { Length: > 0 } id ? id : name;
            if (key.Length > 0 && employeeKeys.Add(key) && !result.Employees.Contains(name))
                result.Employees.Add(name);

            if (Positive(row[vacationIdx])) result.VacationDays++;
            if (Positive(row[sickIdx])) result.SickDays++;
            if (Positive(row[absenceIdx])) result.OtherAbsenceDays++;

            var start = row[startIdx];
            var stop = row[stopIdx];
            if (start.Length == 0 && stop.Length == 0)
            {
                result.SkippedRowCount++;
                continue;
            }

            string? warning = null;
            var breakText = row[breakIdx];
            var pause = breakText.Length == 0 ? 0 : ImportValues.ParseDuration(breakText);
            if (start.Length == 0 || stop.Length == 0) warning = "Start or end time is missing.";
            else if (pause is null) warning = "Invalid break duration.";
            else if (row[validIdx].Equals("false", StringComparison.OrdinalIgnoreCase)) warning = "Marked as invalid in the source file.";

            result.Rows.Add(new ParsedImportRow(
                row.RowNumber,
                ImportValues.NormalizeDate(row[dateIdx]),
                ImportValues.NormalizeTime(start),
                ImportValues.NormalizeTime(stop),
                pause ?? 0,
                CleanNotes(row[notesIdx]),
                warning));
        }
        return result;
    }

    private static bool Positive(string value) => ImportValues.ParseDuration(value) > 0;

    /// <summary>The notes without the tool's bookkeeping remarks; null when nothing is left.</summary>
    public static string? CleanNotes(string notes)
    {
        var kept = SplitTopLevel(notes)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0 && !BookkeepingNotes.Any(b => p.StartsWith(b, StringComparison.OrdinalIgnoreCase)));
        var text = string.Join(", ", kept);
        return text.Length == 0 ? null : text;
    }

    // Splits on ", " outside parentheses ("Neuer Saldo: 67:00 (a, b), Urlaubsanspruch: 21,0 Tage" → 2 parts)
    private static IEnumerable<string> SplitTopLevel(string text)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && depth > 0) depth--;
            else if (text[i] == ',' && depth == 0 && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1])))
            {
                yield return text[start..i];
                start = i + 1;
            }
        }
        yield return text[start..];
    }
}
