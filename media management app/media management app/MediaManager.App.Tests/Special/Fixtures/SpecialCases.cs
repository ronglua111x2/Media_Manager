namespace MediaManager.App.Tests.Special.Fixtures;

/// <summary>
/// Fictional shows whose TMDB Season 0 lists have the same shape (counts, air-date windows, recaps in
/// between, entries before season 1, number/date inversions) as the real lists checked in the parser
/// audit (report sections 10.2-10.4). Pack layouts follow the naming styles seen in real releases.
/// </summary>
internal static class SpecialCases
{
    private static DateTime D(int year, int month, int day) => new(year, month, day);

    public const string BrassRoot = "[Grp] Harbor Brass (Seasons 1-2 + Specials + OVA) [BD 1080p]";
    public const string BrassS1 = BrassRoot + "/[Grp] Harbor Brass - S1";
    public const string BrassS2 = BrassRoot + "/[Grp] Harbor Brass - S2";
    public const string BrassOva = BrassS1 + "/[Grp] Harbor Brass - S1OVA - Pastry Dash.mkv";

    /// <summary>
    /// Shape of the real two-season BD pack: S1 has 7 <c>S1Snn</c> specials plus one unnumbered OVA,
    /// S2 has 7 <c>S2Snn</c> specials. TMDB S00 has 32 entries: block S1 = E1-E8 (8), block S2 =
    /// E9-E19 (11: 7 specials, 3 movie recaps, a promo), block S3 = E20-E32. As on the real list, the films and the
    /// promo carry their own titles, with no "recap" or "promo" in them: no rule can tell them from the specials, so
    /// season 2 stays open for Gemini (the rule that skips recap/promo titles cannot place it).
    /// </summary>
    public static SpecialCase TwoSeasonBrassPack()
    {
        var show = BrassShow();
        show.RegularFiles(1, BrassS1, episode => $"[Grp] Harbor Brass - S1E{episode:00}.mkv")
            .RegularFiles(2, BrassS2, episode => $"[Grp] Harbor Brass - S2E{episode:00}.mkv");

        for (var index = 1; index <= 7; index++)
        {
            show.SpecialFile($"{BrassS1}/Specials/[Grp] Harbor Brass - S1S{index:00}.mkv", index);
        }

        show.SpecialFile(BrassOva, 8);
        for (var index = 1; index <= 7; index++)
        {
            show.SpecialFile($"{BrassS2}/Specials/[Grp] Harbor Brass - S2S{index:00}.mkv", 8 + index);
        }

        return show.Build("brass-two-season-pack", "real pack shape; block S2 has 11 TMDB entries for 7 files");
    }

    public static SpecialShowBuilder BrassShow()
    {
        var show = new SpecialShowBuilder("Harbor Brass", 900001)
            .Season(1, 13, D(2015, 4, 8))
            .Season(2, 13, D(2016, 10, 6))
            .Season(3, 13, D(2024, 4, 7));

        DateTime[] practiceDates =
        [
            D(2015, 6, 17), D(2015, 7, 15), D(2015, 8, 19), D(2015, 9, 16), D(2015, 10, 21), D(2015, 11, 18), D(2015, 12, 16)
        ];
        for (var index = 0; index < practiceDates.Length; index++)
        {
            show.Special(index + 1, $"Practice Room Days #{index + 1}", practiceDates[index]);
        }

        show.Special(8, "Ready, Set, Pastry", D(2015, 12, 16));

        (string Title, DateTime Date)[] welcome =
        [
            ("Welcome to the Night Market", D(2016, 12, 21)),
            ("Welcome to the Beach", D(2017, 1, 18)),
            ("Welcome to the Bakery", D(2017, 2, 15)),
            ("Welcome to the Tournament", D(2017, 3, 18)),
            ("Welcome to the Rooftop", D(2017, 4, 19)),
            ("Welcome to the Library", D(2017, 5, 17)),
            ("Welcome to the Dream", D(2017, 6, 21))
        ];
        for (var index = 0; index < welcome.Length; index++)
        {
            show.Special(9 + index, welcome[index].Title, welcome[index].Date);
        }

        show.Special(16, "Harbor Brass Movie: Encore, Part 1", D(2017, 9, 13))
            .Special(17, "Harbor Brass Movie: Encore, Part 2", D(2017, 9, 13))
            .Special(18, "Harbor Brass Movie: Encore, Part 3", D(2017, 9, 13))
            .Special(19, "Harbor Brass Season 3 Starts Now!", D(2024, 3, 30))
            .Special(20, "April Fools Video", D(2024, 7, 31));

        DateTime[] bonusDates = [D(2024, 6, 26), D(2024, 7, 31), D(2024, 8, 28), D(2024, 9, 25), D(2024, 10, 30), D(2024, 11, 27)];
        for (var index = 0; index < bonusDates.Length; index++)
        {
            show.Special(21 + index, $"Bonus Episode #{index + 1}", bonusDates[index]);
        }

        for (var index = 0; index < bonusDates.Length; index++)
        {
            show.Special(27 + index, $"Unused Footage Vol. {index + 1}", bonusDates[index]);
        }

        return show;
    }

