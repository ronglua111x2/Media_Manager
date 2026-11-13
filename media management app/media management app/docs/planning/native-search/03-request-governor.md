# 03 — IndexerRequestGovernor: quản lý tần suất request

> Yêu cầu của user (12/11/2026): site public miễn phí sẽ **ban IP ngay** nếu bị gọi dồn dập, nên cần một tiến trình quản lý riêng. Đây là **thành phần bắt buộc của P2**. Không indexer nào được ra mạng mà không qua nó.
>
> Cập nhật 13/11/2026 (audit 07c, 07d): tài liệu này mô tả **những gì code đang làm**. Phần thiết kế ban đầu chưa làm được ghi ở mục 6.

## 1. Trách nhiệm

| # | Cơ chế | Code đang làm |
|---|---|---|
| 1 | **Giới hạn theo site (lớp chống ban chính)** | Cửa sổ trượt 1 phút (request mỗi phút), số request đồng thời tối đa (1–2), khoảng cách tối thiểu giữa hai lần bắt đầu request. Không có jitter. |
| 2 | **Quota ngày (cầu chì)** | Mỗi site 1000 request/ngày (quyết định của user 13/11/2026: site chặn theo dồn dập chứ không theo tổng ngày). Quota chỉ để chặn vòng lặp lỗi chạy cả ngày. Ngày tính theo UTC, nên reset lúc 07:00 giờ Việt Nam. Settings chỉ được siết chặt hơn mặc định. |
| 3 | **Tôn trọng tín hiệu từ chối** | 429, hoặc 503 có `Retry-After`: chờ theo `Retry-After`, giới hạn trong 1 phút–24 giờ (giá trị 0 hoặc thời điểm đã qua vẫn chờ 1 phút). Không có header: backoff 30 s × 2ⁿ, tối đa 30 phút. |
| 4 | **Circuit breaker** | 403 hoặc trang challenge: mở 15 phút → 1 giờ → 6 giờ → 24 giờ. 3 lỗi liên tiếp (timeout, 5xx, lỗi mạng, **parse lỗi**): mở 5 phút → 15 phút → 1 giờ → 2 giờ. Hết hạn thì half-open, cho đúng 1 request thử. Thành công chỉ được báo **sau khi indexer parse xong**; một lần báo thành công đến muộn không đóng breaker đang mở. |
| 5 | **Không vượt chặn** | Gặp challenge thì chỉ mở breaker và ghi log. Không đổi User-Agent, không giải challenge. |
| 6 | **Ưu tiên** | Hai lane: `Interactive` (search trong cart, dry-run) và `Background` (Auto-Track). Background chỉ dùng 70% quota ngày, và phải chờ khi có request interactive đang xếp hàng cho cùng site. |
| 7 | **Cache** | Cache response trong bộ nhớ 45 phút theo URL, có single-flight: một URL chỉ ra mạng một lần dù nhiều nơi cùng hỏi. Người chờ sau dùng token của chính mình. Response không parse được thì bị xóa khỏi cache. Trúng cache không tính vào quota. Cache tự dọn mục hết hạn. |
| 8 | **Giảm số query** | Query recipe được rút gọn trước khi tới indexer (`NativeQueryText`): bỏ token chất lượng và năm của phim; gộp các dạng pack còn `<tên> Sxx` và `<tên> season N`. Memo 15 phút cho câu trả lời đầy đủ. |
| 9 | **Giữ trạng thái** | `<StateFolder>/indexer-state.json`: số request đã dùng trong ngày, trạng thái breaker, cooldown, lỗi gần nhất. Ghi bằng file tạm rồi đổi tên, chậm nhất 10 s sau thay đổi đầu tiên. Cửa sổ 1 phút **không** được lưu. |
| 10 | **Quan sát và điều khiển** | Settings → Search engines: bật/tắt từng site, bảng sức khỏe, nút "Pause all" (kill switch). Pill "Search n/m" ở màn Auto, News, Cart: trạng thái và mức dùng của từng site. Mỗi lượt search ghi một dòng log tóm tắt trạng thái từng indexer. |

