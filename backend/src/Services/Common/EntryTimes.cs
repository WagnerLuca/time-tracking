using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;

namespace TimeTracking.Api.Services;

/// <summary>
/// Plausibility limits for tracked times. Besides catching typos, they keep report loops (one
/// row per day since the first entry) bounded: an entry dated year 1 would make every balance
/// call iterate ~740,000 days.
/// </summary>
public static class EntryTimes
{
    public static readonly DateTime Earliest = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(24);

    /// <summary>Null when valid, else a message.</summary>
    /// <summary>Most rows one CSV import may hold.</summary>
    public const int MaxImportRows = 1000;

    /// <summary>Longest range one export or report may cover.</summary>
    public const int MaxRangeDays = 731;

    /// <summary>Checks an export/report range of local days; both ends optional.</summary>
    public static string? ValidateRange(DateOnly? from, DateOnly? to)
    {
        var earliest = DateOnly.FromDateTime(Earliest);
        if (from < earliest || to < earliest) return "Dates must be in the year 2000 or later.";
        if (from.HasValue && to.HasValue)
        {
            if (to < from) return "The end date must not be before the start date.";
            if (to.Value.DayNumber - from.Value.DayNumber > MaxRangeDays) return "A range can cover at most two years.";
        }
        return null;
    }

    public static string? Validate(DateTime startUtc, DateTime? endUtc)
    {
        if (startUtc < Earliest) return "Start time must be in the year 2000 or later.";
        if (startUtc > DateTime.UtcNow.AddDays(1)) return "Start time cannot be in the future.";
        if (endUtc.HasValue)
        {
            if (endUtc.Value <= startUtc) return "End time must be after start time.";
            if (endUtc.Value - startUtc > MaxDuration) return "An entry cannot be longer than 24 hours.";
        }
        return null;
    }

    /// <summary>
    /// True when [start, end) overlaps another entry of the user (in any organization, running
    /// timers included), apart from <paramref name="exceptEntryId"/>.
    /// </summary>
    public static Task<bool> OverlapsAsync(TimeTrackingDbContext context, int userId, DateTime startUtc, DateTime endUtc, int? exceptEntryId = null) =>
        context.TimeEntries.AsNoTracking()
            .AnyAsync(e => e.UserId == userId && e.Id != exceptEntryId
                        && e.StartTime < endUtc && (e.IsRunning || e.EndTime > startUtc));
}
