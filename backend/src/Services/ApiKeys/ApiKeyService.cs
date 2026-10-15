using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

public interface IApiKeyService
{
    Task<ServiceResult<List<ApiKeyResponse>>> ListAsync(string slug);
    Task<ServiceResult<CreatedApiKeyResponse>> CreateAsync(string slug, int callerUserId, CreateApiKeyRequest request);
    Task<ServiceResult<ApiKeyResponse>> RenameAsync(string slug, int keyId, UpdateApiKeyRequest request);
    Task<ServiceResult> DeleteAsync(string slug, int keyId);

    Task<ServiceResult<List<BadgeCardResponse>>> ListCardsAsync(string slug, int memberId);
    Task<ServiceResult<BadgeCardResponse>> AddCardAsync(string slug, int memberId, AddBadgeCardRequest request);
    Task<ServiceResult<BadgeCardResponse>> UpdateCardAsync(string slug, int memberId, int cardId, UpdateBadgeCardRequest request);
    Task<ServiceResult> DeleteCardAsync(string slug, int memberId, int cardId);

    Task<ServiceResult<List<UnknownCardResponse>>> ListUnknownCardsAsync(string slug);
    Task<ServiceResult<List<TerminalPunchResponse>>> ListPunchesAsync(string slug, DateTime? from, DateTime? to, int? limit);

    Task<ServiceResult<PunchResponse>> PunchAsync(string? apiKey, PunchRequest request);
}

/// <summary>
/// Organization API keys for terminals (e.g. RFID scanners), members' cards, the punch protocol
/// and the punch itself. Role checks for management happen in the controller via RequireOrgRole(Admin).
/// <para>
/// Cards and the punch protocol are org admin data without row-level security, like API keys,
/// holidays or requests: access is the admin role check, and the anonymous punch can write its
/// log without an RLS bypass. Only the time entry itself is RLS-protected; the punch reaches it
/// by acting as the card's member (<see cref="RlsContext.ActAsUser"/>), never as the system.
/// </para>
/// </summary>
public class ApiKeyService : IApiKeyService
{
    private const string KeyPrefix = "tt_";

    // The double-scan window, the offline window (oldest queued scan) and the unknown-card budget
    // are organization settings (Organization.Terminal*, TerminalLimits)
    /// <summary>How far a terminal's clock may run ahead.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(2);
    /// <summary>A queued scan closes a failure entry that started at most this long before it.</summary>
    public static readonly TimeSpan FailureCloseWindow = TimeSpan.FromHours(24);
    /// <summary>Window of the unknown-card budget (Organization.TerminalUnknownCardLimit scans per window).</summary>
    public static readonly TimeSpan UnknownScanWindow = TimeSpan.FromMinutes(10);

    private static readonly PunchResult[] Accepted = [PunchResult.Started, PunchResult.Stopped, PunchResult.ClosedFailure];

    private readonly TimeTrackingDbContext _context;
    private readonly ITimeTrackingService _timeTracking;
    private readonly RlsContext _rls;
    private readonly ILogger<ApiKeyService> _logger;

    public ApiKeyService(TimeTrackingDbContext context, ITimeTrackingService timeTracking, RlsContext rls, ILogger<ApiKeyService> logger)
    {
        _context = context;
        _timeTracking = timeTracking;
        _rls = rls;
        _logger = logger;
    }

    // ────────────────────────────────────────────────────
    //  API keys
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<List<ApiKeyResponse>>> ListAsync(string slug)
    {
        var org = await _context.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Slug == slug);
        if (org == null) return ServiceResult.NotFound<List<ApiKeyResponse>>("Organization not found");

        var keys = await _context.ApiKeys.AsNoTracking()
            .Where(k => k.OrganizationId == org.Id)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync();

