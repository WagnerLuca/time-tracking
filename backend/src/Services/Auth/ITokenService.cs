using System.Security.Claims;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    /// <summary>New refresh token: the entity stores only the hash, <c>Token</c> is the plaintext for the client.</summary>
    (RefreshToken Entity, string Token) GenerateRefreshToken(int userId);
    Task<RefreshToken?> ValidateRefreshTokenAsync(string token);
    Task RevokeRefreshTokenAsync(string token);
}
