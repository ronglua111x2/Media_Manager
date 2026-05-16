# Sonarr-lite Local Importer Plan

## 1. Bối cảnh

App hiện tại đã đi được Phase 1 ban đầu theo hướng:

```text
Scan folder download -> parse file/folder -> đoán show -> match metadata -> hardlink
```

Sau khi thử, flow này chưa thật sự thuận tiện vì app đang bắt đầu từ dữ liệu bẩn. Với torrent downloads, tên file/folder rất lộn xộn, nhiều nguồn khác nhau, nhiều tập bị bọc trong folder riêng, nên nếu bắt đầu bằng scan file thì app phải đoán quá nhiều.

Hướng mới nên học theo Sonarr ở phần quản lý show, nhưng bỏ các phần không cần thiết như indexer, torrent client integration, RSS, auto download. App sẽ trở thành một **Sonarr-lite local importer**:

```text
Add show trước -> sync metadata -> biết danh sách season/episode -> scan downloads -> match file vào episode -> hardlink sang Jellyfin library
```

## 2. Mục tiêu của phase tiếp theo

Mục tiêu chính là chuyển app từ kiểu **file-first organizer** sang **show-first manager**.

Sau phase này, app cần làm được:

1. Tạo hoặc mở một project quản lý media.
2. Cấu hình Jellyfin library output folder.
3. Cấu hình torrent/download scan folders.
4. Add TV show từ TMDB.
5. Fetch và cache metadata của show, seasons, episodes.
6. Hiển thị show detail với danh sách episode và trạng thái missing/linked/matched.
7. Scan download folders và chỉ match file vào các show đang được quản lý.
8. Review các match không chắc.
9. Tạo hardlink sang folder Jellyfin sạch.
10. Lưu toàn bộ state vào project database để không phải gọi API hoặc match lại từ đầu.

Mục tiêu không phải là tạo bản clone Sonarr đầy đủ. Mục tiêu là có một app cá nhân, đơn giản, dễ sửa, phục vụ đúng workflow xem Jellyfin từ torrent downloads.

## 3. Non-goals

Không làm trong phase này:

- Không tích hợp torrent client.
- Không tích hợp indexer.
- Không RSS feed.
- Không tự tìm và tải release.
- Không quality upgrade tự động.
- Không multi-user.
- Không web app.
- Không Docker.
- Không service chạy nền phức tạp.
- Không plugin system.
- Không cố metadata hoàn hảo cho mọi edge case.
- Không anime absolute numbering nâng cao, trừ khi rất dễ thêm.
- Không bắt buộc import ngược từ Jellyfin library folder trong phase này.

## 4. Kiến trúc lưu trữ tổng thể

Nên có 3 loại folder rõ ràng:

```text
Project Folder  = nơi app lưu cấu hình, database, cache, metadata, mapping
Scan Folder     = nơi chứa torrent downloads gốc
Library Folder  = output sạch bằng hardlink cho Jellyfin scan
```

Ví dụ:

```text
D:\MediaManagerProjects\MyShowsProject\
  project.json
  media-manager.db
  cache\
  logs\

D:\TorrentDownloads\
  random.folder.name.1080p-group\
    video.mkv

D:\JellyfinLibrary\TV Shows\
  Breaking Bad\
    Season 01\
      Breaking Bad - S01E01 - Pilot.mkv
```

Vai trò:

```text
TorrentDownloads  -> input bẩn
ProjectFolder     -> brain/source of truth của app
JellyfinLibrary   -> generated output sạch
Jellyfin          -> chỉ đọc output sạch
```

## 5. Nguyên tắc quan trọng: Project là source of truth

Project folder và database là nơi lưu state thật.

Hardlink library folder chỉ là output có thể rebuild.

Nghĩa là:

