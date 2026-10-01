namespace TimeTracking.Api.Services;

public interface ITotpService
{
    /// <summary>Generate a new TOTP secret and return setup info (shared key + URI).</summary>
    (string Secret, string SharedKey, string AuthenticatorUri) GenerateSetup(string email);

    /// <summary>
    /// Validate a 6-digit TOTP code against the given base32 secret. A code whose time step is
    /// not newer than <paramref name="lastUsedStep"/> is rejected (replay protection); the
    /// matched step is returned so the caller can store it.
    /// </summary>
    bool VerifyCode(string base32Secret, string code, long? lastUsedStep, out long matchedStep);

    /// <summary>Generate a set of one-time backup codes.</summary>
    List<string> GenerateBackupCodes(int count = 8);

    /// <summary>Generate a short-lived token for 2FA verification during login.</summary>
    string GenerateTwoFactorToken(int userId);

    /// <summary>Validate and extract user ID from a 2FA token.</summary>
    (int UserId, DateTime IssuedAt)? ValidateTwoFactorToken(string token);
}
