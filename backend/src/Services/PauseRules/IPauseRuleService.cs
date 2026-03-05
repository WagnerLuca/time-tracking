using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Handles pause rule CRUD for organizations.
/// </summary>
public interface IPauseRuleService
{
    Task<ServiceResult<List<PauseRuleResponse>>> GetPauseRulesAsync(string slug, int callerUserId);
    Task<ServiceResult<PauseRuleResponse>> CreatePauseRuleAsync(string slug, int callerUserId, CreatePauseRuleRequest request);
    Task<ServiceResult<PauseRuleResponse>> UpdatePauseRuleAsync(string slug, int callerUserId, int ruleId, UpdatePauseRuleRequest request);
    Task<ServiceResult> DeletePauseRuleAsync(string slug, int callerUserId, int ruleId);
}