    /// <summary>The 8 layouts of report section 10.4, rebuilt with fictional shows.</summary>
    public static IReadOnlyList<SpecialCase> Generalisation() =>
    [
        TwoSeasonBrassPack(),
        RailUntitled(),
        RailTitled(),
        CafeBdShorts(),
        CafeLaterShortsNamedAsEpisodes(),
        RooftopCompleteMinis(),
        ArchiveSeasonOneOvas(),
        ClubOneOvaPerSeason()
    ];

    public static IEnumerable<object[]> GeneralisationNames() =>
        Generalisation().Select(item => new object[] { item.Name });

    public static SpecialCase ByName(string name) =>
        Generalisation().Single(item => item.Name == name);

    /// <summary>One season; 9 mini episodes; TMDB puts a recap (E9) between mini 08 (E8) and mini 09 (E10).</summary>
    private static SpecialShowBuilder RailShow()
    {
        var show = new SpecialShowBuilder("Night Rail Journey", 900002).Season(1, 12, D(2024, 4, 1));
        string[] minis =
        [
            "Which Snack Is Best", "Window or Aisle", "Where Does the Line End", "Who Packs the Bags",
            "What Is in the Tunnel", "Can You Read a Timetable", "Would You Ride Again", "Is It Okay to Sleep"
        ];
        for (var index = 0; index < minis.Length; index++)
        {
            show.Special(index + 1, $"Mini Anime {index + 1:00} - {minis[index]}", D(2024, 4, 24).AddDays(7 * index));
        }

        return show.Special(9, "Recap: The Journey So Far", D(2024, 6, 17))
            .Special(10, "Mini Anime 09 - Do You Think We Arrive", D(2024, 6, 26))
            .RegularFiles(1, "Night Rail/Season 01", episode => $"Night Rail S01E{episode:00} 1080p.mkv");
    }

    private static SpecialCase RailUntitled()
    {
        var show = RailShow();
        for (var index = 1; index <= 9; index++)
        {
            show.SpecialFile($"Night Rail/Specials/Night Rail - SP{index:00}.mkv", index <= 8 ? index : 10);
        }

        return show.Build("rail-untitled", "recap E9 sits between mini 08 and 09; 9 files for 10 entries");
    }

    private static SpecialCase RailTitled()
    {
        var show = RailShow();
        for (var index = 1; index <= 9; index++)
        {
            show.SpecialFile($"Night Rail/Specials/Night Rail - Mini Anime {index:00}.mkv", index <= 8 ? index : 10);
        }

        return show.Build("rail-titled", "same as rail-untitled, file names carry 'Mini Anime NN'");
    }

    /// <summary>One season; 4 shorts from the first broadcast and 6 shorts from three years later share one block.</summary>
    private static SpecialShowBuilder CafeShow()
    {
        var show = new SpecialShowBuilder("Clockwork Cafe", 900004).Season(1, 13, D(2022, 7, 2));
        for (var index = 1; index <= 4; index++)
        {
            show.Special(index, $"Ask the Cafe! {index}", D(2022, 7, 20));
        }

        for (var index = 1; index <= 6; index++)
        {
            show.Special(4 + index, $"Lost Hours Short {index}", D(2025, 4, 16).AddDays(7 * (index - 1)));
        }

        return show.RegularFiles(1, "Clockwork Cafe/Season 01", episode => $"Clockwork Cafe S01E{episode:00} 1080p.mkv");
    }

    private static SpecialCase CafeBdShorts()
    {
        var show = CafeShow();
        for (var index = 1; index <= 4; index++)
        {
            show.SpecialFile($"Clockwork Cafe/Specials/Clockwork Cafe - SP{index:00}.mkv", index);
        }

        return show.Build("cafe-bd-shorts", "4 files, 10 TMDB entries in one block");
    }

    private static SpecialCase CafeLaterShortsNamedAsEpisodes()
    {
        var show = CafeShow();
        for (var index = 1; index <= 6; index++)
        {
            show.SpecialFile($"Clockwork Cafe - Lost Hours/Clockwork Cafe - Lost Hours - {index:00}.mkv", 4 + index);
        }

        return show.Build("cafe-later-shorts", "special-only release named like regular episodes 01-06 (TMDB E5-E10)");
    }

