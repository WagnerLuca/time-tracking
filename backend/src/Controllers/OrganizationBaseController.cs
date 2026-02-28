using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TimeTracking.Api.Services;

namespace TimeTracking.Api.Controllers;

/// <summary>
/// Base controller with shared helpers for organization-scoped controllers.
/// Provides user-id extraction and ServiceResult → HTTP response mapping.
/// </summary>
public abstract class OrganizationBaseController : ControllerBase
{
    /// <summary>Extracts the current user's ID from JWT claims.</summary>
    protected int? GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null && int.TryParse(claim.Value, out var id) ? id : null;
    }

    // ────────────────────────────────────────────────────
    //  ServiceResult → HTTP response mapping
    // ────────────────────────────────────────────────────

    /// <summary>Maps a data-less ServiceResult to an HTTP response (204 on success).</summary>
    protected IActionResult ToResponse(ServiceResult result)
    {
        if (result.IsSuccess) return NoContent();
        return MapError(result);
    }

    /// <summary>Maps a typed ServiceResult to an HTTP response (200 with data on success).</summary>
    protected IActionResult ToResponse<T>(ServiceResult<T> result)
    {
        if (result.IsSuccess) return Ok(result.Data);
        return MapError(result);
    }

    /// <summary>Maps a typed ServiceResult to a 201 Created response on success.</summary>
    protected IActionResult ToCreatedResponse<T>(ServiceResult<T> result, string? location = null)
    {
        if (result.IsSuccess)
            return location != null ? Created(location, result.Data) : StatusCode(201, result.Data);
        return MapError(result);
    }

    private IActionResult MapError(ServiceResult result)
    {
        return result.ErrorType switch
        {
            ServiceErrorType.NotFound     => NotFound(new { message = result.ErrorMessage }),
            ServiceErrorType.BadRequest   => BadRequest(new { message = result.ErrorMessage }),
            ServiceErrorType.Forbidden    => Forbid(),
            ServiceErrorType.Unauthorized => Unauthorized(new { message = result.ErrorMessage }),
            ServiceErrorType.Conflict     => Conflict(new { message = result.ErrorMessage }),
            _ => StatusCode(500)
        };
    }
}
