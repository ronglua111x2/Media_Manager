# Native Search: search torrent bằng indexer của chính app

> **Trạng thái (12/11/2026):** user đã xác nhận. **P1–P5 đã triển khai** trên `feat/native-search`. **P6 (chuyển hẳn) đang chờ cổng A/B**: cần chạy chế độ `Both` thật trong vài ngày trước khi đổi mặc định và xoá đường qBittorrent. Chi tiết ở mục [Trạng thái triển khai](#9-trạng-thái-triển-khai).
> **Nhánh:** `feat/native-search`, cắt từ `main` @ `ff2caa3`.
> **Nguồn:** audit [07b — hunt search](../../code-review/2026-11/07b-hunt-search.md) và số liệu đo bằng [`tools/SearchProbe`](../../../tools/SearchProbe/README.md), ngày 12/11/2026.

## 1. Vì sao làm

Hiện tại hunt search qua Web API của qBittorrent, tức là chạy các plugin Python `nova3`. Số liệu đo thật cho thấy:

- **Chỉ 10/23 engine đang bật trả được kết quả.** 13 engine còn lại hỏng vì một trong các lý do: lỗi chứng chỉ của Python, trang chống bot, domain chết, cần OMDb key, hoặc ngoài chuyên môn.
- **Engine treo giữ cả job tới mức trần 180s của qBittorrent.** Mỗi query của hunt vì thế luôn tốn trọn timeout của recipe (45–120s). Thiệt hại chủ yếu là **thời gian**: app vẫn giữ được 91–100% số dòng.
- **Query và bước khớp làm hụt kết quả, nặng nhất với movie cũ:**
  - năm TMDB lệch với năm trong tên bản phát hành loại khoảng 97% số bản của *Coherence*;
  - giữ dấu thanh làm mất một nửa kết quả (`Léon`);
  - không dùng tên thay thế (romaji);
  - query pack chỉ có tên show bỏ lỡ 105/111 season pack.
- **Không kiểm soát được từng engine:** không có timeout riêng, không biết engine nào lỗi, không cache, không giới hạn tần suất request.

## 2. Mục tiêu

| Trục | Mục tiêu đo được (kiểm bằng `SearchProbe`) |
|---|---|
| **Tốc độ** | Mỗi query xong trong **≤ 15s** (p90) thay vì 45–180s. Một show snapshot hoặc pack xong trong ≤ 45s. |
| **Độ phủ** | Với bộ tiêu đề đặc thù, số **infohash khác nhau** ≥ đường qBittorrent hiện tại. Movie cũ và movie có tên gốc tìm ra ứng viên đạt. |
| **Chất lượng** | Không có ứng viên rác (file thực thi, dung lượng phi lý) trong top N. Gộp trùng theo infohash. Hiện **mọi** bản encode đạt để user chọn. |
| **An toàn với site** | Không IP nào bị ban. Mọi request đi qua bộ quản lý tần suất. Gặp challenge thì dừng, không vượt. |
| **Không tụt chất lượng** | Anime nổi tiếng và đang chiếu (hiện đã tốt) không xấu đi. Đây là điều kiện bắt buộc trước khi chuyển hẳn. |

## 3. Quyết định đã chốt (user, 12/11/2026)

1. **Rời hẳn engine search của qBittorrent.** Search do app làm bằng **indexer native viết bằng C#**. qBittorrent chỉ còn **tải** torrent.
2. **Không** dùng Prowlarr hay Jackett (phần lớn indexer ở đó trả phí), **không** chạy plugin Python (hướng B bị bỏ).
3. **Giữ đường search qua qBittorrent sau một cờ (feature flag) tới hết P6**, để so sánh A/B và quay lại được khi cần. P6 xoá hẳn đường này.
4. **Cải tiến toàn bộ:** movie, TV, season pack và anime.
5. **Bắt buộc có bộ quản lý tần suất request** (`IndexerRequestGovernor`), vì site free sẽ ban IP nếu bị gọi dồn.
6. **Không làm cơ chế vượt Cloudflare hay chống bot.** Site chỉ có trang bị challenge thì bỏ.
7. **Khớp năm (SRCH-11) và chuẩn hoá tiêu đề (SRCH-13) phải làm lại toàn bộ** ("sửa lớn"), có fixture test, không chỉ vá nhỏ.
8. Mọi lần search đều đi qua **Cloudflare WARP**, vì tracker bị chặn ở Việt Nam. Logic WARP lease của hunt được dùng lại.

## 4. Ngoài phạm vi

- Private tracker và tracker cần đăng nhập (RuTracker, Ygg…).
- Tự giải challenge, CAPTCHA, hay giả dấu vân tay TLS.
- Đổi cách **tải** torrent: qBittorrent, add gate, bước kiểm nội dung và cart giữ nguyên.
- Thay đổi Jellyfin, symlink hay luồng link thư viện.

## 5. Kiến trúc tóm tắt

```
Recipe v2 ──► SearchRequest (media, tập/season, tiêu đề, năm, IMDb id)
                 │
                 ▼
          QueryPlanner ── TitleNormalizer / YearMatcher / AltTitles
                 │  (biến thể query cho từng indexer)
                 ▼
        SearchOrchestrator ──► IndexerRequestGovernor (tần suất, ngân sách, breaker, cache)
                 │                     │
                 │              ITorrentIndexer × N (Knaben, TPB, nyaa, EZTV, …)
                 ▼
      ResultNormalizer (infohash, thuộc tính) ─► JunkFilter ─► Dedupe theo infohash
                 │
                 ▼
     CandidateEvaluation (khớp, chấm điểm, không loại cứng) ─► Cart / Auto-Track
                 │
                 ▼
          AddPath: magnet → qBittorrent; .torrent → app tự tải rồi add file
```

Chi tiết: [01-architecture.md](./01-architecture.md).

## 6. Lộ trình

| Giai đoạn | Nội dung | Giá trị ngay | Tài liệu |
|---|---|---|---|
| **P1** | Khớp và chấm điểm: `TitleNormalizer`, `YearMatcher`, tên thay thế, đánh giá hết rồi mới lấy top N | **Movie cũ tìm ra ngay** trên đường qBittorrent hiện tại | [04](./04-query-and-matching.md) |
| **P2** | Khung indexer: `ITorrentIndexer`, `SearchOrchestrator`, `IndexerRequestGovernor`, sức khoẻ indexer, đường add file `.torrent`, feature flag, cùng 4 indexer **Knaben, TPB (apibay), nyaa (RSS), limetorrents** | Search nhanh, có kiểm soát | [01](./01-architecture.md), [02](./02-indexers.md), [03](./03-request-governor.md) |
| **P3** | Thêm indexer: **EZTV** (tra theo IMDb id), **BT4G**, YTS, torrents-csv, TsukiHime, SubsPlease, nekoBT, AnimeTosho, bitsearch, torrentdownloads; tùy chọn rutor, kickass | Độ phủ | [02](./02-indexers.md) |
| **P4** | Kế hoạch query: query nhắm đích theo season và tập cho pack và snapshot, biến đổi query theo indexer, cache | Pack và tập cũ | [04](./04-query-and-matching.md) |
| **P5** | Chất lượng: lọc rác, gộp theo infohash, trừ điểm thay vì loại cứng, UI so sánh nhiều bản encode | Chọn bản dễ | [05](./05-quality-and-ui.md) |
| **P6** | Chuyển hẳn: native thành mặc định, migration recipe v2, **xoá code search qBittorrent** | Gọn code | [06](./06-rollout-and-migration.md) |

Danh sách việc, PR và tiêu chí nghiệm thu của từng giai đoạn: [07-phases.md](./07-phases.md).

## 7. Rủi ro chính

| Rủi ro | Giảm thiểu |
|---|---|
| Site đổi HTML hoặc API, khiến indexer hỏng | Parser có fixture test. Sức khoẻ indexer cùng breaker tự tắt indexer hỏng. Ưu tiên API/RSS hơn cào HTML |
| Bị ban IP vì gọi quá nhiều | `IndexerRequestGovernor`: giới hạn theo site, ngân sách theo ngày, cache, single-flight, dừng ngay khi gặp 403 hoặc challenge |
| Anime đang tốt bị xấu đi khi chuyển | Feature flag cùng cổng so sánh A/B ở P6 bằng `SearchProbe` |
| Kết quả DHT (BT4G, bitsearch) nhiều rác | `JunkFilter` cộng điểm tin cậy theo indexer. Kết quả DHT không bao giờ đứng đầu nếu chỉ có một nguồn |
| Một domain chết hoặc đổi tên (YTS đã chuyển sang `yts.gg`) | Base URL đặt trong cấu hình, theo redirect, có danh sách mirror |
| Knaben gom trùng TPB, nyaa, TokyoTosho | Gộp theo infohash, giữ số seed lớn nhất và danh sách nguồn |

## 8. Cách xác nhận và triển khai

1. User đọc và xác nhận bộ plan này (có thể sửa từng mục).
2. Mỗi giai đoạn làm trên nhánh con hoặc commit trên `feat/native-search`. Mỗi PR nhỏ, có test.
3. Sau mỗi giai đoạn: chạy lại `tools/SearchProbe` và ghi số liệu vào [07-phases.md](./07-phases.md#số-liệu-sau-từng-giai-đoạn).

## 9. Trạng thái triển khai

| Giai đoạn | Trạng thái | Ghi chú |
|---|---|---|
| P1 | Xong | `TitleNormalizer`, `YearMatcher` (±1 năm, bỏ số trong tiêu đề, loại remake), query bỏ năm, tên thay thế, đánh giá hết rồi lấy top N, `CandidateDeduplicator` (theo infohash, kể cả hash trong link `.torrent`) |
| P2 | Xong | `ITorrentIndexer`, `SearchOrchestrator`, `IndexerRequestGovernor` (lưu `indexer-state.json`), cache cùng single-flight, cờ `Search.Backend`, card **Settings → Search engines**, magnet add thẳng |
| P3 | Xong | 14 indexer: Knaben, apibay, LimeTorrents, EZTV (theo IMDb id, cache `imdb-ids.json`), YTS, torrents-csv, bitsearch, BT4G, torrentdownloads (RSS), nyaa, TsukiHime, SubsPlease, nekoBT, AnimeTosho (mặc định tắt vì chỉ còn là kho lưu trữ) |
| P4 | Xong | Query pack theo từng season, search nhắm đích cho tập mà snapshot bỏ sót, biến thể số tập anime có đệm số 0 |
| P5 | Xong | `JunkFilter` (`SuspiciousRelease`), picker có dòng thuộc tính và ô lọc nhanh, movie giữ tới 40 ứng viên |
| P6 | **Chờ cổng** | Cách làm: bật `Both` (hoặc `Native` riêng cho Movie) trong Settings, dùng thật vài ngày, chạy `tools/SearchBench compare` và `movies --backend native`, rồi mới đổi mặc định và xoá code theo [06 §5](./06-rollout-and-migration.md#5-danh-sách-xoá-ở-p6-sau-khi-qua-cổng) |

Khác với plan ban đầu: IMDb id và trạng thái governor được lưu bằng file JSON thay cho bảng DB (xem [06](./06-rollout-and-migration.md)). Native hiện dùng **mọi indexer đang bật** cho mỗi loại media, chưa theo danh sách plugin của từng recipe (recipe v2 thuộc P6).

## Mục lục

- [01-architecture.md](./01-architecture.md): các thành phần, interface, luồng dữ liệu, vị trí code, DI, feature flag
- [02-indexers.md](./02-indexers.md): danh mục indexer kèm số liệu đo, endpoint, khả năng, ưu tiên
- [03-request-governor.md](./03-request-governor.md): quản lý tần suất request (chống bị ban)
- [04-query-and-matching.md](./04-query-and-matching.md): chuẩn hoá tiêu đề, khớp năm, tên thay thế, kế hoạch query
- [05-quality-and-ui.md](./05-quality-and-ui.md): chuẩn hoá kết quả, lọc rác, gộp trùng, chấm điểm, UI chọn bản encode
- [06-rollout-and-migration.md](./06-rollout-and-migration.md): feature flag, cổng A/B, recipe v2, migration DB, danh sách xoá ở P6
- [07-phases.md](./07-phases.md): việc, PR và tiêu chí nghiệm thu từng giai đoạn
