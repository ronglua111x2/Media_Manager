# 01 — Call-site inventory

**Live grep:** `MessageBox.Show` in `*.cs` / `*.xaml`. **26** calls in **14** files. No WinForms `MessageBox`.

Intent class (for a later helper, not new copy):

- **Info** — OK + Information; validation or “already running”
- **Confirm** — YesNo or OKCancel + Question; reversible or gated action
- **Destructive confirm** — YesNo + Warning (or Gemini privacy gate with default No)
- **Blocking error** — OK + Error/Warning; failure the user must acknowledge

Owner **none** means the call does not pass a `Window`; Win32 centers on the desktop, not the app caption.

Default result **OS default** means the call does not pass `MessageBoxResult`; Windows picks the first button (usually Yes / OK).

---

## Boot

### [`App.xaml.cs`](../../App.xaml.cs) — `OnStartup`

| | |
|--|--|
| Line | ~34 |
| Caption | Media Manager |
| Body | Media Manager is already running. |
| Buttons / image | OK / Information |
| Owner | none |
| Default | OS default |
| Class | Info |
| Notes | Runs **before** DI and `IThemeService.Apply`. Then `Shutdown()`. |

---

## Services (4 files, 4 calls)

### [`DatabaseService.ShowMigrationFailureDialog`](../../Services/DatabaseService.cs)

| | |
|--|--|
| Line | ~2655 (called from `Initialize` ~74) |
| Caption | Media Manager — Database migration failed |
| Body | Multi-line recovery: restore from Drive backup or `CreateSafeSnapshot()`, plus inner exception |
| Buttons / image | OK / Error |
| Owner | none |
| Default | OS default |
| Class | Blocking error |
| Notes | After `ThemeService.Apply`. `Initialize` rethrows; [`App.xaml.cs`](../../App.xaml.cs) `Shutdown()`. |

### [`JellyfinViewerService.UserConfirmedClose`](../../Services/JellyfinViewerService.cs)

| | |
|--|--|
| Line | ~359 |
| Caption | Jellyfin |
| Body | Close the Jellyfin window? |
| Buttons / image | YesNo / Question |
| Owner | `_window` (WebViewerWindow) |
| Default | OS default |
| Class | Confirm |
| Notes | Alias `WpfMessageBox`. Gated by `Jellyfin.ConfirmCloseViewer`. |

### [`QbittorrentViewerService.UserConfirmedClose`](../../Services/QbittorrentViewerService.cs)

| | |
|--|--|
| Line | ~352 |
| Caption | qBittorrent |
| Body | Close the qBittorrent window? |
| Buttons / image | YesNo / Question |
| Owner | `_window` (WebViewerWindow) |
| Default | OS default |
| Class | Confirm |
| Notes | Same pattern as Jellyfin viewer. Gated by `AutoTorrent.ConfirmCloseViewer`. |

### [`GeminiLinkConfirmationService.TryConfirmPackLink`](../../Services/Gemini/GeminiLinkConfirmationService.cs)

| | |
|--|--|
| Line | ~28 |
| Caption | AI-assisted pack linking |
| Body | Gemini AI is enabled. Linking will send special/OVA file names and TMDB episode data to Google for mapping. Continue? |
| Buttons / image | YesNo / Question |
| Owner | none |
| Default | **No** |
| Class | Destructive confirm |
| Notes | Skipped if Gemini off, no API key, or `ConfirmBeforeLink` false. Later helper **must** keep default No. |

[`JellyfinMediaNavigationService`](../../Services/JellyfinMediaNavigationService.cs) is **not** a MessageBox owner. It returns `JellyfinMediaNavigationResult.ErrorMessage` (including the trigger string *Could not reach Jellyfin…*).

---

## ViewModels

### [`LibraryViewModel`](../../ViewModels/LibraryViewModel.cs) — 8 calls

| Line | Method | Caption | Buttons / image | Default | Class | Body (summary) |
|------|--------|---------|-----------------|--------|--------|----------------|
| ~722 | `ResetEpisode` | Reset Episode | OKCancel / Question | OS | Confirm | Clear download/link state for `{EpisodeCode}`; files on disk not deleted |
| ~811 | `ShowJellyfinNavigationError` | Jellyfin | OK / Warning | OS | Blocking error | `result.ErrorMessage` or *Could not open this title in Jellyfin.* **Trigger screenshot** |
| ~1083 | `ResetMovie` | Reset Movie | OKCancel / Question | OS | Confirm | Same reset copy for movie title |
| ~1126 | `RefreshAllFromTmdb` | Refresh Metadata from TMDB | YesNo / Question | OS | Confirm | Refresh all shows and movies from TMDB |
| ~1528 | episode-org command | Episode Organization | OK / Information | OS | Info | No TMDB episode groups; only default seasons |
| ~1556 | same command, after dialog | Change Episode Organization | YesNo / Warning | OS | Destructive confirm | Rebuild seasons; hardlinks/candidates/pack links cleared |
| ~1592 | `DeleteSelectedMedia` | Delete Media | YesNo / Warning | OS | Destructive confirm | Delete `{Title}` from library |
| ~1613 | `DeleteEntireLibrary` | Delete Entire Library | YesNo / Warning | OS | Destructive confirm | Remove all hardlinks, tracked media, jobs, carts |