        return ServiceResult.Ok(keys.Select(MapKey).ToList());
    }

    public async Task<ServiceResult<CreatedApiKeyResponse>> CreateAsync(string slug, int callerUserId, CreateApiKeyRequest request)
    {
        var org = await _context.Organizations.FirstOrDefaultAsync(o => o.Slug == slug);
        if (org == null) return ServiceResult.NotFound<CreatedApiKeyResponse>("Organization not found");

        var key = KeyPrefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var entity = new ApiKey
        {
            OrganizationId = org.Id,
            Name = request.Name.Trim(),
            KeyPrefix = key[..10],
            KeyHash = Hash(key),
            CreatedByUserId = callerUserId,
            CreatedAt = DateTime.UtcNow
        };

        _context.ApiKeys.Add(entity);
        await _context.SaveChangesAsync();
        _logger.LogInformation("API key {KeyId} ({Name}) created for org {OrgSlug} by user {UserId}", entity.Id, entity.Name, slug, callerUserId);

        return ServiceResult.Ok(new CreatedApiKeyResponse
        {
            Id = entity.Id,
            Name = entity.Name,
            KeyPrefix = entity.KeyPrefix,
            CreatedAt = entity.CreatedAt,
            Key = key
        });
    }

    public async Task<ServiceResult<ApiKeyResponse>> RenameAsync(string slug, int keyId, UpdateApiKeyRequest request)
    {
        var key = await _context.ApiKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.Organization.Slug == slug);
        if (key == null) return ServiceResult.NotFound<ApiKeyResponse>("API key not found");

        key.Name = request.Name.Trim();
        await _context.SaveChangesAsync();
        return ServiceResult.Ok(MapKey(key));
    }

    public async Task<ServiceResult> DeleteAsync(string slug, int keyId)
    {
        var key = await _context.ApiKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.Organization.Slug == slug);
        if (key == null) return ServiceResult.NotFound("API key not found");

        // The punch protocol keeps its rows (ApiKeyId becomes null)
        _context.ApiKeys.Remove(key);
        await _context.SaveChangesAsync();
        _logger.LogInformation("API key {KeyId} revoked for org {OrgSlug}", keyId, slug);
        return ServiceResult.Ok();
    }

    // ────────────────────────────────────────────────────
    //  Cards
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<List<BadgeCardResponse>>> ListCardsAsync(string slug, int memberId)
    {
        var orgId = await FindMemberOrgAsync(slug, memberId);
        if (orgId == null) return ServiceResult.NotFound<List<BadgeCardResponse>>("Member not found in this organization.");

        var cards = await _context.BadgeCards.AsNoTracking()
            .Where(c => c.OrganizationId == orgId && c.UserId == memberId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();
        return ServiceResult.Ok(cards.Select(MapCard).ToList());
    }

    public async Task<ServiceResult<BadgeCardResponse>> AddCardAsync(string slug, int memberId, AddBadgeCardRequest request)
    {
        var orgId = await FindMemberOrgAsync(slug, memberId);
        if (orgId == null) return ServiceResult.NotFound<BadgeCardResponse>("Member not found in this organization.");

        string hash, hint;
        if (request.FromPunchId is long punchId)
        {
            var scan = await _context.TerminalPunches.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == punchId && p.OrganizationId == orgId && p.Result == PunchResult.UnknownCard);
            if (scan == null) return ServiceResult.NotFound<BadgeCardResponse>("Scan not found.");
            (hash, hint) = (scan.CardHash, scan.CardHint);
        }
        else if (CardIds.Normalize(request.CardId ?? "") is { Length: > 0 } normalized)
            (hash, hint) = (CardIds.Hash(normalized), CardIds.Hint(normalized));
        else
            return ServiceResult.BadRequest<BadgeCardResponse>("Enter a card ID or pick a scanned card.");

        if (await _context.BadgeCards.AnyAsync(c => c.OrganizationId == orgId && c.CardHash == hash))
            return ServiceResult.Conflict<BadgeCardResponse>("This card is already registered.");

        var card = new BadgeCard
        {
            OrganizationId = orgId.Value,
            UserId = memberId,
            CardHash = hash,
            CardHint = hint,
            Label = CleanLabel(request.Label),
            CreatedAt = DateTime.UtcNow
        };
        _context.BadgeCards.Add(card);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            _context.Entry(card).State = EntityState.Detached;
            return ServiceResult.Conflict<BadgeCardResponse>("This card is already registered.");
        }
        _logger.LogInformation("Card {CardId} (…{Hint}) added to user {UserId} in org {OrgSlug}", card.Id, hint, memberId, slug);
        return ServiceResult.Ok(MapCard(card));
    }

    public async Task<ServiceResult<BadgeCardResponse>> UpdateCardAsync(string slug, int memberId, int cardId, UpdateBadgeCardRequest request)
    {
        var card = await FindCardAsync(slug, memberId, cardId);
        if (card == null) return ServiceResult.NotFound<BadgeCardResponse>("Card not found.");

        if (request.Label != null) card.Label = CleanLabel(request.Label);
        if (request.IsBlocked is bool blocked) card.IsBlocked = blocked;
        await _context.SaveChangesAsync();
        return ServiceResult.Ok(MapCard(card));
    }

    public async Task<ServiceResult> DeleteCardAsync(string slug, int memberId, int cardId)
    {
        var card = await FindCardAsync(slug, memberId, cardId);
        if (card == null) return ServiceResult.NotFound("Card not found.");

        _context.BadgeCards.Remove(card);
        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    // ────────────────────────────────────────────────────
    //  Punch protocol
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<List<UnknownCardResponse>>> ListUnknownCardsAsync(string slug)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null) return ServiceResult.NotFound<List<UnknownCardResponse>>("Organization not found");

        // ponytail: grouped in memory; bounded by the unknown-scan budget (20 per key and 10 minutes)
        var since = DateTime.UtcNow.AddDays(-7);
        var scans = await _context.TerminalPunches.AsNoTracking()
            .Where(p => p.OrganizationId == org.Id && p.Result == PunchResult.UnknownCard && p.ReceivedAt >= since
                        && !_context.BadgeCards.Any(c => c.OrganizationId == p.OrganizationId && c.CardHash == p.CardHash))
            .Select(p => new { p.Id, p.CardHash, p.CardHint, p.ScannedAt, TerminalName = p.ApiKey == null ? null : p.ApiKey.Name })
            .ToListAsync();

        return ServiceResult.Ok(scans
            .GroupBy(s => s.CardHash)
            .Select(g =>
            {
                var latest = g.OrderByDescending(s => s.ScannedAt).ThenByDescending(s => s.Id).First();
                return new UnknownCardResponse
                {
                    PunchId = latest.Id,
                    CardHint = latest.CardHint,
                    TerminalName = latest.TerminalName,
                    LastSeenAt = latest.ScannedAt,
                    Count = g.Count()
                };
            })
            .OrderByDescending(c => c.LastSeenAt)
            .ToList());
    }

    public async Task<ServiceResult<List<TerminalPunchResponse>>> ListPunchesAsync(string slug, DateTime? from, DateTime? to, int? limit)
    {
        var org = await _context.FindOrgBySlugAsync(slug);
        if (org == null) return ServiceResult.NotFound<List<TerminalPunchResponse>>("Organization not found");

        var query = _context.TerminalPunches.AsNoTracking().Where(p => p.OrganizationId == org.Id);
        if (from.HasValue) query = query.Where(p => p.ScannedAt >= from.Value);
        if (to.HasValue) query = query.Where(p => p.ScannedAt < to.Value);

        var rows = await query
            .OrderByDescending(p => p.ScannedAt).ThenByDescending(p => p.Id)
            .Take(Math.Clamp(limit ?? 200, 1, 1000))
            .Select(p => new
            {
                p.Id, p.ScannedAt, p.ReceivedAt, p.Result, p.CardHint, p.UserId, p.TimeEntryId,
                TerminalName = p.ApiKey == null ? null : p.ApiKey.Name,
                CardLabel = p.BadgeCard == null ? null : p.BadgeCard.Label,
                MemberName = p.User == null ? null : p.User.FirstName + " " + p.User.LastName
            })
            .ToListAsync();

        return ServiceResult.Ok(rows.Select(p => new TerminalPunchResponse
        {
            Id = p.Id,
            ScannedAt = p.ScannedAt,
            ReceivedAt = p.ReceivedAt,
            Result = p.Result.ToString(),
            TerminalName = p.TerminalName,
            CardHint = p.CardHint,
            CardLabel = p.CardLabel,
            UserId = p.UserId,
            MemberName = p.MemberName,
            TimeEntryId = p.TimeEntryId
        }).ToList());
    }

    // ────────────────────────────────────────────────────
    //  Punch
    // ────────────────────────────────────────────────────

    public async Task<ServiceResult<PunchResponse>> PunchAsync(string? apiKey, PunchRequest request)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return ServiceResult.Unauthorized<PunchResponse>("Missing API key.");

        var keyHash = Hash(apiKey.Trim());
        var key = await _context.ApiKeys.Include(k => k.Organization).FirstOrDefaultAsync(k => k.KeyHash == keyHash);
        if (key == null)
            return ServiceResult.Unauthorized<PunchResponse>("Invalid API key.");

        var now = DateTime.UtcNow;
        key.LastUsedAt = now;

        // Idempotent offline replays: a punch id this key already sent returns the stored result
        var clientPunchId = string.IsNullOrWhiteSpace(request.ClientPunchId) ? null : request.ClientPunchId.Trim();
        if (clientPunchId != null
            && await _context.TerminalPunches.AsNoTracking().FirstOrDefaultAsync(p => p.ApiKeyId == key.Id && p.ClientPunchId == clientPunchId) is { } stored)
        {
            await _context.SaveChangesAsync();
            return await ReplayAsync(stored);
        }

        DateTime? scannedAt = request.ScannedAt is { } raw ? ToUtc(raw) : null;
        var offlineHours = key.Organization.TerminalOfflineHours;
        if (scannedAt is { } s && (s < now.AddHours(-offlineHours) || s > now + MaxClockSkew))
            return ServiceResult.BadRequest<PunchResponse>($"The scan time must lie between {offlineHours} hours ago and 2 minutes from now.");
        var at = scannedAt ?? now;

        var normalized = CardIds.Normalize(request.BadgeId);
        if (normalized.Length == 0)
            return ServiceResult.BadRequest<PunchResponse>("Enter a card ID or pick a scanned card.");

        var punch = new TerminalPunch
        {
            OrganizationId = key.OrganizationId,
            ApiKeyId = key.Id,
            CardHash = CardIds.Hash(normalized),
            CardHint = CardIds.Hint(normalized),
            ScannedAt = at,
            ReceivedAt = now
        };

        var card = await _context.BadgeCards.Include(c => c.User)
            .FirstOrDefaultAsync(c => c.OrganizationId == key.OrganizationId && c.CardHash == punch.CardHash);
        // Cards are removed when a member leaves; this guards a deactivated account all the same
        if (card != null && (!card.User.IsActive
            || !await _context.UserOrganizations.AnyAsync(uo => uo.OrganizationId == key.OrganizationId && uo.UserId == card.UserId)))
            card = null;

        if (card == null || card.IsBlocked)
        {
            // Brute-force guard: a key guessing card values runs out of unknown scans; known cards keep working
            var since = now - UnknownScanWindow;
            var misses = await _context.TerminalPunches.CountAsync(p => p.ApiKeyId == key.Id && p.ReceivedAt >= since
                && (p.Result == PunchResult.UnknownCard || p.Result == PunchResult.Blocked));
            if (misses >= key.Organization.TerminalUnknownCardLimit)
            {
                await _context.SaveChangesAsync();
                _logger.LogWarning("Terminal key {KeyId} exceeded the unknown-card budget", key.Id);
                return ServiceResult.TooManyRequests<PunchResponse>("Too many unknown cards were scanned. Try again later.");
            }

            punch.Result = card == null ? PunchResult.UnknownCard : PunchResult.Blocked;
            punch.BadgeCardId = card?.Id;
            punch.UserId = card?.UserId;
            _context.TerminalPunches.Add(punch);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Terminal key {KeyId}: {Result} card …{Hint}", key.Id, punch.Result, punch.CardHint);
            return card == null
                ? ServiceResult.NotFound<PunchResponse>("Unknown card.")
                : ServiceResult.Forbidden<PunchResponse>("This card is blocked.");
        }

        var userId = card.UserId;
        card.LastUsedAt = now;
        punch.BadgeCardId = card.Id;
        punch.UserId = userId;

        var last = await _context.TerminalPunches.AsNoTracking()
            .Where(p => p.OrganizationId == key.OrganizationId && p.UserId == userId && Accepted.Contains(p.Result))
            .OrderByDescending(p => p.ScannedAt).ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync();
        if (last != null)
        {
            if (last.CardHash == punch.CardHash && (at - last.ScannedAt).Duration() < TimeSpan.FromSeconds(key.Organization.TerminalDoubleScanSeconds))
            {
                punch.Result = PunchResult.Repeated;
                punch.TimeEntryId = last.TimeEntryId;
                punch.ClientPunchId = clientPunchId;
                if (await SavePunchAsync(punch) is { } replay) return replay;
                return await RespondAsync(last, card.User, repeated: true);
            }
            // Queued scans arrive in order; a live scan is "now" (a terminal clock running ahead must not block it)
            if (scannedAt != null && at < last.ScannedAt)
                return await RejectAsync(punch, ServiceResult.Conflict<PunchResponse>("This scan is older than the member's last punch. Send queued scans in order."));
        }

        // The request is anonymous; the key + card identified the member, so the database
        // may treat the rest of this request as that member (row-level security)
        _rls.ActAsUser(userId);
        var running = await _context.TimeEntries
            .Where(e => e.UserId == userId && e.IsRunning)
            .Select(e => new { e.OrganizationId })
            .FirstOrDefaultAsync();
        // A terminal of organization A must neither stop nor reveal a timer elsewhere
        if (running != null && running.OrganizationId != key.OrganizationId)
            return await RejectAsync(punch, ServiceResult.Conflict<PunchResponse>("A timer is running outside this organization. Stop it in the app first."));

        ServiceResult<TimeEntryResponse> result;
        try
        {
            if (running != null)
            {
                punch.Result = PunchResult.Stopped;
                result = await _timeTracking.StopAsync(userId, null, scannedAt);
            }
            else if (scannedAt != null && await FindFailureAsync(userId, key.OrganizationId, at) is { } failure)
            {
                punch.Result = PunchResult.ClosedFailure;
                result = await CloseFailureAsync(failure, at);
            }
            else
            {
                punch.Result = PunchResult.Started;
                result = await _timeTracking.StartAsync(userId, new StartTimeEntryRequest { OrganizationId = key.OrganizationId }, scannedAt, TimeEntrySource.Terminal);
            }
        }
        catch (PeriodClosedException closed)
        {
            // A queued scan into a closed month (ClosedPeriods): drop the staged entry, log the refusal
            foreach (var staged in _context.ChangeTracker.Entries<TimeEntry>().Where(e => e.State != EntityState.Unchanged).ToList())
                staged.State = EntityState.Detached;
            result = ServiceResult.Conflict<TimeEntryResponse>(closed.Message, closed.Code);
        }

        if (!result.IsSuccess)
            return await RejectAsync(punch, ServiceResult.Fail<PunchResponse>(result.ErrorType!.Value, result.ErrorMessage!, result.Code));

        punch.TimeEntryId = result.Data!.Id;
        punch.ClientPunchId = clientPunchId;
        if (await SavePunchAsync(punch) is { } raced) return raced;
        _logger.LogInformation("Card punch via key {KeyId}: user {UserId} {Result}", key.Id, userId, punch.Result);

        return ServiceResult.Ok(new PunchResponse
        {
            Action = punch.Result.ToString(),
            FirstName = card.User.FirstName,
            LastName = card.User.LastName,
            Entry = await TerminalNames.FillAsync(_context, result.Data!)
        });
    }

    /// <summary>
    /// Saves the punch row. When the same client punch id raced in concurrently, the other
    /// request's stored result is returned instead.
    /// ponytail: the toggle itself is already saved then; terminals send their queue one at a time.
    /// </summary>
    private async Task<ServiceResult<PunchResponse>?> SavePunchAsync(TerminalPunch punch)
    {
        _context.TerminalPunches.Add(punch);
        try
        {
            await _context.SaveChangesAsync();
            return null;
        }
        catch (DbUpdateException ex) when (punch.ClientPunchId != null && DbErrors.IsUniqueViolation(ex))
        {
            _context.Entry(punch).State = EntityState.Detached;
            var stored = await _context.TerminalPunches.AsNoTracking()
                .FirstAsync(p => p.ApiKeyId == punch.ApiKeyId && p.ClientPunchId == punch.ClientPunchId);
            return await ReplayAsync(stored);
        }
    }

    /// <summary>Logs a refused punch of a known card (no client punch id: a rejected scan may be retried) and returns the error.</summary>
    private async Task<ServiceResult<PunchResponse>> RejectAsync(TerminalPunch punch, ServiceResult<PunchResponse> error)
    {
        punch.Result = PunchResult.Rejected;
        punch.TimeEntryId = null;
        punch.ClientPunchId = null;
        _context.TerminalPunches.Add(punch);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Card punch of user {UserId} rejected: {Reason}", punch.UserId, error.ErrorMessage);
        return error;
    }

    /// <summary>The stored result of an earlier punch with the same client punch id.</summary>
    private async Task<ServiceResult<PunchResponse>> ReplayAsync(TerminalPunch stored)
    {
        var accepted = stored;
        if (stored.Result == PunchResult.Repeated)
            accepted = await _context.TerminalPunches.AsNoTracking()
                .Where(p => p.TimeEntryId == stored.TimeEntryId && p.UserId == stored.UserId && Accepted.Contains(p.Result) && p.Id < stored.Id)
                .OrderByDescending(p => p.Id)
                .FirstOrDefaultAsync() ?? stored;
        var user = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == stored.UserId);
        return await RespondAsync(accepted, user, repeated: stored.Result == PunchResult.Repeated);
    }

    /// <summary>The response of an accepted punch, with the entry as it is now.</summary>
    private async Task<ServiceResult<PunchResponse>> RespondAsync(TerminalPunch accepted, User user, bool repeated)
    {
        _rls.ActAsUser(user.Id);
        var entry = await _context.TimeEntries.AsNoTracking().Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.Id == accepted.TimeEntryId);
        if (entry == null) return ServiceResult.NotFound<PunchResponse>("Time entry not found.");

        return ServiceResult.Ok(new PunchResponse
        {
            Action = accepted.Result.ToString(),
            Repeated = repeated,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Entry = await TerminalNames.FillAsync(_context, TimeTrackingService.MapToResponse(entry))
        });
    }

    /// <summary>The member's latest failure entry in the org that started within a day before the scan.</summary>
    private Task<TimeEntry?> FindFailureAsync(int userId, int orgId, DateTime atUtc)
    {
        var earliest = atUtc - FailureCloseWindow;
        return _context.TimeEntries.Include(e => e.Organization)
            .Where(e => e.UserId == userId && e.OrganizationId == orgId && e.IsFailure && e.StartTime < atUtc && e.StartTime >= earliest)
            .OrderByDescending(e => e.StartTime)
            .FirstOrDefaultAsync();
    }

    /// <summary>A queued "stop" for a timer the business day end already closed: its real end time.</summary>
    private async Task<ServiceResult<TimeEntryResponse>> CloseFailureAsync(TimeEntry entry, DateTime endUtc)
    {
        if (EntryTimes.Validate(entry.StartTime, endUtc) is { } timeError)
            return ServiceResult.BadRequest<TimeEntryResponse>(timeError);
        if (await EntryTimes.OverlapsAsync(_context, entry.UserId, entry.StartTime, endUtc, entry.Id))
            return ServiceResult.Conflict<TimeEntryResponse>("The entry overlaps one of your existing entries.");

        entry.EndTime = endUtc;
        entry.IsFailure = false;
        entry.UpdatedAt = DateTime.UtcNow;
        await PauseRules.ApplyAsync(_context, entry);
        await _context.SaveChangesAsync();
        return ServiceResult.Ok(TimeTrackingService.MapToResponse(entry));
    }

    // ────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────

    private Task<int?> FindMemberOrgAsync(string slug, int memberId) =>
        _context.UserOrganizations.AsNoTracking()
            .Where(uo => uo.UserId == memberId && uo.Organization.Slug == slug)
            .Select(uo => (int?)uo.OrganizationId)
            .FirstOrDefaultAsync();

    private Task<BadgeCard?> FindCardAsync(string slug, int memberId, int cardId) =>
        _context.BadgeCards.FirstOrDefaultAsync(c => c.Id == cardId && c.UserId == memberId && c.Organization.Slug == slug);

    private static string? CleanLabel(string? label) => string.IsNullOrWhiteSpace(label) ? null : label.Trim();

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static ApiKeyResponse MapKey(ApiKey k) => new()
    {
        Id = k.Id,
        Name = k.Name,
        KeyPrefix = k.KeyPrefix,
        CreatedAt = k.CreatedAt,
        LastUsedAt = k.LastUsedAt
    };

    private static BadgeCardResponse MapCard(BadgeCard c) => new()
    {
        Id = c.Id,
        UserId = c.UserId,
        CardHint = c.CardHint,
        Label = c.Label,
        IsBlocked = c.IsBlocked,
        CreatedAt = c.CreatedAt,
        LastUsedAt = c.LastUsedAt
    };

    private static string Hash(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
