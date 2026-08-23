# Media Manager — State Folder Reference

The app stores all persistent runtime data under a single **state folder** (default `D:\MediaManagerState`). The path is configured in `settings.json` → `StateFolder` and loaded at startup by `SettingsService`.

**Code:** `Models/AppSettings.cs`, `Services/SettingsService.cs`, `Common/AppConstants.cs`

---

## Folder Layout (observed from live state)

| Path | Purpose |
|------|---------|
| `settings.json` | Full app configuration (integrations, UI prefs, backup IDs, etc.) |
| `settings-load.log` | Append-only log of settings load/save events |
| `media-manager.db` | Primary SQLite database |
| `media-manager*.db` | Manual/automatic DB backups (naming varies) |
| `logs/` | Rotating system logs (`YYYYMMDD_HHMMSS_mmm_systemlog.txt`) |
| `posters/` | Cached TMDB poster/still images (keyed by provider id) |
| `Recipes/` | Torrent search recipe files (`*.rcp`, JSON) |
| `GoogleDrive/` | Google Drive OAuth storage (see [Google Drive OAuth](#google-drive-oauth)) |
| `gemini-models.json` | Cached Gemini model catalog |
| `gemini-quota.json` | Daily Gemini API quota counters |
| `gemini-mapping-cache.json` | Cached AI special-episode mapping results |
| `Library/` | Optional output library path when `OutputLibraryFolder` points here |
| `WebView2/` | Embedded browser user-data folder for qBittorrent/Jellyfin viewers |

---

## settings.json Structure (top-level keys)

Redacted example — **never commit real secrets**.

```json
{
  "StateFolder": "D:\\MediaManagerState",
  "AutoTorrent": { "QbittorrentWebUiUrl": "...", "DownloadFolders": [], "CategoryName": "..." },
  "Warp": { "Enabled": true },
  "AutoTrack": { "Enabled": true, "AnchorDayOfWeek": 0, "AnchorTimeLocal": "18:00", "Jellyfin": {} },
  "Logs": {},
  "Startup": {},
  "Ui": {},
  "Notifications": { "EnabledByKind": {} },
  "SourceFolders": [],
  "Symlink": { "UnifiedRoot": "C:\\JellyfinLibrary" },
  "TmdbReadAccessToken": "[REDACTED]",
  "Gemini": { "ApiKey": "[REDACTED]" },
  "Backup": {
    "MachineId": "8280cbcd-...",
    "Enabled": true,
    "CredentialsFilePath": "D:\\...\\client_secret_....json",
    "DriveRootFolderId": "...",
    "DriveMachineFolderId": "...",
    "DriveHistoryFolderId": "...",
    "LatestFileId": "..."
  },
  "TorrentValidation": {
    "EnableContentValidation": true,
    "ValidationTimeoutSeconds": 90
  }
}
```

### Sensitive fields (redacted in backups)

`BackupSettingsRedactor` strips these before upload:

- `AutoTorrent.Password`
- `TmdbReadAccessToken`
- `Gemini.ApiKey`
- `AutoTrack.Jellyfin.ApiKey`
- `Backup.CredentialsFilePath` (path only; file itself is not in zip)

---

## Recipes Folder

- **Location:** `{StateFolder}/Recipes/`
- **Format:** One JSON file per recipe, extension `.rcp`
- **Naming:** `{recipeId}.rcp` where `recipeId` is a GUID (no dashes) or a built-in id like `default-tv`
- **Defaults:** `default-tv.rcp`, `default-tv-pack.rcp`, `default-movie.rcp` — auto-created if missing

See [RECIPE_SCHEMA.md](./RECIPE_SCHEMA.md) for the full JSON schema.

**Live inventory example:** 18 user recipes plus 3 defaults (as of Aug 2026).

---

## Google Drive OAuth

Two on-disk locations matter:

| Path | Role |
|------|------|
| `{StateFolder}/GoogleDrive/credentials.json` | **Default** OAuth client JSON (Google Cloud “Desktop app” client secret download) |
| `{Backup.CredentialsFilePath}` | **Optional override** — user can browse to an external `client_secret_*.json` |
| `{StateFolder}/GoogleDrive/token/` | OAuth token cache via Google `FileDataStore` (user id `"media-manager"`) |

### credentials.json format

Standard Google OAuth **Desktop client** JSON downloaded from Google Cloud Console:

```json
{
  "installed": {
    "client_id": "...apps.googleusercontent.com",
    "project_id": "...",
    "auth_uri": "https://accounts.google.com/o/oauth2/auth",
    "token_uri": "https://oauth2.googleapis.com/token",
    "auth_provider_x509_cert_url": "https://www.googleapis.com/oauth2/v1/certs",
    "client_secret": "[REDACTED]",
    "redirect_uris": ["http://localhost"]
  }
}
```

Some downloads use a top-level `"web"` key instead of `"installed"`; `GoogleClientSecrets.FromStreamAsync` accepts both.

### Token folder

After **Connect** in Settings → Backup, `GoogleWebAuthorizationBroker` writes token files under `GoogleDrive/token/`. Filename pattern:

`Google.Apis.Auth.OAuth2.Responses.TokenResponse-media-manager`

Contains refresh/access tokens — **do not share or document contents**.

**Scope used:** `DriveService.Scope.DriveFile` (per-file access only, not full Drive).

**Code:** `Services/Backup/GoogleDriveClient.cs`, `Common/AppConstants.cs` (`BackupGoogleDriveFolderName`, `BackupCredentialsFileName`, `BackupTokenFolderName`)

### Setup steps (summary)

1. Create a Google Cloud project → enable **Google Drive API**
2. OAuth consent screen → add your Google account as **Test user** (if app is in Testing)
3. Credentials → Create **Desktop app** OAuth client → download JSON
4. In app: Settings → Backup → Browse to JSON (or copy to `{StateFolder}/GoogleDrive/credentials.json`)
5. Click **Connect Google Drive** → complete browser consent
6. Enable backup; folder IDs populate automatically on first successful backup

---

## Database

- **File:** `media-manager.db` (SQLite)
- **Init:** `DatabaseService.Initialize()` runs `MigrationRunner.ApplyPendingMigrations()` first (`001_baseline` → `002_fetchjobs_legacy_purge` → `003_torrentblacklist_rebuild`), then the existing `CREATE TABLE IF NOT EXISTS` + `EnsureColumn` safety net
- **History table:** `SchemaMigrations` (`Id`, `Name`, `AppliedUtc`) records each applied script
- **Safe snapshot:** `DatabaseService.CreateSafeSnapshot()` for backups (WAL checkpoint + copy)
- **Migration failure:** startup is blocked with an error dialog; restore `media-manager.db` from a Google Drive backup (Settings → Backup) or a local snapshot from `CreateSafeSnapshot()`

---

## FetchJobs Legacy Purge

One-time migration **`002_fetchjobs_legacy_purge`** (not every boot):

```sql
DELETE FROM FetchJobs;
```

- Table is still created (`001_baseline` / `InitializeFetchJobs`) for schema compatibility
- `FetchJobService` no longer persists rows to this table (in-memory candidate cache only)
- Already-applied databases skip 002 on later startups (`SchemaMigrations` history)
- `DROP TABLE FetchJobs` is deferred (cosmetic)

**Why:** Legacy background fetch job tracking was replaced by `TorrentCartOrders` + in-memory search.

**Code:** `MediaManager.Core/Migrations/002_fetchjobs_legacy_purge.sql` via `MigrationRunner`

---

## TorrentBlacklist Legacy Rebuild

One-time migration **`003_torrentblacklist_rebuild`** (Sprint 3):

- If `TorrentBlacklist` still has legacy `TorrentHash` (NOT NULL, no default), rebuild to the InfoHash-only schema and copy hash data into `InfoHash`
- No-op when `TorrentHash` is already absent (modern / already-rebuilt DBs)
- Implemented in C# (`TorrentBlacklistRebuildMigration`) because the branch depends on `PRAGMA table_info`
- Marker SQL: `MediaManager.Core/Migrations/003_torrentblacklist_rebuild.sql`

**Code:** `MediaManager.Core/Migrations/TorrentBlacklistRebuildMigration.cs` via `MigrationRunner`

---

## Gemini Cache Files

| File | Contents |
|------|----------|
| `gemini-models.json` | Available models, refreshed on startup |
| `gemini-quota.json` | Daily usage counter for quota-limited calls |
| `gemini-mapping-cache.json` | Cached pack special-episode AI mappings (large; keyed by show/file hash) |

---

## Related Docs

- [APP_OVERVIEW.md](./APP_OVERVIEW.md) — workflows using state data
- [RECIPE_SCHEMA.md](./RECIPE_SCHEMA.md) — recipe JSON format
- [FEATURES.md](./FEATURES.md) — backup and recipe features
