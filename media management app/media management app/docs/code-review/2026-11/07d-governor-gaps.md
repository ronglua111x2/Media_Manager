# 07d — Năm chỗ governor lệch thiết kế (nhánh `feat/native-search`)

- **Baseline:** commit `5e2b411` trên `feat/native-search`, ngày 2026-11-13.
- **Phạm vi:** năm chỗ code của `IndexerRequestGovernor` và các phần quanh nó không khớp [doc 03](../../planning/native-search/03-request-governor.md) (cùng doc 01, 06 và 07 P2-2):
  1. jitter ±20% cho khoảng cách request;
  2. cache dài cho EZTV tra theo IMDb id;
  3. cắt biến thể query khi không đủ ngân sách;
  4. policy LimeTorrents;
  5. nơi lưu trạng thái governor.
- **Phương pháp:**
  - Đọc code và đếm request theo từng đường gọi: Auto-Track, cart, pack, movie.
  - Lấy số đo từ log `SearchBench shows-compare` mà phiên chính chạy ngày 13/11/2026: 164 lượt search native trên 41 target (21 tập, 20 pack).
  - Phân tích log bằng script Python chỉ đọc file. Mỗi dòng log cho biết số dòng kết quả và thời gian của từng indexer.
- **Không làm:** không sửa code, không build, không gửi request nào tới site torrent, TMDB hay qBittorrent, và không chạy bench.
- **Kiểm chứng:**
  - **K:** người viết đọc lại đường code và xác nhận.
  - **B:** có số đo từ log bench 13/11.
  - Chưa ca nào được chạy lại bằng test.
- **ID:** `GOV-xx`. Mức độ theo [07c](./07c-native-search.md): **Cao** là nhận sai torrent, dừng cả lượt chạy hoặc có nguy cơ bị ban IP; **TB** là mất kết quả hoặc hỏng một đường chạy; còn lại là **Thấp**.

> **Kết luận chính.**
> 1. **Ngân sách bị cạn vì số query nhân với số lần lặp, không phải vì policy quá chặt.** Trong bench, bitsearch và torrentdownloads hết 200 request ở lượt search thứ 107. Lúc đó bench mới tới target thứ 29 và show thứ 15, và 72% số lượt là pack (GOV-03).
> 2. **Auto-Track có sẵn một vòng lặp gọi dồn** mà doc 03 §4 cấm:
>    - tập không tìm được bản (`NoCandidates`) bị hunt lại ở mỗi chu kỳ 30 phút;
>    - hàng đợi xếp theo tên show, mỗi chu kỳ chỉ lấy 3 show đầu.
>
>    Chỉ cần khoảng 15 tập kẹt là ngân sách nền của bitsearch và torrentdownloads hết trong 2–3 giờ sau lúc reset. Các show đứng sau trong bảng chữ cái không bao giờ được hunt (GOV-04; lỗi này có từ trước nhánh).
> 3. **Đề xuất cho mục 3, theo thứ tự:**
>    1. Auto-Track lùi thời gian hunt lại cho tập kẹt và xoay vòng show (GOV-04).
>    2. Gộp các dạng query pack tương đương (GOV-03 A).
>    3. Đặt trần request cho mỗi lượt search, kèm chế độ "ngân sách thấp" (GOV-03 B). Chế độ này là phần doc §4 yêu cầu.
> 4. **Jitter không giúp tránh ban,** vì site đếm số request chứ không xem nhịp. Thêm jitter để trông giống người còn đi ngược nguyên tắc "không vượt chặn". Chỉ cần sửa doc (GOV-01).
> 5. **Cache dài cho EZTV có lợi nhỏ.** EZTV không phải nút thắt, và một URL đã dùng chung cho mọi query của một show (GOV-02).
> 6. **LimeTorrents:** giữ 10 request/phút và khoảng cách 3 s, vì đã chạy hai ngày không bị chặn và cần mức này để vừa ngân sách 90 s. Nên hạ quota ngày từ 500 xuống 400 tới khi có số đo 7 ngày. Bitsearch và torrentdownloads giữ mức bảo thủ (GOV-05).
> 7. **File JSON lưu trạng thái** được ghi nguyên tử nhưng không flush xuống đĩa. Khi đọc lỗi, app mở lại toàn bộ giới hạn (fail-open). SearchBench chạy governor riêng, nên IP có thể chịu gấp đôi ngân sách (GOV-06, GOV-07).

---

## 1. Bảng tóm tắt

| ID | Mức | Mục | Vấn đề | Kiểm chứng | Đề xuất ngắn |
|---|---|---|---|---|---|
| GOV-01 | Thấp | 1 | Không có jitter ±20% như doc ghi. Doc gọi cơ chế là "token bucket" nhưng code dùng cửa sổ trượt | K | Không làm; sửa doc |
| GOV-02 | Thấp | 2 | Mọi site dùng chung TTL 45 phút; doc ghi EZTV 2–6 giờ | K, B | Để sau GOV-04; nếu làm thì EZTV 2 giờ, chỉ cho lane nền |
| GOV-03 | TB | 3 | Không có bước cắt biến thể query theo ngân sách; pack sinh tới 12 query, mỗi query thêm một bản "+chất lượng" | K, B | Gộp dạng pack, rồi đặt trần request mỗi lượt search kèm chế độ ngân sách thấp |
| GOV-04 | TB | 3 | Auto-Track hunt lại tập `NoCandidates` mỗi 30 phút; hàng đợi theo tên show nên show sau bị bỏ đói; `TorrentHuntIntervalMinutes` không được dùng | K | Lùi thời gian hunt lại theo tập; xoay vòng show |
| GOV-05 | Thấp | 4 | LimeTorrents 10/phút, 3 s, 500/ngày, khác doc (6, 5 s, 300); được nới mà chưa đo | K, B | Giữ 10/phút và 3 s; hạ quota ngày xuống 400; ghi số đo vào doc |
| GOV-06 | Thấp | 5 | `indexer-state.json` không flush; file hỏng thì reset mọi giới hạn; hai lần lưu có thể chạy cùng lúc; không lưu cửa sổ phút; ngày tính theo UTC | K | Flush và giữ bản `.bak`; khi file hỏng thì khởi động thận trọng; khóa lúc lưu; sửa doc 03 |
| GOV-07 | Thấp | 5 | SearchBench tạo governor riêng, không đọc `indexer-state.json` | K | Bench đọc và ghi chung state, hoặc từ chối chạy khi app đang search native |
| GOV-08 | Thấp | 3, 4 | Telemetry không ghi số request mạng và số lần trúng cache của từng indexer | K | Thêm `Requests` và `CacheHits` vào `IndexerRunTelemetry` |
| GOV-09 | Thấp | doc | Doc 01, 03 và 07 còn ghi SQLite, `IIndexerRequestGovernor`, `IndexerRequestOptions`, mốc thời gian breaker cũ | K | Sửa doc (xem mục 4) |

