# 01 — Kiến trúc

## 1. Luồng dữ liệu

```
Caller (Cart / Auto-Track / Recipe dry-run)
   │  SearchRequest { MediaKind, TmdbId, ImdbId?, Titles[], Year?, Season?, Episodes[], Recipe }
   ▼
ISearchService  ── feature flag ──►  QbittorrentSearchService (đường cũ, giữ tới P6)
   │                                   └─ bao lại FetchJobService / ShowSearchSnapshotService / AutomationFlowService
   ▼
NativeSearchService
   ├─ QueryPlanner           → QueryPlan { per-indexer IndexerQuery[] }
   ├─ SearchOrchestrator     → chạy song song, stream, dừng sớm
   │     └─ IndexerRequestGovernor → ITorrentIndexer.SearchAsync(...)
   ├─ ResultNormalizer       → NormalizedResult (infohash, thuộc tính đã parse)
   ├─ JunkFilter             → loại hoặc gắn cờ rác
   ├─ ResultMerger           → gộp theo infohash, gom nguồn, seed lớn nhất
   └─ CandidateEvaluation    → khớp, chấm điểm (P1), trả SearchOutcome
```

`SearchOutcome` gồm danh sách ứng viên đã chấm điểm và **telemetry**: số dòng theo indexer, thời gian, lý do dừng, indexer bị bỏ qua và lý do. Cart, Auto-Track và Recipe dry-run tiêu thụ đúng một kiểu kết quả này, nên các đường search song song hiện nay biến mất (SRCH-06, SRCH-07).

## 2. Interface chính (phác thảo)

```csharp
// MediaManager.Core/Search/Indexers/ITorrentIndexer.cs
public interface ITorrentIndexer
{
    string Id { get; }                       // "knaben", "apibay", "nyaa", "eztv"...
    string DisplayName { get; }
    IndexerCapabilities Capabilities { get; }
    IAsyncEnumerable<IndexerResult> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken ct);
}

public sealed record IndexerCapabilities(
    MediaKindSet Kinds,                      // Movie | TvEpisode | TvSeasonPack | Anime
    bool SupportsImdbLookup,                 // EZTV
    bool SupportsFreeText,
    bool SupportsPaging,
    int MaxPages,
    bool IsMetaIndexer,                      // Knaben gom nguồn khác
    bool IsDhtIndex,                         // BT4G, bitsearch: độ tin cậy thấp hơn
    TrustLevel DefaultTrust);

public sealed record IndexerQuery(string Text, string? ImdbId, int? Season, int? Episode, int Page, MediaKind Kind);

public sealed record IndexerResult(
    string Title, long SizeBytes, int Seeders, int Leechers,
    string? InfoHash, string? MagnetUri, string? TorrentUrl, string? DetailsUrl,
    DateTimeOffset? PublishedUtc, string? Uploader, string? Category, string IndexerId);
```

- `IIndexerHttp` là **lối duy nhất ra mạng** của indexer. Nó được `IndexerRequestGovernor` cấp và bọc `HttpClient` (xem [03](./03-request-governor.md)). Indexer **không** tự tạo `HttpClient`.
- Indexer chỉ **parse**. Nó không lọc theo recipe và không chấm điểm. Nhờ vậy test parser hoàn toàn bằng fixture được.

```csharp
public interface ISearchService
{
    Task<SearchOutcome> SearchEpisodesAsync(EpisodeSearchRequest request, IProgress<SearchProgress>? progress, CancellationToken ct);
    Task<SearchOutcome> SearchSeasonPacksAsync(PackSearchRequest request, IProgress<SearchProgress>? progress, CancellationToken ct);
    Task<SearchOutcome> SearchMovieAsync(MovieSearchRequest request, IProgress<SearchProgress>? progress, CancellationToken ct);
}
```

## 3. Vị trí code

| Thành phần | Project | Lý do |
|---|---|---|
| `ITorrentIndexer` và các indexer, parser, `QueryPlanner`, `TitleNormalizer`, `YearMatcher`, `ResultNormalizer`, `JunkFilter`, `ResultMerger`, logic thuần của governor | **`MediaManager.Core`** (`Search/…`) | Thuần logic, test được bằng `MediaManager.Core.Tests` với fixture |
| Hiện thực `IIndexerHttp` bằng `HttpClient`, lưu trạng thái governor (SQLite), `NativeSearchService`, nối DI, feature flag | App (`Services/Search/…`) | Phụ thuộc DB, settings và logger |
| UI sức khoẻ indexer, UI chọn bản encode | App (`Views`, `ViewModels`) | WPF |
| Fixture HTML, JSON, RSS thật | `MediaManager.Core.Tests/Fixtures/Indexers/<id>/` | Parser test |

