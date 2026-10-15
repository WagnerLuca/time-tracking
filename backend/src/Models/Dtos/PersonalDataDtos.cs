namespace TimeTracking.Api.Models.Dtos;

/// <summary>Everything the application stores about one person (GDPR Art. 15/20).</summary>
public record PersonalDataExport
{
    public DateTime ExportedAt { get; init; } = DateTime.UtcNow;
    public required UserInfo Profile { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public required List<PersonalMembership> Memberships { get; init; }
    /// <summary>Terminal cards, by hint and label (the card value itself is not stored).</summary>
    public required List<PersonalCard> Cards { get; init; }
    /// <summary>The terminal's log of your card scans (kept 400 days).</summary>
    public required List<PersonalPunch> TerminalPunches { get; init; }
    public required List<PersonalTimeEntry> TimeEntries { get; init; }
    public required List<PersonalAbsence> Absences { get; init; }
    public required List<PersonalRequest> Requests { get; init; }
    public required List<PersonalNotification> Notifications { get; init; }
    public required List<PersonalEntryChange> EntryChanges { get; init; }
}

public record PersonalMembership(string Organization, string Role, DateTime JoinedAt, bool Active, double VacationDaysPerYear, double InitialOvertimeHours,
    string? PersonnelNumber);
public record PersonalCard(string Organization, string CardHint, string? Label, bool IsBlocked, DateTime CreatedAt, DateTime? LastUsedAt);
/// <summary>Terminal is null once its API key has been deleted; Result is Started/Stopped/ClosedFailure/Repeated/Blocked/Rejected.</summary>
public record PersonalPunch(string Organization, DateTime ScannedAt, string? Terminal, string Result, string CardHint);
public record PersonalTimeEntry(string? Organization, DateTime StartTime, DateTime? EndTime, int PauseMinutes, string? Description, bool IsFailure);
public record PersonalAbsence(string Organization, DateOnly Date, string Type, bool IsHalfDay, string? Note);
public record PersonalRequest(string Organization, string Type, string Status, string? Message, string? RequestData, DateTime CreatedAt, DateTime? RespondedAt);
/// <summary>Changes is the stored JSON array of {field, oldValue, newValue}.</summary>
public record PersonalEntryChange(int TimeEntryId, DateTime ChangedAt, string Kind, bool ChangedByYou, string Changes);
public record PersonalNotification(string Title, string Message, DateTime CreatedAt, bool IsRead);