- Nếu xóa hardlink library folder, app có thể rebuild lại từ DB và source files.
- Nếu đổi naming template, app có thể recreate/move hardlinks.
- Nếu Jellyfin làm gì đó với metadata riêng của nó, app không phụ thuộc vào đó.
- Nếu source file còn và DB còn, output có thể khôi phục.

Không nên lưu state quan trọng của app trong library folder.

Không nên tạo các file kiểu:

```text
.showmanager.json
.episode-map.json
.app-state.db
```

trong Jellyfin library folder.

Lý do:

- Làm bẩn folder Jellyfin.
- Dễ bị xóa nhầm khi rebuild library.
- Khi đổi output folder sẽ khó maintain.
- State bị phân tán.
- App phụ thuộc ngược vào output folder.

Có thể tạo metadata cho Jellyfin như poster/NFO trong tương lai, nhưng đó là metadata phụ cho Jellyfin, không phải state chính của app.

## 6. Project folder nên chứa gì

Đề xuất cấu trúc:

```text
MyShowsProject\
  project.json
  media-manager.db
  cache\
    tmdb\
      shows\
      seasons\
    images\
      posters\
      backdrops\
  logs\
    app.log
```

### 6.1. project.json

Lưu config cấp project:

```json
{
  "projectName": "My Jellyfin TV Library",
  "libraryRoot": "D:\\JellyfinLibrary\\TV Shows",
  "scanFolders": [
    "D:\\TorrentDownloads"
  ],
  "tmdbLanguage": "en-US",
  "namingTemplate": "{SeriesTitle}\\Season {Season:00}\\{SeriesTitle} - S{Season:00}E{Episode:00} - {EpisodeTitle}{Extension}",
  "duplicatePolicy": "Ask",
  "autoMatchConfidenceThreshold": 0.9
}
```

### 6.2. media-manager.db

SQLite database lưu state chính:

- Managed shows.
- TMDB IDs.
- Seasons.
- Episodes.
- Scan folders.
- Scanned source items.
- Parsed results.
- Match decisions.
- Episode-to-file mapping.
- Hardlink output paths.
- Ignored files.
- Metadata sync timestamps.
- Error states.

### 6.3. cache

Cache dữ liệu phụ:

- Raw TMDB JSON nếu cần debug.
- Poster/backdrop images.
- Metadata responses để hạn chế gọi API lại.

Với app cá nhân, có thể lưu metadata quan trọng vào DB, còn raw JSON/images vào cache folder.

## 7. Hardlink library folder có cần lưu thêm gì không?

Không cần lưu state quan trọng.

Hardlink folder chỉ nên chứa file/folder mà Jellyfin cần scan:

```text
D:\JellyfinLibrary\TV Shows\
  The Last of Us\
    Season 01\
      The Last of Us - S01E01 - When You're Lost in the Darkness.mkv
      The Last of Us - S01E02 - Infected.mkv
```

Optional trong tương lai:

```text
poster.jpg
tvshow.nfo
season.nfo
```

Nhưng các file này không nên là source of truth của app.

## 8. Luồng app mới

Luồng tổng thể:

```text
Create/Open Project
  ↓
Set Project Folder
  ↓
Set Jellyfin Library Folder
  ↓
Set Torrent Scan Folders
  ↓
Add Show from TMDB
  ↓
Fetch Show Metadata
  ↓
Show appears in Managed Shows
  ↓
Scan Downloads
  ↓
Match Source Files to Managed Episodes
  ↓
Review Ambiguous Matches
  ↓
Create Hardlinks
  ↓
Jellyfin scans clean library folder
```

## 9. Add Show flow

Có 2 kiểu add show nên hỗ trợ.

### 9.1. Add Show bằng TMDB Search

Đây là flow chuẩn và nên làm trước.

```text
User nhập tên show
  ↓
App gọi TMDB search TV
  ↓
Hiển thị candidate shows
  ↓
User chọn đúng show
  ↓
App fetch TV details
  ↓
App fetch season details cho từng season
  ↓
Lưu show/seasons/episodes vào DB
  ↓
Show xuất hiện trong Managed Shows
```

