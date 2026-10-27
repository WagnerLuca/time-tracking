using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Services;

/// <summary>
/// Tells members in time that carried-over vacation will lapse (organizations with carry-over mode UntilDate): days only
/// lapse when the employer asked the employee to take them and said when they lapse (EuGH C-684/16, BAG 9 AZR 423/16), so
/// <see cref="VacationBalances"/> lets nothing lapse without <see cref="VacationYear.ExpiryNoticeAt"/>.
/// <list type="bullet">
/// <item>From 1 October: every active member with days left this year that would carry over gets the notice for next year
/// (the stored proof is next year's <see cref="VacationYear.ExpiryNoticeAt"/>).</item>
/// <item>About four weeks before the expiry date (<see cref="ReminderDays"/>): every member with carried-in days not yet
/// used gets a reminder; it counts as the notice when the first one was missed.</item>
/// </list>
/// In-app notification "VacationExpiry" (and with it a push). Run once per org-local day by the minute loop
/// (<c>FailureEntryService</c>); tests call <see cref="SendDueAsync"/> directly.
/// </summary>
public static class VacationNotices
{
    public const string NotificationType = "VacationExpiry";
    public const int ReminderDays = 28;

    public static async Task<int> SendDueAsync(TimeTrackingDbContext db, DateTime nowUtc, CancellationToken ct = default)
    {
        var sent = 0;
        var orgs = await db.Organizations.Where(o => o.VacationCarryOverMode == VacationCarryOverMode.UntilDate).ToListAsync(ct);
        foreach (var org in orgs)
        {
            var clock = OrgClock.For(org);
            var today = clock.Today(nowUtc);
            if (org.VacationNoticesCheckedOn == today) continue;
            org.VacationNoticesCheckedOn = today;
            var rule = VacationBalances.Rule.Of(org);

            var memberIds = await db.UserOrganizations
                .Where(uo => uo.OrganizationId == org.Id && uo.IsActive)
                .Select(uo => uo.UserId).ToListAsync(ct);
            var rows = await db.VacationYears
                .Where(v => v.OrganizationId == org.Id && (v.Year == today.Year || v.Year == today.Year + 1))
                .ToListAsync(ct);
            VacationYear Row(int userId, int year)
            {
                var row = rows.FirstOrDefault(r => r.UserId == userId && r.Year == year);
                if (row != null) return row;
                row = new VacationYear { OrganizationId = org.Id, UserId = userId, Year = year, UpdatedAt = nowUtc };
                db.VacationYears.Add(row);
                rows.Add(row);
                return row;
            }

            var balances = memberIds.Count == 0 ? []
                : await VacationBalances.YearsAsync(db, org, memberIds, today.Year, today);
            foreach (var (userId, b) in balances)
            {
                // Next year's notice: this year's leftover will carry in and lapse (only once carry-in exists)
                if (today.Month >= 10 && b.Remaining > 0 && today.Year + 1 > rule.Since)
                {
                    var next = Row(userId, today.Year + 1);
                    if (next.ExpiryNoticeAt == null)
                    {
                        next.ExpiryNoticeAt = nowUtc;
                        var lapses = next.ExpiresOn ?? rule.ExpiryIn(today.Year + 1);
                        Notify(db, org, userId, b.Remaining, Math.Min(b.Remaining, rule.MaxDays ?? b.Remaining), lapses);
                        sent++;
                    }
                }

                // The reminder about four weeks before this year's date, while carried-in days are left
                var carriedLeft = b.CarriedIn - b.CarriedUsed;
                if (carriedLeft <= 0) continue;
                var row = Row(userId, today.Year);
                var expiry = row.ExpiresOn ?? rule.ExpiryIn(today.Year);
                if (row.ExpiryReminderAt != null || today > expiry || today < expiry.AddDays(-ReminderDays)) continue;
                row.ExpiryReminderAt = nowUtc;
                row.ExpiryNoticeAt ??= nowUtc;
                Notify(db, org, userId, b.Remaining, carriedLeft, expiry);
                sent++;
            }
            await db.SaveChangesAsync(ct);
        }
        return sent;
    }

    private static void Notify(TimeTrackingDbContext db, Organization org, int userId, double left, double lapsing, DateOnly expiry)
    {
        static string Days(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
        db.Notifications.Add(new Notification
        {
            UserId = userId,
            OrganizationId = org.Id,
            Type = NotificationType,
            Title = "Vacation days will lapse",
            // The frontend reads the figures from this sentence (translated there)
            Message = $"{Days(left)} vacation days left, {Days(lapsing)} lapse after {expiry:dd.MM.yyyy} unless taken."
        });
    }
}
