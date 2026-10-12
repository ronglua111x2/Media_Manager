# 03 — Library shape (outline only)

**Do not implement in the docs request.** This describes the helper a later Plan Mode / code pass should add. It is not a csproj, NuGet, or DI spec to copy-paste as-is.

## Goal

One in-app modal that:

- Looks like MainWindow / WebViewerWindow ([02-theme-and-chrome.md](./02-theme-and-chrome.md))
- Follows Dark/Light automatically via `AppBrush*`
- Replaces all **26** `System.Windows.MessageBox.Show` calls in [01-call-site-inventory.md](./01-call-site-inventory.md)
- Keeps existing **copy**, **captions**, **button sets**, and **default results** (especially Gemini and WARP default **No**)

## Prefer in-app helper, not a second library

Stay in the existing WPF project:

- `Views/` window XAML (custom chrome)
- Small static helper (e.g. `AppMessageBox.Show(...)`) **or** a thin `IAppMessageBox` registered in DI for ViewModels/services that already use the container

Do **not** add a second csproj or NuGet unless a later code plan has a concrete reuse need outside this app. “Library” in this pack means **one shared Show API**, not a new assembly.

WPF-UI / Material MessageBox / `ContentDialog` are out of scope: they would not match `WindowChrome` + Lucide + `AppBrush*` without another restyle.

## Drop-in Show API

Keep overloads close to current call sites so replacements stay mechanical:

```text
Show(string message, string caption, buttons, image)
Show(Window? owner, string message, string caption, buttons, image)
Show(..., defaultResult)   // required for Gemini + WARP
```

Return a result enum equivalent to `MessageBoxResult` (`OK`, `Cancel`, `Yes`, `No`) so `if (confirm != MessageBoxResult.Yes)` stays a one-token type swap.

Map `MessageBoxButton` / `MessageBoxImage` internally to the helper’s buttons and Lucide glyph. Do not force every caller to invent a new intent enum on day one. Intent class in the inventory (Info / Confirm / Destructive / Blocking error) is documentation for button **styling**, not a required public API.

## Owner

| Today | Later helper |
|-------|----------------|
| `Show(this, …)` in dialog code-behind | Honor `owner`; `CenterOwner`; modal to that dialog |
| Viewer services pass `_window` | Same; confirm-close stays on the WebView2 window |
| Most ViewModels pass nothing | `Application.Current.MainWindow` when loaded; otherwise center screen |

Do not parent to a closed/hidden MainWindow (tray / toast-launch paths in [`App.xaml.cs`](../../App.xaml.cs)). If `MainWindow` is not a valid owner, fall back to no owner.

## Threading

All 26 current callers run on the WPF UI thread (`OnStartup`, RelayCommands, viewer close). Document:

- Show **must** run on the dispatcher (`CheckAccess` + `Invoke`)
- Treat marshal as a **safety rule**, not a fix for a known off-thread bug

`DatabaseService.Initialize` is called from `OnStartup` (STA). Do not move that dialog to a background thread.

## Boot single-instance

[`App.xaml.cs`](../../App.xaml.cs) shows “already running” **before** `ServiceProvider` exists. The helper must work as a **static** call with only `App.xaml` merged dictionaries. Do not require `IThemeService` or constructor injection for that site.

Migration failure runs **after** `Apply` and **after** DI, but `DatabaseService` today calls `MessageBox` from a private static method. Either keep a static Show or inject a UI facade; do not pull `Application.Current` into Core if a later split forbids it. Today `DatabaseService` already lives in the WPF project and already references `System.Windows.MessageBox`.

## What not to replace

Custom dialogs stay:

- [`EnginePickerDialog`](../../Views/EnginePickerDialog.xaml)
- [`EpisodeOrganizationDialog`](../../Views/EpisodeOrganizationDialog.xaml)
- [`SetAutoTrackDialog`](../../Views/SetAutoTrackDialog.xaml)
- [`AddMediaLockDialog`](../../Views/AddMediaLockDialog.xaml)
- [`QueryTemplateBuilderDialog`](../../Views/QueryTemplateBuilderDialog.xaml)
- [`TorrentAddDiskDialog`](../../Views/TorrentAddDiskDialog.xaml)

Only the nested `MessageBox.Show` validation inside three of those files is in scope.

Do **not** use snackbar, tray balloon, or [`IWindowsNotificationService`](../../Services/IWindowsNotificationService.cs) for these 26 sites. They are **blocking** confirms and errors.

Do **not** change [`JellyfinMediaNavigationService`](../../Services/JellyfinMediaNavigationService.cs) error mapping. The helper only displays whatever string the VM already passes.

## Suggested file split (later code)

Illustrative only:

| Piece | Role |
|-------|------|
| `Views/AppMessageBoxWindow.xaml` | Chrome, icon, body, buttons |
| `Views/AppMessageBoxWindow.xaml.cs` | Result, default button focus |
| `Common/AppMessageBox.cs` (static) | Show overloads, dispatcher, owner fallback |

Optional `IAppMessageBox` if tests need a fake. Not required to start; current code has zero MessageBox tests.

## Non-goals for the helper itself

- Localization / resource strings (copy stays in callers)
- Queueing multiple boxes
- Checkbox “don’t ask again” (viewer close already has settings flags)
- Async `ShowAsync` unless a later plan hits a deadlock (none known)
