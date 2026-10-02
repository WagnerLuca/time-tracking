namespace TimeTracking.Api.Models;

public enum PunchResult
{
    Started = 0,
    Stopped = 1,
    /// <summary>A queued scan closed the member's failure entry (auto-stopped at business day end).</summary>
    ClosedFailure = 2,
    /// <summary>Second scan of the same card within a minute; nothing toggled.</summary>
    Repeated = 3,
    UnknownCard = 4,
    Blocked = 5,
    /// <summary>The card was known but the punch was refused (timer elsewhere, out of order, invalid times).</summary>
    Rejected = 6
}

/// <summary>
/// One scan a terminal sent (the punch protocol). Written for every scan that reached the card
/// lookup, accepted or not. Holds no plain card value, only its hash and hint.
/// </summary>
public class TerminalPunch
{
    public long Id { get; set; }
    public int OrganizationId { get; set; }
    /// <summary>Null once the key has been revoked; the row stays for the protocol.</summary>
    public int? ApiKeyId { get; set; }
    public int? BadgeCardId { get; set; }
    public int? UserId { get; set; }
    public required string CardHash { get; set; }
    public required string CardHint { get; set; }
    public PunchResult Result { get; set; }
    /// <summary>When the card was scanned (the terminal's <c>scannedAt</c>, else the arrival time).</summary>
    public DateTime ScannedAt { get; set; }
    public DateTime ReceivedAt { get; set; }
    /// <summary>The terminal's id for the scan; only stored on accepted punches (idempotent replays).</summary>
    public string? ClientPunchId { get; set; }
    public int? TimeEntryId { get; set; }

    public Organization Organization { get; set; } = null!;
    public ApiKey? ApiKey { get; set; }
    public BadgeCard? BadgeCard { get; set; }
    public User? User { get; set; }
    public TimeEntry? TimeEntry { get; set; }
}