## 4. Mạng

- Dùng `HttpClient` của .NET (SocketsHttpHandler), xác thực chứng chỉ bằng **kho chứng chỉ Windows**. Cách này hết lỗi "certificate has expired" mà Python gặp với bitsearch, torlock, torrents-csv và tsukihime.
- User-Agent trung thực, cố định: `MediaManager/<version> (+native-search)`. Gặp challenge thì **dừng**, không giả trình duyệt để vượt.
- Request đi qua **WARP** như hiện nay. Search native dùng chung `WarpLeaseReason.Hunt` (hoặc thêm `Search`), với preflight: WARP chưa Connected thì không search và báo rõ.
- Theo redirect (YTS đã chuyển sang `yts.gg`). Base URL và mirror đặt trong cấu hình indexer.

## 5. Dừng sớm và thời gian

- Mỗi indexer có **timeout riêng** (mặc định 10s, cấu hình được), tối đa N trang.
- Orchestrator dừng **toàn bộ** khi một trong ba điều xảy ra trước:
  1. mọi indexer đã xong;
  2. hết ngân sách thời gian của request (mặc định 20s, user search tay được 30s);
  3. đã có ≥ K ứng viên **đạt chất lượng tối đa** của recipe (tùy chọn, mặc định bật cho Auto-Track).
- Kết quả **stream**: cart có thể hiện ứng viên ngay khi về.

## 6. Feature flag và lựa chọn backend

- `settings.json` → `Search.Backend = "Qbittorrent" | "Native" | "Both"`.
  - `Both` chạy cả hai, dùng kết quả native và ghi log so sánh. Đây là chế độ A/B trong lúc chuyển.
  - Mặc định là `Qbittorrent` cho tới khi qua cổng ở P6.
- Recipe v2 có thể ép backend theo từng recipe (để thử dần từng loại: movie trước).
- Không có fallback tự động sang qBittorrent **giữa chừng một request**: hai đường khác nhau về hành vi sẽ làm kết quả khó hiểu. Muốn quay lại thì đổi cờ.

## 7. DI

```csharp
services.AddSingleton<IIndexerRegistry, IndexerRegistry>();     // danh sách ITorrentIndexer + cấu hình
services.AddSingleton<IIndexerRequestGovernor, IndexerRequestGovernor>();
services.AddSingleton<IIndexerStateStore, SqliteIndexerStateStore>();
services.AddSingleton<ITitleNormalizer, TitleNormalizer>();
services.AddSingleton<IYearMatcher, YearMatcher>();
services.AddSingleton<IQueryPlanner, QueryPlanner>();
services.AddSingleton<ISearchService, SearchServiceRouter>();     // chọn Native hay Qbittorrent theo cờ
services.AddSingleton<NativeSearchService>();
services.AddSingleton<QbittorrentSearchService>();                // adapter đường cũ, xoá ở P6
```

## 8. Bước add torrent (thay đổi nhỏ, giữ add gate)

| Kết quả | Cách add |
|---|---|
| Có magnet (hoặc infohash, tự dựng magnet kèm tracker công khai) | `torrents/add` với `urls=magnet` như hiện nay |
| Chỉ có link `.torrent` | **App tự tải** file qua `IIndexerHttp` (chịu governor), kiểm đúng định dạng bencode và lấy infohash, rồi add bằng `torrents/add` multipart `torrents=<file>` |
| Không có cả hai (chỉ có trang chi tiết) | Indexer có bước "resolve" lấy magnet từ trang chi tiết, chạy lười: chỉ khi user hoặc Auto-Track chọn bản đó |

`search/downloadTorrent` (cần plugin cài trong qBittorrent) **không còn dùng** cho đường native. `ITorrentAddGateService`, bước kiểm nội dung và blacklist giữ nguyên. Blacklist được bổ sung khoá theo infohash, vì giờ đã có infohash ổn định.

## 9. Quan sát được

- Mỗi request ghi **một dòng tóm tắt**: số query, từng indexer (số dòng, thời gian, trạng thái), số bị gộp, số bị loại theo lý do, top N, lý do dừng.
- Console log Pretty mode (#11) có bộ lọc theo `[Search]`.
- Màn hình **Sức khoẻ indexer** (P2): trạng thái breaker, request trong ngày so với ngân sách, lỗi gần nhất, thời gian trung bình.
