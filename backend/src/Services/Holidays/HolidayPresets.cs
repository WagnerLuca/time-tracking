using TimeTracking.Api.Models.Dtos;

namespace TimeTracking.Api.Services;

/// <summary>
/// Public holiday presets: Germany nationwide (<c>DE</c>, alias <c>de</c>), each of the 16
/// states (<c>DE-BW</c> … <c>DE-TH</c>, ISO 3166-2), Austria and Switzerland. Fixed-date
/// holidays are recurring; Easter-based ones (and Buß- und Bettag) are created as concrete
/// dates per requested year. Holidays only in some municipalities (Mariä Himmelfahrt in
/// Bavaria, Fronleichnam in parts of Saxony/Thuringia, Augsburger Friedensfest) are left out.
/// </summary>
public static class HolidayPresets
{
    private static readonly (string Key, string Name)[] Presets =
    [
        ("DE", "Deutschland (bundesweit)"),
        ("DE-BW", "Baden-Württemberg"),
        ("DE-BY", "Bayern"),
        ("DE-BE", "Berlin"),
        ("DE-BB", "Brandenburg"),
        ("DE-HB", "Bremen"),
        ("DE-HH", "Hamburg"),
        ("DE-HE", "Hessen"),
        ("DE-MV", "Mecklenburg-Vorpommern"),
        ("DE-NI", "Niedersachsen"),
        ("DE-NW", "Nordrhein-Westfalen"),
        ("DE-RP", "Rheinland-Pfalz"),
        ("DE-SL", "Saarland"),
        ("DE-SN", "Sachsen"),
        ("DE-ST", "Sachsen-Anhalt"),
        ("DE-SH", "Schleswig-Holstein"),
        ("DE-TH", "Thüringen"),
        ("AT", "Österreich"),
        ("CH", "Schweiz")
    ];

    public static List<HolidayPresetResponse> All() =>
        Presets.Select(p => new HolidayPresetResponse { Key = p.Key, Name = p.Name }).ToList();

    /// <summary>
    /// The preset's holidays for <paramref name="fromYear"/>..<paramref name="toYear"/>: recurring
    /// fixed dates once (dated in the first year they exist), movable ones for every year. Null for an unknown preset.
    /// </summary>
    public static List<(DateOnly Date, string Name, bool IsRecurring)>? Get(string preset, int fromYear, int toYear)
    {
        var key = preset.Trim().ToUpperInvariant();
        if (!Presets.Any(p => p.Key == key)) return null;

        var result = new List<(DateOnly, string, bool)>();
        var recurringAdded = new HashSet<string>();
        for (var year = fromYear; year <= toYear; year++)
            result.AddRange(ForYear(key, year).Where(h => !h.IsRecurring || recurringAdded.Add(h.Name)));
        return result;
    }

    /// <summary>Easter Sunday (Gregorian calendar, anonymous Gregorian / Meeus–Jones–Butcher algorithm).</summary>
    public static DateOnly Easter(int year)
    {
        int a = year % 19, b = year / 100, c = year % 100;
        int d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4, k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31;
        int day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }

    /// <summary>Buß- und Bettag: the Wednesday before 23 November.</summary>
    public static DateOnly RepentanceDay(int year)
    {
        var d = new DateOnly(year, 11, 22);
        while (d.DayOfWeek != DayOfWeek.Wednesday) d = d.AddDays(-1);
        return d;
    }

    private static IEnumerable<(DateOnly Date, string Name, bool IsRecurring)> ForYear(string key, int year)
    {
        var easter = Easter(year);
        (DateOnly, string, bool) Fixed(int month, int day, string name) => (new DateOnly(year, month, day), name, true);
        (DateOnly, string, bool) Easterly(int offset, string name) => (easter.AddDays(offset), name, false);

        if (key == "AT")
            return
            [
                Fixed(1, 1, "Neujahr"), Fixed(1, 6, "Heilige Drei Könige"), Easterly(1, "Ostermontag"),
                Fixed(5, 1, "Staatsfeiertag"), Easterly(39, "Christi Himmelfahrt"), Easterly(50, "Pfingstmontag"),
                Easterly(60, "Fronleichnam"), Fixed(8, 15, "Mariä Himmelfahrt"), Fixed(10, 26, "Nationalfeiertag"),
                Fixed(11, 1, "Allerheiligen"), Fixed(12, 8, "Mariä Empfängnis"), Fixed(12, 25, "Christtag"),
                Fixed(12, 26, "Stefanitag")
            ];
        if (key == "CH")
            return
            [
                Fixed(1, 1, "Neujahr"), Fixed(1, 2, "Berchtoldstag"), Easterly(-2, "Karfreitag"),
                Easterly(1, "Ostermontag"), Fixed(5, 1, "Tag der Arbeit"), Easterly(39, "Auffahrt"),
                Easterly(50, "Pfingstmontag"), Fixed(8, 1, "Bundesfeier"), Fixed(12, 25, "Weihnachten"),
                Fixed(12, 26, "Stephanstag")
            ];

        var state = key.Length > 3 ? key[3..] : "";
        bool In(params string[] states) => states.Contains(state);

        var list = new List<(DateOnly, string, bool)>
        {
            Fixed(1, 1, "Neujahr"),
            Easterly(-2, "Karfreitag"),
            Easterly(1, "Ostermontag"),
            Fixed(5, 1, "Tag der Arbeit"),
            Easterly(39, "Christi Himmelfahrt"),
            Easterly(50, "Pfingstmontag"),
            Fixed(10, 3, "Tag der Deutschen Einheit"),
            Fixed(12, 25, "1. Weihnachtstag"),
            Fixed(12, 26, "2. Weihnachtstag")
        };
        if (In("BW", "BY", "ST")) list.Add(Fixed(1, 6, "Heilige Drei Könige"));
        if ((In("BE") && year >= 2019) || (In("MV") && year >= 2023)) list.Add(Fixed(3, 8, "Internationaler Frauentag"));
        if (In("BB")) { list.Add(Easterly(0, "Ostersonntag")); list.Add(Easterly(49, "Pfingstsonntag")); }
        if (In("BW", "BY", "HE", "NW", "RP", "SL")) list.Add(Easterly(60, "Fronleichnam"));
        if (In("SL")) list.Add(Fixed(8, 15, "Mariä Himmelfahrt"));
        if (In("TH") && year >= 2019) list.Add(Fixed(9, 20, "Weltkindertag"));
        if (In("BB", "MV", "SN", "ST", "TH") || (In("HB", "HH", "NI", "SH") && year >= 2018)) list.Add(Fixed(10, 31, "Reformationstag"));
        if (In("BW", "BY", "NW", "RP", "SL")) list.Add(Fixed(11, 1, "Allerheiligen"));
        if (In("SN")) list.Add((RepentanceDay(year), "Buß- und Bettag", false));
        return list.OrderBy(h => h.Item1);
    }
}