    /// <summary>One season; 32 minis, 7 aired before season 1, TMDB E9 aired after E10.</summary>
    private static SpecialCase RooftopCompleteMinis()
    {
        var show = new SpecialShowBuilder("Moonlit Rooftop", 900006).Season(1, 12, D(2020, 10, 9));
        DateTime[] dates =
        [
            D(2020, 9, 11), D(2020, 9, 16), D(2020, 9, 23), D(2020, 9, 25), D(2020, 9, 26), D(2020, 10, 1), D(2020, 10, 8),
            D(2020, 10, 10), D(2020, 10, 20), D(2020, 10, 17), D(2020, 10, 21), D(2020, 10, 24), D(2020, 10, 28),
            D(2020, 11, 2), D(2020, 11, 4), D(2020, 11, 9), D(2020, 11, 11), D(2020, 11, 16), D(2020, 11, 18)
        ];
        for (var index = 1; index <= 32; index++)
        {
            var date = index <= dates.Length ? dates[index - 1] : D(2020, 11, 21).AddDays(3 * (index - dates.Length - 1));
            show.Special(index, $"Mini Anime {index}", date);
        }

        show.RegularFiles(1, "Moonlit Rooftop/Season 01", episode => $"Moonlit Rooftop S01E{episode:00} 1080p.mkv");
        for (var index = 1; index <= 32; index++)
        {
            show.SpecialFile($"Moonlit Rooftop/Specials/Moonlit Rooftop - Mini Anime {index:00}.mkv", index);
        }

        return show.Build("rooftop-complete-minis", "complete set of 32; 7 before S1; E9/E10 air dates inverted");
    }

    /// <summary>Four seasons; two OVAs of season 1 in the pack, two recaps of season 2 not in the pack.</summary>
    private static SpecialCase ArchiveSeasonOneOvas()
    {
        var show = new SpecialShowBuilder("Paper Archive", 900007)
            .Season(1, 14, D(2019, 10, 3))
            .Season(2, 12, D(2020, 4, 5))
            .Season(3, 10, D(2022, 4, 12))
            .Season(4, 12, D(2026, 4, 4))
            .Special(1, "Side Story One: The Merchant's Visit", D(2020, 3, 10))
            .Special(2, "Side Story Two: The Noble Girl", D(2020, 3, 10))
            .Special(3, "Re-Archive Part 1", D(2022, 3, 29))
            .Special(4, "Re-Archive Part 2", D(2022, 3, 29))
            .RegularFiles(1, "Paper Archive/Season 01", episode => $"Paper Archive S01E{episode:00}.mkv")
            .RegularFiles(2, "Paper Archive/Season 02", episode => $"Paper Archive S02E{episode:00}.mkv")
            .SpecialFile("Paper Archive/Season 01/Specials/Paper Archive - S1OVA1.mkv", 1)
            .SpecialFile("Paper Archive/Season 01/Specials/Paper Archive - S1OVA2.mkv", 2);

        return show.Build("archive-s1-ovas", "only the season 1 OVAs; block S2 has two other entries");
    }

    /// <summary>Three seasons, one unnumbered OVA in each season folder.</summary>
    private static SpecialCase ClubOneOvaPerSeason()
    {
        var show = new SpecialShowBuilder("Quiet Club", 900008)
            .Season(1, 13, D(2013, 4, 5))
            .Season(2, 13, D(2015, 4, 3))
            .Season(3, 12, D(2020, 7, 10))
            .Special(1, "A Festival Afterword", D(2013, 9, 19))
            .Special(2, "The Gift Exchange", D(2016, 10, 27))
            .Special(3, "The Last Prom", D(2023, 4, 27))
            .RegularFiles(1, "Quiet Club/Season 01", episode => $"Quiet Club S01E{episode:00}.mkv")
            .RegularFiles(2, "Quiet Club/Season 02", episode => $"Quiet Club S02E{episode:00}.mkv")
            .RegularFiles(3, "Quiet Club/Season 03", episode => $"Quiet Club S03E{episode:00}.mkv")
            .SpecialFile("Quiet Club/Season 01/Quiet Club - OVA.mkv", 1)
            .SpecialFile("Quiet Club/Season 02/Quiet Club Second - OVA.mkv", 2)
            .SpecialFile("Quiet Club/Season 03/Quiet Club Finale - OVA.mkv", 3);

        return show.Build("club-ova-per-season", "one unnumbered OVA per season folder");
    }

    /// <summary>
    /// Pack scenario I of the audit: one <c>SnSnn</c> special per season, TMDB S00E01 is a recap that aired
    /// before season 1 and is not in the pack. The right answer is S01S01 = E2, S02S01 = E3.
    /// </summary>
    public static SpecialCase RecapBeforeSeasonOne() =>
        new SpecialShowBuilder("Lantern Harbor", 900010)
            .Season(1, 2, D(2011, 1, 5))
            .Season(2, 2, D(2012, 1, 4))
            .Special(1, "Recap Before the Premiere", D(2010, 12, 20))
            .Special(2, "Season 1 Special", D(2011, 6, 1))
            .Special(3, "Season 2 Special", D(2012, 6, 1))
            .RegularFiles(1, "Show/Season 1", episode => $"Show.S01E{episode:00}.mkv")
            .RegularFiles(2, "Show/Season 2", episode => $"Show.S02E{episode:00}.mkv")
            .SpecialFile("Show/Season 1/Show.S01S01.mkv", 2)
            .SpecialFile("Show/Season 2/Show.S02S01.mkv", 3)
            .Build("recap-before-season-one", "scenario I: TMDB E1 is a recap that is not in the pack");
}
