# Findings Register

## Disposition

No Critical defect was verified in the original audit.

AUD-002 was later approved and fixed: failed qBittorrent deletion is retried, verified, paused
as a fail-safe, and then halts the current add batch. AUD-001 remains an accepted
trade-off (add-then-validate while running).

## Summary

| ID | Severity | Finding | Confidence | Status |
|---|---|---|---|---|
| AUD-001 | High | Torrent downloads while content validation is pending | High | Open — accepted trade-off |
| AUD-002 | High | Failed malicious-torrent deletion is ignored by the add gate | High | Fixed |
| AUD-003 | High | In-process restore can replace SQLite while writers are active | High | Open |
| AUD-004 | High | Background lifecycle event updates WPF state off the UI thread | High | Open |
| AUD-005 | Medium | Tracked media is deleted after hardlink removal errors | High | Open |
| AUD-006 | Medium | Blacklist lookup fails open on database errors | High | Open |
| AUD-007 | Medium | Chart dependencies restore through incompatible framework assets | High | Open |

## High findings

### AUD-001 — Torrent downloads while content validation is pending

Affected code:

- `Services/ITorrentAddGateService.cs:45-73`
- `Services/ITorrentAddGateService.cs:112-190`
- `Services/QbittorrentClient.cs:1229+`

Evidence:

`AddPausedValidateAndResumeAsync` constructs `AddTorrentRequest` with
`Paused = false`. It then waits up to 120 seconds for the file list before
running content validation. The client exposes `PauseTorrentsAsync`, but the
gate does not call it. The method name says “paused, validate, resume,” while
the implementation and logging explicitly allow the download to continue.

Impact and preconditions:

- Applies to normal cart and Auto-Track additions when content validation is
  enabled.
- A rejected executable, obfuscated file, or other unwanted payload can begin
  downloading before its file list is validated.
- The application does not execute that payload, and later cleanup requests
  deletion, so this does not meet the Critical code-execution threshold.

Status: accepted trade-off. Pause-on-add previously caused add regressions
(`search/downloadTorrent` cannot carry pause/stop). The remaining control is
AUD-002: unverified cleanup must halt and surface the hash. See
[06-aud-002-cleanup-failure.md](06-aud-002-cleanup-failure.md).

### AUD-002 — Failed malicious-torrent deletion is ignored by the add gate

Affected code:

- `Services/ITorrentAddGateService.cs:80-109`
- `Services/ITorrentAddGateService.cs:149-185`
- `Services/ITorrentCleanupService.cs:44-82`
- `Services/QbittorrentClient.cs:1201-1226`

Evidence (pre-fix):

`DeleteTorrentsAsync` logged and swallowed non-success responses and exceptions.
`TorrentCleanupService.DeleteTorrentAsync` therefore returned `false` when it
could not verify deletion. Both malicious-content branches in
`TorrentAddGateService` awaited that method but discarded its Boolean result,
continued to blacklist, and threw as though cleanup completed.

Impact and preconditions:

- Requires qBittorrent deletion or verification to fail after the application
  identifies a blacklisted or malicious torrent.
- The torrent and its files can remain active while the cart/Auto-Track caller
  receives a terminal rejection.
- Combined with AUD-001, rejected content may continue downloading without an
  automatic retry or distinct cleanup-failed state.

Status: **fixed**. Delete API failures throw. Cleanup retries up to three times
with a 20-second timeout independent of user cancellation, verifies the torrent
is gone, and on unverified removal best-effort pauses then throws
`TorrentCleanupFailedException` with the hash. The add gate still blacklists
malicious listings when possible, but never reports a successful malware reject
in that state. Cart batch add/retry and Auto-Track hunt halt instead of trying
the next candidate. Confirmed deletion still throws `MaliciousTorrentException`.

Regression tests: `MediaManager.App.Tests` (9 passed). Details:
[06-aud-002-cleanup-failure.md](06-aud-002-cleanup-failure.md).

### AUD-003 — In-process restore can replace SQLite while writers are active

Affected code:

- `Services/Backup/BackupService.cs:144-166`
- `Services/Backup/BackupService.cs:168-279`
- `ViewModels/SettingsViewModel.cs:1498-1528`
- `App.xaml.cs:99-109`

Evidence:

The normal application starts Auto-Track, backup, and symlink background
services. Settings restore downloads a backup and synchronously copies its
database over the active `media-manager.db` after `SqliteConnection.ClearAllPools`.
There is no application-wide database gate, scheduler pause, active-operation
drain, process restart, or exclusive restore mode. `ClearAllPools` does not
coordinate already active operations. Restore then calls database
initialization once in `BackupService` and again in `SettingsViewModel`.

Impact and preconditions:

- Requires a user-confirmed restore while a UI or background database operation
  is active.
- File replacement can fail, race a writer, or leave process-lifetime
  ViewModels and services holding state from before the restore.
- The backup itself remains available, so irreversible loss was not proven.

Status: unsafe concurrency window is statically proven; corruption was not
reproduced against live state.

Minimal recommendation: perform restore only in a quiesced/restart path, with
exclusive ownership and integrity validation before reopening the app.

### AUD-004 — Background lifecycle event updates WPF state off the UI thread

Affected code:

- `Services/AppLifecycleService.cs:18-48`
- `ViewModels/MainViewModel.cs:140-148`
- `ViewModels/MainViewModel.cs:622-631`
- `ViewModels/LibraryViewModel.cs:2752-2772`
- `ViewModels/TorrentWorkspaceViewModel.cs:2037-2057`

