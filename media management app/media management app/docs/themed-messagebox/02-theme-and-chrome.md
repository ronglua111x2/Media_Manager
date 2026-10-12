# 02 — Theme and chrome constraints

**Status:** Docs only. No new color keys and no new window in this pass.

A later helper must look like the app, not like Win32. Reuse existing tokens and chrome. Do not hard-code `#hex` or Material Design default teal/orange.

Source of truth for colors: [ui-polish/THEME.md](../ui-polish/THEME.md). Dark values: [`Resources/AppThemeColors.Dark.xaml`](../../Resources/AppThemeColors.Dark.xaml). Light remaps the same keys in [`Resources/AppThemeColors.Light.xaml`](../../Resources/AppThemeColors.Light.xaml). Styles: [`Resources/AppStyles.xaml`](../../Resources/AppStyles.xaml).

## Why Win32 cannot follow Dark/Light

[`ThemeService.Apply`](../../Services/ThemeService.cs) does four things:

1. Swap the `AppThemeColors.*.xaml` dictionary in `Application.Current.Resources`
2. `Wpf.Ui.Appearance.ApplicationThemeManager.Apply`
3. Material Design `PaletteHelper.SetTheme`
4. LiveCharts theme

`System.Windows.MessageBox` never reads those dictionaries. WPF-UI / Material theme apply also does not restyle Win32 `MessageBoxEx`. That is why the Jellyfin failure box stays OS-themed while Library behind it uses `AppBrushCanvas`.

A window that binds `{StaticResource AppBrush*}` **does** follow the swap. No extra `IThemeService` hook is required on the dialog itself.

## Tokens to use (no new keys)

| Role | Token | Use on the helper |
|------|--------|-------------------|
| Window / page | `AppBrushCanvas` | Root background |
| Cards / panels | `AppBrushSurface` | Optional inner well |
| Hairline | `AppBrushBorder` | Window edge, button outlines |
| Title text | `AppBrushText` | Caption + body |
| Labels / meta | `AppBrushMutedText` | Secondary line if needed |
| Primary / OK / Yes | `AppBrushAccent` + `AppBrushOnAccent` | Filled primary button |
| Caution | `AppBrushWarning` | Warning icon + destructive-confirm emphasis |
| Error | `AppBrushDanger` | Error icon + Delete-class primary if a later plan uses a danger button |
| Error well | `AppBrushStatusErrorBg` | Optional icon chip behind Error |

Rules from THEME.md that apply here:

- Green (`AppBrushAccent`) is the primary action, not a second “info” brand.
- Orange (`AppBrushWarning`) is caution / override, not a second primary.
- Destructive actions in the app already use `AppBrushDanger` (see AppStyles danger buttons). A later plan may use outlined secondary for Cancel/No and filled accent or danger for Yes depending on intent class in [01-call-site-inventory.md](./01-call-site-inventory.md).

If a color is missing, add `AppColor*` + `AppBrush*` to **both** Dark and Light, then use the brush. Do not invent a one-off hex on the dialog.

## Window chrome

Match app windows, not OS caption:

| Pattern | Where it lives |
|---------|----------------|
| `WindowStyle="None"` | [`MainWindow.xaml`](../../MainWindow.xaml), [`WebViewerWindow.xaml`](../../Views/WebViewerWindow.xaml) |
| `WindowChrome` CaptionHeight 0, CornerRadius 8, GlassFrameThickness 0 | same |
| `Background="{StaticResource AppBrushCanvas}"` | MainWindow, WebViewerWindow, [`EnginePickerDialog.xaml`](../../Views/EnginePickerDialog.xaml) |
| `UseLayoutRounding` / `SnapsToDevicePixels` | MainWindow, WebViewerWindow |

Message-box specifics (later code, not this folder):

- `ResizeMode="NoResize"` (not the shell’s `CanResize`)
- `WindowStartupLocation="CenterOwner"` when owner is set
- `ShowInTaskbar="False"`
- Compact caption row (title + close), not the full MainWindow 38px tool buttons copied blindly — keep Lucide close consistent with shell `PackIconLucide` usage

[`EnginePickerDialog`](../../Views/EnginePickerDialog.xaml) is themed **body only** (standard WPF caption). Do not copy that for the helper; the whole point is to drop the OS caption that the screenshot shows.

Layout: [ui-polish/LAYOUT.md](../ui-polish/LAYOUT.md) — wrap body text (`TextWrapping="Wrap"` is required here; the “no wrap” rule is for cart overview rows, not modal copy). Cap width so long migration text scrolls instead of ballooning the window.

## Lucide severity icons

Kind strings **must** exist in [ui-polish/LUCIDE_ICONS.md](../ui-polish/LUCIDE_ICONS.md) (MahApps IconPacks Lucide **6.2.1**). Grep that file before wiring `Kind`. Do not copy names from lucide.dev.

Suggested map from `MessageBoxImage` (grep-confirmed Kinds):

| Win32 image | Lucide Kind | Brush |
|-------------|-------------|--------|
| Information | `Info` or `BadgeInfo` | `AppBrushAccent` |
| Question | `CircleQuestionMark` | `AppBrushAccent` |
| Warning | `TriangleAlert` | `AppBrushWarning` |
| Error | `CircleAlert` or `OctagonAlert` | `AppBrushDanger` |
| None | hide icon | — |

`CircleAlert` and `TriangleAlert` are in the catalog. Unknown `Kind` fails to bind and shows an empty tile.

## Window icons vs severity

[`Common/WindowIcons.cs`](../../Common/WindowIcons.cs) already has pack URIs for App, ConsoleLog, qBittorrent, Jellyfin. Those belong on **viewer windows** and the main shell, not as `MessageBoxImage` substitutes.

Helper caption text can stay **Jellyfin** / **qBittorrent** / **WARP** as today. Optional later: set `Window.Icon` from `WindowIcons.GetPackUri` when the caption is a branded viewer. Severity Lucide stays in the body; do not mix the Jellyfin .ico into the warning glyph.

## Boot-time dictionaries

[`App.xaml`](../../App.xaml) merges Light colors **before** `OnStartup` runs. Single-instance box (before `ThemeService.Apply`) can still resolve `AppBrush*` from that Light dictionary. After Apply, Dark users get Dark keys for every later box including migration failure.

`ThemeService.CurrentTheme` defaults to `AppTheme.Light` until Apply; that matches App.xaml’s initial merge.
