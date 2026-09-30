using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

public class TokenService : ITokenService
{
    private readonly JwtSettings _jwtSettings;
    private readonly TimeTrackingDbContext _context;
    private readonly ILogger<TokenService> _logger;

    public TokenService(
        IOptions<JwtSettings> jwtSettings,
        TimeTrackingDbContext context,
        ILogger<TokenService> logger)
    {
        _jwtSettings = jwtSettings.Value;
        _context = context;
        _logger = logger;
    }

    public string GenerateAccessToken(User user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
            new("firstName", user.FirstName),
            new("lastName", user.LastName)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpiryMinutes);

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: expiry,
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Lower-case hex SHA-256; refresh tokens are only ever stored and looked up by this hash.</summary>
    public static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public (RefreshToken Entity, string Token) GenerateRefreshToken(int userId)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        return (new RefreshToken
        {
            UserId = userId,
            Token = HashToken(token),
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays),
            CreatedAt = DateTime.UtcNow,
            IsRevoked = false
        }, token);
    }

    public async Task<RefreshToken?> ValidateRefreshTokenAsync(string token)
    {
        var hash = HashToken(token);
        var refreshToken = await _context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == hash);

        if (refreshToken == null)
            return null;

        if (refreshToken.IsRevoked)
        {
            // A rotated-away token came back: it was stolen or replayed. Revoke the user's
            // whole session family so the attacker's copy dies with the victim's.
            var active = await _context.RefreshTokens
                .Where(rt => rt.UserId == refreshToken.UserId && !rt.IsRevoked)
                .ToListAsync();
            foreach (var t in active) { t.IsRevoked = true; t.RevokedAt = DateTime.UtcNow; }
            await _context.SaveChangesAsync();
            _logger.LogWarning("Reuse of a revoked refresh token for user {UserId}; revoked {Count} active session(s)", refreshToken.UserId, active.Count);
            return null;
        }

        if (refreshToken.ExpiresAt < DateTime.UtcNow || !refreshToken.User.IsActive)
            return null;

        return refreshToken;
    }

    public async Task RevokeRefreshTokenAsync(string token)
    {
        var hash = HashToken(token);
        var refreshToken = await _context.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == hash);
        if (refreshToken == null || refreshToken.IsRevoked) return;

        refreshToken.IsRevoked = true;
        refreshToken.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        _logger.LogInformation("Refresh token {TokenId} of user {UserId} revoked", refreshToken.Id, refreshToken.UserId);
    }
}
