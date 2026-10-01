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
    Task<ServiceResult<AuthResponse>> DeleteAccountAsync(int userId, string password);
    Task<ServiceResult<UserInfo>> GetCurrentUserAsync(int userId);
    Task<ServiceResult<UserInfo>> UpdateProfileAsync(int userId, UpdateProfileRequest request);

    // Email verification / change and password reset
    Task<ServiceResult<AuthResponse>> VerifyEmailAsync(string token);
    Task<ServiceResult<AuthResponse>> ResendVerificationAsync(int userId);
    Task<ServiceResult<AuthResponse>> ForgotPasswordAsync(string email);
    Task<ServiceResult<AuthResponse>> ResetPasswordAsync(ResetPasswordRequest request);

    // Two-Factor Authentication
    Task<ServiceResult<LoginResponse>> VerifyTwoFactorAsync(TwoFactorVerifyRequest request);
    Task<ServiceResult<TwoFactorSetupResponse>> SetupTwoFactorAsync(int userId);
    Task<ServiceResult<TwoFactorConfirmResponse>> ConfirmTwoFactorAsync(int userId, TwoFactorConfirmRequest request);
    Task<ServiceResult<AuthResponse>> DisableTwoFactorAsync(int userId, string currentPassword, string code);
    Task<ServiceResult<PersonalDataExport>> ExportPersonalDataAsync(int userId);
}