`ShowJellyfinNavigationError` is shared by `OpenEpisodeInJellyfin` and `OpenSelectedTitleInJellyfin`.

### [`NewsViewModel.OpenEpisodeInJellyfin`](../../ViewModels/NewsViewModel.cs)

| | |
|--|--|
| Line | ~187 |
| Caption | Jellyfin |
| Body | `result.ErrorMessage` or *Could not open this episode in Jellyfin.* |
| Buttons / image | OK / Warning |
| Owner | none |
| Default | OS default |
| Class | Blocking error |
| Notes | Same navigation service as Library; duplicate host, not a second error mapper. |

### [`MainViewModel.UserConfirmedWarpDisconnect`](../../ViewModels/MainViewModel.cs)

| | |
|--|--|
| Line | ~330 |
| Caption | WARP |
| Body | Auto-Track turned WARP on for a job that is still running. Disconnecting may break TMDB recover, torrent hunt, or Jellyfin refresh. Disconnect anyway? |
| Buttons / image | YesNo / Warning |
| Owner | none |
| Default | **No** |
| Class | Destructive confirm |
| Notes | Gated by `Warp.ConfirmDisconnectDuringAutoTrack`. Keep default No. |

### [`SettingsViewModel.RestoreSelectedBackup`](../../ViewModels/SettingsViewModel.cs)

| | |
|--|--|
| Line | ~1507 |
| Caption | Confirm restore |
| Body | Restore database and recipes from `{DisplayLabel}`? Overwrites current DB/recipes. Settings from the backup are saved separately, not applied automatically. |
| Buttons / image | YesNo / Warning |
| Owner | none |
| Default | OS default |
| Class | Destructive confirm |

### [`TorrentWorkspaceViewModel`](../../ViewModels/TorrentWorkspaceViewModel.cs) — 5 calls

| Line | Method | Caption | Buttons / image | Default | Class | Body (summary) |
|------|--------|---------|-----------------|--------|--------|----------------|
| ~808 | `ClearCart` | Clear Cart | YesNo / Question | OS | Confirm | Clear all orders in `{Title}`'s cart |
| ~832 | `ClearCandidates` | Clear Candidates | YesNo / Question | OS | Confirm | Clear all candidates in `{Title}`'s cart |
| ~851 | `ClearAllCarts` | Clear All Carts | YesNo / Question | OS | Confirm | Clear all carts for every media item |
| ~895 | `BlacklistCandidateAsync` | Blacklist listing | YesNo / Warning | OS | Destructive confirm | Blacklist listing for `{Title}` plus name/URL |
| ~1773 | multi-season pack accept | Multi-Season Pack | YesNo / Warning | OS | Destructive confirm | Pack covers seasons; accepting cancels listed cart pack orders |

### [`RecipeWorkspaceViewModel.DeleteRecipe`](../../ViewModels/RecipeWorkspaceViewModel.cs)

| | |
|--|--|
| Line | ~191 |
| Caption | Delete Recipe |
| Body | Delete recipe `{Name}`? |
| Buttons / image | YesNo / Warning |
| Owner | none |
| Default | OS default |
| Class | Destructive confirm |

---

## View code-behind (nested in custom dialogs)

Do **not** replace the parent dialogs. Replace only these `MessageBox.Show` calls. All three pass `this` as owner.

### [`SetAutoTrackDialog.Confirm_Click`](../../Views/SetAutoTrackDialog.xaml.cs)

| Line | Caption | Body | Buttons / image | Class |
|------|---------|------|-----------------|--------|
| ~82 | Auto-Track | Select a checkpoint episode. | OK / Information | Info |
| ~93 | Auto-Track | Select a download folder for auto-track. | OK / Information | Info |

### [`EpisodeOrganizationDialog.Confirm_Click`](../../Views/EpisodeOrganizationDialog.xaml.cs)

| | |
|--|--|
| Line | ~67 |
| Caption | Episode Organization |
| Body | Select an episode organization. |
| Buttons / image | OK / Information |
| Class | Info |

### [`EnginePickerDialog.Confirm_Click`](../../Views/EnginePickerDialog.xaml.cs)

| | |
|--|--|
| Line | ~101 |
| Caption | Engines |
| Body | `errorMessage` from `EnginePickerDialogViewModel.Validate` (*Select at least one enabled engine…*) |
| Buttons / image | OK / Information |
| Class | Info |

---

## Counts by class

| Class | Count | Typical use |
|-------|-------|-------------|
| Info | 6 | Boot, validation, no episode groups |
| Confirm | 8 | Viewer close, reset episode/movie, TMDB refresh, cart clears |
| Destructive confirm | 9 | Delete/restore/blacklist/org/WARP/Gemini/multi-season pack/recipe |
| Blocking error | 3 | Migration; Library + News Jellyfin navigation |

26 = 6 + 8 + 9 + 3.

## Explicit default-No sites (must not change)

- Gemini pack-link confirm
- WARP disconnect during Auto-Track

Every other YesNo/OKCancel uses the OS default (first button). A later helper should keep that unless a code plan decides otherwise **per site**.
