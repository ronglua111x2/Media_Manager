# 03 — IndexerRequestGovernor: quản lý tần suất request

> Yêu cầu của user (12/11/2026): site public miễn phí sẽ **ban IP ngay** nếu bị gọi dồn dập, nên cần một tiến trình quản lý riêng. Đây là **thành phần bắt buộc của P2**. Không indexer nào được ra mạng mà không qua nó.
>
> Cập nhật 13/11/2026 (audit 07c, 07d, rồi đợt sửa GOV-03 B/06/07/08): tài liệu này mô tả **những gì code đang làm**. Phần thiết kế ban đầu không làm được ghi ở mục 6.

## 1. Trách nhiệm

| # | Cơ chế | Code đang làm |
|---|---|---|
| 1 | **Giới hạn theo site (lớp chống ban chính)** | Cửa sổ trượt 1 phút (request mỗi phút), số request đồng thời tối đa (1–2), khoảng cách tối thiểu giữa hai lần bắt đầu request. Không có jitter (quyết định 13/11/2026, 07d GOV-01): app là một client duy nhất với User-Agent trung thực, site đếm số request chứ không xem nhịp, và thêm ngẫu nhiên để trông giống người là né phát hiện, trái mục 5. |
| 2 | **Quota ngày (cầu chì)** | Mỗi site 1000 request/ngày (quyết định của user 13/11/2026: site chặn theo dồn dập chứ không theo tổng ngày). Quota chỉ để chặn vòng lặp lỗi chạy cả ngày. Ngày tính theo **UTC**, nên reset lúc 07:00 giờ Việt Nam. Giữ UTC có chủ đích: quota chỉ là cầu chì nên mốc reset không ảnh hưởng chống ban; mốc UTC không đổi khi máy đổi múi giờ; đổi sang giờ máy sẽ làm ngày chuyển đổi đếm sai một lần. Pill "Search" hiện giờ reset theo giờ máy. Settings chỉ được siết chặt hơn mặc định. |
| 3 | **Tôn trọng tín hiệu từ chối** | 429, hoặc 503 có `Retry-After`: chờ theo `Retry-After`, giới hạn trong 1 phút–24 giờ (giá trị 0 hoặc thời điểm đã qua vẫn chờ 1 phút). Không có header: backoff 30 s × 2ⁿ, tối đa 30 phút. |
| 4 | **Circuit breaker** | 403 hoặc trang challenge: mở 15 phút → 1 giờ → 6 giờ → 24 giờ. 3 lỗi liên tiếp (timeout, 5xx, lỗi mạng, **parse lỗi**): mở 5 phút → 15 phút → 1 giờ → 2 giờ. Hết hạn thì half-open, cho đúng 1 request thử. Thành công chỉ được báo **sau khi indexer parse xong**; một lần báo thành công đến muộn không đóng breaker đang mở. |
| 5 | **Không vượt chặn** | Gặp challenge thì chỉ mở breaker và ghi log. Không đổi User-Agent, không giải challenge. |
| 6 | **Ưu tiên** | Hai lane: `Interactive` (search trong cart, dry-run) và `Background` (Auto-Track). Background chỉ dùng 70% quota ngày, và phải chờ khi có request interactive đang xếp hàng cho cùng site. |
| 7 | **Cache** | Cache response trong bộ nhớ 45 phút theo URL, có single-flight: một URL chỉ ra mạng một lần dù nhiều nơi cùng hỏi. EZTV (một URL theo IMDb id cho mọi query của một show) giữ 2 giờ cho lane nền (07d GOV-02); search tay vẫn 45 phút để tập mới hiện nhanh. Người chờ sau dùng token của chính mình. Response không parse được thì bị xóa khỏi cache. Trúng cache không tính vào quota hay trần mỗi lượt. Cache tự dọn mục hết hạn. |
| 8 | **Giảm số query** | Query recipe được rút gọn trước khi tới indexer (`NativeQueryText`): bỏ token chất lượng và năm của phim; gộp các dạng pack còn `<tên> Sxx` và `<tên> season N`. Memo 15 phút cho câu trả lời đầy đủ. |
| 8b | **Trần mỗi lượt search (07d GOV-03 B)** | Một `SearchSession` (AsyncLocal) bao mọi query của một target: một tập, một pack, một movie hoặc một snapshot show. Mỗi site nhận tối đa `MaxRequestsPerSearch` request mạng cho target đó (bảng mục 2; lane tay gấp đôi). Khi lane còn dưới 20% ngân sách ngày của site, mỗi lượt chỉ gửi request đầu tiên (query chính, trang 1, không bản "+chất lượng"). Request bị cắt cho trạng thái `trimmed`: giữ các dòng đã có và vẫn tính là câu trả lời đầy đủ cho memo; indexer phân trang giữ các trang đã đọc. Trúng cache không bị tính. Session cũng chốt backend cho cả lượt chạy (doc 06 §1) và biết khi không site nào hỏi được (mục 4). |
| 9 | **Giữ trạng thái** | `<StateFolder>/indexer-state.json` (`IndexerStateFile`): số request đã dùng trong ngày, breaker, cooldown, lỗi gần nhất, thời điểm request cuối (giữ khoảng cách tối thiểu qua lần khởi động lại). Ghi file tạm với write-through và flush xuống đĩa, rồi `File.Replace` giữ bản trước ở `.bak`; mỗi lúc chỉ một lần lưu; lưu chậm nhất 10 s sau thay đổi đầu tiên (kể cả khi cấp lease) và khi thoát (chờ lần lưu đang chạy). Đọc lỗi thì dùng `.bak`; cả hai đều hỏng thì khởi động **thận trọng**: phần nền của mọi site coi như đã dùng hết hôm nay (Auto-Track chờ tới ngày mới, search tay vẫn chạy). Cửa sổ 1 phút không được lưu. App giữ khóa `indexer-state.lock` khi chạy; SearchBench đọc và ghi chung file này, và từ chối chạy khi khóa đang bị giữ (trừ `--force 1`: ngân sách riêng, không lưu) (07d GOV-06, GOV-07). |
| 10 | **Quan sát và điều khiển** | Settings → Search engines: bật/tắt từng site, bảng sức khỏe, nút "Pause all" (kill switch). Pill "Search n/m" ở màn Auto, News, Cart: trạng thái và mức dùng của từng site. Mỗi lượt search ghi một dòng log tóm tắt từng indexer, gồm số request mạng và lượt trúng cache, ví dụ `limetorrents:ok:69/6358ms/3req+1hit` (07d GOV-08). |

