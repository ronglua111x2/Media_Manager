# Sonarr Parser Attribution

This folder contains episode title parsing code ported from [Sonarr](https://github.com/Sonarr/Sonarr).

## Source

- **Repository:** https://github.com/Sonarr/Sonarr
- **Commit:** `49df353fa14c453929b79f32144b2c8c86d94b09`
- **License:** GNU General Public License v3.0 (see [LICENSE.md](LICENSE.md))

## Ported files

| Sonarr source | Local file | Modifications |
|---------------|------------|---------------|
| `src/NzbDrone.Core/Parser/Parser.cs` | `Parser/Parser.cs` | Removed NLog, Quality/Language models; namespace change |
| `src/NzbDrone.Core/Parser/ParserCommon.cs` | `Parser/ParserCommon.cs` | Namespace change |
| `src/NzbDrone.Core/Parser/RegexReplace.cs` | `Parser/RegexReplace.cs` | Namespace change |
| `src/NzbDrone.Core/Parser/ReleaseGroupParser.cs` | `Parser/ReleaseGroupParser.cs` | Namespace change; nullable return |
| `src/NzbDrone.Core/Parser/Model/ParsedEpisodeInfo.cs` | `Parser/Model/ParsedEpisodeInfo.cs` | Slimmed DTO |
| `src/NzbDrone.Core/Parser/Model/SeriesTitleInfo.cs` | `Parser/Model/SeriesTitleInfo.cs` | Namespace change |
| `src/NzbDrone.Core/Parser/InvalidDateException.cs` | `Parser/InvalidDateException.cs` | Base Exception instead of NzbDroneException |
| `src/NzbDrone.Core/MediaFiles/FileExtensions.cs` | `Parser/Extensions/FileExtensions.cs` | Simplified extension list |

## Stubs (not from Sonarr)

- `Parser/Stubs/QualityParser.cs` — no-op quality parsing
- `Parser/Stubs/LanguageParser.cs` — no-op language parsing
- `Parser/ParserLog.cs` — no-op logger replacing NLog

## Obtaining source

The complete corresponding source for this ported component is available in this repository under `ThirdParty/Sonarr.Parser/`, and in the upstream Sonarr repository at the commit above.
