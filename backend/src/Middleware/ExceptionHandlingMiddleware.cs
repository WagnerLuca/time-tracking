using System.Net;
using Microsoft.AspNetCore.Mvc;

namespace TimeTracking.Api.Middleware;

/// <summary>
/// Global exception handling middleware that returns RFC 7807 Problem Details
/// responses for unhandled exceptions. Prevents stack-trace leaks and provides
/// a machine-readable, standardised error format.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Services.PeriodClosedException ex)
        {
            // A refused write into a closed month (ClosedPeriods): an expected answer, not a fault
            _logger.LogInformation("Refused {Method} {Path}: {Message}", context.Request.Method, context.Request.Path, ex.Message);
            var problem = new ProblemDetails
            {
                Type = "https://httpstatuses.io/409",
                Title = "Conflict",
                Status = StatusCodes.Status409Conflict,
                Detail = ex.Message,
                Instance = context.Request.Path,
                Extensions = { ["code"] = ex.Code, ["month"] = ex.Month.ToString("yyyy-MM") }
            };
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title, detail, code) = exception switch
        {
            ArgumentException        => (StatusCodes.Status400BadRequest,            "Bad Request",            "The request is invalid.", "common.badRequest"),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized,       "Unauthorized",           "Authentication is required to access this resource.", "common.unauthorized"),
            KeyNotFoundException     => (StatusCodes.Status404NotFound,              "Not Found",              "The requested resource was not found.", "common.notFound"),
            InvalidOperationException => (StatusCodes.Status409Conflict,             "Conflict",               "The request conflicts with the current state.", "common.conflict"),
            _                        => (StatusCodes.Status500InternalServerError,   "Internal Server Error",  "An unexpected error occurred.", "common.unexpected")
        };

        var problem = new ProblemDetails
        {
            Type = $"https://httpstatuses.io/{statusCode}",
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = context.Request.Path,
            Extensions = { ["code"] = code }
        };

        context.Response.StatusCode = statusCode;
        // Pass the content type to WriteAsJsonAsync: setting Response.ContentType first gets overwritten by it
        await context.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
    }
}
