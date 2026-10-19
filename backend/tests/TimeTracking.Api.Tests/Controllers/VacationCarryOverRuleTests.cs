using FluentAssertions;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;
using static TimeTracking.Api.Services.VacationBalances;

namespace TimeTracking.Api.Tests.Controllers;

/// <summary>
/// The pure carry-over rule (<see cref="VacationBalances.Chain"/>, Phase 2b of docs/PERFORMANCE_PLAN.md): no database,
/// one chain of consecutive years in, the last year's balance out.
/// </summary>
public class VacationCarryOverRuleTests
{
    private static Rule Until(double? max = null, int month = 3, int day = 31, int since = 2026) =>
        new(VacationCarryOverMode.UntilDate, month, day, max, since);

    private static Rule Unlimited(double? max = null) => new(VacationCarryOverMode.Unlimited, 3, 31, max, 2026);
    private static Rule NoCarry() => new(VacationCarryOverMode.None, 3, 31, null, 2026);

    private static DateOnly D(string date) => DateOnly.Parse(date);

    /// <summary><paramref name="count"/> full vacation days, one per calendar day from <paramref name="from"/>.</summary>
    private static IEnumerable<Day> Run(string from, int count) =>
        Enumerable.Range(0, count).Select(i => new Day(D(from).AddDays(i), 1));

    private static YearInput Y(int year, double entitlement, IEnumerable<Day>? days = null, double adjustment = 0) =>
        new(year, entitlement, adjustment, (days ?? []).ToList());

    /// <summary>The worked example: since 2026, 30 days, cap 10; 2026 used 25; 2027 used 2 in February + 10 in July.</summary>
    private static List<YearInput> WorkedExample() =>
    [
        Y(2026, 30, Run("2026-06-01", 25)),
        Y(2027, 30, Run("2027-02-10", 2).Concat(Run("2027-07-01", 10)))
    ];

    [Fact]
    public void WorkedExample_MidMarch_CarriedDaysUsedFirst_NothingExpiredYet()
    {
        var b = Chain(Until(10), WorkedExample(), D("2027-03-15"));

        b.Year.Should().Be(2027);
        b.Entitlement.Should().Be(30);
        b.CarriedIn.Should().Be(5);
        b.CarriedUsed.Should().Be(2);
        b.Expired.Should().Be(0);
        b.ExpiresOn.Should().Be(D("2027-03-31"));
        b.Available.Should().Be(35);
        b.Taken.Should().Be(2);
        b.Planned.Should().Be(10);
        b.Used.Should().Be(12);
        b.Remaining.Should().Be(23);
    }

    [Fact]
    public void WorkedExample_FirstOfApril_TheUnusedCarryLapses()
    {
        var b = Chain(Until(10), WorkedExample(), D("2027-04-01"));

        b.CarriedIn.Should().Be(5);
        b.CarriedUsed.Should().Be(2);
        b.Expired.Should().Be(3);
        b.Available.Should().Be(32);
        b.Remaining.Should().Be(20);
    }

    [Fact]
    public void WorkedExample_CarryOutOf2027_IsCappedAt10()
    {
        // carryOut 2027 = 30 + 5 − 3 − 12 = 20, capped to 10
        var years = WorkedExample().Append(Y(2028, 30)).ToList();
        var b = Chain(Until(10), years, D("2028-01-10"));
        b.CarriedIn.Should().Be(10);
        b.ExpiresOn.Should().Be(D("2028-03-31"));
        b.Available.Should().Be(40);
        b.Remaining.Should().Be(40);

        // Without the cap all 20 move on
        Chain(Until(), years, D("2028-01-10")).CarriedIn.Should().Be(20);
    }

    [Fact]
    public void FirstYear_GetsNoCarryIn_AndAnEmptyChainIsRejected()
    {
        var b = Chain(Until(), [Y(2026, 30, Run("2026-02-02", 3))], D("2026-10-19"));
        b.CarriedIn.Should().Be(0);
        b.CarriedUsed.Should().Be(0);
        b.Expired.Should().Be(0);
        b.ExpiresOn.Should().BeNull();
        b.Remaining.Should().Be(27);

        FluentActions.Invoking(() => Chain(Until(), [], D("2026-10-19"))).Should().Throw<ArgumentException>();
    }

    [Theory]
    // A day used on the expiry date itself consumes carry; a day after it does not
    [InlineData("2027-03-30", 1, 0, 33)]   // only the 31 March day is planned and counts as used
    [InlineData("2027-03-31", 1, 0, 33)]   // today == E: not expired yet
    [InlineData("2027-04-01", 1, 4, 29)]   // today == E + 1: the 4 unused carried days lapse
    public void ExpiryDay_IsTheLastDayTheCarryCanBeUsed(string today, double carriedUsed, double expired, double remaining)
    {
        var years = new List<YearInput>
        {
            Y(2026, 30, Run("2026-06-01", 25)),
            Y(2027, 30, [new Day(D("2027-03-31"), 1), new Day(D("2027-04-01"), 1)])
        };
        var b = Chain(Until(), years, D(today));
        b.CarriedIn.Should().Be(5);
        b.CarriedUsed.Should().Be(carriedUsed);
        b.Expired.Should().Be(expired);
        b.Remaining.Should().Be(remaining);
    }

