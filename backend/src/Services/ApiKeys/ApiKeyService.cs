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
    Task<ServiceResult> DeleteAsync(string slug, int keyId);
    Task<ServiceResult> SetBadgeAsync(string slug, int memberId, SetBadgeRequest request);
    Task<ServiceResult<PunchResponse>> PunchAsync(string? apiKey, PunchRequest request);
}

/// <summary>
/// Organization API keys for terminals (e.g. RFID scanners) and the badge punch they allow.
/// Role checks for key/badge management happen in the controller via RequireOrgRole(Admin).
/// </summary>
public class ApiKeyService : IApiKeyService
{
    private const string KeyPrefix = "tt_";

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

    public async Task<ServiceResult<List<ApiKeyResponse>>> ListAsync(string slug)
    {
        var org = await _context.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Slug == slug);
        if (org == null) return ServiceResult.NotFound<List<ApiKeyResponse>>("Organization not found");

        var keys = await _context.ApiKeys.AsNoTracking()
            .Where(k => k.OrganizationId == org.Id)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeyResponse
            {
                Id = k.Id,
                Name = k.Name,
                KeyPrefix = k.KeyPrefix,
                CreatedAt = k.CreatedAt,
                LastUsedAt = k.LastUsedAt
            })
            .ToListAsync();

        return ServiceResult.Ok(keys);
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

    public async Task<ServiceResult> DeleteAsync(string slug, int keyId)
    {
        var key = await _context.ApiKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.Organization.Slug == slug);
        if (key == null) return ServiceResult.NotFound("API key not found");

        _context.ApiKeys.Remove(key);
        await _context.SaveChangesAsync();
        _logger.LogInformation("API key {KeyId} revoked for org {OrgSlug}", keyId, slug);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> SetBadgeAsync(string slug, int memberId, SetBadgeRequest request)
    {
        var membership = await _context.UserOrganizations
            .FirstOrDefaultAsync(uo => uo.UserId == memberId && uo.Organization.Slug == slug);
        if (membership == null) return ServiceResult.NotFound("Member not found in this organization.");

        var badgeId = string.IsNullOrWhiteSpace(request.BadgeId) ? null : request.BadgeId.Trim();
        if (badgeId != null)
        {
            var taken = await _context.UserOrganizations.AnyAsync(uo =>
                uo.OrganizationId == membership.OrganizationId && uo.BadgeId == badgeId && uo.Id != membership.Id);
            if (taken) return ServiceResult.Conflict("This badge is already assigned to another member.");
        }

        membership.BadgeId = badgeId;
        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<PunchResponse>> PunchAsync(string? apiKey, PunchRequest request)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return ServiceResult.Unauthorized<PunchResponse>("Missing API key.");

        var hash = Hash(apiKey.Trim());
        var key = await _context.ApiKeys.Include(k => k.Organization).FirstOrDefaultAsync(k => k.KeyHash == hash);
        if (key == null)
            return ServiceResult.Unauthorized<PunchResponse>("Invalid API key.");

        var badgeId = request.BadgeId.Trim();
        var membership = await _context.UserOrganizations
            .Include(uo => uo.User)
            .FirstOrDefaultAsync(uo => uo.OrganizationId == key.OrganizationId && uo.BadgeId == badgeId);
        if (membership == null || !membership.User.IsActive)
            return ServiceResult.NotFound<PunchResponse>("Unknown badge.");

        key.LastUsedAt = DateTime.UtcNow;

        // The request is anonymous; the key + badge identified the member, so the database
        // may treat the rest of this request as that member (row-level security)
        var userId = membership.UserId;
        _rls.ActAsUser(userId);
        var isRunning = await _context.TimeEntries.AnyAsync(e => e.UserId == userId && e.IsRunning);
        var result = isRunning
            ? await _timeTracking.StopAsync(userId, null)
            : await _timeTracking.StartAsync(userId, new StartTimeEntryRequest { OrganizationId = key.OrganizationId });

        if (!result.IsSuccess)
            return ServiceResult.Fail<PunchResponse>(result.ErrorType!.Value, result.ErrorMessage!);

        // StartAsync/StopAsync save their own changes; persist LastUsedAt in case they didn't touch the context
        await _context.SaveChangesAsync();
        _logger.LogInformation("Badge punch via key {KeyId}: user {UserId} {Action}", key.Id, userId, isRunning ? "stopped" : "started");

        return ServiceResult.Ok(new PunchResponse
        {
            Action = isRunning ? "Stopped" : "Started",
            FirstName = membership.User.FirstName,
            LastName = membership.User.LastName,
            Entry = result.Data!
        });
    }

    private static string Hash(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
