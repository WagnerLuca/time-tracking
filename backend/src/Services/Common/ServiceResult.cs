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
    Conflict,
    TooManyRequests
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
    /// <summary>
    /// Stable, language-neutral error code (dotted lowerCamel, e.g. <c>auth.invalidCredentials</c>)
    /// the frontend translates. Sent as the ProblemDetails extension <c>code</c>. When a call site
    /// passes none, it is looked up from the message in <see cref="ErrorCodes"/>.
    /// </summary>
    public string? Code { get; init; }

    // ── Factory methods ──

    public static ServiceResult Ok() => new() { IsSuccess = true };

    public static ServiceResult Fail(ServiceErrorType type, string message, string? code = null) =>
        new() { IsSuccess = false, ErrorType = type, ErrorMessage = message, Code = code ?? ErrorCodes.For(message) };

    public static ServiceResult<T> Ok<T>(T data) =>
        new() { IsSuccess = true, Data = data };

    public static ServiceResult<T> Fail<T>(ServiceErrorType type, string message, string? code = null) =>
        new() { IsSuccess = false, ErrorType = type, ErrorMessage = message, Code = code ?? ErrorCodes.For(message) };

    // ── Convenience shortcuts ──

    public static ServiceResult NotFound(string message, string? code = null) => Fail(ServiceErrorType.NotFound, message, code);
    public static ServiceResult BadRequest(string message, string? code = null) => Fail(ServiceErrorType.BadRequest, message, code);
    public static ServiceResult Forbidden(string message = "Forbidden", string? code = null) => Fail(ServiceErrorType.Forbidden, message, code);
    public static ServiceResult Unauthorized(string message = "Unauthorized", string? code = null) => Fail(ServiceErrorType.Unauthorized, message, code);
    public static ServiceResult Conflict(string message, string? code = null) => Fail(ServiceErrorType.Conflict, message, code);

    public static ServiceResult<T> NotFound<T>(string message, string? code = null) => Fail<T>(ServiceErrorType.NotFound, message, code);
    public static ServiceResult<T> BadRequest<T>(string message, string? code = null) => Fail<T>(ServiceErrorType.BadRequest, message, code);
    public static ServiceResult<T> Forbidden<T>(string message = "Forbidden", string? code = null) => Fail<T>(ServiceErrorType.Forbidden, message, code);
    public static ServiceResult<T> Conflict<T>(string message, string? code = null) => Fail<T>(ServiceErrorType.Conflict, message, code);
    public static ServiceResult<T> Unauthorized<T>(string message = "Unauthorized", string? code = null) => Fail<T>(ServiceErrorType.Unauthorized, message, code);
    public static ServiceResult<T> TooManyRequests<T>(string message, string? code = null) => Fail<T>(ServiceErrorType.TooManyRequests, message, code);
}

/// <summary>
/// Typed result carrying data on success.
/// </summary>
public class ServiceResult<T> : ServiceResult
{
    public T? Data { get; init; }
}
