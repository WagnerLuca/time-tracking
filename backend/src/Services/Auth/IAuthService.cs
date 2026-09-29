using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

public interface IAuthService
{
    Task<ServiceResult<LoginResponse>> RegisterAsync(RegisterRequest request);
    /// <summary>Data is a <see cref="LoginResponse"/>, or a <see cref="TwoFactorRequiredResponse"/> when 2FA is enabled.</summary>
    Task<ServiceResult<object>> LoginAsync(LoginRequest request);
    Task<ServiceResult<RefreshTokenResponse>> RefreshTokenAsync(string refreshToken);
    Task<ServiceResult<AuthResponse>> LogoutAsync(int userId, string refreshToken);
    Task<ServiceResult<AuthResponse>> ChangePasswordAsync(int userId, ChangePasswordRequest request);
    Task<ServiceResult<AuthResponse>> DeleteAccountAsync(int userId);
    Task<ServiceResult<UserInfo>> GetCurrentUserAsync(int userId);
    Task<ServiceResult<UserInfo>> UpdateProfileAsync(int userId, UpdateProfileRequest request);

    // Two-Factor Authentication
    Task<ServiceResult<LoginResponse>> VerifyTwoFactorAsync(TwoFactorVerifyRequest request);
    Task<ServiceResult<TwoFactorSetupResponse>> SetupTwoFactorAsync(int userId);
    Task<ServiceResult<TwoFactorConfirmResponse>> ConfirmTwoFactorAsync(int userId, TwoFactorConfirmRequest request);
    Task<ServiceResult<AuthResponse>> DisableTwoFactorAsync(int userId, string currentPassword);
}
