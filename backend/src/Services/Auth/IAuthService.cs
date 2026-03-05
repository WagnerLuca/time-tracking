using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

public interface IAuthService
{
    Task<(bool Success, string Message, LoginResponse? Response)> RegisterAsync(RegisterRequest request);
    Task<(bool Success, string Message, LoginResponse? Response)> LoginAsync(LoginRequest request);
    Task<(bool Success, string Message, RefreshTokenResponse? Response)> RefreshTokenAsync(string refreshToken);
    Task<(bool Success, string Message)> ChangePasswordAsync(int userId, ChangePasswordRequest request);
    Task<(bool Success, string Message)> RevokeRefreshTokenAsync(string refreshToken);
    Task<(bool Success, string Message)> DeleteAccountAsync(int userId);
    Task<(bool Success, string Message, UserInfo? User)> UpdateProfileAsync(int userId, UpdateProfileRequest request);
}