Ví dụ:

```text
Search: Breaking Bad
Candidates:
1. Breaking Bad (2008)
2. Breaking Bad: Original Minisodes
3. ...
```

Sau khi chọn, app lưu được:

- TMDB ID.
- Name.
- Original name.
- First air date.
- Status.
- Number of seasons.
- Number of episodes.
- Season list.
- Episode list.
- Poster path.

### 9.2. Add Show from Source Folder/File

Làm sau flow search TMDB.

```text
User scan download folder trước
  ↓
App thấy candidate title từ file/folder
  ↓
User chọn “Add as show”
  ↓
App search TMDB bằng title đã parse
  ↓
User chọn show đúng
  ↓
App add show và tiếp tục match source files
```

Flow này tiện khi đã tải sẵn một đống show.

## 10. TMDB API usage

TMDB đủ để app biết show có bao nhiêu season/tập và danh sách episode.

Flow API đề xuất:

### 10.1. Search TV show

```http
GET /3/search/tv?query={query}&language={language}
```

Dùng để tìm show khi user add show.

### 10.2. Get TV show details

```http
GET /3/tv/{series_id}?language={language}
```

Dùng để lấy:

- name
- original_name
- first_air_date
- status
- number_of_seasons
- number_of_episodes
- seasons
- poster_path
- backdrop_path

### 10.3. Get season details

```http
GET /3/tv/{series_id}/season/{season_number}?language={language}
```

Dùng để lấy danh sách episodes trong season:

- episode_number
- name
- air_date
- overview
- runtime
- still_path

### 10.4. Get episode details

```http
GET /3/tv/{series_id}/season/{season_number}/episode/{episode_number}?language={language}
```

Không cần dùng thường xuyên trong phase này. Chỉ dùng nếu sau này cần episode detail sâu hơn.

## 11. Data model đề xuất

### 11.1. ProjectConfig

```csharp
public class ProjectConfig
{
    public string ProjectName { get; set; } = string.Empty;
    public string LibraryRootPath { get; set; } = string.Empty;
    public List<string> ScanFolders { get; set; } = new();
    public string Language { get; set; } = "en-US";
    public string NamingTemplate { get; set; } = "{SeriesTitle}\\Season {Season:00}\\{SeriesTitle} - S{Season:00}E{Episode:00} - {EpisodeTitle}{Extension}";
    public double AutoMatchThreshold { get; set; } = 0.9;
}
```

### 11.2. ManagedShow

```csharp
public class ManagedShow
{
    public int Id { get; set; }
    public int TmdbId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public DateOnly? FirstAirDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public int NumberOfSeasons { get; set; }
    public int NumberOfEpisodes { get; set; }
    public string PosterPath { get; set; } = string.Empty;
    public DateTime? LastMetadataSyncAt { get; set; }
}
```

### 11.3. ManagedSeason

```csharp
public class ManagedSeason
{
    public int Id { get; set; }
    public int ShowId { get; set; }
    public int SeasonNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public int EpisodeCount { get; set; }
    public DateOnly? AirDate { get; set; }
}
```

### 11.4. ManagedEpisode

```csharp
public class ManagedEpisode
{
    public int Id { get; set; }
    public int ShowId { get; set; }
    public int SeasonId { get; set; }
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly? AirDate { get; set; }
    public string Overview { get; set; } = string.Empty;
    public int? RuntimeMinutes { get; set; }
}
```

### 11.5. SourceItem

```csharp
public class SourceItem
{
    public int Id { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public string VideoFilePath { get; set; } = string.Empty;
    public string? ContainerFolderPath { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string Status { get; set; } = "New";
}
```

### 11.6. ParsedSourceItem

