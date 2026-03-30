using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OtpNet;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

public class TotpService : ITotpService
{
    private readonly JwtSettings _jwtSettings;

    public TotpService(IOptions<JwtSettings> jwtSettings)
    {
        _jwtSettings = jwtSettings.Value;
    }

    public (string Secret, string SharedKey, string AuthenticatorUri) GenerateSetup(string email)
    {
        var secret = KeyGeneration.GenerateRandomKey(20);
        var base32Secret = Base32Encoding.ToString(secret);

        var uri = new OtpUri(OtpType.Totp, secret, email, "TimeTracking")
            .ToString();

        return (base32Secret, base32Secret, uri);
    }

    public bool VerifyCode(string base32Secret, string code)
    {
        var secretBytes = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(secretBytes);
        return totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
    }

    public List<string> GenerateBackupCodes(int count = 8)
    {
        var codes = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            // Generate 8-character alphanumeric codes in format XXXX-XXXX
            var bytes = RandomNumberGenerator.GetBytes(5);
            var raw = Convert.ToHexString(bytes).ToLowerInvariant()[..8];
            codes.Add($"{raw[..4]}-{raw[4..]}");
        }
        return codes;
    }

    public string GenerateTwoFactorToken(int userId)
    {
        // Create a short-lived HMAC-based token: userId|timestamp|hmac
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = $"{userId}|{timestamp}";
        var key = Encoding.UTF8.GetBytes(_jwtSettings.Secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hmac = HMACSHA256.HashData(key, payloadBytes);
        var sig = Convert.ToBase64String(hmac);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{payload}|{sig}"));
    }

    public int? ValidateTwoFactorToken(string token)
    {
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split('|');
            if (parts.Length != 3) return null;

            var userId = int.Parse(parts[0]);
            var timestamp = long.Parse(parts[1]);
            var sig = parts[2];

            // Token expires after 5 minutes
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now - timestamp > 300) return null;

            // Verify HMAC
            var payload = $"{userId}|{timestamp}";
            var key = Encoding.UTF8.GetBytes(_jwtSettings.Secret);
            var expectedHmac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));
            var expectedSig = Convert.ToBase64String(expectedHmac);

            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(sig),
                    Encoding.UTF8.GetBytes(expectedSig)))
            {
                return null;
            }

            return userId;
        }
        catch
        {
            return null;
        }
    }
}
