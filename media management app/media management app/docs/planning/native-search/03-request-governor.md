# 03 — IndexerRequestGovernor: quản lý tần suất request

> Yêu cầu của user (12/11/2026): site public miễn phí sẽ **ban IP ngay** nếu bị gọi dồn dập, nên cần một tiến trình quản lý riêng. Đây là **thành phần bắt buộc của P2**. Không indexer nào được ra mạng mà không qua nó.

## 1. Trách nhiệm

| # | Cơ chế | Chi tiết |
|---|---|---|
| 1 | **Giới hạn theo site** | Token bucket (request mỗi phút), số request đồng thời tối đa (mặc định 1–2), khoảng cách tối thiểu giữa hai request, cộng jitter ngẫu nhiên ±20% |
| 2 | **Ngân sách theo ngày** | Mỗi site có trần request/24h (cấu hình được, chỉ giảm). Hết ngân sách thì bỏ qua site đó, ghi lý do `BudgetExhausted` |
| 3 | **Tôn trọng tín hiệu từ chối** | 429 hoặc 503: đọc `Retry-After`, nếu không có thì backoff luỹ thừa (30s → 1m → 2m → … tối đa 30m). Trong lúc chờ, site ở trạng thái `CoolingDown` |
| 4 | **Circuit breaker** | 403, trang challenge, hoặc N lỗi liên tiếp (timeout, 5xx, parse lỗi): **mở** breaker, không gọi site đó trong T phút (15m → 1h → 6h tăng dần). Sau đó half-open thử 1 request |
| 5 | **Không vượt chặn** | Gặp challenge thì chỉ mở breaker và báo. Không đổi User-Agent, không giải challenge |
| 6 | **Ưu tiên** | Hai làn: `Interactive` (user bấm search trong cart, Recipe dry-run) và `Background` (Auto-Track, retry tự động). Interactive được dùng trước; Background chỉ dùng phần ngân sách còn lại (ví dụ ≤ 70% ngân sách ngày) và bị dừng trước |
| 7 | **Cache** | Cache phản hồi đã parse theo `(indexer, query chuẩn hoá, page)`, TTL 30–60 phút (EZTV tra theo IMDb id: 2–6 giờ). Cache nằm trong bộ nhớ, tùy chọn lưu xuống đĩa |
| 8 | **Single-flight** | Hai caller cùng gọi một `(indexer, query, page)` cùng lúc thì chỉ một request ra mạng, caller kia chờ kết quả |
| 9 | **Giữ trạng thái** | Số request đã dùng trong ngày, trạng thái breaker, `blockedUntil`, lỗi gần nhất được lưu SQLite (bảng `IndexerState`), để khởi động lại app không reset giới hạn |
| 10 | **Quan sát và điều khiển** | Màn hình Sức khoẻ indexer; công tắc tắt từng site; nút **"Tạm dừng mọi search"** (kill switch); log mỗi lần breaker mở hoặc đóng |

## 2. Chính sách mặc định (an toàn, có thể chỉnh trong code sau khi đo)

| Indexer | Request/phút | Đồng thời | Khoảng cách tối thiểu | Ngân sách/ngày | Ghi chú |
|---|---|---|---|---|---|
| Knaben API | 10 | 1 | 2s | 500 | Meta: một request phủ nhiều nguồn |
| apibay | 15 | 2 | 1s | 800 | |
| nyaa RSS | 10 | 1 | 2s | 600 | Nyaa nổi tiếng rate-limit chặt |
| limetorrents | 6 | 1 | 5s | 300 | HTML, nhẹ tay |
| EZTV API | 10 | 1 | 2s | 300 | Có cache dài theo IMDb id |
| BT4G RSS | 6 | 1 | 5s | 200 | |
| Còn lại | 6 | 1 | 5s | 200 | Mặc định bảo thủ |

Các số trên là **điểm xuất phát bảo thủ**. Sau P2, dùng `SearchProbe` cộng log thật để chỉnh. Không bao giờ tăng quá mức đã được chứng minh an toàn.

## 3. Interface (phác thảo)

```csharp
public interface IIndexerRequestGovernor
{
    // Cấp IIndexerHttp cho một lượt search; mọi request của indexer đi qua đây.
    IIndexerHttp For(string indexerId, SearchLane lane);
    IndexerAvailability GetAvailability(string indexerId);   // Ready / CoolingDown / Open / BudgetExhausted / Disabled
    IReadOnlyList<IndexerHealthSnapshot> Snapshot();
    void SetKillSwitch(bool paused);
}

public interface IIndexerHttp
{
    // Chờ token, kiểm breaker, cache, single-flight; ném IndexerUnavailableException
    // (kèm lý do) thay vì gửi request khi không được phép.
    Task<IndexerHttpResponse> GetAsync(Uri uri, IndexerRequestOptions? options, CancellationToken ct);
}
```

## 4. Tương tác với Orchestrator

- Trước khi lập kế hoạch query, Orchestrator hỏi `GetAvailability` và **bỏ** indexer không sẵn sàng (ghi lý do vào telemetry), thay vì xếp hàng chờ.
- Nếu một lượt search cần nhiều request hơn token còn lại, QueryPlanner **cắt bớt** biến thể query theo thứ tự ưu tiên (biến thể quan trọng nhất trước).
- Auto-Track chạy nhiều show liên tiếp: governor tự giãn nhịp. Auto-Track không có vòng lặp gọi dồn.

## 5. Test

- Logic thuần (token bucket, backoff, chuyển trạng thái breaker, ngân sách, ưu tiên) nằm trong `MediaManager.Core` với **đồng hồ giả** (`TimeProvider`), test không cần mạng.
- Test tích hợp với `HttpMessageHandler` giả: 429 kèm `Retry-After`, 403 trả trang challenge, timeout, 5xx liên tiếp.
- Test lưu và khôi phục trạng thái qua lần khởi động lại.