---

## 2. Một lượt search tốn bao nhiêu request

### 2.1 Từ query recipe tới request

- `NativeSearchService.SearchWithMemoAsync` (`NativeSearchService.cs:177-235`) xử lý mỗi query của recipe như sau:
  1. rút gọn query (`NativeQueryText.Reduce`): bỏ token chất lượng; với movie bỏ thêm năm;
  2. sinh danh sách bản "cụ thể" (`SpecificQueries`, `NativeQueryText.cs:82-103`):
     - với TV, đó là chính query nếu query có token chất lượng (0 hoặc 1 bản);
     - với movie, đó là "tên + 2 chất lượng tốt nhất của recipe";
  3. gửi cả nhóm `[reduced, specifics…]` cho orchestrator (`:280-283`).
- Orchestrator chạy mọi query của nhóm trên từng indexer (`SearchOrchestrator.cs:232-253`). Indexer không có `PrefersSpecificQueries` bỏ qua các bản cụ thể.
- Hai lớp cache:
  - **Cache HTTP** 45 phút theo URL (`NativeSearchService.cs:273-274`): hai query recipe có cùng bản rút gọn chỉ tốn một request.
  - **Memo** 15 phút cho cả câu trả lời (`:78`).

| Indexer | Nhận query nào | Request cho mỗi lượt (1 bản rút gọn + s bản cụ thể) |
|---|---|---|
| bitsearch, torrentdownloads | rút gọn và cụ thể | 1 + s (không phân trang) |
| limetorrents | rút gọn (1–4 trang, dừng khi trang có dưới 40 dòng) và cụ thể (1–2 trang) (`LimeTorrentsIndexer.cs:20-41`) | (1–4) + s × (1–2) |
| knaben | rút gọn | 1–2 (movie 1–3) |
| eztv | IMDb id; URL chỉ gồm id và số trang (`EztvIndexer.cs:41`) | 1–2 cho mỗi show, các lượt sau trúng cache |
| apibay, nyaa, bt4g, torrents-csv, sktorrent, tsukihime… | rút gọn | 1 (một vài site 2) |

### 2.2 Theo loại search

| Loại | Đường code | Số lượt gọi orchestrator | Request tới bitsearch/torrentdownloads |
|---|---|---|---|
| Tập, Auto-Track | `AutoTrackService` → `FetchEpisodeCandidatesAsync`, ép chế độ song song (`AutoTrackSearchSettings.cs:11`) → `SearchEpisodeCandidatesSequentialAsync`, dừng khi đủ 3 ứng viên (`FetchJobService.cs:781`) | tập có bản: thường 1; tập không có bản: chạy hết, bench đo 1–4 lượt (trung bình 2,1) | khoảng 2 nếu có bản, khoảng 4 nếu không |
| Tập, cart (mặc định snapshot) | `ShowSearchSnapshotService`: query cấp show, sau đó search riêng tối đa 6 tập còn thiếu (`FetchJobService.cs:575`) | 1–3 cấp show + mỗi tập thiếu 1–4 | |
| Pack (chỉ cart) | `FetchSeasonPacksAsync` → `CaptureSnapshotAsync(TvSeasonPack)` → `BuildSeasonPackQueries` (2 tên × template mùa × số mùa, cộng biến thể dấu nháy) rồi query cấp show (`ShowSearchSnapshotService.cs:57-66`) | bench đo 2–12 lượt (trung bình 6), cộng query cấp show. Ngưỡng dừng 2000 dòng (`AutoTorrentSettings.cs:37`) gần như không bao giờ đạt với native | bench khoảng 10 mỗi pack, có thể tới khoảng 24 |
| Movie | `BuildMovieQueries`: rút gọn bỏ năm và chất lượng; mỗi tên thêm 2 bản cụ thể | khoảng 1 cho mỗi tên | 3 mỗi tên; LimeTorrents tới 8 trang mỗi tên |

**Số đo (B).** Trước khi bitsearch và torrentdownloads hết ngân sách, bench đã chạy 107 lượt (77 lượt pack, 30 lượt tập) trên 29 target:
- 200 request cho 107 lượt, tức khoảng **1,87 request mỗi lượt**;
- 14 pack tốn khoảng 144 request (**khoảng 10 mỗi pack**);
- 15 tập tốn khoảng 56 request (**khoảng 3,7 mỗi tập**), vì bench không dừng sớm.

### 2.3 Tải hằng ngày của Auto-Track với khoảng 35 show

**Các tham số trong code:**
- **Nhịp hunt:** scheduler chạy TMDB discovery mỗi 30 phút (`AutoTrackSchedulerService.cs:221-225`, `AutoTrackSettings.cs:20`), và hunt chạy ở cuối mỗi lượt (`AutoTrackService.cs:359-363`). Setting `TorrentHuntIntervalMinutes = 60` (`AutoTrackSettings.cs:22`) không được đọc ở đâu.
- **Kích thước mỗi chu kỳ:**
  - tối đa 3 show (`AutoTrackSearchSettings.cs:5`), lấy theo **thứ tự tên show** (`AutoTrackService.cs:799`, `:487-488`);
  - tối đa 5 tập mỗi show;
  - chỉ tập đã chiếu ít nhất 4 giờ.
