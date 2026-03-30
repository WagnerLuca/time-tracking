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

    /// <summary>Start of date range (inclusive).</summary>
    public DateTime? From { get; init; }

    /// <summary>End of date range (inclusive).</summary>
    public DateTime? To { get; init; }

    /// <summary>Columns to include in the export. If empty/null, all columns are included.</summary>
    public List<string>? Columns { get; init; }
}

/// <summary>Preview of parsed CSV rows before confirming import.</summary>
public record ImportPreviewResponse
{
    public required List<ImportPreviewRow> Rows { get; init; }
    public int TotalRows { get; init; }
    public int DuplicateCount { get; init; }
    public List<string>? Warnings { get; init; }
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

    public int PauseMinutes { get; init; }
    public string? Description { get; init; }
}

/// <summary>Result of a confirmed CSV import.</summary>
public record ImportResultResponse
{
    public int ImportedCount { get; init; }
    public int SkippedCount { get; init; }
    public List<string>? Errors { get; init; }
}
