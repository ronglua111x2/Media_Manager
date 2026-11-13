using FluentAssertions;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Evaluation;

/// <summary>
/// Audit 07c NS-01/02 and their re-audit: the positional title check used when a movie release's year does not
/// confirm the film. Most names are real rows from the native indexers (bench run 12-13/11/2026); the rest are the
/// re-audit's probe cases. Title variants are separated by '|'.
/// </summary>
public class MovieReleaseTitleTests
{
    private static bool Match(string name, string variants) =>
        MovieReleaseTitle.MatchesTitle(name, variants.Split('|'));

    [Theory]
    // Chinese/Korean group tags and native titles before the English title
    [InlineData("[ 不太灵公益影视站 www.butailing.com ]彗星来的那一夜[简繁英字幕].Coherence.2013.1080p.BluRay.DTS.x265-10bit-ENTHD", "Coherence")]
    [InlineData("【首发于高清影视之家 www.BBQDDQ.com】彗星来的那一夜[中文字幕].Coherence.2013.BluRay.1080p.DTS-HDMA.5.1.x265.10bit-CTRLHD", "Coherence")]
    [InlineData("2018 - Coherence [1080p]", "Coherence")]
    [InlineData("오펜하이머 Oppenheimer,2023.1080p.KORSUB.WEBRip.H264.AAC", "Oppenheimer")]
    [InlineData("Spider-Man No Way Home,2021.2160p.UHD.BluRay.x264", "Spider-Man: No Way Home")]
    [InlineData("스파이더맨 노 웨이 홈 (고화질) Spider-Man No Way Home,2021.1080p.KOR.FHDRip.H264.AAC-JTC.mkv", "Spider-Man: No Way Home")]
    // Anime movies: romaji / English pairs, brackets, "Movie 01" numbering, versions
    [InlineData("【喵萌奶茶屋】★劇場版★[超時空輝夜姬！加長版 / Chou Kaguya-hime! Extend ver.][BDRip][1080p][繁體]", "Cosmic Princess Kaguya!|Chou Kaguya-hime!")]
    [InlineData("[LinRip] Chou Kaguya-hime! 超かぐや姫! [BDRemux 1080p AVC FLAC]", "Cosmic Princess Kaguya!|Chou Kaguya-hime!")]
    [InlineData("Chou Kaguya-hime! v2 (WEB 1080p HEVC E-AC3) [kikuri] mkv", "Cosmic Princess Kaguya!|Chou Kaguya-hime!")]
    [InlineData("[Pre-S&三明治摸鱼部] 超时空辉夜姬 Cosmic Princess Kaguya [Movie][WebRip 1080P AVC 8Bit AAC MP4]", "Cosmic Princess Kaguya!|Chou Kaguya-hime!")]
    [InlineData("[Judas] Jujutsu Kaisen - Movie 01 - Jujutsu Kaisen 0 [BD 1080p][HEVC x265 10bit][Dual-Audio][Multi-Subs].mkv", "Jujutsu Kaisen 0|Jujutsu Kaisen Zero")]
    [InlineData("[Group] 劇場版 呪術廻戦 0 Jujutsu Kaisen 0 [BD 1080p]", "Jujutsu Kaisen 0|Jujutsu Kaisen Zero")]
    [InlineData("[Group] Jujutsu Kaisen 0 + Extras (BD 1080p)", "Jujutsu Kaisen 0|Jujutsu Kaisen Zero")]
    [InlineData("[35mm] Liz to Aoi Tori - Liz and the Blue Bird [1080p] [562EF58C].mkv", "Liz and the Blue Bird|Liz to Aoi Tori")]
    [InlineData("[Airota][Liz and the Blue Bird][Movie][BDRip_1080p][x264_AAC][CHS]", "Liz and the Blue Bird|Liz to Aoi Tori")]
    [InlineData("[Family & KnK & Houkago & Yarashii] Liz to Aoi Tori - Liz et l'Oiseau Bleu VOSTFR [BD 1080p FLAC]", "Liz and the Blue Bird|Liz to Aoi Tori")]
    [InlineData("【喵萌奶茶屋】[剧场版][莉兹与青鸟/Liz and the Blue Bird/Liz to Aoi Tori/リズと青い鳥][BDRip][1080p][Fin][简体]", "Liz and the Blue Bird|Liz to Aoi Tori")]
    [InlineData("【悠哈璃羽字幕社】[利兹与青鸟_Liz to Aoi Tori][BDRip 1920x1080 HEVC-YUV420P10 FLAC DTS-HDMA][简日/繁日外挂]", "Liz and the Blue Bird|Liz to Aoi Tori")]
    [InlineData("[Anime Time] Hibike! Euphonium Movie 3- Chikai no Finale [1080p] mp4", "Sound! Euphonium the Movie - Our Promise: A Brand New Day|Hibike! Euphonium: Chikai no Finale")]
    [InlineData("[Deadmau- RAWS] Hibike Euphonium Movie 3 Chikai no Finale RUS ENG JAP BDRip 1080p Deadmauvlad mkv", "Sound! Euphonium the Movie - Our Promise: A Brand New Day|Hibike! Euphonium: Chikai no Finale")]
    [InlineData("[Mabors-Sub][Hibike! Euphonium Chikai no Finale][Movie][1080P][GB][WebRip][AVC AAC YUV420P8].mp4", "Sound! Euphonium the Movie - Our Promise: A Brand New Day|Hibike! Euphonium: Chikai no Finale")]
    [InlineData("[Airota][Tokubetsu Hen Hibike! Euphonium： Ensemble Contest Hen][Movie][BDRip 1080p AVC AAC][JPSC].mp4", "Sound! Euphonium: Ensemble Contest|Tokubetsu-hen Hibike! Euphonium: Ensemble Contest")]
    [InlineData("Your Name - Kimi No Na Wa BDRIP 1080p X265 10bit VOSTFR", "Your Name.|Kimi no Na wa.")]
    [InlineData("Your Name Dual Audio ENJP 1080p 8bit x264", "Your Name.|Kimi no Na wa.")]
    [InlineData("[BDMV] Your Name. - Kimi No Na Wa 「君の名は。」[Collector's Edition][1080p]", "Your Name.|Kimi no Na wa.")]
    [InlineData("[Cand] Kimi No Nawa (Your Name) [1080p] pt_BR [DUBLADO].mp4", "Your Name.|Kimi no Na wa.")]
    [InlineData("[JYFanSub x LKSUB&ANK-Raws] 君の名は。 Kimi no Na wa. Your Name. (BDrip 1920x1080 HEVC-YUV420P10 FLAC DTS-HDMA SUP)", "Your Name.|Kimi no Na wa.")]
    [InlineData("[NAZOrip] Kimi no Na wa. Your Name. 君の名は. V2 (BD.HEVC.1080P.YUV420P10.AAC) (Multiple Subtitles Included)", "Your Name.|Kimi no Na wa.")]
    [InlineData("[Nep_Blanc] Your Name - Kimi no Na Wa [1080p] [x265] [10Bit] [Subbed]", "Your Name.|Kimi no Na wa.")]
    [InlineData("[RED-Raws] Kimi no na wa  Your Name  (BDRip 1920x1080 x264 FLAC) mkv", "Your Name.|Kimi no Na wa.")]
    [InlineData("[Ryuuga] - Kimi no na wa aka Your Name Bluray [1080p-AC3-x265]", "Your Name.|Kimi no Na wa.")]
    [InlineData("[VCB-Studio] Kimi no Na wa. -your name.- [1080p]", "Your Name.|Kimi no Na wa.")]
    [InlineData("[Group] Koe no Katachi - A Silent Voice [BD 1080p HEVC]", "A Silent Voice|Koe no Katachi")]
    [InlineData("[Arid] Sen to Chihiro no Kamikakushi - Spirited Away (BD 1080p)", "Spirited Away|Sen to Chihiro no Kamikakushi")]
    [InlineData("[Group] Studio Ghibli - Spirited Away (BD 1080p)", "Spirited Away")]
    [InlineData("[Group] Satoshi Kon's Paprika [BD 1080p]", "Paprika")]
    [InlineData("[Group] Detective Conan Movie 01 - The Time Bombed Skyscraper [BD 1080p]", "Detective Conan: The Time Bombed Skyscraper")]
    [InlineData("[Group] One Piece Movie 10 - Strong World (BD 1080p)", "One Piece Film: Strong World|One Piece: Strong World")]
    [InlineData("Detective.Conan.Movie.26.Black.Iron.Submarine.2024.1080p.WEB-DL.x264-GROUP", "Detective Conan: Black Iron Submarine")]
    [InlineData("[Group] Evangelion 1.0 You Are (Not) Alone [BD 1080p]", "Evangelion: 1.0 You Are (Not) Alone")]
    // Western releases: language/edition tags, title words that look like tags, spelling variants
    [InlineData("Amelie.FRENCH.1080p.BluRay.x264-GROUP", "Amélie")]
    [InlineData("Das.Boot.GERMAN.Directors.Cut.1080p.BluRay.x264-GROUP", "Das Boot")]
    [InlineData("Seven.Samurai.Criterion.Collection.1080p.BluRay.x264-GROUP", "Seven Samurai")]
    [InlineData("Akira.25th.Anniversary.Edition.1080p.BluRay.x264-GROUP", "Akira")]
    [InlineData("Charlottes.Web.2007.1080p.BluRay.x264-GROUP", "Charlotte's Web")]
    [InlineData("(500) Days of Summer 1080p BluRay x264-GROUP", "(500) Days of Summer")]
    [InlineData("C.era.Una.Volta.Il.West.1969.1080p.BluRay.x264-GROUP", "Once Upon a Time in the West|C'era una volta il West")]
    [InlineData("Spiderman.Into.The.Spiderverse.2019.1080p.WEB-DL.x264-GROUP", "Spider-Man: Into the Spider-Verse")]
    [InlineData("Kill.Bill.Volume.1.2004.1080p.BluRay.x264-GROUP", "Kill Bill: Vol. 1")]
    [InlineData("Blade.Runner.H.264.2018.1080p", "Blade Runner")]
    [InlineData("Dune.Extended.Cut.1080p.WEB-DL-GROUP", "Dune")]
    // Unbracketed group as one CamelCase word before the title, or glued after it (2026-11-13 follow-up, real rows)
    [InlineData("AnimeKaizoku Kimi no Na wa Your Name 1080p mkv", "Your Name.|Kimi no Na wa.")]
    [InlineData("LoliHouse Kimi no Na wa BDRip 1920x1080 AVC yuv420p8 AAC PGS chs eng jpn", "Your Name.|Kimi no Na wa.")]
    [InlineData("Chou.Kaguya.hime.GoLTFilm.1080p.mkv", "Cosmic Princess Kaguya!|Chou Kaguya-hime!")]
    [InlineData("[UHA-WINGS][Liz to Aoi Tori][BDRip 1920x1080 x264 FLAC][SC_JP].mkv", "Liz and the Blue Bird|Liz to Aoi Tori")]
    [InlineData("[Airota][Liz and the Blue Bird_Libretto ver.][Movie][BDRip_1080p][x264_AAC][CHS].mp4", "Liz and the Blue Bird|Liz to Aoi Tori")]
    [InlineData("Kimi.no.na.wa.A.K.A.Your.Name.1080p.BluRay", "Your Name.|Kimi no Na wa.")]
    public void SameFilm_IsAccepted(string name, string variants)
    {
        Match(name, variants).Should().BeTrue();
    }