- **Tập `NoCandidates` không bị chặn hunt lại** (`TorrentCartService.cs:178-188`). `PrepareAutoTrackEpisodeOrder` đưa nó về `Draft` (`:202-215`).
- **Tác dụng của cache:**
  - memo 15 phút ngắn hơn chu kỳ 30 phút, nên không có tác dụng;
  - cache HTTP 45 phút bắt được lượt cách 30 phút, nên cùng một URL ra mạng khoảng **60 phút một lần, tức khoảng 24 lần mỗi ngày**.
- **Ngân sách lane nền** (70% quota ngày, `IndexerRequestGovernor.cs:25, 358-364`):

  | Site | Ngân sách nền/ngày |
  |---|---|
  | bitsearch, torrentdownloads | 140 |
  | limetorrents, knaben | 350 |
  | nyaa | 420 |
  | eztv | 210 |
  | apibay | 560 |

  Ngày tính theo UTC, nên quota reset lúc **07:00 giờ Việt Nam**.

"Tập kẹt" là tập đã chiếu, vẫn `Missing`, mà recipe không nhận bản nào: bản chưa lên tracker, recipe quá chặt, hoặc show ít người seed. Bench có vài ca như vậy, ví dụ Avatar S03E21 (recipe Korra) và Oregairu S03E12. Mỗi tập kẹt tốn khoảng 2 lượt × 1,9 request, tức **khoảng 3,8 request** cho bitsearch (torrentdownloads cũng vậy) trong mỗi vòng ra mạng.

| Kịch bản | bitsearch/ngày (torrentdownloads tương tự) | Ngân sách nền 140 hết sau | LimeTorrents/ngày (nền 350) |
|---|---|---|---|
| Bình thường: khoảng 20 show đang chiếu, khoảng 3 tập mới mỗi ngày, có bản sau 1–2 chu kỳ | khoảng 10–15 | không hết | khoảng 20–40 |
| 2 tập kẹt | khoảng 180 | khoảng 18 giờ | khoảng 240 |
| 5 tập kẹt (một show tồn 5 tập) | khoảng 450 | khoảng 7 giờ | khoảng 600, hết sau khoảng 14 giờ |
| 15 tập kẹt (3 show × 5 tập) | khoảng 1 400 | **2–3 giờ** (khoảng 09:30 giờ Việt Nam) | hết sau khoảng 5 giờ |

Ngoài chuyện ngân sách: khi 3 show đầu bảng chữ cái có tập kẹt, chúng chiếm cả 3 chỗ ở mọi chu kỳ, nên các show còn lại **không bao giờ được hunt** (GOV-04).

---

## 3. Chi tiết

### GOV-01 — Jitter ±20% (Thấp)

- **Doc nói:** §1 dòng 1 ghi "token bucket, … khoảng cách tối thiểu giữa hai request, cộng jitter ngẫu nhiên ±20%".
- **Code làm:**
  - Khoảng cách tối thiểu là cố định: `now - _lastStart < MinInterval` (`IndexerRequestGovernor.cs:384-388`). `_lastStart` được ghi lúc cấp lease (`:238`).
  - Giới hạn mỗi phút là **cửa sổ trượt** 60 s, dùng hàng đợi thời điểm (`:390-400`), không phải token bucket. Code không có jitter.
- **Jitter có giúp tránh ban không:**
  - Rate limit của Cloudflare và kiểu fail2ban đếm **số request** trong một khoảng thời gian. Jitter không đổi con số đó.
  - Bộ chấm điểm bot có xét nhịp request, nhưng app đã tự khai là bot bằng User-Agent trung thực `MediaManager/1.0 (+native-search)` (`NativeSearchService.cs:59`). Thêm ngẫu nhiên để trông giống người là một kỹ thuật né phát hiện, trái với doc 03 §1.5 và nguyên tắc của dự án.
  - Lợi ích hợp lệ duy nhất của jitter là tránh nhiều client gọi cùng một nhịp. App chỉ có một client, request tới mỗi site đi tuần tự, và chu kỳ Auto-Track tính từ lúc app khởi động chứ không theo giờ tròn. Vì vậy lợi ích này gần như bằng 0.
  - Điều có tác dụng thật là **số request**, tức là GOV-03 và GOV-04.
- **Nếu vẫn muốn làm** (ví dụ để giãn nhịp cho lịch sự):
  - Chỉ **cộng thêm** 0–20%. Không trừ, nếu không khoảng cách sẽ xuống dưới mức tối thiểu đã cấu hình. Doc ghi "±" là sai ở điểm này.
  - **Cách làm:** khi cấp lease (`IndexerRequestGovernor.cs:233-247`), tính `_nextAllowed[id] = now + MinInterval × (1 + 0,2 × r)` một lần. `BlockingReason` so sánh với `_nextAllowed` thay cho `_lastStart + MinInterval`. Như vậy các lần gọi `BlockingReason` liên tiếp trả cùng một kết quả.
  - **Giữ test tất định:** truyền nguồn ngẫu nhiên vào constructor, ví dụ `Func<double>? random = null`, mặc định là `Random.Shared.NextDouble`.
    - Test hiện có truyền `() => 0`, nên không đổi hành vi.
    - Test mới truyền `() => 0.99` và dùng `ManualTime` (`IndexerRequestGovernorTests.cs:9-16`) để kiểm tra rằng `AcquireAsync(maxWait: 0)` bị từ chối trước `1,198 × MinInterval` và được cấp sau đó.
- **Đề xuất:** không làm. Sửa doc 03 §1 dòng 1 (mục 4).

### GOV-02 — Cache EZTV 2–6 giờ (Thấp)

