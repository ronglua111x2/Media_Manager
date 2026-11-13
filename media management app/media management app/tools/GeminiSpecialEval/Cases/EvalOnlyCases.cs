using MediaManager.App.Tests.Special.Fixtures;

namespace GeminiSpecialEval.Cases;

/// <summary>
/// Synthetic cases written for the eval, aimed at what prompt v3 changed (order in a folder outside any season,
/// order against the pairs the rules already fixed) and at abstaining. Fictional names only: the repository is
/// public. Every answer here can be worked out from the payload (titles, order, air dates); a case whose truth a
/// careful human could not deduce does not belong here.
/// Since the rules count OVA-titled entries and skip recap/promo titles (<c>SpecialSeasonWindowMapper.MapByKind</c>),
/// both root-OVA cases and SP01..04 of the blooper case are placed by the rules and no longer reach Gemini; they
/// stay as regression cases for the rules tier. Measuring Gemini on order in a root folder needs a case whose TMDB
/// titles do not name the OVAs.
/// </summary>
internal static class EvalOnlyCases
{
    private static DateTime D(int year, int month, int day) => new(year, month, day);

    public static IReadOnlyList<EvalCase> All() =>
    [
        EvalCase.FromFixture(RootOvaTitledInTmdb(), CaseSources.Eval, "root", "order"),
        EvalCase.FromFixture(RootOvaFixedMiddle(), CaseSources.Eval, "root", "order", "fx"),
        EvalCase.FromFixture(SeasonSpecialsFixedMiddle(), CaseSources.Eval, "season", "order", "fx"),
        EvalCase.FromFixture(AbstainBlooperReel(), CaseSources.Eval, "abstain")
    ];

    /// <summary>One season; TMDB S00 has four "OVA:" entries with a recap and a drama CD between them.</summary>
    private static SpecialShowBuilder GlassOrchard() =>
        new SpecialShowBuilder("Glass Orchard", 900101)
            .Season(1, 12, D(2021, 1, 8))
            .Special(1, "Recap Special", D(2021, 3, 20))
            .Special(2, "OVA: Summer Trip", D(2021, 8, 25))
            .Special(3, "OVA: Winter Festival", D(2021, 12, 22))
            .Special(4, "Picture Drama Collection", D(2022, 2, 23))
            .Special(5, "OVA: Hot Springs", D(2022, 4, 27))
            .Special(6, "OVA: The Last Day", D(2022, 6, 29))
            .RegularFiles(1, "Glass Orchard/Season 01", episode => $"Glass Orchard S01E{episode:00}.mkv");

    /// <summary>
    /// A root <c>OVA</c> folder (no parent season), files "OVA - 01..04" without titles. The four OVAs are the four
    /// "OVA:" entries in airing order: 2, 3, 5, 6.
    /// </summary>
    private static SpecialCase RootOvaTitledInTmdb()
    {
        var show = GlassOrchard();
        int[] expected = [2, 3, 5, 6];
        for (var index = 1; index <= 4; index++)
        {
            show.SpecialFile($"Glass Orchard/OVA/Glass Orchard - OVA - {index:00}.mkv", expected[index - 1]);
        }

        return show.Build("root-ova-titled-tmdb", "root OVA folder, 4 untitled files, 6 TMDB entries (recap and drama CD between)");
    }

    /// <summary>
    /// As <see cref="RootOvaTitledInTmdb"/>, but OVA 02 carries its title, so the title rule fixes it (E3) and
    /// Gemini gets OVA 01, 03 and 04 with that pair in <c>ctx.fx</c>: OVA 01 must stay below 3, the others above.
    /// </summary>
    private static SpecialCase RootOvaFixedMiddle()
    {
        var show = GlassOrchard()
            .SpecialFile("Glass Orchard/OVA/Glass Orchard - OVA - 01.mkv", 2)
            .SpecialFile("Glass Orchard/OVA/Glass Orchard - OVA - 02 - Winter Festival.mkv", 3)
            .SpecialFile("Glass Orchard/OVA/Glass Orchard - OVA - 03.mkv", 5)
            .SpecialFile("Glass Orchard/OVA/Glass Orchard - OVA - 04.mkv", 6);
        return show.Build("root-ova-fixed-middle", "root OVA folder; OVA 02 is titled and fixed by the rules, the rest must keep the order around it");
    }

    /// <summary>
    /// Season 1 specials S1S01..S1S04 against five entries in season 1's window (not an exact count, so the window
    /// rule only suggests). S1S03 carries its title and is fixed by the title rule (E3). S1S01/S1S02 must be 1 and
    /// 2; S1S04 is "Picture Drama 4" (E5), not the talk show (E4).
    /// </summary>
    private static SpecialCase SeasonSpecialsFixedMiddle() =>
        new SpecialShowBuilder("Copper Lantern", 900102)
            .Season(1, 12, D(2019, 4, 5))
            .Season(2, 12, D(2021, 4, 2))
            .Special(1, "Picture Drama 1", D(2019, 6, 26))
            .Special(2, "Picture Drama 2", D(2019, 7, 24))
            .Special(3, "Station Bento", D(2019, 8, 28))
            .Special(4, "Cast Talk Show", D(2019, 9, 1))
            .Special(5, "Picture Drama 4", D(2019, 9, 25))
            .Special(6, "Season 2 Preview Special", D(2021, 3, 27))
            .RegularFiles(1, "Copper Lantern/Season 01", episode => $"Copper Lantern - S01E{episode:00}.mkv")
            .RegularFiles(2, "Copper Lantern/Season 02", episode => $"Copper Lantern - S02E{episode:00}.mkv")
            .SpecialFile("Copper Lantern/Season 01/Specials/Copper Lantern - S1S01.mkv", 1)
            .SpecialFile("Copper Lantern/Season 01/Specials/Copper Lantern - S1S02.mkv", 2)
            .SpecialFile("Copper Lantern/Season 01/Specials/Copper Lantern - S1S03 - Station Bento.mkv", 3)
            .SpecialFile("Copper Lantern/Season 01/Specials/Copper Lantern - S1S04.mkv", 5)
            .Build("season-specials-fixed-middle", "season folder; S1S03 fixed by its title; 4 files for 5 window entries");

    /// <summary>
    /// SP01..SP04 are "Mini Theater 1..4"; the blooper reel is in no TMDB entry, so the right answer for it is to
    /// leave it out (mapping it to the interview or the event digest is a wrong link).
    /// </summary>
    private static SpecialCase AbstainBlooperReel()
    {
        var show = new SpecialShowBuilder("Paper Kite Diaries", 900103)
            .Season(1, 12, D(2023, 1, 6));
        for (var index = 1; index <= 4; index++)
        {
            show.Special(index, $"Mini Theater {index}", D(2023, 1, 20).AddDays(14 * (index - 1)));
        }

        show.Special(5, "Director Interview", D(2023, 3, 31))
            .Special(6, "Cast Event Digest", D(2023, 6, 18))
            .RegularFiles(1, "Paper Kite Diaries/Season 01", episode => $"Paper Kite Diaries S01E{episode:00}.mkv");
        for (var index = 1; index <= 4; index++)
        {
            show.SpecialFile($"Paper Kite Diaries/Specials/Paper Kite Diaries - SP{index:00}.mkv", index);
        }

        show.SpecialFile("Paper Kite Diaries/Specials/Paper Kite Diaries - Blooper Reel.mkv", null);
        return show.Build("abstain-blooper-reel", "4 numbered minis plus a blooper reel that TMDB does not list");
    }
}