## 2. Chính sách mặc định (`IndexerPolicy.DefaultFor`)

| Indexer | Request/phút | Đồng thời | Khoảng cách tối thiểu | Quota/ngày | Request mỗi lượt search (nền / tay) |
|---|---|---|---|---|---|
| Knaben API | 10 | 1 | 2 s | 1000 | 6 / 12 |
| apibay | 15 | 2 | 1 s | 1000 | 6 / 12 |
| nyaa RSS | 10 | 1 | 2 s | 1000 | 6 / 12 |
| limetorrents | 10 | 1 | 3 s | 1000 | 12 / 24 |
| EZTV API | 10 | 1 | 2 s | 1000 | 4 / 8 |
| Còn lại (bitsearch, torrentdownloads, bt4g, yts, torrents-csv, sktorrent, tsukihime, subsplease, nekobt, kickass…) | 6 | 1 | 5 s | 1000 | 6 / 12 |

LimeTorrents được nới từ 6/phút, 5 s (doc gốc) lên 10/phút, 3 s trong commit `7f4bbcb` mà chưa có số đo riêng (07d GOV-05). Số đo có được tới 13/11/2026: hai ngày dùng thật (bench plan 08 ngày 12/11 và bench TV/anime 13/11, vài trăm request mỗi ngày) không có 429, 403 hay trang challenge. Từ đợt này mỗi dòng log ghi số request thật (`req`), nên 7 ngày ở chế độ Both (cổng doc 06 §4) sẽ cho con số chính xác. Không tăng thêm giới hạn mỗi phút nếu chưa có số đo cho thấy site chịu được, và không đo bằng cách gọi dồn.

## 3. Interface (code thật)