- **Doc nói:** §1 dòng 7 ghi TTL 30–60 phút, riêng "EZTV tra theo IMDb id: 2–6 giờ".
- **Code làm:**
  - Một TTL 45 phút cho mọi indexer, đặt trong factory `GovernedIndexerHttp` (`NativeSearchService.cs:273-274`). Bench dùng 30 phút (`ShowsCompareCommand.cs:63-64`).
  - Không chọn được TTL theo request: doc 03 §3 có tham số `IndexerRequestOptions` nhưng `IIndexerHttp.GetAsync(Uri, ct)` không có (`ITorrentIndexer.cs:27`).
- **Số đo (B):**
  - URL của EZTV chỉ gồm IMDb id và số trang (`EztvIndexer.cs:41`), nên mọi query của một show dùng chung response.
  - Trong bench, EZTV báo 0 ms ở **143/164** lượt, nghĩa là trúng cache.
  - Vì vậy cache dài chỉ có tác dụng **giữa các lượt Auto-Track**, khoảng 60 phút một lần.
- **Lợi ích:**
  - Với mỗi show kẹt, EZTV ra mạng 1–2 request mỗi vòng, tối đa khoảng 48 request mỗi ngày. Ba show kẹt là tối đa khoảng 144, vẫn dưới ngân sách nền 210.
  - Khi mọi thứ bình thường thì lợi ích gần bằng 0. **EZTV không phải nút thắt.**
- **Rủi ro dữ liệu cũ:**
  - Trang 1 là 100 dòng mới nhất của show. Với TTL 6 giờ, tập mới lên EZTV có thể bị trễ tới 6 giờ.
  - Chuyện này xảy ra khi show vừa được tải gần đây, ví dụ show có một tập kẹt khác hoặc user vừa mở cart. Tập mới vẫn được các indexer khác tìm ra.
- **Đề xuất:**
  - Làm sau GOV-04. Khi tập kẹt đã được hunt thưa đi thì lợi ích này gần như biến mất.
  - Nếu vẫn làm: TTL theo indexer ngay trong factory (`id == "eztv" && lane == Background ? 2 h : 45 min`), không cần thêm `IndexerRequestOptions`.
  - Sửa doc cho khớp (mục 4).

### GOV-03 — Không cắt biến thể query theo ngân sách (TB)

- **Doc nói:** §4 ghi "Nếu một lượt search cần nhiều request hơn token còn lại, QueryPlanner **cắt bớt** biến thể query theo thứ tự ưu tiên".
- **Code làm:**
  - Không có `QueryPlanner`, và governor không có hàm báo phần ngân sách còn lại.
  - Orchestrator chỉ khử trùng query (`SearchOrchestrator.cs:135-138`) và bỏ site đã hết ngân sách (`:160-165`). Các site còn chạy được thì chạy **mọi** query (`:232-253`).
  - Nơi gọi đưa từng query một:
    - `SearchEpisodeCandidatesSequentialAsync` (`FetchJobService.cs:716-792`);
    - vòng lặp snapshot (`ShowSearchSnapshotService.cs:113-153`).

    Vì vậy orchestrator không bao giờ thấy cả kế hoạch, và **chỉ cắt ở orchestrator thì không đủ**.
  - Mỗi query còn được gửi thêm bản "+chất lượng" cho site phân trang (`NativeSearchService.cs:182-183`).
- **Pack sinh nhiều query:**
  - `BuildSeasonPackQueries` lấy 2 tên × mọi template có `{season}` (`SearchPlanBuilder.cs:55-89`, `Take(2)` ở `:69`).
  - `QueryTemplateRenderer.Normalize` thêm biến thể tách dấu nháy vào cuối danh sách (`QueryTemplateRenderer.cs:33-55`, có từ `5e2b411`).
  - Bench có 3 pack với 12 query:
    - Mangaka: "A Mangakas…", "Egao no…", "A Mangaka s…";
    - Gals Can't Be Kind to Otaku;
    - I'm in Love with the Villainess.
- **Số đo dạng query pack (B).** Dòng của mọi indexer trừ EZTV (EZTV không phụ thuộc chữ), cộng theo lượt, chưa khử trùng giữa các lượt:

  | Dạng | Lượt | Dòng | Lượt có dòng | Nguồn chính |
  |---|---|---|---|---|
  | `<tên> season N` | 35 | 3 811 | 30 | torrentdownloads 1 633, LimeTorrents 969, Knaben 311, apibay 277 |
  | `<tên> <năm> season N` | 33 | 1 450 | 20 | torrentdownloads 1 330, LimeTorrents 43, bitsearch 31 |
  | `<tên> Sxx pack` | 33 | 1 499 | 17 | torrentdownloads 1 489, các site khác ≤ 9 |
  | `<tên> Sxx complete` | 24 | 873 | 12 | torrentdownloads 869, các site khác ≤ 2 |
  | `<tên> Sxx` | 2 | 511 | 2 | Knaben 253, torrentdownloads 100, LimeTorrents 56, bitsearch 40 |

  - Ngoài torrentdownloads, các dạng "complete", "pack" và "năm + season" gần như không trả gì.
  - torrentdownloads trả dòng với mọi dạng vì RSS của nó khớp **bất kỳ** từ nào, rồi indexer chỉ giữ dòng có đủ chữ của tên (`TorrentDownloadsIndexer.cs:38-63`). Các dòng đó nhiều khả năng là cùng bản với dạng `season N`, nhưng log hiện tại không tách được (cần `--dump` ghi kèm query).
  - Biến thể tách dấu nháy chỉ có dòng ở dạng `season 1` (9, 9, 2, 21 dòng). Các dạng khác của nó đều trả 0 dòng.
- **Rủi ro:**
  - Bench: hai site hết quota ở target thứ 29 trên khoảng 70.
  - Ứng dụng thật:
    - ngân sách nền 140 hết theo bảng ở mục 2.3;
    - một lượt search pack trong cart tốn khoảng 10–24 request mỗi site, nên khoảng 8–20 lượt pack là hết quota tương tác của ngày.
  - Sau đó, cho tới 07:00 hôm sau, hai nguồn phân trang bị bỏ qua (`skipped`). Riêng torrentdownloads là nguồn pack mạnh nhất trong bench.
  - Không có nguy cơ ban, vì governor vẫn chặn đúng quota. Cái mất là kết quả.

