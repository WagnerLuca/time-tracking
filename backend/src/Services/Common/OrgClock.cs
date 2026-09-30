using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

/// <summary>
/// An organization's wall clock. Instants are stored in UTC, but "which day", "today",
/// "this week" and the times people read and type (reports, CSV) are local to the
/// organization's <see cref="Organization.TimeZone"/>. Every such conversion goes through here.
/// </summary>
public sealed class OrgClock
{
    /// <summary>Used for personal entries (no organization) and unknown time zone ids.</summary>
    public const string DefaultTimeZone = "Europe/Berlin";

    public TimeZoneInfo Zone { get; }

    public OrgClock(TimeZoneInfo zone) => Zone = zone;

    public static OrgClock For(Organization? org) => For(org?.TimeZone);

    public static OrgClock For(string? timeZoneId) => new(ResolveTimeZone(timeZoneId));

    public static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz)) return tz;
        return TimeZoneInfo.TryFindSystemTimeZoneById(DefaultTimeZone, out var fallback) ? fallback : TimeZoneInfo.Utc;
    }

    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// <summary>Local wall-clock time (as typed in a CSV or form) → UTC. Times inside a DST gap move forward.</summary>
    public DateTime ToUtc(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (Zone.IsInvalidTime(unspecified)) unspecified = unspecified.AddMinutes(30);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone);
    }

    public DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    public DateOnly Today(DateTime? nowUtc = null) => LocalDate(nowUtc ?? DateTime.UtcNow);

    /// <summary>UTC instant at which the local <paramref name="day"/> begins.</summary>
    public DateTime StartOfDayUtc(DateOnly day) => ToUtc(day.ToDateTime(TimeOnly.MinValue));

    /// <summary>
    /// UTC bounds [start, end) of the local days <paramref name="from"/>..<paramref name="to"/>
    /// (inclusive); without them, the current local week Monday–Sunday.
    /// </summary>
    public (DateTime StartUtc, DateTime EndUtc) DayRangeUtc(DateOnly? from, DateOnly? to)
    {
        var today = Today();
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var first = from ?? monday;
        var last = to ?? (from.HasValue ? first : monday.AddDays(6));
        return (StartOfDayUtc(first), StartOfDayUtc(last.AddDays(1)));
    }
}
