# 07 — Các giai đoạn: việc, PR, tiêu chí nghiệm thu

Quy ước:
- Mỗi PR nhỏ, có test, build và test xanh theo `.github/actions/build-test/action.yml`.
- PR nhắm vào `feat/native-search` (hoặc `main` với P1, vì P1 độc lập và có giá trị ngay; user quyết định).
- Sau mỗi giai đoạn: chạy lại `tools/SearchProbe` và ghi số liệu vào mục [Số liệu sau từng giai đoạn](#số-liệu-sau-từng-giai-đoạn).

---

## P1: Khớp và chấm điểm (độc lập với backend)

**Mục tiêu:** movie cũ và movie có tên gốc tìm ra ứng viên đạt **ngay trên đường qBittorrent hiện tại**.

| PR | Việc | File chính |
|---|---|---|
| P1-1 | `TitleNormalizer` (bỏ dấu thanh, hai biến thể dấu nháy, `&`/and, phụ đề, số La Mã, mạo từ, token hoá) cùng fixture test | `MediaManager.Core/Search/Matching/TitleNormalizer.cs`; thay ở `QueryTemplateRenderer`, `EnglishAlternativeTitleFilter.NormalizeForSearchKey`, `CandidateEvaluationService.EvaluateTitleMatch` |
| P1-2 | `YearMatcher` (đọc năm bằng parser, bỏ số trong tiêu đề, tập năm hợp lệ, chấm theo bậc, loại khi lệch ≥ 2) cùng migration `010` và lấy `release_dates` từ TMDB | `MediaManager.Core/Search/Matching/YearMatcher.cs`, `CandidateEvaluationService.EvaluateMovie`, `TmdbMetadataProvider` |
| P1-3 | Template `{title} {year}` sinh thêm biến thể không năm; tên thay thế mặc định bật cho movie (tách "search bằng" và "khớp bằng") | `SearchPlanBuilder`, `SearchTitleResolver`, `RecipeRuntimeSettings` |
| P1-4 | Đánh giá hết rồi mới lấy top N (SRCH-04) | `FetchJobService.SearchEpisodeCandidatesSequentialAsync` |

**Nghiệm thu:**
- Test fixture cho mọi quy tắc ở [04 §1–2](./04-query-and-matching.md).
- Bộ golden lấy từ JSON `engine_probe` thật: *Movie 01* nhận các bản 2013; *Léon* khớp bản không dấu; *Schindler's List* khớp `Schindlers.List`; *2001* không nhầm năm; *Dune* 1984 không nhận bản 2021.
- Chạy dry-run thật với *Movie 01*, *Movie 04*, *Movie Two*: có ứng viên đạt.

---

## P2: Khung indexer cùng 4 indexer đầu tiên

**Mục tiêu:** đường native chạy được sau cờ, nhanh, có kiểm soát tần suất.

| PR | Việc |
|---|---|
| P2-1 | Model và interface: `ITorrentIndexer`, `IndexerQuery`, `IndexerResult`, `IndexerCapabilities`, `ISearchService`, `SearchOutcome` cùng telemetry. Cờ `Search.Backend`. Adapter `QbittorrentSearchService` bọc đường cũ |
| P2-2 | `IndexerRequestGovernor` (logic thuần trong Core, dùng `TimeProvider`): token bucket, đồng thời, khoảng cách, ngân sách, backoff, breaker, hai làn ưu tiên, cache, single-flight. Migration `011_indexer_state` cùng `SqliteIndexerStateStore`. Hiện thực `IIndexerHttp` bằng `HttpClient` |
| P2-3 | `SearchOrchestrator`, `ResultNormalizer` (infohash, magnet), `ResultMerger` bản cơ bản. Telemetry và log một dòng tóm tắt |
| P2-4 | Indexer **Knaben**, **apibay**, **nyaa (RSS)**, **limetorrents** cùng fixture test |
| P2-5 | Bước add: magnet giữ nguyên; `.torrent` thì app tự tải (qua governor), kiểm bencode, lấy infohash, add multipart. Giữ add gate |
| P2-6 | Nối Cart, Auto-Track và Recipe dry-run vào `ISearchService` (cả hai backend). Chế độ `Both` ghi log so sánh |
| P2-7 | Màn hình Sức khoẻ indexer cùng kill switch. `SearchProbe` thêm chế độ native (CLI harness gọi indexer qua app Core) |

**Nghiệm thu:**
- Test governor với đồng hồ giả: 429 kèm `Retry-After`, 403 hay trang challenge mở breaker, ngân sách ngày, ưu tiên Interactive hơn Background, khôi phục trạng thái sau khởi động lại.
- Fixture test cho 4 indexer.
- Chế độ `Both` chạy 3 ngày: không lỗi chưa xử lý, không 429, p90 mỗi query ≤ 15s.

---

## P3: Mở rộng indexer

| PR | Indexer | Phụ thuộc |
|---|---|---|
| P3-1 | **EZTV** (API theo IMDb id) | Migration `009` cùng backfill IMDb id từ TMDB `external_ids` |
| P3-2 | **BT4G** (RSS, độ tin cậy thấp), **bitsearch** | `JunkFilter` bản cơ bản (P5-1 có thể kéo lên sớm) |
| P3-3 | **YTS**, **torrents-csv** | |
| P3-4 | **TsukiHime**, **SubsPlease**, **nekoBT**, **AnimeTosho** | Biến đổi query anime (P4) |
| P3-5 | **torrentdownloads** (resolve lười), tùy chọn **rutor**, **kickass** | Bước "resolve" magnet từ trang chi tiết |

**Nghiệm thu:** mỗi indexer có fixture, chính sách tần suất và một dòng trong `site_candidates.json`. `engine_probe` ở chế độ native cho thấy mọi indexer `ok` trên các tiêu đề thuộc chuyên môn của nó.

---

## P4: Kế hoạch query

| PR | Việc |
|---|---|
| P4-1 | `QueryPlanner`: biến thể theo loại media (movie, tập, anime, pack, snapshot) có thứ tự ưu tiên; cắt bớt khi thiếu ngân sách |
| P4-2 | Biến đổi query theo indexer qua capability (SubsPlease chỉ nhận tên show; nyaa dùng `- 05`; EZTV dùng IMDb id) |
| P4-3 | Snapshot cả show cộng **query nhắm đích bổ sung** cho tập hoặc season chưa có ứng viên đạt (thay cho việc lọc âm thầm template, SRCH-03) |
| P4-4 | Recipe editor cảnh báo template không áp dụng |

**Nghiệm thu:** pack *Show 16* S03 tìm ra ≥ 100 pack S03 (số đo: query nhắm đích 111, query chỉ tên show 7). Tập anime đang chiếu có ứng viên đạt nhanh như hiện nay hoặc nhanh hơn.

---

## P5: Chất lượng và UI

| PR | Việc |
|---|---|
| P5-1 | `JunkFilter` đầy đủ (dung lượng theo thời lượng và chất lượng, đuôi file, từ khoá, `virusDetection`, kết quả chỉ có từ DHT) |
| P5-2 | `ResultMerger` đầy đủ (gộp nhiều nguồn, điểm tin cậy). Migration `012` blacklist theo infohash |
| P5-3 | Recipe: `Require` / `Prefer` / `Avoid`, trừ điểm thay vì loại cứng; profile "movie cũ" |
| P5-4 | UI chọn bản encode: hiện mọi bản đạt, gom nhóm, chip lọc, lý do điểm, nguồn |

**Nghiệm thu:** *Movie 08* hiện hàng trăm bản, lọc được theo codec, HDR và âm thanh. Không còn rác trong top N của bộ tiêu đề đặc thù.

---

## P6: Chuyển hẳn và dọn

| PR | Việc |
|---|---|
| P6-1 | Migration recipe v2 (`.rcp`), backup v1, ánh xạ plugin sang indexer |
| P6-2 | Qua **cổng chuyển hẳn** ([06 §4](./06-rollout-and-migration.md#4-cổng-chuyển-hẳn-trước-khi-đổi-mặc-định-sang-native-ở-p6)), rồi đổi mặc định sang `Native`. Gắn tag `pre-native-search-cutover` |
| P6-3 | Xoá code search qBittorrent ([06 §5](./06-rollout-and-migration.md#5-danh-sách-xoá-ở-p6-sau-khi-qua-cổng)) cùng feature flag. Cập nhật `docs/APP_OVERVIEW.md`, `docs/FEATURES.md`, `docs/RECIPE_SCHEMA.md` |

---

## Thứ tự và phụ thuộc

```
P1 ──────────────────────────────┐ (độc lập, merge sớm được)
P2-1 → P2-2 → P2-3 → P2-4 → P2-5 → P2-6 → P2-7
                     │
                     ├─► P3 (từng indexer, song song được)
                     ├─► P4 (cần P1 cho TitleNormalizer/YearMatcher)
                     └─► P5 (P5-1 có thể làm cùng P3-2)
P3 + P4 + P5 ──► cổng A/B ──► P6
```

P1 và P2-1/P2-2 có thể làm song song ở hai phiên khác nhau, vì chúng không đụng cùng file.

---

## Số liệu sau từng giai đoạn

| Giai đoạn | Ngày | Infohash khác nhau (bộ đặc thù) | p90 mỗi query | Movie cũ có ứng viên | Ghi chú |
|---|---|---|---|---|---|
| Baseline (qBittorrent) | 12/11/2026 | xem [07b §5c](../../code-review/2026-11/07b-hunt-search.md) | 45–180s (bị engine treo giữ) | *Movie 01*: 0 bản đạt (năm 2014 so với 2013) | 10/23 engine hoạt động |
| P1 | 12/11/2026 | (P1 không đổi nguồn search) | như baseline | 15/15 movie có ứng viên đạt (*Movie 01* 2 → **27**, *Movie Two* 206 → **531**, *Movie J0* 12 → **168**, *Show E – Movie Subtitle* 9 → **48**, *Movie 04* 44 → **67**) | `tools/SearchBench movies`: không bản nào luật cũ nhận mà luật mới loại. Các movie giảm (*Movie 06* 131 → 85) là do dedupe theo infohash; số thô trước dedupe vẫn tăng (131 → 134) |
| P2–P5 (native) | 12/11/2026 | `compare` (10 tiêu đề, trước khi có phân trang): native nhiều hơn với anime/romaji (`Alt Title D 06` 58 so với 25, `Alt Title A` 119 so với 96), ngang với season pack (162 so với 166), ít hơn 2–3 lần với movie phương Tây | Native **~1 s/query** khi chưa phải chờ lượt; khi xếp hàng chờ governor (15 movie liên tiếp) trung bình ~12 s/query. qBittorrent 96–181 s/query | **15/15 movie có ứng viên đạt** qua native (`movies --backend native`), bản đứng đầu giống đường qBittorrent ở *Movie Three*, *Hero-Verse*, *Movie 05*, *Movie 06*. Số ứng viên ít hơn đường qBittorrent khoảng 30–70% (ví dụ *Movie Two* 148 so với 531) | Không site nào chặn, không breaker nào mở. **Cổng P6 chưa đạt** tiêu chí "số infohash ≥ qBittorrent trên ≥ 90% case" với movie phương Tây; cần thêm nguồn hoặc thêm trang (kickass, sktorrent chưa có bản native) |
| P6 | | | | | |