**Các hướng và đánh giá:**

| Hướng | Làm gì | Hiệu quả | Rủi ro |
|---|---|---|---|
| **A. Gộp query tương đương** trước khi tới indexer | Với native, pack chỉ dùng `<tên> season N` và `<tên> Sxx` cho mỗi tên. Bỏ "complete", "pack" và "năm + season". Biến thể dấu nháy chỉ đi với dạng `season N`. Bản "+chất lượng" chỉ cho tên chính. Đặt trong `NativeQueryText` hoặc một bước lọc ở `NativeSearchService`, không đổi `BuildSeasonPackQueries` của đường qBittorrent | Pack 2 tên: từ 8–12 lượt (khoảng 10–24 request mỗi site) xuống 4–5 lượt (khoảng 5–6 request) | Thấp. Số đo trên cho thấy các dạng bị bỏ gần như không đóng góp ngoài torrentdownloads. Nên xác nhận bằng một lần `shows-compare --dump` có ghi query |
| **B. Trần request mỗi lượt search, cộng chế độ ngân sách thấp** (đúng doc §4) | Thêm `SearchSession` dạng `AsyncLocal`, giống `BackgroundScope` (`NativeSearchService.cs:108-113`). FetchJob mở một session cho mỗi tập, pack hoặc movie. `GovernedIndexerHttp` đếm request mạng (chỉ `fromNetwork`, không đếm trúng cache) cho từng indexer. Policy có `MaxRequestsPerSearch`, ví dụ bitsearch/torrentdownloads 4, LimeTorrents 8 trang, Knaben 4. Governor có `Remaining(id, lane)`. Khi còn dưới 20% ngân sách lane: mỗi lượt chỉ gửi query ưu tiên 1 (tên chính, dạng chính), không gửi bản "+chất lượng", LimeTorrents chỉ 1 trang. Query bị cắt có trạng thái `trimmed`, được coi là đủ khi lưu memo | Không lượt nào tốn quá trần. Quota ngày được chia cho nhiều show thay vì vài pack đầu | Trung bình: cần đổi interface (session, `IndexerQuery.MaxPages`). Thứ tự ưu tiên đã có sẵn: `Normalize` để dạng chính trước và biến thể sau (`QueryTemplateRenderer.cs:27-31`) |
| **C. Giữ riêng cho interactive theo site** | Đã có: lane nền chỉ dùng 70% (`IndexerRequestGovernor.cs:358-364`) | Đủ, nếu có B để một lượt pack không ăn hết phần interactive | Không cần làm thêm. Lưu ý bench chạy lane interactive nên dùng được 100% (GOV-07) |
| **D. Dùng cache và memo khéo hơn** | Tăng TTL cho lane nền, hoặc nhớ kết quả rỗng | Giảm tối đa khoảng 2 lần với vòng lặp tập kẹt | Kết quả cũ làm trễ bản mới. Không giải quyết được gốc |
| **E. Lùi thời gian hunt lại cho tập kẹt** (GOV-04) | Ngoài governor, xem bên dưới | Tập kẹt từ khoảng 24 vòng xuống khoảng 5 vòng mỗi ngày: **giảm khoảng 5 lần** ở kịch bản tốn nhất | Thấp |

**Đề xuất: làm E trước, sau đó A, rồi B.**
- **E** bỏ hệ số nhân lớn nhất: cùng một query lặp 24 lần mỗi ngày. Lượt hunt đầu của tập mới không mất gì.
- **A** rẻ, là hàm thuần trong Core nên test được bằng unit test. Nó bỏ những dạng mà số đo cho thấy gần như không đóng góp, và cắt khoảng một nửa request của pack.
- **B** là lưới an toàn mà doc §4 yêu cầu. Nếu chỉ làm B, vòng lặp vẫn chạy, chỉ là query bị cắt sớm hơn, nên ngân sách vẫn không đủ dùng cả ngày.
- **D** chỉ làm cho EZTV (GOV-02).

**Test:**
- A: unit test cho danh sách query pack sau khi gộp (2 tên, có và không có dấu nháy).
- B: test Core với `ManualTime`, dùng indexer giả đếm số lần gọi:
  - trần mỗi lượt;
  - chế độ ngân sách thấp;
  - trúng cache không bị tính.

### GOV-04 — Auto-Track lặp lại tập kẹt và bỏ đói show sau (TB, có từ trước nhánh)

- **Doc nói:** 03 §4 ghi "Auto-Track chạy nhiều show liên tiếp: governor tự giãn nhịp. Auto-Track không có vòng lặp gọi dồn."
- **Code làm:**
  - `FindPendingEpisodes` (`AutoTrackTmdbEligibility.cs:112-138`) chỉ xét các điều kiện: đã chiếu đủ 4 giờ, `Missing`, chưa có hash, không có order đang chạy. Nó không đếm số lần thử và không có thời điểm thử lại.
  - `NoCandidates` không nằm trong các trạng thái chặn (`TorrentCartService.cs:178-188`). `PrepareAutoTrackEpisodeOrder` đưa nó về `Draft` và search lại (`:202-215`).
  - Hàng đợi xếp theo `Title` (`AutoTrackService.cs:799`) và lấy 3 show đầu (`:487-488`).
  - Hunt chạy sau **mỗi** lượt TMDB discovery, tức 30 phút một lần (`:359-363`, `AutoTrackSchedulerService.cs:221-225`). Setting `TorrentHuntIntervalMinutes` không có tác dụng.
  - Khi phiên debug hunt đang bật, bước dừng sớm ở 3 ứng viên bị bỏ qua (`FetchJobService.cs:781`), nên mọi query đều chạy.
- **Có từ trước nhánh:** `main` có cùng thứ tự theo tên và cùng `Take(batchSize)`. Trước đây vòng lặp này gọi plugin qBittorrent, không có governor nào giữ.
- **Rủi ro:**
  - Ngân sách: xem bảng ở mục 2.3.
  - Show đứng sau trong bảng chữ cái không bao giờ được hunt khi 3 show đầu có tập kẹt. Log chỉ cho thấy hunt chạy đều mỗi 30 phút, nên khó nhận ra.
