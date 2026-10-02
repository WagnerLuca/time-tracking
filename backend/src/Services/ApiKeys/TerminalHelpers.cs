using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Card values are not secrets (a plain UID is printed on nothing but readable by any phone),
/// yet they are never stored or logged in full: only the SHA-256 of the normalised value plus
/// its last four characters as a hint.
/// </summary>
public static class CardIds
{
    /// <summary>Trim, uppercase, strip ':' and spaces — "04:a2 3b" and "04A23B" are the same card.</summary>
    public static string Normalize(string value) =>
        new(value.Trim().ToUpperInvariant().Where(c => c != ':' && !char.IsWhiteSpace(c)).ToArray());

    /// <summary>Hex SHA-256 of an already normalised value (the migration of old badge IDs computes the same in SQL).</summary>
    public static string Hash(string normalized) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));

    public static string Hint(string normalized) =>
        normalized.Length <= 4 ? normalized : normalized[^4..];
}

/// <summary>Fills the terminal names (API key names) that started/stopped entries, from the punch protocol.</summary>
public static class TerminalNames
{
    private static readonly PunchResult[] StopResults = [PunchResult.Stopped, PunchResult.ClosedFailure];

    /// <summary>One indexed query per list (by entry id); entries without punches stay as they are.</summary>
    public static async Task<List<TimeEntryResponse>> FillAsync(TimeTrackingDbContext context, List<TimeEntryResponse> entries)
    {
        if (entries.Count == 0) return entries;
        var ids = entries.Select(e => e.Id).ToList();
        var punches = await context.TerminalPunches.AsNoTracking()
            .Where(p => p.TimeEntryId != null && ids.Contains(p.TimeEntryId.Value)
                        && (p.Result == PunchResult.Started || p.Result == PunchResult.Stopped || p.Result == PunchResult.ClosedFailure))
            .Select(p => new { EntryId = p.TimeEntryId!.Value, p.Result, Name = p.ApiKey == null ? null : p.ApiKey.Name })
            .ToListAsync();
        if (punches.Count == 0) return entries;

        var byEntry = punches.ToLookup(p => p.EntryId);
        return entries.Select(e =>
        {
            var rows = byEntry[e.Id].ToList();
            if (rows.Count == 0) return e;
            return e with
            {
                TerminalStartName = rows.FirstOrDefault(p => p.Result == PunchResult.Started)?.Name ?? e.TerminalStartName,
                TerminalStopName = rows.FirstOrDefault(p => StopResults.Contains(p.Result))?.Name ?? e.TerminalStopName
            };
        }).ToList();
    }

    public static async Task<TimeEntryResponse> FillAsync(TimeTrackingDbContext context, TimeEntryResponse entry) =>
        (await FillAsync(context, [entry]))[0];
}
