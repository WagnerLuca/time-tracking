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
}