    [Fact]
    public void DaysUsedOnlyAfterTheExpiry_LeaveTheWholeCarryToLapse()
    {
        var years = new List<YearInput> { Y(2026, 30, Run("2026-06-01", 25)), Y(2027, 30, Run("2027-05-03", 3)) };
        var b = Chain(Until(), years, D("2027-10-01"));
        b.CarriedUsed.Should().Be(0);
        b.Expired.Should().Be(5);
        b.Available.Should().Be(30);
        b.Remaining.Should().Be(27);

        // Lapsed at year end: carryOut 2027 = 30 + 5 − 5 − 3 = 27
        Chain(Until(), years.Append(Y(2028, 30)).ToList(), D("2028-01-02")).CarriedIn.Should().Be(27);
    }

    [Fact]
    public void LapsedCarry_DoesNotMoveOn_EvenBeforeTheExpiryHasPassedForTheReportedYear()
    {
        // Asking for 2028 on 2 January: 2027's unused carry lapsed at its year end however "today" relates to 2027's E
        var years = new List<YearInput> { Y(2026, 30, Run("2026-06-01", 20)), Y(2027, 30), Y(2028, 30) };
        Chain(Until(), years, D("2028-01-02")).CarriedIn.Should().Be(30);   // 30 + 10 − 10 − 0
    }

    [Fact]
    public void Feb29Expiry_IsClampedInShortYears()
    {
        var rule = Until(month: 2, day: 29);
        rule.ExpiryIn(2027).Should().Be(D("2027-02-28"));
        rule.ExpiryIn(2028).Should().Be(D("2028-02-29"));

        var years = new List<YearInput>
        {
            Y(2026, 30, Run("2026-06-01", 25)),
            Y(2027, 30, [new Day(D("2027-02-28"), 1), new Day(D("2027-03-01"), 1)])
        };
        var onE = Chain(rule, years, D("2027-02-28"));
        onE.ExpiresOn.Should().Be(D("2027-02-28"));
        onE.CarriedUsed.Should().Be(1);
        onE.Expired.Should().Be(0);

        var after = Chain(rule, years, D("2027-03-01"));
        after.CarriedUsed.Should().Be(1);
        after.Expired.Should().Be(4);
        after.Remaining.Should().Be(29);   // 30 + 5 − 4 − 2
    }

    [Fact]
    public void OtherClampedExpiries_FollowTheMonthLength()
    {
        Until(month: 4, day: 31).ExpiryIn(2027).Should().Be(D("2027-04-30"));
        Until(month: 12, day: 31).ExpiryIn(2027).Should().Be(D("2027-12-31"));
    }

    [Fact]
    public void Unlimited_NeverLapses_CarryUsedFirstWheneverTaken()
    {
        var years = new List<YearInput> { Y(2026, 30, Run("2026-06-01", 25)), Y(2027, 30, Run("2027-07-01", 2)) };
        var b = Chain(Unlimited(), years, D("2027-12-01"));
        b.CarriedIn.Should().Be(5);
        b.CarriedUsed.Should().Be(2);
        b.Expired.Should().Be(0);
        b.ExpiresOn.Should().BeNull();
        b.Available.Should().Be(35);
        b.Remaining.Should().Be(33);

        // Everything left moves on: 30 + 5 − 2 = 33
        Chain(Unlimited(), years.Append(Y(2028, 30)).ToList(), D("2028-01-02")).CarriedIn.Should().Be(33);
        Chain(Unlimited(10), years.Append(Y(2028, 30)).ToList(), D("2028-01-02")).CarriedIn.Should().Be(10);
    }

    [Fact]
    public void None_NothingCarries()
    {
        var years = new List<YearInput> { Y(2026, 30, Run("2026-06-01", 5)), Y(2027, 30, Run("2027-02-01", 2)) };
        var b = Chain(NoCarry(), years, D("2027-10-01"));
        b.CarriedIn.Should().Be(0);
        b.CarriedUsed.Should().Be(0);
        b.Expired.Should().Be(0);
        b.ExpiresOn.Should().BeNull();
        b.Available.Should().Be(30);
        b.Remaining.Should().Be(28);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(2.5, 2.5)]
    [InlineData(5.0, 5.0)]
    [InlineData(100.0, 5.0)]
    public void Cap_LimitsWhatMovesOn(double cap, double carriedIn)
    {
        var years = new List<YearInput> { Y(2026, 30, Run("2026-06-01", 25)), Y(2027, 30) };
        var b = Chain(Until(cap), years, D("2027-01-10"));
        b.CarriedIn.Should().Be(carriedIn);
        b.Available.Should().Be(30 + carriedIn);
        // A cap of 0 means no carry-in at all: no expiry date either
        if (carriedIn == 0) b.ExpiresOn.Should().BeNull();
        else b.ExpiresOn.Should().Be(D("2027-03-31"));
    }

