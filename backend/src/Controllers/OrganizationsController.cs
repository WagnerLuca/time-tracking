using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;
using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrganizationsController : ControllerBase
{
    private readonly TimeTrackingDbContext _context;

    public OrganizationsController(TimeTrackingDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Get all organizations
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrganizationResponse>>> GetOrganizations()
    {
        var organizations = await _context.Organizations
            .Where(o => o.IsActive)
            .Select(o => new OrganizationResponse
            {
                Id = o.Id,
                Name = o.Name,
                Description = o.Description,
                Slug = o.Slug,
                Website = o.Website,
                LogoUrl = o.LogoUrl,
                CreatedAt = o.CreatedAt,
                MemberCount = o.UserOrganizations.Count(uo => uo.IsActive)
            })
            .ToListAsync();

        return Ok(organizations);
    }

    /// <summary>
    /// Get organization by ID with members
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<OrganizationDetailResponse>> GetOrganization(int id)
    {
        var organization = await _context.Organizations
            .Where(o => o.Id == id && o.IsActive)
            .Select(o => new OrganizationDetailResponse
            {
                Id = o.Id,
                Name = o.Name,
                Description = o.Description,
                Slug = o.Slug,
                Website = o.Website,
                LogoUrl = o.LogoUrl,
                CreatedAt = o.CreatedAt,
                Members = o.UserOrganizations
                    .Where(uo => uo.IsActive)
                    .Select(uo => new OrganizationMemberResponse
                    {
                        Id = uo.User.Id,
                        Email = uo.User.Email,
                        FirstName = uo.User.FirstName,
                        LastName = uo.User.LastName,
                        ProfileImageUrl = uo.User.ProfileImageUrl,
                        Role = uo.Role.ToString(),
                        JoinedAt = uo.JoinedAt
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        if (organization == null)
        {
            return NotFound(new { message = "Organization not found" });
        }

        return Ok(organization);
    }

    /// <summary>
    /// Get user's organizations
    /// </summary>
    [HttpGet("user/{userId}")]
    public async Task<ActionResult<IEnumerable<UserOrganizationResponse>>> GetUserOrganizations(int userId)
    {
        var userExists = await _context.Users.AnyAsync(u => u.Id == userId && u.IsActive);
        if (!userExists)
        {
            return NotFound(new { message = "User not found" });
        }

        var organizations = await _context.UserOrganizations
            .Where(uo => uo.UserId == userId && uo.IsActive)
            .Select(uo => new UserOrganizationResponse
            {
                OrganizationId = uo.Organization.Id,
                Name = uo.Organization.Name,
                Description = uo.Organization.Description,
                Slug = uo.Organization.Slug,
                Role = uo.Role.ToString(),
                JoinedAt = uo.JoinedAt,
                MemberCount = uo.Organization.UserOrganizations.Count(x => x.IsActive)
            })
            .ToListAsync();

        return Ok(organizations);
    }
}
