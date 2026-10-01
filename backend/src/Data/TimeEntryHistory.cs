using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Data;

/// <summary>A field of a time entry before and after a change (null = not set).</summary>
public record TimeEntryFieldChange(string Field, string? OldValue, string? NewValue);

/// <summary>
/// Turns the tracked changes of time entries into <see cref="TimeEntryChange"/> rows. Running
/// timers (start, stop, auto-stop) are not history; everything done to a finished entry is.
/// </summary>
internal static class TimeEntryHistory
{
    private static readonly string[] Fields =
    [
        nameof(TimeEntry.StartTime), nameof(TimeEntry.EndTime), nameof(TimeEntry.PauseDurationMinutes),
        nameof(TimeEntry.Description), nameof(TimeEntry.OrganizationId), nameof(TimeEntry.IsFailure)
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record Pending(TimeEntry Entry, TimeEntryChangeKind Kind, List<TimeEntryFieldChange> Changes);

    /// <summary>Changes about to be saved; call before saving (original values are still known).</summary>
    public static List<Pending> Capture(DbContext context, TimeEntryChangeKind? kindOverride)
    {
        var pending = new List<Pending>();
        foreach (var e in context.ChangeTracker.Entries<TimeEntry>())
        {
            List<TimeEntryFieldChange> changes;
            TimeEntryChangeKind kind;
            switch (e.State)
            {
                case EntityState.Added when !e.Entity.IsRunning:
                    kind = TimeEntryChangeKind.Created;
                    changes = Fields.Select(f => new TimeEntryFieldChange(f, null, Format(e.Property(f).CurrentValue)))
                        .Where(c => c.NewValue != null && !(c.Field == nameof(TimeEntry.IsFailure) && c.NewValue == "false"))
                        .ToList();
                    break;
                case EntityState.Modified when !(bool)e.Property(nameof(TimeEntry.IsRunning)).OriginalValue!:
                    kind = TimeEntryChangeKind.Updated;
                    changes = Fields.Select(f => Diff(e, f)).OfType<TimeEntryFieldChange>().ToList();
                    if (changes.Count == 0) continue;
                    break;
                case EntityState.Deleted when !(bool)e.Property(nameof(TimeEntry.IsRunning)).OriginalValue!:
                    kind = TimeEntryChangeKind.Deleted;
                    changes = Fields.Select(f => new TimeEntryFieldChange(f, Format(e.Property(f).OriginalValue), null))
                        .Where(c => c.OldValue != null).ToList();
                    break;
                default:
                    continue;
            }
            pending.Add(new Pending(e.Entity, kindOverride ?? kind, changes));
        }
        return pending;
    }

    /// <summary>The history rows, once the entries have their ids.</summary>
    public static IEnumerable<TimeEntryChange> Rows(IEnumerable<Pending> pending, int? changedByUserId) =>
        pending.Select(p => new TimeEntryChange
        {
            TimeEntryId = p.Entry.Id,
            UserId = p.Entry.UserId,
            OrganizationId = p.Entry.OrganizationId,
            ChangedByUserId = changedByUserId,
            ChangedAt = DateTime.UtcNow,
            Kind = p.Kind,
            Changes = JsonSerializer.Serialize(p.Changes, Json)
        });

    public static List<TimeEntryFieldChange> Parse(string changes) =>
        JsonSerializer.Deserialize<List<TimeEntryFieldChange>>(changes, Json) ?? [];

    private static TimeEntryFieldChange? Diff(EntityEntry<TimeEntry> e, string field)
    {
        var p = e.Property(field);
        var (oldValue, newValue) = (Format(p.OriginalValue), Format(p.CurrentValue));
        return oldValue == newValue ? null : new TimeEntryFieldChange(field, oldValue, newValue);
    }

    private static string? Format(object? value) => value switch
    {
        null => null,
        DateTime d => DateTime.SpecifyKind(d, d.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : d.Kind)
            .ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
}
