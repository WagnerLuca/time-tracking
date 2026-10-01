using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TimeTracking.Api.Data;

namespace TimeTracking.Api.Filters;

/// <summary>
/// Global action filter that rejects requests from deactivated users.
/// Runs after JWT validation on every authenticated request and checks
/// that the user's account is still active in the database.
/// Results are cached in-memory for <see cref="CacheDuration"/> to reduce DB load.
/// </summary>
public class ActiveUserFilter : IAsyncActionFilter
{
    /// <summary>How long active-status is cached. Short TTL ensures deactivations take effect quickly.</summary>
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly TimeTrackingDbContext _context;
    private readonly IMemoryCache _cache;

    public ActiveUserFilter(TimeTrackingDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        // Anonymous endpoints (login, refresh, …) must keep working with a stale token still attached
        var anonymous = context.ActionDescriptor.EndpointMetadata.OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any();
        if (user.Identity?.IsAuthenticated == true && !anonymous)
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out var userId))
            {
                var current = await _cache.GetOrCreateAsync(CacheKey(userId), async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                    return await _context.Users
                        .AsNoTracking()
                        .Where(u => u.Id == userId)
                        .Select(u => new SessionState(u.IsActive, u.SecurityStamp))
                        .FirstOrDefaultAsync();
                });

                if (current is not { IsActive: true })
                {
                    context.Result = Unauthorized("Your account has been deactivated.");
                    return;
                }

                // Password changed/reset or account deleted since this token was issued → session over
                if (user.FindFirst(TimeTracking.Api.Services.TokenService.SecurityStampClaim)?.Value != current.SecurityStamp)
                {
                    context.Result = Unauthorized("Your session has ended. Please sign in again.");
                    return;
                }
            }
        }

        await next();
    }

    internal static string CacheKey(int userId) => $"user_session:{userId}";

    private sealed record SessionState(bool IsActive, string SecurityStamp);

    private static UnauthorizedObjectResult Unauthorized(string detail) => new(new ProblemDetails
    {
        Type = "https://httpstatuses.io/401",
        Title = "Unauthorized",
        Status = StatusCodes.Status401Unauthorized,
        Detail = detail
    });
}
