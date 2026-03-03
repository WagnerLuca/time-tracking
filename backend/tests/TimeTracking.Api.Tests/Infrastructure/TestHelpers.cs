using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TimeTracking.Api.Tests.Infrastructure;

/// <summary>
/// Helper methods for common test operations: registration, login, auth headers, etc.
/// </summary>
public static class TestHelpers
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // Seed user credentials
    public const string SeedOwnerEmail = "max.mueller@example.com";
    public const string SeedAdminEmail = "anna.schmidt@example.com";
    public const string SeedMemberEmail = "tom.weber@example.com";
    public const string SeedPassword = "Password123";
    public const string SeedOrgSlug = "mueller-software";

    /// <summary>Register a new user and return the login response.</summary>
    public static async Task<LoginResponseDto> RegisterAsync(
        HttpClient client, string email, string password, string firstName, string lastName)
    {
        var response = await client.PostAsJsonAsync("/api/Auth/register", new
        {
            email,
            password,
            firstName,
            lastName
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponseDto>(JsonOptions))!;
    }

    /// <summary>Login with credentials and return the login response.</summary>
    public static async Task<LoginResponseDto> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            email,
            password
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponseDto>(JsonOptions))!;
    }

    /// <summary>Login and set the Authorization header on the client.</summary>
    public static async Task<LoginResponseDto> AuthenticateAsync(HttpClient client, string email, string password)
    {
        var login = await LoginAsync(client, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return login;
    }

    /// <summary>Set a bearer token on the client.</summary>
    public static void SetBearerToken(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>Clear auth headers from the client.</summary>
    public static void ClearAuth(HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = null;
    }

    /// <summary>Create a new organization and return its slug.</summary>
    public static async Task<OrganizationResponseDto> CreateOrganizationAsync(
        HttpClient client, string name, string slug, string? description = null)
    {
        var response = await client.PostAsJsonAsync("/api/Organizations", new
        {
            name,
            slug,
            description
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrganizationResponseDto>(JsonOptions))!;
    }

    /// <summary>Register a new user, log them in, and return the client with auth set.</summary>
    public static async Task<(HttpClient Client, LoginResponseDto Login)> CreateAuthenticatedUserAsync(
        TimeTrackingApiFactory factory, string email, string firstName = "Test", string lastName = "User")
    {
        var client = factory.CreateClient();
        var login = await RegisterAsync(client, email, "Password123!", firstName, lastName);
        SetBearerToken(client, login.AccessToken);
        return (client, login);
    }
}

// Lightweight DTOs used only by tests to deserialize API responses
public record LoginResponseDto
{
    public string AccessToken { get; init; } = "";
    public string RefreshToken { get; init; } = "";
    public DateTime ExpiresAt { get; init; }
    public UserInfoDto User { get; init; } = null!;
}

public record UserInfoDto
{
    public int Id { get; init; }
    public string Email { get; init; } = "";
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string? ProfileImageUrl { get; init; }
    public bool EmailConfirmed { get; init; }
}

public record RefreshTokenResponseDto
{
    public string AccessToken { get; init; } = "";
    public string RefreshToken { get; init; } = "";
    public DateTime ExpiresAt { get; init; }
}

public record AuthResponseDto
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
}

public record OrganizationResponseDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public string Slug { get; init; } = "";
    public string? Website { get; init; }
    public string? LogoUrl { get; init; }
    public DateTime CreatedAt { get; init; }
    public int MemberCount { get; init; }
    public string JoinPolicy { get; init; } = "";
}

public record OrganizationDetailResponseDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public string Slug { get; init; } = "";
    public string? Website { get; init; }
    public string? LogoUrl { get; init; }
    public bool AutoPauseEnabled { get; init; }
    public string EditPastEntriesMode { get; init; } = "";
    public string EditPauseMode { get; init; } = "";
    public string InitialOvertimeMode { get; init; } = "";
    public string JoinPolicy { get; init; } = "";
    public string WorkScheduleChangeMode { get; init; } = "";
    public bool MemberTimeEntryVisibility { get; init; }
    public DateTime? SettingsUpdatedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public List<OrganizationMemberResponseDto> Members { get; init; } = [];
    public List<PauseRuleResponseDto>? PauseRules { get; init; }
}

