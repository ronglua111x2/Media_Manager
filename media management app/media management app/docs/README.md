# Media Manager Documentation

Comprehensive documentation for the **Media Manager** Windows desktop application — a WPF/C# media library and torrent automation tool integrated with qBittorrent, Jellyfin, TMDB, Gemini, and Google Drive.

**Documentation generated from branch:** `auto-torrent`  
**Commit:** `631c3d7` — *remove add paused and added polling validation after torrent add*

## Documents

| File | Audience | Description |
|------|----------|-------------|
| [APP_OVERVIEW.md](./APP_OVERVIEW.md) | Humans | Purpose, architecture, workflows, integrations, and data model |
| [FEATURES.md](./FEATURES.md) | Humans | Exhaustive feature catalog (UI + background) with code paths and DB tables |
| [IMPROVEMENTS.md](./IMPROVEMENTS.md) | Humans | App evaluation, strengths, gaps, and improvement suggestions |
| [planning/05-sprint-timeline.md](./planning/05-sprint-timeline.md) | Humans | **Execution schedule** — 11 sprints, test gates, migration milestones |
| [planning/06-ai-execution-guide.md](./planning/06-ai-execution-guide.md) | Humans / AI | **How to execute** — AI-assisted workflow, prompts, compressed timeline |
| [planning/00-integrated-roadmap.md](./planning/00-integrated-roadmap.md) | Humans | How the 4 High items connect, phased timeline, industry standards |
| [planning/](./planning/) | Humans | Per-item deep dives (migrations, tests, splits, VM lifetime) |
| [BUILD.md](./BUILD.md) | Humans / AI | **Build & test commands** — x64, MSBuild, Git Bash quirks, ThirdParty restore |
| [AI_CONTEXT.md](./AI_CONTEXT.md) | AI / tooling | Structured machine-readable reference (modules, entities, flows, key files) |
| [LUCIDE_ICONS.md](./LUCIDE_ICONS.md) | Humans / AI | **Valid `PackIconLucideKind` names** for MahApps Lucide 6.2.1 — grep before setting sidebar `IconKind` |
| [STATE_FOLDER.md](./STATE_FOLDER.md) | Humans / AI | Live state folder layout, OAuth paths, FetchJobs purge, sensitive fields |
| [RECIPE_SCHEMA.md](./RECIPE_SCHEMA.md) | Humans / AI | Recipe `.rcp` JSON schema with examples from real state files |
| [debug/](./debug/) | Humans / AI | **Live issue investigations** — symptoms, log evidence, root cause |
| [debug/torrent-hunt/](./debug/torrent-hunt/) | Humans / AI | Auto-Track torrent hunt pipeline debug notes |
| [legacy-overlap/](./legacy-overlap/) | Humans / AI | **Leftover DB/code vs recipes** — show `PreferredQuality` vs cart/hunt matching; inventory; decouple outline (docs only, no DROP) |
| [qbittorrent-webapi/](./qbittorrent-webapi/) | Humans / AI | **qBittorrent 5.1 vs 5.2 WebAPI** — architecture, app inventory, breaking changes, upgrade outline (docs only) |

## Quick Summary

Media Manager automates the lifecycle of TV shows and movies:

1. **Track** media via TMDB (Find/Add, Auto-Track)
2. **Search** torrents via qBittorrent plugins and configurable **recipes**
3. **Acquire** through a **torrent cart** with candidate scoring, validation, and disk assignment
4. **Link** completed downloads into a per-drive library via **hardlinks**
5. **Expose** media to Jellyfin via **symlinks** and library refresh notifications
6. **Backup** state to Google Drive on a schedule

## Tech Stack

- **UI:** WPF (.NET 8), MVVM (CommunityToolkit.Mvvm), WPF-UI, Material Design, Lucide icons
- **Database:** SQLite (`media-manager.db` in state folder)
- **External:** qBittorrent WebUI, Jellyfin API, TMDB API, Google Gemini, Google Drive, Cloudflare WARP CLI
- **Build:** `net8.0-windows`, x64, single-file self-contained publish

## Project Layout

```
media management app/
├── App.xaml.cs              # DI bootstrap, background services startup
├── MainWindow.xaml          # Shell: sidebar nav, status bar, workspace host
├── Common/                  # Enums, constants, shared utilities
├── Models/                  # Domain + settings POCOs
├── ViewModels/              # MVVM view models (41 files)
├── Views/                   # XAML views, dialogs, controls
├── Services/                # Business logic (~157 service files)
│   ├── Backup/              # Google Drive backup
│   ├── Gemini/              # AI special-episode mapping
│   └── Symlink/             # Jellyfin symlink coordination
├── Converters/              # WPF value converters
├── Resources/               # Themes, styles, view templates
└── ThirdParty/Sonarr.Parser # Filename parsing library
```

## Default Paths

| Path | Purpose |
|------|---------|
| `D:\MediaManagerState` | Default state folder (`settings.json`, DB, logs, posters, recipes) |
| `C:\JellyfinLibrary` | Default unified symlink root for Jellyfin |
| Per-drive `MediaManagerLibrary` | Hardlink library roots (auto per drive) |
