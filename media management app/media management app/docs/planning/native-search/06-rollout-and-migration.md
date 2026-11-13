# 06 — Triển khai dần, migration và cổng chuyển hẳn

## 1. Feature flag

> **Đã thay bằng §1b (13/11/2026):** `Search.Backend` chỉ còn được đọc một lần để migrate; backend do recipe chọn.

| Giá trị `Search.Backend` | Hành vi |
|---|---|
| `Qbittorrent` (mặc định tới P6) | Đường cũ, không đổi (P1 vẫn áp dụng vì nằm ở bước khớp và chấm điểm) |
| `Native` | Chỉ đường native |
| `Both` | Chạy cả hai, **dùng kết quả native**, ghi log so sánh (số infohash, ứng viên top, thời gian) |

- Recipe v2 có thể đặt `backend` riêng, ví dụ bật native cho **movie trước** (nơi đang đau nhất), anime để sau cùng.
- Cờ đổi được trong Settings mà không cần khởi động lại; lượt search đang chạy dùng cờ tại thời điểm bắt đầu.

## 1b. Backend theo recipe (P6 bước 1, 13/11/2026)

User đã duyệt: **recipe quyết định backend**, Settings không còn chọn backend.

- **Settings → Search engines** chỉ còn: `Search.NativeEnabled` (Enable native search, công tắc tổng), `PauseAll` (kill switch), timeout mỗi request, ngân sách mỗi search, bật/tắt và sức khoẻ từng site.
- **Recipe** (module Search, key `searchBackend`): `Qbittorrent` / `Native` / `Both`. Recipe mới mặc định `Qbittorrent`. Giá trị `Default` không còn trong UI; recipe nào còn `Default` hoặc thiếu key (file import từ bản cũ) thì chạy qBittorrent.
- Quyết định nằm ở `SearchBackendResolver` (Core): native tắt hoặc kill switch bật mà recipe chọn `Native`/`Both` thì **lùi về qBittorrent**, ghi cảnh báo vào log (mỗi recipe tối đa một lần mỗi 30 phút) và vào dòng trạng thái của order (cart và hunt) qua `SearchSession.BackendNote`. Lý do chọn lùi thay vì báo "No search source available": qBittorrent chính là đường search đã chạy ổn trước khi có native, nên hunt không bị đứng; người dùng vẫn thấy rõ vì sao. Trường hợp native bật nhưng mọi site bị bỏ qua (breaker, hết ngân sách) vẫn báo "No search source available" như cũ.
- **Migration một lần** (`SearchBackendMigration` trong Core, chạy lúc khởi động qua `SearchBackendMigrator`):
  - recipe đang `Default`/thiếu key được ghi backend hiệu lực lúc đó: `BackendByKind[TargetKind]` nếu có, không thì `Backend`;
  - `NativeEnabled = true` nếu `Backend` khác `Qbittorrent` hoặc có recipe nào (sau migration) dùng `Native`/`Both`;
  - mỗi recipe đổi được ghi một dòng log; recipe lưu xong mới đặt `Search.RecipeBackendsMigrated = true` rồi lưu settings, nên lưu lỗi thì lần khởi động sau chạy lại.
- `Backend` và `BackendByKind` **vẫn nằm trong settings.json** (không ghi nữa) để bản cũ hơn đọc vẫn chạy như trước; chúng không còn quyết định search nào.
- Trình sửa recipe: backend `Native` làm mờ Plugins, Category, Result limit, phân trang, timeout/idle timeout kèm ghi chú "chỉ dùng cho qBittorrent"; `Both` ghi rõ các ô đó chỉ áp dụng cho phần so sánh qBittorrent; có dòng liệt kê những gì native vẫn dùng (danh sách query, số search song song, chế độ đánh số anime, các module lọc/chấm điểm).
- **Chưa làm (PR sau):** chọn site native theo recipe (danh sách indexer bật/tắt, mặc định theo loại media). Hiện native dùng mọi site đang bật trong Settings cho loại media đó. Xem §3.

## 2. Migration DB (đánh số tiếp từ `008_…`)

> **Thay đổi khi triển khai (12/11/2026):** để tránh migration và không phải đổi interface DB, các mục sau được lưu bằng **file JSON trong state folder** thay cho bảng hoặc cột SQLite. Dữ liệu đều lấy lại được (từ TMDB) hoặc chỉ là trạng thái vận hành:
> - IMDb id: `imdb-ids.json` (cache `tv:<tmdbId>` / `movie:<tmdbId>` → `tt…`, lấy từ TMDB `external_ids` khi EZTV cần)
> - Trạng thái governor: `indexer-state.json` (thay cho bảng `IndexerState`)
> - Tập năm từ TMDB `release_dates` (migration 010) chưa làm. P1 dùng dung sai ±1 năm, đã xử lý được case *Movie 01*.
>
> Các migration còn lại trong bảng dưới chỉ làm khi thật sự cần.

