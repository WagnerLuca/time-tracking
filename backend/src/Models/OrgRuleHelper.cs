using TimeTracking.Api.Services;

namespace TimeTracking.Api.Models;

/// <summary>
/// Centralises organisation rule lookups and enforcement checks.
/// </summary>
public static class OrgRuleHelper
{
    /// <summary>
    /// Maps a <see cref="RequestType"/> to the corresponding <see cref="RuleMode"/>
    /// on the organisation.  Returns <see cref="RuleMode.Allowed"/> for unknown types.
    /// </summary>
    public static RuleMode GetRuleMode(Organization org, RequestType action) => action switch
    {
        RequestType.EditPastEntry      => org.EditPastEntriesMode,
        RequestType.EditPause          => org.EditPauseMode,
        RequestType.SetInitialOvertime => org.InitialOvertimeMode,
        RequestType.WorkScheduleChange => org.WorkScheduleChangeMode,
        RequestType.JoinOrganization   => org.JoinPolicy,
        _ => RuleMode.Allowed
    };

    /// <summary>
    /// If the rule is not <see cref="RuleMode.Allowed"/>, returns a Forbidden
    /// <see cref="ServiceResult{T}"/> with an appropriate message.
    /// Returns <c>null</c> when the action is allowed and the caller should proceed.
    /// </summary>
    public static ServiceResult<T>? EnforceAllowed<T>(RuleMode mode, string featureName)
    {
        return mode switch
        {
            RuleMode.Disabled          => ServiceResult.Forbidden<T>($"{featureName} is disabled in this organization."),
            RuleMode.RequiresApproval  => ServiceResult.Forbidden<T>($"{featureName} requires admin approval in this organization. Please submit a request."),
            _ => null   // Allowed → proceed
        };
    }
}
