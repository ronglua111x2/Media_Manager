# 04 — Later code outline

**Do not implement in the docs request.** This is the outline for a later Plan Mode / code pass.

When implementing, write a **local plan** (same idea as [`docs/planning/sprint-plans/`](../planning/sprint-plans/README.md)): read the 14 files end-to-end, then list exact diffs, MSBuild x64 per [`docs/BUILD.md`](../BUILD.md), and Dark/Light verify steps. This file only names **phases and constraints**.

## Constraints

- Keep **copy, captions, button sets, and default results** identical. Gemini pack-link and WARP disconnect stay default **No**.
- Tokens and chrome only as in [02-theme-and-chrome.md](./02-theme-and-chrome.md). No new hex. Lucide Kind names grepped from [ui-polish/LUCIDE_ICONS.md](../ui-polish/LUCIDE_ICONS.md).
- In-app helper, not a second csproj ([03-library-shape.md](./03-library-shape.md)).
- Do not replace custom dialogs; only nested `MessageBox.Show`.
- Do not change Jellyfin reachability or `FormatUserMessage`.
- Static Show must work from `App.OnStartup` before DI.

## Suggested sequence

### 1. Helper window + Show API

Add the themed window and static overloads (`message`, `caption`, buttons, image, optional owner, optional default result). Wire Lucide severity and `AppBrush*` buttons. Focus the default button to match Win32.

### 2. Replace services first (4 files)

| File | Why first |
|------|-----------|
| [`JellyfinViewerService`](../../Services/JellyfinViewerService.cs) | Owner = viewer; matches screenshot product area |
| [`QbittorrentViewerService`](../../Services/QbittorrentViewerService.cs) | Twin of Jellyfin viewer |
| [`GeminiLinkConfirmationService`](../../Services/Gemini/GeminiLinkConfirmationService.cs) | Default No must be proven |
| [`DatabaseService`](../../Services/DatabaseService.cs) | Blocking Error + long body; after theme Apply |

Drop the `WpfMessageBox` aliases in the viewer services when the helper is in.

### 3. ViewModels, then dialog validation

Order of risk (destructive last among VMs is optional; mechanical replace is fine):

1. [`NewsViewModel`](../../ViewModels/NewsViewModel.cs) + [`LibraryViewModel.ShowJellyfinNavigationError`](../../ViewModels/LibraryViewModel.cs) — trigger screenshot path
2. Remaining [`LibraryViewModel`](../../ViewModels/LibraryViewModel.cs) boxes
3. [`MainViewModel`](../../ViewModels/MainViewModel.cs) WARP (default No)
4. [`SettingsViewModel`](../../ViewModels/SettingsViewModel.cs) restore
5. [`TorrentWorkspaceViewModel`](../../ViewModels/TorrentWorkspaceViewModel.cs)
6. [`RecipeWorkspaceViewModel`](../../ViewModels/RecipeWorkspaceViewModel.cs)
7. [`App.xaml.cs`](../../App.xaml.cs) single-instance (pre-DI static Show)
8. [`SetAutoTrackDialog`](../../Views/SetAutoTrackDialog.xaml.cs), [`EpisodeOrganizationDialog`](../../Views/EpisodeOrganizationDialog.xaml.cs), [`EnginePickerDialog`](../../Views/EnginePickerDialog.xaml.cs) — owner = `this`

After the pass, grep must find **zero** `System.Windows.MessageBox.Show` / `WpfMessageBox.Show` in app code.

### 4. Visual check (Dark and Light)

Switch theme in Settings and hit one of each class:

| Class | Easy path |
|-------|-----------|
| Info | Second-instance launch, or EnginePicker with no engine selected |
| Confirm | Clear Cart, or close Jellyfin viewer with confirm enabled |
| Destructive confirm | Delete recipe (low blast radius) or Gemini pack-link with confirm on |
| Blocking error | Open in Jellyfin with Jellyfin stopped (the original screenshot) |

Also open the migration box only if a later plan has a safe way to trigger it; do not corrupt the live DB to verify.

Confirm: owner-centered on EnginePicker; viewer-centered on WebViewerWindow; MainWindow-centered for Library.

## Tests to add when coding (optional)

There are no MessageBox tests today. If adding any:

- Default result No for Gemini-shaped and WARP-shaped overloads
- Dispatcher invoke does not deadlock when already on UI thread
- Owner null when MainWindow is hidden still shows the window

UI pixel tests are not required.

## Explicitly later / not this outline

- Snackbar for non-blocking toasts
- “Don’t ask again” on the helper (use existing settings flags)
- Restyling EnginePicker / other custom dialogs’ **OS caption** (separate from MessageBox)
- Changing any confirmation copy or adding new confirms
- WPF-UI `MessageBox` control

## Non-goals for the first code pass

- New color tokens
- New assembly
- Moving `DatabaseService` off `System.Windows`
- i18n