```csharp
public class ParsedSourceItem
{
    public int Id { get; set; }
    public int SourceItemId { get; set; }
    public string RawTitle { get; set; } = string.Empty;
    public string ParsedShowTitle { get; set; } = string.Empty;
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string Quality { get; set; } = string.Empty;
    public string ReleaseGroup { get; set; } = string.Empty;
    public double Confidence { get; set; }
}
```

### 11.7. MatchDecision

```csharp
public class MatchDecision
{
    public int Id { get; set; }
    public int SourceItemId { get; set; }
    public int? ShowId { get; set; }
    public int? EpisodeId { get; set; }
    public double Confidence { get; set; }
    public string DecisionType { get; set; } = string.Empty; // Auto, Manual, Ignored, Rejected
    public DateTime CreatedAt { get; set; }
}
```

### 11.8. EpisodeLink

```csharp
public class EpisodeLink
{
    public int Id { get; set; }
    public int EpisodeId { get; set; }
    public int SourceItemId { get; set; }
    public string SourceVideoPath { get; set; } = string.Empty;
    public string LibraryVideoPath { get; set; } = string.Empty;
    public string LinkStatus { get; set; } = string.Empty; // Linked, LinkLost, SourceLost, Failed
    public DateTime? LinkedAt { get; set; }
}
```

## 12. Episode status model

Episode status nên được tính từ DB, không nhất thiết lưu cứng.

Các trạng thái chính:

```text
Missing      = chưa có source file match vào episode
Matched      = đã tìm thấy source file nhưng chưa tạo hardlink
Linked       = đã tạo hardlink
Conflict     = nhiều source file cùng map vào một episode
SourceLost   = source file gốc bị mất
LinkLost     = hardlink output bị mất
Ignored      = user bỏ qua
Failed       = từng cố link nhưng lỗi
```

Ví dụ UI:

```text
The Last of Us

Season 1
S01E01 - When You're Lost in the Darkness    Linked
S01E02 - Infected                            Missing
S01E03 - Long, Long Time                     Matched
S01E04 - Please Hold to My Hand              Missing
```

## 13. UI/UX mới

Phase tiếp theo nên chuyển từ Inbox-first sang Show-first.

### 13.1. Sidebar

```text
Project
Shows
Imports
Settings
Logs
```

### 13.2. Project page

Hiển thị:

- Project name.
- Project folder.
- Library folder.
- Number of managed shows.
- Number of episodes.
- Linked/missing count.
- Last scan time.

### 13.3. Shows page

Danh sách show đã add:

```text
Breaking Bad       62 episodes   58 linked   4 missing
The Last of Us      9 episodes    9 linked    0 missing
The Bear           28 episodes   12 linked   16 missing
```

Actions:

- Add show.
- Sync metadata.
- Open show detail.
- Remove show from project.

### 13.4. Add Show dialog

Flow:

```text
Search box
  ↓
TMDB candidate results
  ↓
Select candidate
  ↓
Fetch metadata
  ↓
Add show
```

Candidate item nên hiển thị:

- Poster.
- Title.
- First air year.
- Overview ngắn.
- Original language.

### 13.5. Show detail page

Hiển thị:

- Poster.
- Title.
- Status.
- First air date.
- Number of seasons.
- Number of episodes.
- Last metadata sync.
- Season tabs hoặc season list.
- Episode table.

Episode table columns:

```text
Episode
Title
Air Date
Status
Source File
Library File
Action
```

Actions:

- Link matched file.
- Review match.
- Clear match.
- Open source folder.
- Open library folder.

### 13.6. Imports page

Nơi xử lý files scan được.

Columns:

```text
Source file
Parsed guess
Suggested show
Suggested episode
Confidence
Status
Action
```

Actions:

- Scan now.
- Review.
- Link.
- Ignore.
- Add as show.

### 13.7. Settings page

Cấu hình:

- Project folder.
- Jellyfin library folder.
- Scan folders.
- TMDB API key.
- Language.
- Naming template.
- Auto-match threshold.
- Duplicate policy.

