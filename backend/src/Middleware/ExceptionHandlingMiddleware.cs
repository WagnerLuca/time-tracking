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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title, detail) = exception switch
        {
            ArgumentException        => (StatusCodes.Status400BadRequest,            "Bad Request",            exception.Message),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized,       "Unauthorized",           "Authentication is required to access this resource."),
            KeyNotFoundException     => (StatusCodes.Status404NotFound,              "Not Found",              "The requested resource was not found."),
            InvalidOperationException => (StatusCodes.Status409Conflict,             "Conflict",               exception.Message),
            _                        => (StatusCodes.Status500InternalServerError,   "Internal Server Error",  "An unexpected error occurred.")
        };

        var problem = new ProblemDetails
        {
            Type = $"https://httpstatuses.io/{statusCode}",
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = context.Request.Path
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    }
}