- **Đề xuất:**
  1. **Lùi thời gian hunt lại cho từng tập.**
     - Lượt `NoCandidates` thứ n hẹn lượt sau sau 1 giờ, 3 giờ, 6 giờ, 12 giờ, rồi 24 giờ.
     - Trong 12 giờ đầu sau thời điểm được phép hunt (air + 4 giờ), có thể giữ nhịp dày hơn (2–4 lượt), vì bản simulcast thường lên trong vài giờ.
     - Xóa mốc chờ khi: TMDB đổi ngày chiếu, recipe hoặc override đổi, user bấm Run Now, hoặc user search tay.
     - Lưu trên order `NoCandidates` sẵn có (`NextAttemptUtc`, `Attempts`). Cách này cần migration; nếu muốn tránh migration như doc 06 thì lưu bằng file JSON.
  2. **Xoay vòng show:** xếp hàng đợi theo "lần hunt gần nhất" (cũ nhất trước) thay vì theo tên.
  3. **`TorrentHuntIntervalMinutes`:** dùng nó hoặc bỏ khỏi UI.
     - Nếu dùng với 60 phút thì log bớt ồn, nhưng tải mạng không đổi: cache 45 phút vốn chỉ bắt được lượt cách 30 phút, nên request vẫn ra mạng 60 phút một lần.
     - Tải chỉ giảm nhờ mục 1.
- **Test:** unit test cho `FindPendingEpisodes` hoặc hàm chọn hàng đợi:
  - tập vừa `NoCandidates` không được chọn trước mốc chờ;
  - show bị bỏ qua lâu nhất được chọn trước.

### GOV-05 — Policy LimeTorrents (Thấp)

- **Doc nói:** §2 ghi 6 request/phút, 1 đồng thời, cách nhau 5 s, 300/ngày, "nhẹ tay". Doc cũng ghi "Không bao giờ tăng quá mức đã được chứng minh an toàn".
- **Code làm:** `IndexerRequestGovernor.cs:35` đặt 10/phút, 3 s, 500.

  | Commit | Policy | Lý do ghi trong commit |
  |---|---|---|
  | `062a5ff` | 6/phút, 5 s, 300 | ban đầu |
  | `29a2753` | 8/phút, 4 s, 400 | tối đa 2 trang |
  | `7f4bbcb` | 10/phút, 3 s, 500 | 4 trang như plugin cũ; nguồn của 20 trên 31 bản tốt mà native còn thiếu |

  - Không commit nào có số đo về ngưỡng của site.
  - Indexer lấy tối đa 4 trang cho query rút gọn và 2 trang cho query cụ thể. Nó dừng khi trang có dưới 40 dòng (`LimeTorrentsIndexer.cs:20-41`); phần `FullPageRows` của NS-26 đã sửa.
- **Số liệu có được (B):**
  - Đợt đo plan 08 ngày 12/11 (15 movie, nhiều lần chạy) và bench 13/11 (164 lượt): LimeTorrents báo "ok" ở mọi lượt. Không có 429, 403 hay trang challenge.
  - Số request của ngày 13/11 ước khoảng 300–400: mỗi lượt mất 3–6 s với khoảng cách 3 s, chỉ 19/164 lượt có từ 40 dòng trở lên. Đây chỉ là ước lượng, vì telemetry không đếm request (GOV-08).
  - Plugin qBittorrent cũ lấy 4 trang liền nhau, không giãn nhịp, và chạy nhiều search song song. Theo những gì đã biết thì chưa bị chặn, nhưng đây chỉ là giai thoại, không phải số đo.
- **Ảnh hưởng tới tốc độ:**
  - Thời gian chờ governor nằm trong ngân sách của lượt search: 90 s cho interactive (`NativeIndexerCatalog.cs:32`, `NativeSearchService.cs:264-267`).

    | Số trang | 10/phút, 3 s | 6/phút, 5 s |
    |---|---|---|
    | 12 (một pack điển hình) | khoảng 63 s | khoảng 85 s |
    | 16 (movie 2 tên) | khoảng 75 s | khoảng 135 s, **vượt 90 s** |

  - Khi vượt ngân sách, LimeTorrents bị ghi "timeout", các trang sau bị mất và câu trả lời không được lưu memo.
  - LimeTorrents là nguồn movie tốt nhất theo plan 08, nên quay về 6/phút làm hỏng chính trường hợp dùng chính.
- **Đề xuất:**
  - **Giữ 10/phút và 3 s.** Hai ngày dùng thật không bị chặn, nhịp này vẫn nhẹ hơn plugin cũ, và cần nó để vừa ngân sách 90 s.
  - **Hạ quota ngày từ 500 xuống 400** (nền 280). Theo luật của doc, 400 là mức cao nhất đã thấy chạy trong một ngày mà không bị chặn; 500 chưa từng đạt. Chi phí của việc hạ chỉ lộ ra vào ngày chạy bench. Sau GOV-03 và GOV-04, nhu cầu thật thấp hơn nhiều.
  - Chỉ nâng lại khi chế độ Both chạy 7 ngày không có 429, 403 hay challenge. Đây cũng là cổng của doc 06 §4.
  - Ghi bảng lịch sử và số đo này vào doc 03 §2.
- **Bitsearch và torrentdownloads** (`Conservative`: 6/phút, 5 s, 200; `IndexerRequestGovernor.cs:27`):
  - Mỗi query chỉ tốn 1 request (không phân trang), nên 6/phút không làm chậm đáng kể: một lượt 2 request chờ 5 s.
  - Cả hai đứng sau Cloudflare. torrentdownloads còn chèn beacon của Cloudflare vào RSS (`GovernedIndexerHttp.cs:22-28`).
  - Bench chạm 200 mà không bị chặn, nên 200 ở 6/phút đã được chứng minh an toàn.
  - Nút thắt là số query (GOV-03, GOV-04), không phải policy. **Giữ nguyên.** Chỉ xem xét 300 sau khi GOV-03, GOV-04 và GOV-08 xong, có số đo 7 ngày không bị chặn, và chỉ khi quota vẫn hết thường xuyên.