public record OrganizationMemberResponseDto
{
    public int Id { get; init; }
    public string Email { get; init; } = "";
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string? ProfileImageUrl { get; init; }
    public string Role { get; init; } = "";
    public DateTime JoinedAt { get; init; }
    public double InitialOvertimeHours { get; init; }
}

public record UserOrganizationResponseDto
{
    public int OrganizationId { get; init; }
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public string Slug { get; init; } = "";
    public string Role { get; init; } = "";
    public DateTime JoinedAt { get; init; }
    public int MemberCount { get; init; }
}

public record TimeEntryResponseDto
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public int? OrganizationId { get; init; }
    public string? OrganizationName { get; init; }
    public string? Description { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public bool IsRunning { get; init; }
    public double? DurationMinutes { get; init; }
    public int PauseDurationMinutes { get; init; }
    public double? NetDurationMinutes { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record OrgRequestResponseDto
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public string UserFirstName { get; init; } = "";
    public string UserLastName { get; init; } = "";
    public string UserEmail { get; init; } = "";
    public int OrganizationId { get; init; }
    public string OrganizationName { get; init; } = "";
    public string OrganizationSlug { get; init; } = "";
    public string Type { get; init; } = "";
    public string Status { get; init; } = "";
    public string? Message { get; init; }
    public int? RelatedEntityId { get; init; }
    public string? RequestData { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? RespondedAt { get; init; }
    public string? RespondedByName { get; init; }
}

public record AdminNotificationResponseDto
{
    public int PendingRequests { get; init; }
    public List<OrgRequestResponseDto> Requests { get; init; } = [];
}

public record UserNotificationResponseDto
{
    public int Count { get; init; }
    public List<OrgRequestResponseDto> Requests { get; init; } = [];
}

public record WorkScheduleResponseDto
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public int OrganizationId { get; init; }
    public string ValidFrom { get; init; } = "";
    public string? ValidTo { get; init; }
    public double? WeeklyWorkHours { get; init; }
    public double TargetMon { get; init; }
    public double TargetTue { get; init; }
    public double TargetWed { get; init; }
    public double TargetThu { get; init; }
    public double TargetFri { get; init; }
    public double InitialOvertimeHours { get; init; }
    public string InitialOvertimeMode { get; init; } = "";
    public string WorkScheduleChangeMode { get; init; } = "";
}

public record HolidayResponseDto
{
    public int Id { get; init; }
    public int OrganizationId { get; init; }
    public string Date { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsRecurring { get; init; }
}

public record AbsenceDayResponseDto
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public int OrganizationId { get; init; }
    public string Date { get; init; } = "";
    public string Type { get; init; } = "";
    public string? Note { get; init; }
    public string? UserFirstName { get; init; }
    public string? UserLastName { get; init; }
}

public record PauseRuleResponseDto
{
    public int Id { get; init; }
    public int OrganizationId { get; init; }
    public double MinHours { get; init; }
    public int PauseMinutes { get; init; }
}

public record NotificationResponseDto
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public int? OrganizationId { get; init; }
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string Type { get; init; } = "";
    public bool IsRead { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record MemberTimeOverviewResponseDto
{
    public int UserId { get; init; }
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Email { get; init; } = "";
    public string Role { get; init; } = "";
    public double? WeeklyWorkHours { get; init; }
    public double TotalTrackedMinutes { get; init; }
    public double NetTrackedMinutes { get; init; }
    public int EntryCount { get; init; }
}

/// <summary>Generic paginated response wrapper used by list endpoints.</summary>
public record PaginatedResponseDto<T>
{
    public List<T> Items { get; init; } = new();
    public int TotalCount { get; init; }
    public int Limit { get; init; }
    public int Offset { get; init; }
    public bool HasMore { get; init; }
}
