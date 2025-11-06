using System.Security.Claims;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    RefreshToken GenerateRefreshToken(int userId);
    ClaimsPrincipal? ValidateToken(string token);
    Task<RefreshToken?> ValidateRefreshTokenAsync(string token);
    Task RevokeRefreshTokenAsync(string token, string? replacedByToken = null);
}
