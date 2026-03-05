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
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

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
        if (user.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out var userId))
            {
                var cacheKey = $"user_active:{userId}";
                var isActive = await _cache.GetOrCreateAsync(cacheKey, async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                    return await _context.Users
                        .AsNoTracking()
                        .Where(u => u.Id == userId)
                        .Select(u => (bool?)u.IsActive)
                        .FirstOrDefaultAsync();
                });

                if (isActive != true)
                {
                    context.Result = new UnauthorizedObjectResult(new ProblemDetails
                    {
                        Type = "https://httpstatuses.io/401",
                        Title = "Unauthorized",
                        Status = StatusCodes.Status401Unauthorized,
                        Detail = "Your account has been deactivated."
                    });
                    return;
                }
            }
        }

        await next();
    }
}
