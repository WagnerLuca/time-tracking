using System.Text.Json.Serialization;

namespace TimeTracking.Api.Models;

/// <summary>
/// A planned appointment ("Termin", issue #83) a member registers for the team calendar, e.g. "Arzt 10–12".
/// Information only: never counted in hours, balance, payroll or reports, so it is not part of closed months
/// (editable in the past and in closed months — it changes no figure). The one effect: its time counts as
/// covered for the core-hours check (<c>CoreHours</c>), an all-day one exempts the day.
/// Visibility: <see cref="MemberInfo.Appointments"/> (the slot) and <see cref="MemberInfo.AppointmentNotes"/> (the note).
/// RLS like TimeEntries.
/// </summary>
public class Appointment
{
    public const int NoteMaxLength = 200;

    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int UserId { get; set; }
    /// <summary>The org-local day.</summary>
    public DateOnly Date { get; set; }
    /// <summary>Org-local start/end on <see cref="Date"/> (end after start); both null = all day.</summary>
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public string? Note { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    [JsonIgnore]
    public Organization Organization { get; set; } = null!;
    [JsonIgnore]
    public User User { get; set; } = null!;
}
