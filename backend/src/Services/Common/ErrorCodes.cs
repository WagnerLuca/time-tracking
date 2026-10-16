namespace TimeTracking.Api.Services;

/// <summary>
/// Stable error codes (dotted lowerCamel) for the messages members and admins actually see, so
/// the frontend can show them in German or English. The English message stays as the
/// ProblemDetails <c>detail</c>; the code goes into the <c>code</c> extension.
/// <para>
/// ponytail: keyed by the exact English message so the ~300 failure call sites stay untouched; a
/// reworded message silently loses its code (the frontend then falls back to the detail).
/// <c>ErrorCodeTests</c> checks that every message listed here still exists in the source.
/// Messages with variable parts are matched by prefix/suffix in <see cref="For"/>.
/// </para>
/// </summary>
public static class ErrorCodes
{
    public static readonly IReadOnlyDictionary<string, string> ByMessage = new Dictionary<string, string>
    {
        // ── Generic ──
        ["Forbidden"] = "common.forbidden",
        ["Unauthorized"] = "common.unauthorized",
        ["Organization not found"] = "org.notFound",
        ["Organization not found."] = "org.notFound",
        ["You are not a member of this organization."] = "org.notMember",
        ["You must be a member of this organization."] = "org.notMember",
        ["User is not a member of this organization."] = "org.userNotMember",
        ["Member not found in this organization"] = "org.memberNotFound",
        ["Member not found in this organization."] = "org.memberNotFound",
        ["Member not found"] = "org.memberNotFound",
        ["User not found"] = "user.notFound",
        ["User not found."] = "user.notFound",
        ["This organization requires two-factor authentication. Enable it in your account settings to continue."] = "org.twoFactorRequired",

        // ── Auth / account ──
        ["Invalid email or password"] = "auth.invalidCredentials",
        ["This account has been deactivated"] = "auth.accountDeactivated",
        ["Your account has been deactivated."] = "auth.accountDeactivated",
        ["Your session has ended. Please sign in again."] = "auth.sessionEnded",
        ["Invalid or expired refresh token"] = "auth.invalidRefreshToken",
        ["A user with this email already exists"] = "auth.emailInUse",
        ["Email is already in use by another account."] = "auth.emailInUse",
        ["Current password is incorrect"] = "auth.wrongCurrentPassword",
        ["Password is incorrect"] = "auth.wrongPassword",
        ["Enter your current password to change your email address."] = "auth.currentPasswordRequired",
        ["Password must be at least 8 characters long."] = "auth.passwordTooShort",
        ["Password must contain at least one uppercase letter."] = "auth.passwordNeedsUppercase",
        ["Password must contain at least one lowercase letter."] = "auth.passwordNeedsLowercase",
        ["Password must contain at least one digit."] = "auth.passwordNeedsDigit",
        ["Password must contain at least one special character."] = "auth.passwordNeedsSpecial",
        ["A confirmation email was just sent. Please wait a few minutes before changing the address again."] = "auth.mailCooldown",
        ["A confirmation email was just sent. Please wait a few minutes before asking again."] = "auth.mailCooldown",
        ["This link is invalid or has expired."] = "auth.invalidLink",
        ["Your email address is already confirmed."] = "auth.emailAlreadyConfirmed",
        ["Invalid or expired two-factor token"] = "auth.twoFactorTokenExpired",
        ["Invalid two-factor request"] = "auth.twoFactorTokenExpired",
        ["Too many wrong codes. Try again in 15 minutes."] = "auth.twoFactorLocked",
        ["Invalid verification code"] = "auth.twoFactorInvalidCode",
        ["Invalid verification code. Make sure the code from your authenticator app is correct."] = "auth.twoFactorInvalidCode",
        ["Two-factor authentication is already enabled. Disable it first to reconfigure."] = "auth.twoFactorAlreadyEnabled",
        ["Two-factor authentication is already enabled"] = "auth.twoFactorAlreadyEnabled",
        ["Call setup first to generate a TOTP secret"] = "auth.twoFactorSetupRequired",
        ["Two-factor authentication is not enabled"] = "auth.twoFactorNotEnabled",

        // ── Time entries ──
        ["Start time must be in the year 2000 or later."] = "entry.startTooEarly",
        ["Start time cannot be in the future."] = "entry.startInFuture",
        ["End time must be after start time."] = "entry.endBeforeStart",
        ["An entry cannot be longer than 24 hours."] = "entry.tooLong",
        ["A timer is already running."] = "timer.alreadyRunning",
        ["No running time entry found."] = "timer.notRunning",
        ["No running time entry."] = "timer.notRunning",
        ["Cannot edit a running time entry."] = "entry.running",
        ["Cannot delete a running time entry."] = "entry.running",
        ["The entry overlaps one of your existing entries."] = "entry.overlap",
        ["The entry overlaps an existing entry of the member."] = "entry.overlap",
        ["A manual entry must lie in the past."] = "entry.manualInFuture",
        ["Adding past entries is disabled in this organization."] = "entry.addDisabled",
        ["Only admins can export payroll data."] = "export.payrollAdminOnly",
        ["Time entry not found."] = "entry.notFound",
        ["Cannot edit a running time entry. Stop it first."] = "entry.running",
        ["Editing past entries is disabled in this organization."] = "entry.editDisabled",
        ["Editing past entries requires admin approval in this organization. Please submit a request."] = "entry.editRequiresApproval",

        // ── Ranges, import/export ──
        ["Dates must be in the year 2000 or later."] = "range.tooEarly",
        ["The end date must not be before the start date."] = "range.endBeforeStart",
        ["A range can cover at most two years."] = "range.tooLong",
        ["Month must be between 1 and 12."] = "report.invalidMonth",
        ["You do not have permission to export this member's report."] = "export.forbidden",
        ["You do not have permission to export this member's data."] = "export.forbidden",
        ["Organization slug is required for daily report export."] = "export.orgRequired",
        ["Organization slug is required to export another member's data."] = "export.orgRequired",
        ["CSV import is disabled in this organization."] = "import.disabled",
        ["CSV import requires admin approval in this organization. Please submit a request."] = "import.requiresApproval",
        ["CSV file must have a header row and at least one data row."] = "import.empty",
        ["CSV must contain 'Date', 'Start Time', and 'End Time' columns."] = "import.missingColumns",
        ["Unknown import format."] = "import.unknownFormat",
        ["The file does not have the columns of the selected import format."] = "import.formatMismatch",
        ["The file contains data of several employees. Import one employee per file."] = "import.severalEmployees",

        // ── Requests ──
        ["You already have a pending request of this type for this organization."] = "request.duplicate",
        ["This request has already been responded to."] = "request.alreadyResponded",
        ["Request not found."] = "request.notFound",
        ["Only pending requests can be withdrawn."] = "request.notWithdrawable",
        ["You are already a member of this organization."] = "request.alreadyMember",
        ["This organization does not accept join requests. Ask an admin to add you."] = "request.joinClosed",
        ["This feature is disabled for this organization."] = "request.featureDisabled",
        ["You can perform this action directly without a request."] = "request.notNeeded",

        // ── Absences ──
        ["Absence not found"] = "absence.notFound",
        ["This absence was approved or entered by an admin. Ask an admin to remove it."] = "absence.adminOwned",
        ["An absence already exists on this date."] = "absence.exists",
        ["Choose a range of at most one year with at least one working day."] = "absence.invalidRange",
        ["Adding absences is disabled in this organization. Ask an admin."] = "absence.disabled",
        ["Absences require approval in this organization. Please submit an absence request."] = "absence.requiresApproval",
        ["Unknown or inactive custom absence type."] = "absence.unknownType",
        ["An absence type with this name already exists."] = "absenceType.nameTaken",
        ["Absence type not found"] = "absenceType.notFound",

        // ── Invitations ──
        ["Invite as Member or Admin; ownership is transferred separately."] = "invitation.invalidRole",
        ["Only the owner can invite admins."] = "invitation.ownerOnly",
        ["This person is already a member."] = "invitation.alreadyMember",
        ["Invitation not found"] = "invitation.notFound",
        ["This invitation is invalid or has expired."] = "invitation.invalid",
        ["This invitation was sent to a different email address. Sign in with that address to accept it."] = "invitation.wrongEmail",

        // ── Organization, schedules, holidays, pause rules, groups ──
        ["An organization with this slug already exists"] = "org.slugTaken",
        ["Cannot change the Owner's role. Transfer ownership first."] = "org.ownerRoleFixed",
        ["Owner cannot leave. Delete the organization or transfer ownership first."] = "org.ownerCannotLeave",
        ["Enable two-factor authentication for your own account before requiring it for the organization."] = "org.enableOwnTwoFactorFirst",
        ["Core working hours need a valid start and a different end."] = "org.coreHoursOrder",
        ["Past days to check must be between 1 and 31."] = "org.coreHoursBackfillDays",
        ["The core hours tolerance must be between 0 and 120 minutes."] = "group.coreHoursTolerance",
        ["Work schedule not found"] = "schedule.notFound",
        ["Initial overtime is disabled for this organization."] = "overtime.initialDisabled",
        ["Initial overtime requires admin approval. Please submit a request."] = "overtime.initialRequiresApproval",
        ["A holiday already exists on this date."] = "holiday.exists",
        ["Holiday not found"] = "holiday.notFound",
        ["A pause rule with this threshold already exists."] = "pauseRule.exists",
        ["Pause rule not found."] = "pauseRule.notFound",
        ["Group not found."] = "group.notFound",
        ["Unknown kind of information."] = "visibility.unknownKind",
        ["User is already in this group."] = "group.alreadyMember",
        ["Member not found in group."] = "group.memberNotFound",
        ["Parent group not found."] = "group.parentNotFound",
        ["This personnel number is already used by another member."] = "org.personnelNumberTaken",
        ["Push notifications are not configured on this server."] = "push.notConfigured",
        ["This push service is not supported."] = "push.unsupportedService",
        ["The push subscription is invalid."] = "push.invalidSubscription",
        ["This device receives notifications for another account."] = "push.otherAccount",
        ["Choose either a parent group or the top level, not both."] = "group.parentOrTopLevel",
        ["A group cannot be moved into itself or one of its sub-groups."] = "group.cycle",
        ["Groups can be nested at most 3 levels deep."] = "group.tooDeep",
        ["This group has sub-groups. Choose whether to move them up one level or delete them with it."] = "group.hasSubGroups",
        ["Unknown group admin capability."] = "group.unknownCapability",
        ["API key not found"] = "apiKey.notFound",

        // ── Terminal ──
        ["Missing API key."] = "terminal.invalidKey",
        ["Invalid API key."] = "terminal.invalidKey",
        ["Unknown card."] = "terminal.unknownCard",
        ["This card is blocked."] = "terminal.cardBlocked",
        ["Too many unknown cards were scanned. Try again later."] = "terminal.tooManyUnknown",
        ["The double-scan window must be between 5 and 600 seconds."] = "terminal.doubleScanRange",
        ["The offline window must be between 1 and 168 hours."] = "terminal.offlineRange",
        ["The unknown-card budget must be between 5 and 200 scans per 10 minutes."] = "terminal.unknownLimitRange",
        ["This scan is older than the member's last punch. Send queued scans in order."] = "terminal.outOfOrder",
        ["A timer is running outside this organization. Stop it in the app first."] = "terminal.timerElsewhere",
        ["This card is already registered."] = "terminal.cardTaken",
        ["Enter a card ID or pick a scanned card."] = "terminal.cardRequired",
        ["Card not found."] = "terminal.cardNotFound",
        ["Scan not found."] = "terminal.scanNotFound",
    };