### GOV-06 — Lưu trạng thái bằng JSON (Thấp)

- **Doc nói:**
  - 03 §1 dòng 9 ghi lưu vào SQLite, bảng `IndexerState`.
  - 06 §2 đã ghi là đổi sang `indexer-state.json`.
  - 01 §3, §7 và 07 P2-2 vẫn ghi `SqliteIndexerStateStore` và migration `011`.
- **Code làm:**
  - File `<StateFolder>/indexer-state.json` (`NativeSearchService.cs:55, 514`).
  - Đọc ở `:516-529`, lưu ở `:531-569`, `Dispose` ở `:571-576`.
  - Governor khôi phục trạng thái ở `IndexerRequestGovernor.cs:120-125`.

| Điểm kiểm tra | Kết quả |
|---|---|
| Ghi nguyên tử | **Có.** Ghi `.tmp` rồi `File.Move(overwrite: true)` (`:560-563`), nên app crash bình thường không để lại file ghi dở |
| Bền khi mất điện | **Không.** `File.WriteAllText` không flush xuống đĩa. NTFS có thể ghi lệnh đổi tên trước dữ liệu, nên sau khi mất điện file có thể rỗng hoặc toàn số 0. Máy này đã từng crash hoặc mất điện (xem `92737a7`) |
| File hỏng | `LoadState` bắt lỗi rồi chạy với **trạng thái trắng** (`:524-528`): mọi quota về 0, breaker đang mở bị quên. Đây là **fail-open**, hỏng theo hướng nguy hiểm cho chuyện ban. Không có `.bak`. Chỉ một mục sai cũng làm mất cả file |
| Ghi đồng thời | `Timer.Dispose()` không chờ callback đang chạy, và `Change()` trong lúc callback đang chạy có thể sinh callback thứ hai. Hai lần `SaveState` cùng ghi một `.tmp`, gây `IOException`. Lỗi bị bỏ qua nên một lần lưu (có thể là lần cuối khi thoát) bị mất |
| Gộp thay đổi | Chờ 2 s, tối đa 10 s (NS-24 đã sửa). Crash mất tối đa 10 s trạng thái |
| Lúc báo thay đổi | `StateChanged` chỉ được gọi từ `Report` và kill switch (`IndexerRequestGovernor.cs:318, 134`), không gọi khi cấp lease (`:239`). Request bị hủy giữa chừng chỉ được lưu ở lần `Report` sau |
| Được lưu | `IndexerStateSnapshot` (`:49-78`): `DayKey`, `RequestsToday`, `Breaker`, `OpenUntilUtc`, `CooldownUntilUtc`, `ConsecutiveFailures`, `OpenCount`, `RateLimitLevel`, lỗi gần nhất, lần thành công gần nhất. `InFlight` được đặt về 0 khi đọc lại |
| Không được lưu | Cửa sổ phút `_recent` và `_lastStart` (`:107-108`). Sau khi khởi động lại, request đầu đi ngay và phút đầu có thể thêm tối đa `RequestsPerMinute` request. Rủi ro nhỏ, vì app khởi động lâu hơn `MinInterval`. Cache, memo và miss IMDb mất khi tắt app, và cái đó chấp nhận được |
| Ngày | Tính theo UTC (`RollDay`, `:421-434`), nên reset lúc 07:00 giờ Việt Nam. Doc ghi "trần request/24h", trong khi quanh lúc reset site có thể nhận tới 2 lần quota trong 24 giờ. Mục này đã có trong danh sách bỏ qua của đợt 2 |
| Nhiều process | Xem GOV-07 |

- **Đề xuất:**
  1. Ghi bằng `FileStream` với `FileOptions.WriteThrough` và `Flush(true)`, rồi `File.Replace(tmp, path, path + ".bak")` để luôn giữ bản trước.
  2. Khi đọc lỗi, thử `.bak`. Nếu cả hai đều hỏng mà file vẫn tồn tại, khởi động ở chế độ **thận trọng**: mọi site coi như hôm nay đã dùng đủ phần nền (`RequestsToday = BackgroundShare × DailyBudget`). Auto-Track dừng tới ngày mới, search tay vẫn chạy. Ghi Warning.
  3. Dùng một khóa cho `SaveState`. `Dispose` gọi `Timer.Dispose(WaitHandle)` và chờ callback xong rồi mới lưu lần cuối.
  4. Lưu `LastRequestUtc` của từng site. Rẻ, và giữ được khoảng cách tối thiểu qua lần khởi động lại. Không cần lưu cả cửa sổ phút.
  5. Sửa doc 03 theo mục 4.
- **Test:**
  - đọc file rỗng hoặc hỏng thì vào chế độ thận trọng;
  - đọc `.bak` khi file chính hỏng;
  - hai lần lưu đồng thời không làm mất lần cuối.

### GOV-07 — SearchBench có ngân sách riêng (Thấp)

- `CompareCommand.cs:32`, `MoviesCompareCommand.cs:51`, `ShowsCompareCommand.cs:61` và `Program.cs:26` đều tạo `new IndexerRequestGovernor()` mà không đọc `indexer-state.json`, và không lưu lại.
- Vì vậy khi app đang chạy Auto-Track native (hoặc Both) mà bench cũng chạy, **cùng một IP** nhận tối đa 2 lần quota ngày và 2 lần tốc độ mỗi phút.
- Bench còn chạy lane interactive nên dùng được 100% quota.
- Ghi chú `native-search-audit-progress.md` đã cảnh báo đúng chuyện này ("Do NOT run another bench concurrently (double site load)"), nhưng code không chặn.
- **Đề xuất:**
  - Bench đọc `indexer-state.json` của state folder lúc bắt đầu để lấy số đã dùng, và ghi lại khi kết thúc.
  - Nếu app đang giữ file khóa thì bench từ chối chạy, hoặc chỉ chạy khi có `--force`.
  - Thêm cờ `--budget-share 0.5` để bench không dùng hết quota của ngày.

