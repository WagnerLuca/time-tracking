namespace TimeTracking.Api.Models.Dtos;

public record GroupResponse(
    int Id,
    int OrganizationId,
    string Name,
    string? Description,
    string? Color,
    DateTime CreatedAt,
    int MemberCount,
    bool IsGroupAdmin
);

public record GroupDetailResponse(
    int Id,
    int OrganizationId,
    string Name,
    string? Description,
    string? Color,
    DateTime CreatedAt,
    IList<GroupMemberResponse> Members
);

public record GroupMemberResponse(
    int UserId,
    string FirstName,
    string LastName,
    string Email,
    string? ProfileImageUrl,
    string Role,
    DateTime JoinedAt
);

public record CreateGroupRequest(
    string Name,
    string? Description,
    string? Color
);

public record UpdateGroupRequest(
    string Name,
    string? Description,
    string? Color
);

public record AddGroupMemberRequest(
    int UserId,
    string Role = "Member"
);

public record UpdateGroupMemberRoleRequest(
    string Role
);
