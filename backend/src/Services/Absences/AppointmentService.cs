using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Planned appointments (issue #83, <see cref="Appointment"/>). Members manage their own without approval
/// (also in the past and in closed months — appointments change no figure, so <c>ClosedPeriods</c> ignores
/// them); org admins and group admins who may approve the member's absences (<see cref="GroupTree.ActsFor"/>)
/// manage them for their members. Reading follows the member visibility matrix: the slot needs
/// <see cref="MemberInfo.Appointments"/>, the note <see cref="MemberInfo.AppointmentNotes"/> (else null).
/// </summary>
public class AppointmentService(TimeTrackingDbContext context)
{
    public async Task<ServiceResult<List<AppointmentResponse>>> ListAsync(
        string slug, int callerUserId, DateOnly from, DateOnly to, int? userId, int? groupId)
    {
        if (EntryTimes.ValidateRange(from, to) is { } rangeError)
            return ServiceResult.BadRequest<List<AppointmentResponse>>(rangeError);
        var org = await context.FindOrgBySlugAsync(slug);
        if (org == null) return ServiceResult.NotFound<List<AppointmentResponse>>("Organization not found");
        var visibility = await MemberVisibility.ForCallerAsync(context, org, callerUserId);

        var ids = visibility.Ids(MemberInfo.Member | MemberInfo.Appointments);
        if (userId is { } uid)
        {
            if (!visibility.Can(uid, MemberInfo.Member))
                return ServiceResult.NotFound<List<AppointmentResponse>>("Member not found in this organization.");
            ids.IntersectWith([uid]);
        }
        if (groupId is { } gid)
        {
            // Like the absences' group filter: only people whose groups the caller may see
            var tree = visibility.Tree;
            if (tree.Find(gid) is null) return ServiceResult.NotFound<List<AppointmentResponse>>("Group not found.");
            ids.IntersectWith(tree.Subtree(gid).SelectMany(id => tree.Find(id)!.Members).Select(m => m.UserId)
                .Where(id => visibility.CallerIsOrgAdmin || visibility.Can(id, MemberInfo.Member | MemberInfo.Groups)));
        }

        var list = await context.Appointments.AsNoTracking()
            .Where(a => a.OrganizationId == org.Id && ids.Contains(a.UserId) && a.Date >= from && a.Date <= to)
            .OrderBy(a => a.Date).ThenBy(a => a.StartTime).ThenBy(a => a.Id)
            .ToListAsync();
        return ServiceResult.Ok(list.Select(a => ToResponse(a, visibility)).ToList());
    }

    public async Task<ServiceResult<AppointmentResponse>> CreateAsync(string slug, int callerUserId, SaveAppointmentRequest request)
    {
        var org = await context.FindOrgBySlugAsync(slug);
        if (org == null) return ServiceResult.NotFound<AppointmentResponse>("Organization not found");
        var visibility = await MemberVisibility.ForCallerAsync(context, org, callerUserId);
        var userId = request.UserId ?? callerUserId;
        if (Denied(visibility, userId) is { } denied) return ServiceResult.Fail<AppointmentResponse>(denied.Type, denied.Message);

        var appointment = new Appointment { OrganizationId = org.Id, UserId = userId, CreatedByUserId = callerUserId, CreatedAt = DateTime.UtcNow };
        if (Apply(appointment, request) is { } error) return ServiceResult.BadRequest<AppointmentResponse>(error.Message, error.Code);
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();
        return ServiceResult.Ok(ToResponse(appointment, visibility));
    }

    public async Task<ServiceResult<AppointmentResponse>> UpdateAsync(string slug, int callerUserId, int id, SaveAppointmentRequest request)
    {
        var (appointment, visibility, denied) = await FindAsync(slug, callerUserId, id);
        if (denied is { } d) return ServiceResult.Fail<AppointmentResponse>(d.Type, d.Message);
        if (Apply(appointment!, request) is { } error) return ServiceResult.BadRequest<AppointmentResponse>(error.Message, error.Code);
        appointment!.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        return ServiceResult.Ok(ToResponse(appointment, visibility!));
    }

    public async Task<ServiceResult> DeleteAsync(string slug, int callerUserId, int id)
    {
        var (appointment, _, denied) = await FindAsync(slug, callerUserId, id);
        if (denied is { } d) return ServiceResult.Fail(d.Type, d.Message);
        context.Appointments.Remove(appointment!);
        await context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    /// <summary>Whether the caller manages the person's appointments: themselves, org admins, group admins who may approve their absences.</summary>
    public static bool MayManage(MemberVisibility visibility, int personId) =>
        personId == visibility.CallerUserId || visibility.CallerIsOrgAdmin
        || visibility.Tree.ActsFor(visibility.CallerUserId, personId, g => g.GroupAdminCanApproveAbsences == true);

    private async Task<(Appointment?, MemberVisibility?, (ServiceErrorType Type, string Message)?)> FindAsync(string slug, int callerUserId, int id)
    {
        var org = await context.FindOrgBySlugAsync(slug);
        if (org == null) return (null, null, (ServiceErrorType.NotFound, "Organization not found"));
        var visibility = await MemberVisibility.ForCallerAsync(context, org, callerUserId);
        var appointment = await context.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.OrganizationId == org.Id);
        if (appointment == null || !visibility.Can(appointment.UserId, MemberInfo.Member | MemberInfo.Appointments))
            return (null, null, (ServiceErrorType.NotFound, NotFoundMessage));
        return (appointment, visibility, Denied(visibility, appointment.UserId));
    }

    public const string NotFoundMessage = "Appointment not found.";

    /// <summary>Hidden people and non-members (the matrix holds active members only) 404, visible ones the caller does not manage 403.</summary>
    private static (ServiceErrorType Type, string Message)? Denied(MemberVisibility visibility, int personId) =>
        !visibility.Can(personId, MemberInfo.Member) ? (ServiceErrorType.NotFound, "Member not found in this organization.")
        : !MayManage(visibility, personId) ? (ServiceErrorType.Forbidden, "You can only manage your own appointments.")
        : null;

    /// <summary>Validates and copies the request; the error message and its code, or null.</summary>
    private static (string Message, string Code)? Apply(Appointment appointment, SaveAppointmentRequest request)
    {
        if (request.Date.Year is < 2000 or > 2100) return ("Dates must be between the years 2000 and 2100.", "appointment.date");
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > Appointment.NoteMaxLength })
            return ($"The note can have at most {Appointment.NoteMaxLength} characters.", "appointment.noteTooLong");
        TimeOnly? start = null, end = null;
        if (request.StartTime != null || request.EndTime != null)
        {
            if (!TimeOnly.TryParseExact(request.StartTime, "HH:mm", out var s) || !TimeOnly.TryParseExact(request.EndTime, "HH:mm", out var e) || e <= s)
                return ("An appointment needs a start before its end (HH:mm), or neither for all day.", "appointment.times");
            (start, end) = (s, e);
        }
        appointment.Date = request.Date;
        appointment.StartTime = start;
        appointment.EndTime = end;
        appointment.Note = note;
        return null;
    }

    private static AppointmentResponse ToResponse(Appointment a, MemberVisibility visibility) => new()
    {
        Id = a.Id,
        UserId = a.UserId,
        Date = a.Date,
        StartTime = a.StartTime?.ToString("HH:mm"),
        EndTime = a.EndTime?.ToString("HH:mm"),
        AllDay = a.StartTime == null,
        Note = visibility.Can(a.UserId, MemberInfo.AppointmentNotes) ? a.Note : null,
        CanEdit = MayManage(visibility, a.UserId),
        CreatedAt = a.CreatedAt,
        CreatedByUserId = a.CreatedByUserId
    };
}
