using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TimeTracking.Api.Tests.Infrastructure;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// Integration tests for the Auth controller covering registration, login,
/// token refresh, password change, logout, account deletion, and error cases.
/// </summary>
public class AuthControllerTests : IClassFixture<TimeTrackingApiFactory>
{
    private readonly TimeTrackingApiFactory _factory;

    public AuthControllerTests(TimeTrackingApiFactory factory) => _factory = factory;

    // ── Registration ─────────────────────────────────────────────────────

    [Fact]
    public async Task Register_WithValidData_ReturnsTokensAndUserInfo()
    {
        var client = _factory.CreateClient();
        var login = await TestHelpers.RegisterAsync(
            client, "newuser@test.com", "StrongPass1!", "John", "Doe");

        login.AccessToken.Should().NotBeNullOrWhiteSpace();
        login.RefreshToken.Should().NotBeNullOrWhiteSpace();
        login.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        login.User.Email.Should().Be("newuser@test.com");
        login.User.FirstName.Should().Be("John");
        login.User.LastName.Should().Be("Doe");
        login.User.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns400()
    {
        var client = _factory.CreateClient();
        // First registration
        await TestHelpers.RegisterAsync(client, "dup@test.com", "StrongPass1!", "A", "B");
        // Second with same email
        var response = await client.PostAsJsonAsync("/api/Auth/register", new
        {
            email = "dup@test.com",
            password = "StrongPass1!",
            firstName = "C",
            lastName = "D"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_WeakPassword_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/Auth/register", new
        {
            email = "weakpw@test.com",
            password = "short",
            firstName = "A",
            lastName = "B"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("", "Pass1234!", "A", "B")]           // empty email
    [InlineData("not-an-email", "Pass1234!", "A", "B")] // invalid email
    [InlineData("ok@test.com", "Pass1234!", "", "B")]   // empty first name
    public async Task Register_InvalidInput_Returns400(string email, string password, string first, string last)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/Auth/register", new
        {
            email,
            password,
            firstName = first,
            lastName = last
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Login ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_SeedUser_ReturnsTokens()
    {
        var client = _factory.CreateClient();
        var login = await TestHelpers.LoginAsync(
            client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        login.AccessToken.Should().NotBeNullOrWhiteSpace();
        login.RefreshToken.Should().NotBeNullOrWhiteSpace();
        login.User.Email.Should().Be(TestHelpers.SeedOwnerEmail);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            email = TestHelpers.SeedOwnerEmail,
            password = "WrongPassword!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_NonExistentUser_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            email = "nobody@test.com",
            password = "Password123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Token refresh ────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshToken_ValidToken_ReturnsNewTokens()
    {
        var client = _factory.CreateClient();
        var login = await TestHelpers.LoginAsync(
            client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.PostAsJsonAsync("/api/Auth/refresh", new
        {
            refreshToken = login.RefreshToken
        });
        response.EnsureSuccessStatusCode();

        var refreshed = await response.Content.ReadFromJsonAsync<RefreshTokenResponseDto>(TestHelpers.JsonOptions);
        refreshed.Should().NotBeNull();
        refreshed!.AccessToken.Should().NotBeNullOrWhiteSpace();
        refreshed.RefreshToken.Should().NotBeNullOrWhiteSpace();
        // New token should be different (rotation)
        refreshed.RefreshToken.Should().NotBe(login.RefreshToken);
    }

    [Fact]
    public async Task RefreshToken_InvalidToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/Auth/refresh", new
        {
            refreshToken = "invalid-token-string"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshToken_UsedTokenCannotBeReused()
    {
        var client = _factory.CreateClient();
        var login = await TestHelpers.LoginAsync(
            client, TestHelpers.SeedAdminEmail, TestHelpers.SeedPassword);

        // First refresh succeeds
        var first = await client.PostAsJsonAsync("/api/Auth/refresh", new { refreshToken = login.RefreshToken });
        first.EnsureSuccessStatusCode();

        // Reusing the same old token should fail
        var second = await client.PostAsJsonAsync("/api/Auth/refresh", new { refreshToken = login.RefreshToken });
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Get current user ─────────────────────────────────────────────────

    [Fact]
    public async Task GetMe_Authenticated_ReturnsUserInfo()
    {
        var client = _factory.CreateClient();
        var login = await TestHelpers.AuthenticateAsync(
            client, TestHelpers.SeedOwnerEmail, TestHelpers.SeedPassword);

        var response = await client.GetAsync("/api/Auth/me");
        response.EnsureSuccessStatusCode();

        var user = await response.Content.ReadFromJsonAsync<UserInfoDto>(TestHelpers.JsonOptions);
        user.Should().NotBeNull();
        user!.Email.Should().Be(TestHelpers.SeedOwnerEmail);
        user.Id.Should().Be(login.User.Id);
    }

    [Fact]
    public async Task GetMe_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/Auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Change password ──────────────────────────────────────────────────

    [Fact]
    public async Task ChangePassword_ValidOldPassword_Succeeds()
    {
        var client = _factory.CreateClient();
        // Register a fresh user so we don't break other tests
        await TestHelpers.RegisterAsync(client, "chpw@test.com", "OldPass123!", "A", "B");
        await TestHelpers.AuthenticateAsync(client, "chpw@test.com", "OldPass123!");

        var response = await client.PostAsJsonAsync("/api/Auth/change-password", new
        {
            currentPassword = "OldPass123!",
            newPassword = "NewPass456!"
        });
        response.EnsureSuccessStatusCode();

        // Can login with new password
        TestHelpers.ClearAuth(client);
        var login = await TestHelpers.LoginAsync(client, "chpw@test.com", "NewPass456!");
        login.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_Returns400()
    {
        var client = _factory.CreateClient();
        await TestHelpers.RegisterAsync(client, "chpw2@test.com", "OldPass123!", "A", "B");
        await TestHelpers.AuthenticateAsync(client, "chpw2@test.com", "OldPass123!");

        var response = await client.PostAsJsonAsync("/api/Auth/change-password", new
        {
            currentPassword = "WrongPassword!",
            newPassword = "NewPass456!"
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Logout ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var client = _factory.CreateClient();
        var login = await TestHelpers.LoginAsync(
            client, TestHelpers.SeedMemberEmail, TestHelpers.SeedPassword);
        TestHelpers.SetBearerToken(client, login.AccessToken);

        var logoutResponse = await client.PostAsJsonAsync("/api/Auth/logout", new
        {
            refreshToken = login.RefreshToken
        });
        logoutResponse.EnsureSuccessStatusCode();

        // The refresh token should no longer work
        var refreshResponse = await client.PostAsJsonAsync("/api/Auth/refresh", new
        {
            refreshToken = login.RefreshToken
        });
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Delete account ───────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAccount_RemovesUserAndRevokesAccess()
    {
        var client = _factory.CreateClient();
        await TestHelpers.RegisterAsync(client, "deleteme@test.com", "Password123!", "Del", "Ete");
        await TestHelpers.AuthenticateAsync(client, "deleteme@test.com", "Password123!");

        var response = await client.DeleteAsync("/api/Auth/account");
        response.EnsureSuccessStatusCode();

        // Can no longer login
        TestHelpers.ClearAuth(client);
        var loginResponse = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            email = "deleteme@test.com",
            password = "Password123!"
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Health endpoint ──────────────────────────────────────────────────

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }
}