Evidence:

`EnterBackgroundMode` uses `Task.Run`; after a five-minute delay it raises
`AppModeChanged` directly on that worker thread. `MainViewModel` handles the
event by stopping or starting a UI-created `DispatcherTimer`.
`LibraryViewModel` and `TorrentWorkspaceViewModel` clear bound poster
properties and collections without dispatcher marshaling. Other application
event handlers explicitly marshal to the WPF dispatcher, confirming that
events are not generally assumed to be UI-affine.

Impact and preconditions:

- Triggered after the app remains hidden or minimized for the debounce period.
- WPF dispatcher-owned state is accessed from a worker thread, which can throw
  or produce inconsistent UI notification behavior.
- The exception occurs in an unawaited background task, reducing visibility
  and potentially preventing later lifecycle work.

Status: thread transition and handlers are statically proven; the five-minute
runtime scenario was not executed.

Minimal recommendation: marshal the mode transition/event to the application
dispatcher, or marshal every UI subscriber.

## Medium findings

### AUD-005 — Tracked media is deleted after hardlink removal errors

Affected code:

- `Services/LibraryManagementService.cs:39-61`
- `Services/LibraryManagementService.cs:74-119`
- `Services/HardlinkService.cs:127-183`

Evidence:

Library deletion merges `RemoveShowLinks` or `RemoveMovieLinks` error counts,
then unconditionally deletes tracked rows, cart state, and poster state.
`HardlinkService.RemoveHardLink` can return `false` for a locked file,
permission failure, unresolved root, or path-safety refusal. The result is
reported but does not stop metadata deletion.

Impact:

Generated hardlinks can remain without corresponding tracked database state,
making later cleanup and reconciliation harder. Source media is protected by
the hardlink path guards, so this is not a source-data-loss finding.

Status: statically proven.

Minimal recommendation: retain tracking when unlinking fails, or persist an
explicit cleanup-required record.

### AUD-006 — Blacklist lookup fails open on database errors

Affected code:

- `Services/ITorrentBlacklistService.cs:88-107`
- `Services/ITorrentAddGateService.cs:50-58`
- `Services/AutoTrackService.cs:1131`

Evidence:

`IsBlacklisted` catches every exception and returns `false`. Its comments
acknowledge the malware tradeoff. Both the add gate and Auto-Track candidate
filter therefore treat a database failure as permission to continue.

Impact:

A transient SQLite error can allow a previously rejected URL or infohash to be
selected and added again. Content validation remains a second control, so this
does not independently reach High or Critical severity.

Status: statically proven.

Minimal recommendation: abort the add when blacklist state cannot be checked,
while allowing non-add UI operations to remain available.

### AUD-007 — Chart dependencies restore through incompatible framework assets

Affected code:

- `media management app.csproj:61-76`
- generated restore graph in `obj/project.assets.json`

Evidence:

Both restore and 64-bit MSBuild report `NU1701` for `OpenTK 3.3.1`,
`OpenTK.GLWpfControl 3.3.0`, and `SkiaSharp.Views.WPF 3.119.0`. NuGet selects
.NET Framework assets rather than assets matching the app's
`net8.0-windows10.0.17763.0` target. The packages are transitive through the
WPF chart dependency.

Impact:

The current app compiles, but NuGet cannot guarantee runtime compatibility for
the affected chart path. No chart runtime failure was reproduced, so this is a
compatibility risk rather than a functional failure.

Status: warning reproduced on restore and Release/x64 MSBuild.

Minimal recommendation: evaluate a compatible chart dependency version in a
separate approved maintenance session; do not upgrade during this audit.

## Reviewed concerns not promoted to findings

- `DispatcherUnhandledException` is logged but not marked handled. Crashing
  after an unhandled UI exception can be an intentional fail-fast policy; no
  contrary recovery policy was found.
- `AutomationFlowService.RunNowAsync` bypasses the add gate, but repository
  search found no caller. It is latent dead API, not a current execution path.
- `SourceReconciliationService` and `MarkMissingSourceItems` are registered or
  implemented but have no production caller. This is dead/incomplete behavior,
  not evidence of a currently promised workflow failing.
- Pack linking and scan upserts can complete partially. Their results expose
  per-item errors and retries are possible; atomic all-or-nothing behavior is
  not claimed.
- `BuildSourcePath` trusts qBittorrent file names. Exploitation would require
  a malformed or compromised local qBittorrent API and an existing external
  path; no reachable traversal case was proven.
- Backup manifests contain SHA-256 checksums, but restore does not consume
  them. Zip extraction still validates archive structure and CRC; no corrupt
  accepted backup was reproduced.
- Settings contain credentials in plaintext. This is documented behavior for
  the user-selected state folder. Cloud backup creation redacts known API keys,
  usernames, and passwords.
- WebView2 new-window handlers allow only HTTP(S) external launches and retain
  configured-host navigation in the embedded viewer.
- Numeric IDs for tracked shows and movies share the blacklist's `ShowId`
  field. A practical cross-media false match also requires the same normalized
  listing URL or infohash, so collision impact was not demonstrated.
- Recipe import and its unvalidated recipe ID have no production caller.
- The status timer uses an asynchronous tick handler, but no concrete overlap
  failure was reproduced.

## Follow-up

AUD-002 was implemented after the original documentation-only audit. AUD-001
was explicitly left unchanged. Isolated live qBittorrent delete-failure injection
was not run against the live state folder.