    [Theory]
    // Another film that contains or neighbours the title
    [InlineData("Dej mi sve jmeno / Call Me by Your Name (2017)(CZ/EN)(1080p-HEVC)(BluRay-Rip)", "Your Name.|Kimi no Na wa.")]
    [InlineData("BabyGotBoobs 15 09 04 Britney Amber Name Your Price XXX 1080p MP4-KTR", "Your Name.|Kimi no Na wa.")]
    [InlineData("[Judas] Kimi to, Nami ni Noretara (Ride Your Wave) [BD 1080p][HEVC x265 10bit][Eng-Subs]", "Your Name.|Kimi no Na wa.")]
    [InlineData("[VCB-Studio] Kimi no Na wa. Orchestra Concert [Ma10p_1080p]", "Your Name.|Kimi no Na wa.")]
    [InlineData("[Group] Kimi no Koe wo Todoketai (2017) [BD 1080p HEVC FLAC]", "Your Name.|Kimi no Na wa.")]
    [InlineData("[Group] Kimi no Iro [BD 1080p HEVC FLAC]", "Your Name.|Kimi no Na wa.")]
    [InlineData("Skutečný Oppenheimer / The Real Oppenheimer (2023)(CZ,HU)[HDTV][1080i]", "Oppenheimer")]
    [InlineData("To End All War: Oppenheimer & the Atomic Bomb (2023)(EN)[WebRip][1080p]", "Oppenheimer")]
    [InlineData("Spider-Man No Way Home Extra Scenes - 1080p", "Spider-Man: No Way Home")]
    [InlineData("[SubsPlease] Jujutsu Kaisen - 51 (1080p) [84C776B4].mkv", "Jujutsu Kaisen 0|Jujutsu Kaisen Zero")]
    [InlineData("JUJUTSU.KAISEN.Season.2.E21.Transformation.2160p.B-Global.WEB-DL.AAC2.0.H264.MSubs-ToonsHub.mkv", "Jujutsu Kaisen 0|Jujutsu Kaisen Zero")]
    // Sequels and franchise neighbours
    [InlineData("Dune.Part.Two.1080p.WEB-DL.DDP5.1.Atmos-GROUP", "Dune")]
    [InlineData("Frozen.II.1080p.BluRay.x264-GROUP", "Frozen")]
    [InlineData("Frozen.Fever.1080p.BluRay.x264-GROUP", "Frozen")]
    [InlineData("Kill.Bill.Vol.1.2003.1080p.BluRay.x264-GROUP", "Kill Bill: Vol. 2")]
    [InlineData("Harry.Potter.and.the.Deathly.Hallows.Part.2.2011.1080p.BluRay.x264-GROUP", "Harry Potter and the Deathly Hallows: Part 1")]
    [InlineData("The.Hunger.Games.Catching.Fire.2013.1080p.BluRay.x264-GROUP", "The Hunger Games")]
    [InlineData("The.Twilight.Saga.New.Moon.2009.1080p.BluRay.x264-GROUP", "Twilight")]
    [InlineData("Star.Wars.Holiday.Special.1978.1080p.WEB-DL-GROUP", "Star Wars")]
    [InlineData("Detective.Conan.The.Black.Iron.Submarine.2023.1080p.BluRay.x264-GROUP", "Detective Conan: The Million-dollar Pentagram")]
    // Two films in the bracket that holds the title; sequel words are not a CamelCase group
    [InlineData("[bilibili][Kimi no Na wa  X Tenki no Ko][1080P+][2 0][JPN,CHN][FLV]", "Your Name.|Kimi no Na wa.")]
    [InlineData("Dune.PartTwo.1080p.WEB-DL", "Dune")]
    [InlineData("Real Oppenheimer 1080p WEB-DL", "Oppenheimer")]
    public void AnotherFilm_IsRejected(string name, string variants)
    {
        Match(name, variants).Should().BeFalse();
    }
    // Same-year sequels: the year confirms nothing, the title must (prefix words are not checked).
    [Theory]
    [InlineData("The.Matrix.Revolutions.2003.1080p.BluRay.x264-GROUP", "The Matrix Reloaded", false)]
    [InlineData("The.Matrix.Reloaded.2003.1080p.BluRay.x264-GROUP", "The Matrix Reloaded", true)]
    [InlineData("www.SiteName.org - The.Matrix.Reloaded.2003.1080p.BluRay.x264-GROUP", "The Matrix Reloaded", true)]
    [InlineData("AnimeKaizoku Kimi no Na wa 2016 1080p", "Your Name.|Kimi no Na wa.", true)]
    [InlineData("续命之徒 绝命毒师电影El Camino A Breaking Bad Movie 2019 BluRay 1080p ..", "El Camino: A Breaking Bad Movie|El Camino", true)]
    [InlineData("To End All War: Oppenheimer & the Atomic Bomb (2023) [1080p] [web] [YTS]", "Oppenheimer", false)]
    [InlineData("Oppenheimer The Real Story 2023 1080p WEBRip DDP5 1 x265 10bit-G", "Oppenheimer", false)]
    [InlineData("Oppenheimer.2023.IMAX.1080p.BluRay.x264-GROUP", "Oppenheimer", true)]
    // Titles of one or two words: what precedes them is checked even when the year matches
    [InlineData("Taxi Whats Your Name 2016 VIETNAMESE ENSUBBED 1080p WEBRip x264", "Your Name.|Kimi no Na wa.", false)]
    [InlineData("Burn Your Name 2016 1080p WEBRip x264-RARBG", "Your Name.|Kimi no Na wa.", false)]
    [InlineData("Skutečný Oppenheimer / The Real Oppenheimer (2023)(CZ,HU)[HDTV][1080i]", "Oppenheimer", false)]
    [InlineData("XTC Kimi no Na wa 2016 1080p HEVC 5FF69950 mkv", "Your Name.|Kimi no Na wa.", true)]
    [InlineData("L'ultima missione Project Hail Mary (2026) WEBRip 1080p x264 EAC3 ITA ENG SUB ITA", "Project Hail Mary", true)]
    public void SameYear_ChecksTitleAndSuffixOnly(string name, string variants, bool expected)
    {
        MovieReleaseTitle.MatchesTitle(name, variants.Split('|'), ignorePrefix: true).Should().Be(expected);
    }
}
