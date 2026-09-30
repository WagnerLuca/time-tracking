using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using TimeTracking.Api.Middleware;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>Unhandled exceptions become RFC 7807 problem details without leaking internals.</summary>
public class ExceptionHandlingMiddlewareTests
{
    private static async Task<(int Status, JsonElement Problem, string ContentType)> RunAsync(Exception ex)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/something";
        context.Response.Body = new MemoryStream();

        var middleware = new ExceptionHandlingMiddleware(_ => throw ex, NullLogger<ExceptionHandlingMiddleware>.Instance);
        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var problem = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);
        return (context.Response.StatusCode, problem, context.Response.ContentType!);
    }

    [Theory]
    [InlineData(typeof(ArgumentException), 400)]
    [InlineData(typeof(UnauthorizedAccessException), 401)]
    [InlineData(typeof(KeyNotFoundException), 404)]
    [InlineData(typeof(InvalidOperationException), 409)]
    [InlineData(typeof(NullReferenceException), 500)]
    public async Task MapsExceptionTypesToStatusCodes(Type exceptionType, int expected)
    {
        var (status, problem, contentType) = await RunAsync((Exception)Activator.CreateInstance(exceptionType, "boom")!);

        status.Should().Be(expected);
        contentType.Should().Be("application/problem+json");
        problem.GetProperty("status").GetInt32().Should().Be(expected);
        problem.GetProperty("instance").GetString().Should().Be("/api/v1/something");
    }

    [Fact]
    public async Task ServerErrors_DoNotExposeTheExceptionMessage()
    {
        var (_, problem, _) = await RunAsync(new Exception("connection string Password=secret"));
        problem.GetProperty("detail").GetString().Should().Be("An unexpected error occurred.");
    }

    [Fact]
    public async Task PassesThroughWhenNothingThrows()
    {
        var context = new DefaultHttpContext();
        var middleware = new ExceptionHandlingMiddleware(c => { c.Response.StatusCode = 204; return Task.CompletedTask; },
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        context.Response.StatusCode.Should().Be(204);
    }
}