## 2. Chính sách mặc định (`IndexerPolicy.DefaultFor`)

| Indexer | Request/phút | Đồng thời | Khoảng cách tối thiểu | Quota/ngày |
|---|---|---|---|---|
| Knaben API | 10 | 1 | 2 s | 1000 |
| apibay | 15 | 2 | 1 s | 1000 |
| nyaa RSS | 10 | 1 | 2 s | 1000 |
| limetorrents | 10 | 1 | 3 s | 1000 |
| EZTV API | 10 | 1 | 2 s | 1000 |
| Còn lại (bitsearch, torrentdownloads, bt4g, yts, torrents-csv, sktorrent, tsukihime, subsplease, nekobt…) | 6 | 1 | 5 s | 1000 |

LimeTorrents được nới từ 6/phút, 5 s (doc gốc) lên 10/phút, 3 s trong commit `7f4bbcb` mà chưa có số đo riêng (07d GOV-05). Không tăng thêm giới hạn mỗi phút nếu chưa có số đo cho thấy site chịu được.

## 3. Interface (code thật)

```csharp
public sealed class IndexerRequestGovernor
{
    Task<IDisposable> AcquireAsync(string indexerId, SearchLane lane, TimeSpan maxWait, CancellationToken ct);
    void Report(string indexerId, IndexerFailureKind outcome, TimeSpan? retryAfter = null, string? detail = null);
    IndexerAvailability GetAvailability(string indexerId, SearchLane lane = SearchLane.Interactive);
    IReadOnlyList<IndexerStateSnapshot> Snapshot();
    bool KillSwitch { get; set; }
    void SetDisabled(string indexerId, bool disabled);
    event Action? StateChanged;
}

public interface IIndexerHttp   // GovernedIndexerHttp: lease, timeout, cache, phân loại 429/403/5xx
{
    Task<IndexerHttpResponse> GetAsync(Uri uri, CancellationToken ct);
    Task<IndexerHttpResponse> PostJsonAsync(Uri uri, string json, CancellationToken ct);
    void ReportParsed(bool parsed);   // orchestrator gọi sau khi indexer parse xong một query
}
```

## 4. Tương tác với Orchestrator và Auto-Track

- Trước khi chạy, orchestrator hỏi `GetAvailability` và **bỏ** indexer không sẵn sàng (ghi lý do vào telemetry), thay vì xếp hàng chờ.
- Mỗi indexer chạy các query của nó lần lượt, các indexer chạy song song, trong giới hạn thời gian của lượt search (interactive 90 s, Auto-Track 5 phút).
- Auto-Track (07d GOV-04): tập không tìm được bản chờ 1 giờ → 3 giờ → 6 giờ → 24 giờ tùy độ mới; show được xoay vòng; hunt chạy theo `TorrentHuntIntervalMinutes`.

## 5. Test

- Logic thuần (giới hạn mỗi phút, backoff, chuyển trạng thái breaker, quota, ưu tiên lane) nằm trong `MediaManager.Core` với đồng hồ giả (`TimeProvider`): `IndexerRequestGovernorTests`.
- `GovernedIndexerHttp` và orchestrator với `HttpMessageHandler` giả: trang lạ trả 200 mở breaker, trúng cache không bị tính, người chờ sau không bị hủy theo người khởi tạo, một indexer ném lỗi lạ không làm mất kết quả của indexer khác: `GovernedHttpAndOrchestratorTests`.
- Mô tả trạng thái cho UI: `NativeSearchHealthTests` (App).

## 6. Chưa làm (so với thiết kế ban đầu)

- Jitter ±20% (07d GOV-01: giá trị thấp, chỉ ghi lại).
- Cache dài 2–6 giờ cho EZTV (GOV-02).
- Trần request mỗi lượt search và chế độ ngân sách thấp (GOV-03 B).
- Bench dùng chung trạng thái với app (GOV-07); telemetry đếm request và lượt trúng cache (GOV-08); ghi trạng thái bền hơn khi file hỏng (GOV-06).