### GOV-08 — Telemetry không đếm request (Thấp)

- `IndexerRunTelemetry.Queries` đếm số query, không đếm request (`SearchOrchestrator.cs:83-89, 246`). `Summary` chỉ in số dòng và thời gian (`:96-98`).
- Vì vậy không biết LimeTorrents dùng bao nhiêu trang, hay bao nhiêu lượt trúng cache. Hai số này cần để chỉnh GOV-03 và GOV-05, và để màn hình Sức khỏe hiển thị "request/ngày theo lane".
- **Đề xuất:**
  - `GovernedIndexerHttp` đã đếm `_pendingNetworkResponses`. Trả thêm `Requests` và `CacheHits` vào telemetry và in chúng trong `Summary`, ví dụ `limetorrents:ok:69/6358ms/3req`.
  - Bench ghi kèm query vào `--dump` để tính được mỗi dạng query đóng góp bao nhiêu bản tốt (dùng cho GOV-03 A).

---

## 4. Nên sửa doc 03 thế nào

- **§1 dòng 1:**
  > Giới hạn theo site: cửa sổ trượt 60 s (request/phút), số request đồng thời (1–2), khoảng cách tối thiểu giữa hai lần bắt đầu request. Không có jitter: app là một client duy nhất với User-Agent trung thực, và site đếm số request chứ không xem nhịp.
- **§1 dòng 7:**
  > Cache response theo URL, TTL 45 phút cho mọi site. EZTV dùng chung một URL theo IMDb id cho mọi query của một show. Memo 15 phút cho cả câu trả lời; chỉ lưu khi mọi site trả "ok".

  Nếu làm GOV-02 thì thêm "EZTV lane nền 2 giờ".
- **§1 dòng 9:**
  > Trạng thái được lưu ở `<StateFolder>/indexer-state.json` (danh sách `IndexerStateSnapshot`): ngày (UTC), số request trong ngày, breaker, `OpenUntilUtc`, `CooldownUntilUtc`, số lần lỗi, số lần mở breaker, lỗi gần nhất. Ghi file tạm rồi đổi tên, gộp thay đổi trong 2 s (tối đa 10 s), và ghi khi thoát app. Không lưu cửa sổ phút, cache và memo. Đọc lỗi thì [sau GOV-06: thử `.bak`, rồi khởi động ở chế độ thận trọng].
- **§1 dòng 2:** thêm "ngân sách tính theo ngày UTC, reset lúc 07:00 giờ Việt Nam", hoặc đổi code sang giờ địa phương.
- **§1 dòng 4:** mốc thời gian breaker trong code:
  - bị chặn: 15 phút → 1 giờ → 6 giờ → 24 giờ;
  - lỗi liên tiếp: 5 phút → 15 phút → 1 giờ → 2 giờ.

  (`IndexerRequestGovernor.cs:91-95`.)
- **§2:**
  - LimeTorrents: 10/phút, 3 s, 400 (sau GOV-05), kèm lịch sử và số đo ở GOV-05.
  - Bitsearch và torrentdownloads: thêm dòng "200/ngày đã chạm trong bench 13/11, không bị chặn".
- **§3:**
  - Interface thật là class `IndexerRequestGovernor` với `AcquireAsync`, `Report`, `GetAvailability(id, lane)` trả `IndexerAvailability(CanRun, Reason)`, `Snapshot` và `KillSwitch`. Không có `IIndexerRequestGovernor` và không có `IndexerRequestOptions`.
  - `IIndexerHttp` có `GetAsync`, `PostJsonAsync` và `ReportParsed`.
- **§4:**
  - Bỏ tên `QueryPlanner`; mô tả cơ chế thật sau GOV-03: gộp dạng pack, trần mỗi lượt search, chế độ ngân sách thấp.
  - Sửa câu "Auto-Track không có vòng lặp gọi dồn" thành mô tả việc lùi thời gian hunt lại sau GOV-04.
- **Doc khác:**
  - 01 §3: "lưu trạng thái governor (SQLite)" đổi thành `indexer-state.json`.
  - 01 §7: bỏ `IIndexerStateStore, SqliteIndexerStateStore`.
  - 06 §2: đánh dấu dòng `011_indexer_state.sql` là "không làm (dùng JSON)".
  - 07 P2-2: bỏ "Migration `011_indexer_state` cùng `SqliteIndexerStateStore`".

---

## 5. Thứ tự sửa đề xuất

1. **GOV-04:** lùi thời gian hunt lại cho tập kẹt và xoay vòng show. Giảm tải nhiều nhất, nằm ngoài governor, và sửa luôn chuyện show bị bỏ đói. Kèm theo: dùng hoặc bỏ `TorrentHuntIntervalMinutes`.
2. **GOV-03 A:** gộp dạng query pack; bản "+chất lượng" chỉ cho tên chính; biến thể dấu nháy chỉ đi với dạng chính. Trước khi bỏ hẳn các dạng, xác nhận bằng một lần `shows-compare --dump` có ghi query (chạy khi không có bench nào khác và quota ngày còn).
3. **GOV-08:** đếm request và lượt trúng cache trong telemetry, để mọi bước chỉnh sau có số đo.
4. **GOV-03 B:** `SearchSession`, trần request mỗi lượt search và chế độ ngân sách thấp. Phần này đóng doc §4.
5. **GOV-06 và GOV-07:** ghi state bền hơn, khởi động thận trọng khi file hỏng, khóa lúc lưu; bench dùng chung state với app.
6. **GOV-05:** hạ quota ngày của LimeTorrents xuống 400 (sửa một dòng) và ghi số đo vào doc.
7. **GOV-09 và mục 4:** sửa doc 01, 03, 06 và 07.
8. **GOV-01, GOV-02:** chỉ sửa doc. EZTV 2 giờ cho lane nền chỉ làm nếu sau GOV-04 vẫn thấy EZTV tốn nhiều.
