# Dark theme color guide

Use `StaticResource AppBrush*` only. Do not hard-code `#hex` or Material Design default teal/orange in new XAML. Light theme remaps the same keys in `Resources/AppThemeColors.Light.xaml`.

Source of truth for dark values: [`Resources/AppThemeColors.Dark.xaml`](../../Resources/AppThemeColors.Dark.xaml). Styles: [`Resources/AppStyles.xaml`](../../Resources/AppStyles.xaml).

## Roles

| Role | Token | Dark hex | Use |
|------|--------|----------|-----|
| Window / page | `AppBrushCanvas` | `#121820` | Root background |
| Cards / panels | `AppBrushSurface` | `#1a2230` | Recipe panel, lists, cards |
| Raised inputs | `AppBrushSurfaceRaised` | `#232d3d` | ComboBox, text fields |
| Sidebar | `AppBrushSidebar` | `#161e2a` | Right nav rail |
| Hairline | `AppBrushBorder` | `#2f3a4d` | Card edges |
| Stronger hairline | `AppBrushBorderStrong` | `#3d4a60` | Focused/emphasized borders |
| Title text | `AppBrushText` | `#e8edf5` | Headers, recipe values |
| Labels / meta | `AppBrushMutedText` | `#93a0b8` | Overview labels, subtitles, secondary copy |
| Primary / success | `AppBrushAccent` | `#4fa88f` | Run Cart, Run Now, active nav, healthy status |
| Text on primary | `AppBrushOnAccent` | `#ffffff` | Icons/text on filled accent buttons |
| Accent well | `AppBrushAccentSoft` | `#1e3a32` | Icon wells, selected list row |
| Override / caution | `AppBrushWarning` | `#d99a3a` | Cart and Auto-Track recipe override titles and values |
| Error | `AppBrushDanger` | `#e05555` | Destructive actions, failed status |
| Error well | `AppBrushStatusErrorBg` | `#3d2020` | Failed status chips |
| Muted well | `AppBrushStatusMutedBg` | `#252d3d` | Neutral status chips |

## Rules

- **Green (`AppBrushAccent`)** is the primary action and healthy status color (Run Cart, Run Now, Idle, Candidates found, selected sidebar icon).
- **Orange (`AppBrushWarning`)** is **user override / warning**, not a second primary. Use it when a cart or Auto-Track recipe property is overridden, not for generic highlights.
- **Muted grey** for labels; **near-white** for the value the user should read first.
- Primary buttons: filled accent background + `AppBrushOnAccent` foreground. Secondary: outlined surface + `AppBrushBorder` (Stop, Reset week, Clear Cart).
- Brand status dots stay `AppBrushWarpBrand` (qBittorrent) and `AppBrushJellyfinBrand` (Jellyfin). Watch status stays `AppBrushWatch*`.
- Hover/pressed: `AppBrushAccentHover` / `AppBrushAccentPressed` / `AppBrushListHover` / `AppBrushListSelected`. Do not invent new greens.
- If a new color is required, add `AppColor*` + `AppBrush*` to **both** Dark and Light dictionaries, then use the brush.
