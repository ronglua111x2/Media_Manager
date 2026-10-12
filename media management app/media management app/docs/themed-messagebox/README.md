# Themed MessageBox — app-chrome dialogs

**Status:** Helper implemented. Call sites use [`AppMessageBox`](../../Common/AppMessageBox.cs) + [`AppMessageBoxWindow`](../../Views/AppMessageBoxWindow.xaml). Inventory below remains the record of the 26 former Win32 boxes.  
**Trigger (fixed):** Opening a Library/News title in Jellyfin while the server is down now shows the app-themed dialog (caption **Jellyfin**, `AppBrush*` + custom chrome), not stock Windows `MessageBoxEx`.

`ThemeService` swaps Dark/Light dictionaries; the helper binds `{StaticResource AppBrush*}` so it follows the theme. Do **not** add new `System.Windows.MessageBox.Show` call sites — use `AppMessageBox.Show`.

## Documents (read in order)

| # | File | Contents |
|---|------|----------|
| 0 | [00-executive-summary.md](./00-executive-summary.md) | Why OS boxes appeared; 27 calls; services vs ViewModels; theme apply order |
| 1 | [01-call-site-inventory.md](./01-call-site-inventory.md) | Every former `MessageBox.Show`: caption, buttons, icon, owner, intent class |
| 2 | [02-theme-and-chrome.md](./02-theme-and-chrome.md) | `AppBrush*` tokens, `WindowChrome`, Lucide severity, `ThemeService` swap |
| 3 | [03-library-shape.md](./03-library-shape.md) | Drop-in Show API, owner rules, boot-time constraint |
| 4 | [04-later-code-outline.md](./04-later-code-outline.md) | Replacement sequence (now landed) |

## Related

- Theme tokens: [ui-polish/THEME.md](../ui-polish/THEME.md), source [`Resources/AppThemeColors.Dark.xaml`](../../Resources/AppThemeColors.Dark.xaml) / Light twin
- Layout / chrome: [ui-polish/LAYOUT.md](../ui-polish/LAYOUT.md), [`MainWindow.xaml`](../../MainWindow.xaml), [`Views/WebViewerWindow.xaml`](../../Views/WebViewerWindow.xaml)
- Lucide Kind names: [ui-polish/LUCIDE_ICONS.md](../ui-polish/LUCIDE_ICONS.md)
- Theme apply: [`Services/ThemeService.cs`](../../Services/ThemeService.cs), [`App.xaml.cs`](../../App.xaml.cs)
- Window icons: [`Common/WindowIcons.cs`](../../Common/WindowIcons.cs)
- Jellyfin error copy (no dialog in the service): [`Services/JellyfinMediaNavigationService.cs`](../../Services/JellyfinMediaNavigationService.cs) `FormatUserMessage`

## Out of scope (this folder)

- A separate NuGet or extra csproj
- WPF-UI or Material Design MessageBox controls
- Replacing custom dialogs (`EnginePickerDialog`, `EpisodeOrganizationDialog`, and so on) — only nested message boxes inside them were replaced
- Changing Jellyfin connectivity, copy, or default button results