    /// <summary>The code for a failure message, or null for messages without one.</summary>
    public static string? For(string? message)
    {
        if (string.IsNullOrEmpty(message)) return null;
        if (ByMessage.TryGetValue(message, out var code)) return code;

        // Messages with variable parts
        if (message.StartsWith("Absences already exist on ")) return "absence.exists";
        if (message.StartsWith("A CSV file can hold at most ") || message.StartsWith("An import can hold at most ")) return "import.tooManyRows";
        if (message.StartsWith("The request cannot be applied: ")) return "request.cannotApply";
        if (message.StartsWith("You own ")) return "account.ownsOrganizations";
        if (message.EndsWith(" require two-factor authentication. Leave them first, or ask an admin to turn the requirement off.")) return "account.twoFactorRequiredByOrgs";
        if (message.StartsWith("Unknown time zone ")) return "org.unknownTimeZone";
        if (message.StartsWith("The scan time must lie between ")) return "terminal.scannedAtOutOfRange";
        if (message.StartsWith("This schedule overlaps with an existing one ")) return "schedule.overlap";
        // OrgRuleHelper.EnforceAllowed / WorkScheduleService.CheckRuleMode ("<feature> is/are disabled…")
        if (message.EndsWith(" is disabled in this organization.") || message.EndsWith(" are disabled for this organization.")) return "org.featureDisabled";
        if (message.Contains(" require admin approval") || message.Contains(" requires admin approval")) return "org.featureRequiresApproval";
        return null;
    }
}
