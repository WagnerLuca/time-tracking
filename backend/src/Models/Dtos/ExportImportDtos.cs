using System.ComponentModel.DataAnnotations;

namespace TimeTracking.Api.Models.Dtos;

/// <summary>Request payload for exporting time entries or daily reports.</summary>
public record ExportRequest
{
    /// <summary>Export type: "entries" for individual time entries, "daily" for daily summary.</summary>
    [Required]
    public required string Type { get; init; }

    /// <summary>Organization slug to filter by. Null for all organizations.</summary>
    public string? OrganizationSlug { get; init; }

    /// <summary>
    /// Optional member to export data for, instead of the caller. Requires
    /// <see cref="OrganizationSlug"/> to be set and the caller to have export
    /// permission for that member (self, org Admin/Owner, or a Group Admin of a
    /// shared group with the export-data capability).
    /// </summary>
    public int? TargetUserId { get; init; }

    /// <summary>Start of date range (inclusive).</summary>
    public DateTime? From { get; init; }

    /// <summary>End of date range (inclusive).</summary>
    public DateTime? To { get; init; }

    /// <summary>Columns to include in the export. If empty/null, all columns are included.</summary>
    public List<string>? Columns { get; init; }

    /// <summary>Language of headers, weekdays and status texts: "de" (default) or "en". Numbers stay invariant.</summary>
    [MaxLength(10)]
    public string? Lang { get; init; }
}

/// <summary>Preview of parsed CSV rows before confirming import.</summary>
public record ImportPreviewResponse
{
    public required List<ImportPreviewRow> Rows { get; init; }
    public int TotalRows { get; init; }
    public int DuplicateCount { get; init; }
    public List<string>? Warnings { get; init; }

    /// <summary>Format the file was read as: "standard" (Date/Start/End columns) or "evaluation" (daily "Auswertungen" export).</summary>
    public required string Format { get; init; }

    /// <summary>Data lines without a time entry (e.g. days without work in a daily export); not imported.</summary>
    public int SkippedRowCount { get; init; }

    /// <summary>Days with vacation / sick / other absence time in the file. Absences are not imported.</summary>
    public int SkippedVacationDays { get; init; }
    public int SkippedSickDays { get; init; }
    public int SkippedOtherAbsenceDays { get; init; }

    /// <summary>Employee named in the file ("First Last"), for formats that carry names.</summary>
    public string? EmployeeName { get; init; }

    /// <summary>Whether <see cref="EmployeeName"/> is the caller's own name; null when the file names nobody.</summary>
    public bool? EmployeeMatchesUser { get; init; }
}

/// <summary>A single row from the parsed CSV preview.</summary>
public record ImportPreviewRow
{
    public int RowNumber { get; init; }
    public string? Date { get; init; }
    public string? StartTime { get; init; }
    public string? EndTime { get; init; }
    public int PauseMinutes { get; init; }
    public string? Description { get; init; }
    public bool IsDuplicate { get; init; }
    public string? Warning { get; init; }
}

/// <summary>Request payload to confirm and execute a CSV import.</summary>
public record ImportEntryRequest
{
    [Required]
    public required string Date { get; init; }

    [Required]
    public required string StartTime { get; init; }

    [Required]
    public required string EndTime { get; init; }

    [Range(0, 1440)]
    public int PauseMinutes { get; init; }

    [MaxLength(500)]
    public string? Description { get; init; }
}

/// <summary>Result of a confirmed CSV import.</summary>
public record ImportResultResponse
{
    public int ImportedCount { get; init; }
    public int SkippedCount { get; init; }
    public List<string>? Errors { get; init; }
}