    [Fact]
    public void NegativeAdjustment_NeverMakesRemainingOrCarryNegative()
    {
        var years = new List<YearInput> { Y(2026, 30, adjustment: -35), Y(2027, 30) };
        var first = Chain(Until(), years.Take(1).ToList(), D("2026-12-01"));
        first.Adjustment.Should().Be(-35);
        first.Available.Should().Be(-5);
        first.Remaining.Should().Be(0);
        Chain(Until(), years, D("2027-01-10")).CarriedIn.Should().Be(0);

        // A smaller negative adjustment reduces the leftover: 30 − 5 − 20 = 5
        var partial = new List<YearInput> { Y(2026, 30, Run("2026-06-01", 20), adjustment: -5), Y(2027, 30, adjustment: 2) };
        var b = Chain(Until(), partial, D("2027-01-10"));
        b.CarriedIn.Should().Be(5);
        b.Adjustment.Should().Be(2);
        b.Available.Should().Be(37);
    }

    [Fact]
    public void HalfDays_CountHalf_InUsageCarryAndExpiry()
    {
        var y2026 = Run("2026-06-01", 29).Append(new Day(D("2026-08-03"), 0.5));
        var years = new List<YearInput>
        {
            Y(2026, 30, y2026),
            Y(2027, 30, [new Day(D("2027-03-01"), 0.5), new Day(D("2027-06-01"), 0.5)])
        };
        var march = Chain(Until(), years, D("2027-03-02"));
        march.CarriedIn.Should().Be(0.5);
        march.CarriedUsed.Should().Be(0.5);
        march.Taken.Should().Be(0.5);
        march.Planned.Should().Be(0.5);
        march.Remaining.Should().Be(29.5);   // 30.5 − 1

        // A half day before E against a full carried day leaves half a day to lapse
        var halfLeft = new List<YearInput>
        {
            Y(2026, 30, Run("2026-06-01", 29)),
            Y(2027, 30, [new Day(D("2027-03-01"), 0.5)])
        };
        var april = Chain(Until(), halfLeft, D("2027-04-01"));
        april.CarriedIn.Should().Be(1);
        april.CarriedUsed.Should().Be(0.5);
        april.Expired.Should().Be(0.5);
        april.Remaining.Should().Be(30);   // 30 + 1 − 0.5 − 0.5
    }

    [Fact]
    public void ZeroActivityYears_KeepCarryingOnlyWhatTheRuleAllows()
    {
        var years = new List<YearInput> { Y(2024, 30), Y(2025, 30), Y(2026, 30) };
        var since = new Rule(VacationCarryOverMode.UntilDate, 3, 31, null, 2024);

        // UntilDate: each year's carry lapses unused, so only the year's own 30 days move on
        var until = Chain(since, years, D("2026-01-05"));
        until.CarriedIn.Should().Be(30);
        until.Remaining.Should().Be(60);

        // Unlimited: it piles up (30 → 60)
        Chain(since with { Mode = VacationCarryOverMode.Unlimited }, years, D("2026-01-05")).CarriedIn.Should().Be(60);
        Chain(since with { Mode = VacationCarryOverMode.Unlimited, MaxDays = 10 }, years, D("2026-01-05")).CarriedIn.Should().Be(10);
    }

    [Fact]
    public void EntitlementPerYear_IsTakenFromItsInput()
    {
        var years = new List<YearInput> { Y(2026, 20, Run("2026-06-01", 15)), Y(2027, 28) };
        var b = Chain(Until(), years, D("2027-01-10"));
        b.Entitlement.Should().Be(28);
        b.CarriedIn.Should().Be(5);
        b.Available.Should().Be(33);
    }

    [Fact]
    public void TakenAndPlanned_SplitAtToday_PlannedCountsAsUsed()
    {
        var years = new List<YearInput> { Y(2026, 30, Run("2026-10-15", 10)) };
        var b = Chain(Until(), years, D("2026-10-19"));
        b.Taken.Should().Be(5);    // 15.–19.10.
        b.Planned.Should().Be(5);
        b.Remaining.Should().Be(20);
    }

    [Fact]
    public void EntitlementFor_OwnRow_ElseLatestEarlierRow_ElseAllowance()
    {
        var rows = new List<VacationYear>
        {
            new() { Year = 2024, Entitlement = 20 },
            new() { Year = 2026, Entitlement = null, Adjustment = 3 },   // adjustment only: inherits
            new() { Year = 2027, Entitlement = 25 },
            new() { Year = 2029, Entitlement = 0 }
        };
        EntitlementFor(2023, rows, 30).Should().Be(30);
        EntitlementFor(2024, rows, 30).Should().Be(20);
        EntitlementFor(2025, rows, 30).Should().Be(20);
        EntitlementFor(2026, rows, 30).Should().Be(20);
        EntitlementFor(2027, rows, 30).Should().Be(25);
        EntitlementFor(2028, rows, 30).Should().Be(25);
        EntitlementFor(2029, rows, 30).Should().Be(0);    // 0 is a value, not "inherit"
        EntitlementFor(2035, rows, 30).Should().Be(0);
        EntitlementFor(2026, [], 27.5).Should().Be(27.5);
    }
}