```csharp
public sealed class IndexerRequestGovernor
{
    Task<IDisposable> AcquireAsync(string indexerId, SearchLane lane, TimeSpan maxWait, CancellationToken ct);
    void Report(string indexerId, IndexerFailureKind outcome, TimeSpan? retryAfter = null, string? detail = null);
    IndexerAvailability GetAvailability(string indexerId, SearchLane lane = SearchLane.Interactive);
    int Remaining(string indexerId, SearchLane lane);                                  // request còn lại hôm nay của lane
    (int Cap, bool LowBudget) RequestCapPerSearch(string indexerId, SearchLane lane); // trần mỗi lượt (mục 1, dòng 8b)
    void StartCautiously(IEnumerable<string> indexerIds);                             // khi state hỏng
    IReadOnlyList<IndexerStateSnapshot> Snapshot();
    bool KillSwitch { get; set; }
    void SetDisabled(string indexerId, bool disabled);
    event Action? StateChanged;
}

public interface IIndexerHttp   // GovernedIndexerHttp: lease, timeout, cache, phân loại 429/403/5xx, trần mỗi lượt
{
    Task<IndexerHttpResponse> GetAsync(Uri uri, CancellationToken ct);
    Task<IndexerHttpResponse> PostJsonAsync(Uri uri, string json, CancellationToken ct);
    void ReportParsed(bool parsed);   // orchestrator gọi sau khi indexer parse xong một query
    int NetworkRequests { get; }      // telemetry (GOV-08)
    int CacheHits { get; }
}

public sealed class SearchSession      // một target: trần request, backend đã chốt, NoSourceAvailable / NoSourceReason
public static class IndexerStateFile   // Load (.bak, Cautious), Save (flush + .bak), TryLock (indexer-state.lock)
public interface IDetailPageResolver   // Kickass: magnet từ trang chi tiết, chỉ cho tối đa 3 dòng recipe có thể nhận
```

## 4. Tương tác với Orchestrator và Auto-Track

- Trước khi chạy, orchestrator hỏi `GetAvailability` và **bỏ** indexer không sẵn sàng (ghi lý do vào telemetry), thay vì xếp hàng chờ.
- Mỗi indexer chạy các query của nó lần lượt, các indexer chạy song song, trong giới hạn thời gian của lượt search (interactive 90 s, Auto-Track 5 phút).
- Khi trong cả một target không site nào hỏi được (mọi site bị bỏ qua vì Pause all, breaker, hết quota, hoặc bị chặn/lỗi), order trong cart và Auto-Track chuyển **Failed** với lý do "No search source available: …" thay vì "No candidates". Auto-Track không bắt đầu thời gian chờ của `NoCandidates` cho các order này, nên lượt hunt sau thử lại ngay khi có site. Ở chế độ Both, kết quả qBittorrent được dùng.
- Auto-Track (07d GOV-04): tập không tìm được bản chờ 1 giờ → 3 giờ → 6 giờ → 24 giờ tùy độ mới; show được xoay vòng; hunt chạy theo `TorrentHuntIntervalMinutes`.

## 5. Test

- Logic thuần (giới hạn mỗi phút, backoff, chuyển trạng thái breaker, quota, ưu tiên lane) nằm trong `MediaManager.Core` với đồng hồ giả (`TimeProvider`): `IndexerRequestGovernorTests`.
- `GovernedIndexerHttp` và orchestrator với `HttpMessageHandler` giả: trang lạ trả 200 mở breaker, trúng cache không bị tính, người chờ sau không bị hủy theo người khởi tạo, một indexer ném lỗi lạ không làm mất kết quả của indexer khác: `GovernedHttpAndOrchestratorTests`.
- Trần mỗi lượt, ngân sách thấp, cache hit miễn phí, telemetry, "không có nguồn": `SearchSessionTests`. File trạng thái (`.bak`, khởi động thận trọng, khóa, khoảng cách qua lần khởi động lại): `IndexerStateFileTests`. Kickass mở trang chi tiết có giới hạn: `KickassSkTorrentIndexerTests`.
- Mô tả trạng thái cho UI: `NativeSearchHealthTests` (App). Session, backend chốt theo lượt, Both quay về qBittorrent, khởi động thận trọng: `NativeSearchServiceTests` (App).

## 6. Không làm (so với thiết kế ban đầu)

- Jitter ±20% (07d GOV-01): quyết định không làm, xem mục 1 dòng 1. Nếu sau này cần giãn nhịp cho lịch sự thì chỉ **cộng** 0–20% vào khoảng cách tối thiểu (07d GOV-01 có cách làm và cách test tất định).
- SQLite cho trạng thái: dùng JSON (`indexer-state.json`), xem mục 1 dòng 9.
