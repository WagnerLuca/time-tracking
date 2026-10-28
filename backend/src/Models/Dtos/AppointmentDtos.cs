namespace TimeTracking.Api.Models.Dtos;

/// <summary>
/// An appointment as the caller may see it: <see cref="Note"/> is null when the note is hidden
/// (<c>AppointmentNotes</c>), <see cref="StartTime"/>/<see cref="EndTime"/> ("HH:mm") null = all day.
/// </summary>
public record AppointmentResponse
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public DateOnly Date { get; init; }
    public string? StartTime { get; init; }
    public string? EndTime { get; init; }
    public bool AllDay { get; init; }
    public string? Note { get; init; }
    /// <summary>Whether the caller may edit/delete it (own, or org admin / group admin who may approve the member's absences).</summary>
    public bool CanEdit { get; init; }
    public DateTime CreatedAt { get; init; }
    public int? CreatedByUserId { get; init; }
}

/// <summary>Create or replace an appointment. <c>UserId</c> (create only) defaults to the caller; times "HH:mm", both left out = all day.</summary>
public record SaveAppointmentRequest(DateOnly Date, string? StartTime = null, string? EndTime = null, string? Note = null, int? UserId = null);
