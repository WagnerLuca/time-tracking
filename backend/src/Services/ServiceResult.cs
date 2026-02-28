namespace TimeTracking.Api.Services;

/// <summary>
/// Categorizes service errors so controllers can map them to HTTP status codes.
/// </summary>
public enum ServiceErrorType
{
    NotFound,
    BadRequest,
    Forbidden,
    Unauthorized,
    Conflict
}

/// <summary>
/// Standard result type for service-to-controller communication.
/// Decouples business logic from HTTP concerns.
/// </summary>
public class ServiceResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public ServiceErrorType? ErrorType { get; init; }

    // ── Factory methods ──

    public static ServiceResult Ok() => new() { IsSuccess = true };

    public static ServiceResult Fail(ServiceErrorType type, string message) =>
        new() { IsSuccess = false, ErrorType = type, ErrorMessage = message };

    public static ServiceResult<T> Ok<T>(T data) =>
        new() { IsSuccess = true, Data = data };

    public static ServiceResult<T> Fail<T>(ServiceErrorType type, string message) =>
        new() { IsSuccess = false, ErrorType = type, ErrorMessage = message };

    // ── Convenience shortcuts ──

    public static ServiceResult NotFound(string message) => Fail(ServiceErrorType.NotFound, message);
    public static ServiceResult BadRequest(string message) => Fail(ServiceErrorType.BadRequest, message);
    public static ServiceResult Forbidden(string message = "Forbidden") => Fail(ServiceErrorType.Forbidden, message);
    public static ServiceResult Unauthorized(string message = "Unauthorized") => Fail(ServiceErrorType.Unauthorized, message);
    public static ServiceResult Conflict(string message) => Fail(ServiceErrorType.Conflict, message);

    public static ServiceResult<T> NotFound<T>(string message) => Fail<T>(ServiceErrorType.NotFound, message);
    public static ServiceResult<T> BadRequest<T>(string message) => Fail<T>(ServiceErrorType.BadRequest, message);
    public static ServiceResult<T> Forbidden<T>(string message = "Forbidden") => Fail<T>(ServiceErrorType.Forbidden, message);
    public static ServiceResult<T> Conflict<T>(string message) => Fail<T>(ServiceErrorType.Conflict, message);
}

/// <summary>
/// Typed result carrying data on success.
/// </summary>
public class ServiceResult<T> : ServiceResult
{
    public T? Data { get; init; }
}