| Migration | Nội dung | Giai đoạn |
|---|---|---|
| `009_tracked_media_imdb_ids.sql` | Thêm `ImdbId TEXT NULL` vào `TrackedShows` và `TrackedMovies`. Backfill dần qua TMDB `external_ids` khi refresh metadata | P3 (cần cho EZTV) |
| `010_movie_release_years.sql` | `TrackedMovies.ReleaseYearsJson` (tập năm từ TMDB `release_dates`) | P1 |
| `011_indexer_state.sql` | Bảng `IndexerState` (`IndexerId`, `DayKey`, `RequestsUsed`, `BreakerState`, `OpenUntilUtc`, `ConsecutiveFailures`, `LastErrorKind`, `LastErrorUtc`, `LastSuccessUtc`) | P2 |
| `012_blacklist_infohash.sql` | Thêm khoá theo infohash vào blacklist | P5 |
| `015_recipe_v2_marker.sql` (hoặc migration file `.rcp`; 013 đã dùng cho `013_reset_torrent_hashes`, 014 cho `014_torrentblacklist_global`) | Đánh dấu recipe đã chuyển sang v2 | P6 |

Mọi migration đi qua `MigrationRunner` hiện có, kèm test trong `MediaManager.Core.Tests/Migrations`.

## 3. Recipe v2

- Module `SearchSource`:
  - `searchBackend` đã có từ P6 bước 1 (§1b);
  - `plugins` (tên plugin qBittorrent) được thay bằng `indexers` (id indexer native) cùng override cho từng indexer (bật/tắt, số trang). **Bước tiếp theo (PR riêng):** key `nativeIndexers` (danh sách id, rỗng = mặc định theo loại media như hiện tại) và ô chọn site native trong trình sửa recipe; khoá memo của `NativeSearchService` phải gồm danh sách này;
  - các key timeout, idle, pagination, snapshot bị **bỏ**, vì governor và orchestrator đã quản lý.
- Gỡ các key chết nằm sai module (SRCH-09).
- `Require` / `Prefer` / `Avoid` thay cho `excludeTerms` / `preferTerms` (xem [05 §4](./05-quality-and-ui.md#4-chấm-điểm-trừ-điểm-thay-vì-loại-cứng-srch-14)).
- **Migration file `.rcp`:**
  - backup nguyên file sang `Recipes/v1-backup/`;
  - ánh xạ plugin sang indexer: `thepiratebay`, `piratebay` → `apibay`; `nyaasi` → `nyaa`; `limetorrents` → `limetorrents`; `subsplease` → `subsplease`; `tsukihime` → `tsukihime`; `torrentscsv` → `torrents-csv`; `solidtorrents`, `bitsearch` → `bitsearch`; `eztvx` → `eztv`; plugin hỏng hoặc bị bỏ thì bỏ qua và ghi log;
  - `enabled` → bộ indexer mặc định theo loại media cộng **Knaben**.
- Recipe v1 vẫn đọc được cho tới P6 (đường cũ cần chúng).

## 4. Cổng chuyển hẳn (trước khi đổi mặc định sang `Native` ở P6)

Chạy `tools/SearchProbe` (thêm chế độ native ở P2), so native với qBittorrent trên **bộ tiêu đề đặc thù** cộng **danh sách show và movie thật** trong DB:

| Tiêu chí | Ngưỡng |
|---|---|
| Số infohash khác nhau | Native ≥ qBittorrent trên ≥ 90% case |
| Anime nổi tiếng và đang chiếu | Ứng viên top 1 của native **không kém** (cùng hoặc tốt hơn theo điểm recipe) trên ≥ 95% case |
| Movie cũ trong danh sách thử | Native tìm ra ứng viên đạt ở mọi case mà qBittorrent tìm ra, **cộng** các case qBittorrent bỏ lỡ |
| Thời gian | p90 mỗi query ≤ 15s |
| Bị chặn | Không có breaker mở vì 429 trong 7 ngày chạy chế độ `Both` |
| Ổn định | Không lỗi chưa xử lý trong 7 ngày chạy chế độ `Both` |

## 5. Danh sách xoá ở P6 (sau khi qua cổng)

- `QbittorrentClient`: `SearchAsync`, `StartSearchAsync`, `GetSearchResultsAsync`, `StopSearchAsync`, `DeleteSearchAsync`, `PostSearchDownloadTorrentAsync` và phần `search/plugins`.
- `QbittorrentSearchPluginService`, `IQbittorrentSearchPluginService`, `QbittorrentSearchCapacityException`, `SearchEngineDiagnostics` (thay bằng telemetry native).
- `ShowSearchSnapshotService`.
- Các vòng polling trong `FetchJobService` (`SearchSingleQueryAsync`, `SearchQueryWithPaginationAsync`, `SearchManyAsync`) và `AutomationFlowService` (`SearchAsync`, `SearchQueryWithPaginationAsync`). Phần còn lại của hai service chuyển sang gọi `ISearchService`.
- Các key search cũ trong `RecipeRuntimeSettings` cùng `AutoTorrentSettings` (`MaxParallelSearches`, `*SearchTimeoutSeconds`, `Snapshot*`).
- UI chọn plugin trong Recipe workspace, thay bằng UI chọn indexer.
- Adapter `QbittorrentSearchService` và feature flag.

## 6. Quay lại khi có sự cố

- Trước P6: đổi cờ về `Qbittorrent`.
- Sau P6: revert PR P6 (đường cũ còn nguyên trong lịch sử git). Giữ tag `pre-native-search-cutover` trước khi merge P6.