## 14. Matching logic mới

Thay vì match file với toàn bộ TMDB, app nên match file với các managed shows trước.

Flow:

```text
Scan source file
  ↓
Parse filename/foldername
  ↓
Extract possible show title, season, episode
  ↓
Compare show title with ManagedShow list
  ↓
If show match + S/E exists -> suggest ManagedEpisode
  ↓
If confidence high -> Matched
  ↓
If confidence medium -> Needs Review
  ↓
If no show match -> Unrecognized / Add as Show
```

Ưu điểm:

- Ít gọi TMDB hơn.
- Ít match nhầm hơn.
- App biết trước episode list.
- Có thể hiện missing/linked rõ ràng.

## 15. Hardlink output logic

Naming template Phase này:

```text
{SeriesTitle}\Season {Season:00}\{SeriesTitle} - S{Season:00}E{Episode:00} - {EpisodeTitle}{Extension}
```

Ví dụ:

```text
D:\JellyfinLibrary\TV Shows\The Last of Us\Season 01\The Last of Us - S01E02 - Infected.mkv
```

Nguyên tắc:

- Không rename source file.
- Không move source file.
- Không copy file trong Phase này.
- Chỉ tạo hardlink.
- Nếu source và library khác volume, báo lỗi rõ.
- Nếu output đã tồn tại, dùng duplicate policy.

Duplicate policy Phase này nên đơn giản:

```text
Ask
Skip existing
Replace existing
Keep both with suffix
```

Phase đầu nên chọn mặc định: `Ask` hoặc `Skip existing`.

## 16. Repair/Rebuild concept

Chưa cần làm full trong phase này, nhưng kiến trúc nên chuẩn bị.

Vì project DB là source of truth và library folder là output, sau này có thể làm:

### Rebuild Library

Xóa hoặc bỏ qua output hiện có rồi tạo lại hardlinks từ DB.

### Repair Links

Kiểm tra:

- source file còn không?
- library hardlink còn không?
- output path đúng naming template hiện tại không?

### Clean Orphans

Tìm file trong library folder không có record trong DB.

### Rename Template Migration

Đổi naming template rồi recreate/move hardlinks.

## 17. Import từ hardlink/library folder

Không cần làm trong phase này.

Chỉ cần nếu:

- User đã có Jellyfin library cũ.
- Project DB bị mất nhưng library còn.
- Muốn reconstruct mapping từ output folder.

Tính năng này để sau:

```text
Import Existing Library
  ↓
Scan Jellyfin library folder
  ↓
Parse clean folder/file names
  ↓
Match to managed episodes
  ↓
Create EpisodeLink records
```

Phase này chỉ cần import từ scan folders.

## 18. Phase breakdown đề xuất

### Phase 1B: Project + Managed Shows

Mục tiêu: tạo xương sống show-first.

Cần làm:

- Project folder model.
- Project open/create.
- project.json config.
- SQLite DB setup.
- Settings page: library folder, scan folders, TMDB API key.
- TMDB client.
- Add Show dialog.
- Fetch show details.
- Fetch seasons/episodes.
- Save show/seasons/episodes to DB.
- Shows page.
- Show detail page với episode status mặc định Missing.

Done criteria:

- Tạo project được.
- Add show từ TMDB được.
- App hiển thị danh sách seasons/episodes đầy đủ.
- Đóng/mở app không mất show.
- Không cần scan file ở phase này.

### Phase 1C: Local Import into Managed Episodes

Mục tiêu: scan files và match vào show đã add.

Cần làm:

- Scan folders.
- Detect video files.
- Detect folder container episode.
- Parse filename/foldername.
- Match against ManagedShows.
- Match season/episode to ManagedEpisode.
- Imports page.
- Review ambiguous match.
- Save MatchDecision.
- Mark episodes as Matched.

Done criteria:

- Scan download folder được.
- File như `The.Last.of.Us.S01E02.1080p.mkv` match vào show đã add.
- Episode status đổi từ Missing sang Matched.
- Case không chắc vào Review.

### Phase 1D: Hardlink Output

Mục tiêu: tạo library sạch cho Jellyfin.

Cần làm:

- Build output path từ naming template.
- Check same volume for hardlink.
- Create hardlink.
- Link subtitle cơ bản nếu cùng folder và cùng base name.
- Save EpisodeLink.
- Episode status Linked.
- Handle duplicate simple.
- Open source/library folder actions.

Done criteria:

- Bấm Link tạo hardlink đúng folder.
- Jellyfin có thể scan folder output.
- Đóng/mở app vẫn biết file đã Linked.

### Phase 1E: Polish tối thiểu

Mục tiêu: dùng hàng ngày bớt khó chịu.

Cần làm:

- Better error messages.
- Refresh metadata button.
- Rescan button.
- Ignore source item.
- Clear match.
- Basic logs UI.
- Basic stats dashboard.

## 19. Việc của người dùng vs Codex

### Người dùng nên làm

- Chốt UX flow.
- Chọn naming template.
- Cung cấp TMDB API key.
- Cung cấp sample filenames/folder paths thật.
- Test với folder torrent thật.
- Review match sai và chỉ ra rule cần sửa.
- Quyết định duplicate policy.
- Quyết định language metadata.

### Codex nên làm

- Refactor architecture sang show-first.
- Tạo project config model.
- Tạo SQLite schema.
- Implement TMDB client.
- Implement Add Show dialog.
- Implement Shows/Show Detail UI.
- Implement scanner.
- Implement parser.
- Implement matcher.
- Implement hardlink service.
- Implement repositories.
- Viết unit tests cho parser/matcher/path builder.

## 20. Prompt đề xuất cho Codex

Dùng prompt này để Codex hiểu hướng refactor:

```text
We need to refactor the app direction from file-first scanning to a Sonarr-lite show-first local importer.

Current app direction is not convenient because it starts from messy torrent files and tries to infer everything. New direction:
- Project folder is the source of truth.
- SQLite DB stores managed shows, seasons, episodes, scanned source items, match decisions, and hardlink records.
- Jellyfin library folder is generated output only.
- Torrent scan folders are input only.
- User adds shows from TMDB first.
- App fetches show/seasons/episodes metadata and stores it locally.
- Then scanner matches local files only against managed shows.
- Ambiguous matches go to review.
- Confirmed matches create hardlinks into Jellyfin library folder.

Please inspect the existing codebase and propose a refactor plan. Do not implement everything at once.

First target slice: Phase 1B Project + Managed Shows.
Implement:
1. Project config model if missing.
2. SQLite schema/entities for ManagedShow, ManagedSeason, ManagedEpisode.
3. TMDB client abstraction and implementation.
4. Add Show flow using TMDB TV search.
5. Fetch TV details and season details.
6. Persist shows/seasons/episodes.
7. Shows page and Show Detail page showing episode list with Missing status.

Keep the implementation pragmatic and simple. Avoid overengineering. This is a personal WPF .NET 8 app.
```

## 21. Kết luận thiết kế

Hướng mới nên chốt như sau:

```text
Project folder = workspace/source of truth
SQLite DB      = state chính
TMDB cache     = metadata local
Scan folders   = input bẩn
Library folder = output generated bằng hardlink
Jellyfin       = chỉ đọc library folder
```

Flow đúng:

```text
Add show -> sync metadata -> biết episode list -> scan files -> match to episode -> review -> hardlink
```

Đây là hướng hợp lý hơn so với flow ban đầu vì app không còn phải đoán mọi thứ từ dữ liệu bẩn. Nó trở thành một **personal Sonarr-lite local importer**, đúng với nhu cầu: quản lý show và import file local bằng hardlink cho Jellyfin.
