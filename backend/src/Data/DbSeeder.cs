using TimeTracking.Api.Data;
using TimeTracking.Api.Models;

namespace TimeTracking.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(TimeTrackingDbContext context)
    {
        // Check if database is already seeded
        if (context.Users.Any())
        {
            return; // Database already has data
        }

        var rng = new Random(42); // deterministic seed for reproducibility

        // ── Users ──────────────────────────────────────────────────────
        var max = new User
        {
            Email = "max.mueller@example.com",
            FirstName = "Max",
            LastName = "Mueller",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123"),
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow.AddMonths(-6),
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };

        var anna = new User
        {
            Email = "anna.schmidt@example.com",
            FirstName = "Anna",
            LastName = "Schmidt",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123"),
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow.AddMonths(-4),
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };

        var tom = new User
        {
            Email = "tom.weber@example.com",
            FirstName = "Tom",
            LastName = "Weber",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123"),
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow.AddMonths(-3),
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };

        context.Users.AddRange(max, anna, tom);
        await context.SaveChangesAsync();

        // ── Organization ───────────────────────────────────────────────
        var org = new Organization
        {
            Name = "Mueller Software GmbH",
            Description = "A growing software consultancy based in Munich. We build custom web and mobile applications for mid-size enterprises.",
            Slug = "mueller-software",
            CreatedAt = DateTime.UtcNow.AddMonths(-6),
            UpdatedAt = DateTime.UtcNow,
            IsActive = true,
            Website = "https://mueller-software.example.com",
            AutoPauseEnabled = true,
            EditPastEntriesMode = RuleMode.Allowed,
            EditPauseMode = RuleMode.Allowed,
            InitialOvertimeMode = RuleMode.Allowed,
            JoinPolicy = RuleMode.Allowed, // open for joining
            WorkScheduleChangeMode = RuleMode.Allowed
        };

        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        // ── User-Organization memberships ──────────────────────────────
        var maxMembership = new UserOrganization
        {
            UserId = max.Id,
            OrganizationId = org.Id,
            Role = OrganizationRole.Owner,
            JoinedAt = DateTime.UtcNow.AddMonths(-6),
            IsActive = true,
            InitialOvertimeHours = 12.5 // carried over from previous employer
        };

        var annaMembership = new UserOrganization
        {
            UserId = anna.Id,
            OrganizationId = org.Id,
            Role = OrganizationRole.Admin,
            JoinedAt = DateTime.UtcNow.AddMonths(-4),
            IsActive = true,
            InitialOvertimeHours = 0
        };

        var tomMembership = new UserOrganization
        {
            UserId = tom.Id,
            OrganizationId = org.Id,
            Role = OrganizationRole.Member,
            JoinedAt = DateTime.UtcNow.AddMonths(-3),
            IsActive = true,
            InitialOvertimeHours = 0
        };

        context.UserOrganizations.AddRange(maxMembership, annaMembership, tomMembership);
        await context.SaveChangesAsync();

        // ── Pause Rules ────────────────────────────────────────────────
        context.PauseRules.AddRange(
            new PauseRule { OrganizationId = org.Id, MinHours = 6, PauseMinutes = 30, CreatedAt = DateTime.UtcNow },
            new PauseRule { OrganizationId = org.Id, MinHours = 9, PauseMinutes = 45, CreatedAt = DateTime.UtcNow }
        );
        await context.SaveChangesAsync();

        // ── Holidays (German public holidays 2025) ─────────────────────
        var holidays = new List<Holiday>
        {
            new() { OrganizationId = org.Id, Date = new DateOnly(2025, 1, 1),  Name = "New Year's Day",      IsRecurring = true },
            new() { OrganizationId = org.Id, Date = new DateOnly(2025, 1, 6),  Name = "Epiphany",            IsRecurring = true },
            new() { OrganizationId = org.Id, Date = new DateOnly(2025, 4, 18), Name = "Good Friday",         IsRecurring = false },
            new() { OrganizationId = org.Id, Date = new DateOnly(2025, 4, 21), Name = "Easter Monday",       IsRecurring = false },
            new() { OrganizationId = org.Id, Date = new DateOnly(2025, 5, 1),  Name = "Labour Day",          IsRecurring = true },
            new() { OrganizationId = org.Id, Date = new DateOnly(2025, 5, 29), Name = "Ascension Day",       IsRecurring = false },
            new() { OrganizationId = org.Id, Date = new DateOnly(2025, 6, 9),  Name = "Whit Monday",         IsRecurring = false },
            new() { OrganizationId = org.Id, Date = new DateOnly(2025, 6, 19), Name = "Corpus Christi",      IsRecurring = false },
            new() { OrganizationId = org.Id, Date = new DateOnly(2025, 8, 15), Name = "Assumption Day",      IsRecurring = false },
            new() { OrganizationId = org.Id, Date = new DateOnly(2025, 10, 3), Name = "German Unity Day",    IsRecurring = true },
            new() { OrganizationId = org.Id, Date = new DateOnly(2025, 11, 1), Name = "All Saints' Day",     IsRecurring = true },
            new() { OrganizationId = org.Id, Date = new DateOnly(2025, 12, 25), Name = "Christmas Day",      IsRecurring = true },
            new() { OrganizationId = org.Id, Date = new DateOnly(2025, 12, 26), Name = "St. Stephen's Day",  IsRecurring = true },
        };
        context.Holidays.AddRange(holidays);
        await context.SaveChangesAsync();

        // ── Helper: generate time entries for a user ────────────────────
        static List<TimeEntry> GenerateEntries(
            User user, Organization org, DateTime startDate, Random rng,
            HashSet<DateOnly> offDays)
        {
            var entries = new List<TimeEntry>();
            var cursor = startDate.Date;
            var now = DateTime.UtcNow;

            while (cursor < now.Date)
            {
                var dow = cursor.DayOfWeek;
                // Skip weekends
                if (dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday)
                {
                    cursor = cursor.AddDays(1);
                    continue;
                }
                // Skip days off (holidays + absences)
                if (offDays.Contains(DateOnly.FromDateTime(cursor)))
                {
                    cursor = cursor.AddDays(1);
                    continue;
                }

                // Realistic start time: 7:30 - 9:00
                var startHour = 7 + rng.Next(0, 2);
                var startMin = rng.Next(0, 4) * 15; // 0, 15, 30, 45
                if (startHour == 7 && startMin < 30) startMin = 30;
                var entryStart = cursor.AddHours(startHour).AddMinutes(startMin);
                // Convert to UTC (assume CET = UTC+1, rough approximation)
                entryStart = DateTime.SpecifyKind(entryStart, DateTimeKind.Utc);

                // Realistic work duration: 7.5h - 9h
                var workMinutes = 450 + rng.Next(-30, 61); // 420-510min = 7h-8.5h
                var entryEnd = entryStart.AddMinutes(workMinutes);

                // Pause: 30min for 6h+, 45min for 9h+ (matches pause rules)
                var pauseMins = 0;
                var totalHours = workMinutes / 60.0;
                if (totalHours >= 9) pauseMins = 45;
                else if (totalHours >= 6) pauseMins = 30;

                entries.Add(new TimeEntry
                {
                    UserId = user.Id,
                    OrganizationId = org.Id,
                    Description = PickDescription(rng, dow),
                    StartTime = entryStart,
                    EndTime = entryEnd,
                    IsRunning = false,
                    PauseDurationMinutes = pauseMins,
                    CreatedAt = entryStart,
                    UpdatedAt = entryEnd
                });

                cursor = cursor.AddDays(1);
            }

            return entries;
        }

        static string PickDescription(Random rng, DayOfWeek dow)
        {
            var descriptions = new[]
            {
                "Feature development – user dashboard",
                "Bug fixes and code review",
                "Sprint planning meeting + dev work",
                "Client workshop preparation",
                "API integration work",
                "Frontend styling and responsive fixes",
                "Database optimization and queries",
                "Code review and pair programming",
                "Release preparation and testing",
                "Documentation and knowledge transfer",
                "Infrastructure and DevOps tasks",
                "Performance profiling and tuning",
                "Refactoring authentication module",
                "Unit tests and integration tests",
                "Customer support & hotfix"
            };
            // Monday often has meetings
            if (dow == DayOfWeek.Monday && rng.Next(3) == 0)
                return "Sprint planning + standup";
            // Friday sometimes shorter descriptions
            if (dow == DayOfWeek.Friday && rng.Next(4) == 0)
                return "Week wrap-up, documentation";
            return descriptions[rng.Next(descriptions.Length)];
        }

        // ── Absences for Max ───────────────────────────────────────────
        var maxAbsences = new List<AbsenceDay>
        {
            // One week vacation in February 2025
            new() { UserId = max.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 2, 17), Type = AbsenceType.Vacation, Note = "Winter vacation" },
            new() { UserId = max.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 2, 18), Type = AbsenceType.Vacation, Note = "Winter vacation" },
            new() { UserId = max.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 2, 19), Type = AbsenceType.Vacation, Note = "Winter vacation" },
            new() { UserId = max.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 2, 20), Type = AbsenceType.Vacation, Note = "Winter vacation" },
            new() { UserId = max.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 2, 21), Type = AbsenceType.Vacation, Note = "Winter vacation" },
            // Sick days in March
            new() { UserId = max.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 3, 10), Type = AbsenceType.SickDay, Note = "Flu" },
            new() { UserId = max.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 3, 11), Type = AbsenceType.SickDay, Note = "Flu" },
            // Doctor's appointment in April
            new() { UserId = max.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 4, 14), Type = AbsenceType.Other, Note = "Doctor's appointment" },
            // Summer vacation in June (2 weeks around Corpus Christi bridge day)
            new() { UserId = max.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 6, 16), Type = AbsenceType.Vacation, Note = "Summer vacation" },
            new() { UserId = max.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 6, 17), Type = AbsenceType.Vacation, Note = "Summer vacation" },
            new() { UserId = max.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 6, 18), Type = AbsenceType.Vacation, Note = "Summer vacation" },
            new() { UserId = max.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 6, 20), Type = AbsenceType.Vacation, Note = "Summer vacation" },
            new() { UserId = max.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 6, 23), Type = AbsenceType.Vacation, Note = "Summer vacation" },
            new() { UserId = max.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 6, 24), Type = AbsenceType.Vacation, Note = "Summer vacation" },
            new() { UserId = max.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 6, 25), Type = AbsenceType.Vacation, Note = "Summer vacation" },
            new() { UserId = max.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 6, 26), Type = AbsenceType.Vacation, Note = "Summer vacation" },
            new() { UserId = max.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 6, 27), Type = AbsenceType.Vacation, Note = "Summer vacation" },
        };

        // ── Absences for Anna ──────────────────────────────────────────
        var annaAbsences = new List<AbsenceDay>
        {
            new() { UserId = anna.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 3, 24), Type = AbsenceType.Vacation, Note = "Family trip" },
            new() { UserId = anna.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 3, 25), Type = AbsenceType.Vacation, Note = "Family trip" },
            new() { UserId = anna.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 3, 26), Type = AbsenceType.Vacation, Note = "Family trip" },
            new() { UserId = anna.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 5, 5),  Type = AbsenceType.SickDay,  Note = "Migraine" },
        };

        // ── Absences for Tom ───────────────────────────────────────────
        var tomAbsences = new List<AbsenceDay>
        {
            new() { UserId = tom.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 4, 7),  Type = AbsenceType.Vacation, Note = "Easter holiday" },
            new() { UserId = tom.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 4, 8),  Type = AbsenceType.Vacation, Note = "Easter holiday" },
            new() { UserId = tom.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 4, 9),  Type = AbsenceType.Vacation, Note = "Easter holiday" },
            new() { UserId = tom.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 5, 12), Type = AbsenceType.SickDay,  Note = "Back pain" },
            new() { UserId = tom.Id, OrganizationId = org.Id, Date = new DateOnly(2025, 5, 13), Type = AbsenceType.SickDay,  Note = "Back pain" },
        };

        context.AbsenceDays.AddRange(maxAbsences);
        context.AbsenceDays.AddRange(annaAbsences);
        context.AbsenceDays.AddRange(tomAbsences);
        await context.SaveChangesAsync();

        // Build holiday + absence date sets for entry generation
        var holidayDates = holidays.Select(h => h.Date).ToHashSet();

        var maxOffDays = new HashSet<DateOnly>(holidayDates);
        foreach (var a in maxAbsences) maxOffDays.Add(a.Date);

        var annaOffDays = new HashSet<DateOnly>(holidayDates);
        foreach (var a in annaAbsences) annaOffDays.Add(a.Date);

        var tomOffDays = new HashSet<DateOnly>(holidayDates);
        foreach (var a in tomAbsences) tomOffDays.Add(a.Date);

        // ── Time Entries ───────────────────────────────────────────────
        // Max: working since 6 months ago
        var maxEntries = GenerateEntries(max, org, DateTime.UtcNow.AddMonths(-6), rng, maxOffDays);
        // Anna: working since 4 months ago
        var annaEntries = GenerateEntries(anna, org, DateTime.UtcNow.AddMonths(-4), rng, annaOffDays);
        // Tom: working since 3 months ago
        var tomEntries = GenerateEntries(tom, org, DateTime.UtcNow.AddMonths(-3), rng, tomOffDays);

        context.TimeEntries.AddRange(maxEntries);
        context.TimeEntries.AddRange(annaEntries);
        context.TimeEntries.AddRange(tomEntries);
        await context.SaveChangesAsync();

        // ── Work Schedules ───────────────────────────────────────────
        // Max: consistent 40h
        context.WorkSchedules.AddRange(
            new WorkSchedule
            {
                UserId = max.Id, OrganizationId = org.Id,
                ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)),
                ValidTo = null,
                WeeklyWorkHours = 40,
                TargetMon = 8, TargetTue = 8, TargetWed = 8, TargetThu = 8, TargetFri = 8,
                CreatedAt = DateTime.UtcNow.AddMonths(-6)
            }
        );
        // Anna: 32h/week (Mon-Thu)
        context.WorkSchedules.Add(
            new WorkSchedule
            {
                UserId = anna.Id, OrganizationId = org.Id,
                ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-4)),
                ValidTo = null,
                WeeklyWorkHours = 32,
                TargetMon = 8, TargetTue = 8, TargetWed = 8, TargetThu = 8, TargetFri = 0,
                CreatedAt = DateTime.UtcNow.AddMonths(-4)
            }
        );
        // Tom: 40h/week
        context.WorkSchedules.Add(
            new WorkSchedule
            {
                UserId = tom.Id, OrganizationId = org.Id,
                ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-3)),
                ValidTo = null,
                WeeklyWorkHours = 40,
                TargetMon = 8, TargetTue = 8, TargetWed = 8, TargetThu = 8, TargetFri = 8,
                CreatedAt = DateTime.UtcNow.AddMonths(-3)
            }
        );
        await context.SaveChangesAsync();

        Console.WriteLine("✅ Database seeded successfully!");
        Console.WriteLine($"   - {context.Users.Count()} users");
        Console.WriteLine($"   - {context.Organizations.Count()} organizations");
        Console.WriteLine($"   - {context.UserOrganizations.Count()} memberships");
        Console.WriteLine($"   - {context.Holidays.Count()} holidays");
        Console.WriteLine($"   - {context.AbsenceDays.Count()} absence days");
        Console.WriteLine($"   - {context.TimeEntries.Count()} time entries");
        Console.WriteLine($"   - {context.PauseRules.Count()} pause rules");
        Console.WriteLine($"   - {context.WorkSchedules.Count()} work schedules");
        Console.WriteLine($"   Max Mueller: max.mueller@example.com / Password123 (Owner)");
        Console.WriteLine($"   Anna Schmidt: anna.schmidt@example.com / Password123 (Admin)");
        Console.WriteLine($"   Tom Weber: tom.weber@example.com / Password123 (Member)");
    }
}
