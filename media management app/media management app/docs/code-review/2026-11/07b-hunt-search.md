# 07b — Giai đoạn search bằng engine trong hunt

- **Baseline:** commit `ff2caa3` (`main`), ngày 2026-11-12.
- **Phạm vi:** cách hunt gọi search của qBittorrent (Web API `search/*`), cách recipe quyết định tham số search, cách sinh query, và bước lọc ngay sau search.
- **Phương pháp:** đọc code, đọc 22 recipe thật trong `<StateFolder>\Recipes`, `settings.json` (khối `AutoTorrent`, `Warp`), DB (recipe nào đang gán cho show/movie), và log `<StateFolder>\logs` (33 systemlog, 29/10 → 12/11/2026).
- **Chưa làm:** chạy search A/B thật trên qBittorrent. Bước này cần user cho phép (xem mục 7).
- **Bối cảnh mạng:** user ở Việt Nam, tracker bị chặn, nên **mọi lần search (hunt và UI) đều chạy qua WARP**. Đây là thiết kế có chủ đích (`Warp.ForceTunnelForHunt=true`), không phải finding.
- **Thang mức độ:** theo [00-method-and-scope.md](../00-method-and-scope.md). ID tạm `SRCH-xx`.

> Kết luận chính: cả qBittorrent UI lẫn Web API đều chạy **cùng một bộ plugin**. Hunt kém hơn UI là do cách app **chờ**, **đọc** và **dựng query**, không phải do API yếu hơn. Với các recipe đang dùng nhiều nhất (snapshot), vấn đề **không** phải "chỉ đọc 100 dòng đầu". Có hai vấn đề chính: (1) app dừng search khi engine vẫn đang chạy, và (2) chế độ snapshot âm thầm bỏ mọi template có `{season}`/`{episode}`, nên chỉ search bằng tên show.

---

## 1. Hunt thực sự đi đường nào

Cờ `useShowSnapshotSearch` và loại recipe quyết định đường đi. Đường đi không phụ thuộc vào cờ phân trang.

| Tình huống | Đường code | Ghi chú |
|---|---|---|
| Tập TV, chạy cart theo lô hoặc Auto-Track, `useShowSnapshotSearch=true` | `FetchJobService.FetchEpisodeCandidatesSnapshotAsync` → `ShowSearchSnapshotService.CaptureSnapshotAsync` | Query cấp show, chạy tuần tự. Đọc `offset=0, limit=snapshotTargetResults`. **Bỏ qua mọi key pagination.** |
| Tập TV, chạy cart theo lô hoặc Auto-Track, snapshot tắt | `FetchJobService.FetchEpisodeCandidatesParallelAsync` → `SearchSingleQueryAsync` | Mỗi tập một worker (`parallelSearchCount`). Các query của một tập chạy tuần tự. |
|  └ `enableSearchPagination=true` | `FetchJobService.SearchQueryWithPaginationAsync` | Mỗi chu kỳ đọc lại từ offset 0, tối đa `maxPagesTvParallel × pageSize` dòng |
|  └ phân trang tắt (**mặc định của TvEpisode**) | `QbittorrentClient.SearchAsync` | Mỗi lần poll đọc `offset=0, limit=ResultLimit` |
| Season pack (mọi recipe pack) | `FetchJobService.FetchSeasonPacksAsync` → `ShowSearchSnapshotService` | **Luôn đi snapshot**, bất kể cờ snapshot hay phân trang |
| Movie | `AutomationFlowService.DryRunMovieAsync` → `SearchAsync` | Query chạy song song bằng `SemaphoreSlim`. Phân trang dùng con trỏ đi tiếp (mặc định bật cho Movie). |
| **Retry một order tập TV** trong cart | `TorrentWorkspaceViewModel.SearchOrderAsync` → `AutomationFlowService.DryRunEpisodeAsync` | **Bỏ qua cờ snapshot**, luôn search theo từng tập. Đây là đường khác với chạy theo lô (SRCH-06). |

Nguồn: `Services/FetchJobService.cs:176-196`, `:291-296`, `:759-826`; `Services/ShowSearchSnapshotService.cs:33-118`; `ViewModels/TorrentWorkspaceViewModel.cs:1342-1366`, `:1396-1440`; `Services/AutomationFlowService.cs:48-150`.

Recipe chỉ được đọc qua module **`SearchSource` đầu tiên** (`RecipeRuntimeSettings.GetSearchModule`, `:555-556`). Các key cùng tên nằm trong module Identity, QueryBuilder, CandidateParser hay CandidateFilter đều **không có tác dụng** (SRCH-09).

---

## 2. Tham số hiệu lực theo từng recipe đang dùng

Giá trị hiệu lực lấy theo thứ tự: recipe → `settings.json` (`AutoTorrent`) → default trong code. Ký hiệu: không đánh dấu = lấy từ recipe, **ˢ** = fallback `settings.json`, **ᶜ** = default code. "Dùng" là số show hoặc movie gán recipe đó (AT = số show có lịch Auto-Track). `RecipeId = NULL` sẽ rơi về recipe default (1 show pack, 3 movie).

Settings fallback hiện tại: `MaxParallelSearches=3`, `ParallelSearchTimeoutSeconds=30`, `MovieSearchTimeoutSeconds=30`, `SnapshotTimeoutSeconds=60`, `SnapshotIdleTimeoutSeconds=10`, `SnapshotTargetResults=2000`, `UseShowSnapshotSearch=false`, `MaxCandidatesPerFetch=3`.

### Tập TV và season pack

| Recipe | Loại | Dùng | Đường search | Timeout/query | Idle | Đọc buffer | Song song | Max cand. | Plugins | Query thực tế |
|---|---|---|---|---|---|---|---|---|---|---|
| Common Anime | Ep | 15 (AT 1) | Snapshot | 45s | **8s** | 2000 | 1 | 3 | enabled | **Mặc định snapshot**: `{title} {year}`, `{title} {quality}`, `{title}`. Hai template `{title} {episode} {quality}` bị bỏ (SRCH-03). |
| Common Anime Pack | Pack | 16 | Snapshot | 40s | **8s** | 2000 | 1 | 3 | enabled | Mặc định snapshot (template `{episode}` bị bỏ) |
| Default TV Pack Recipe | Pack | 15 (+1 NULL) | Snapshot | 60s | 10sˢ | 5000 | 1 | 3 | enabled | Mặc định snapshot. **Cả 4 template `S{season:00} complete…`, `season {season}…` bị bỏ.** |
| Common Anime no Probe Pagination | Ep | 6 (AT 5) | Snapshot | 60s | 28s | 2000 | 1 | 5 | 10 chọn tay | `{title}`. Tên recipe có "Pagination" nhưng **hunt không phân trang**. |
| Common Anime no Probe Parallel | Ep | 4 (AT 4) | Per-episode + phân trang FJS | 35s | 12s | tối đa 2×500 | 3 tập | 5 | 10 chọn tay | `{title} {episode} {quality}`, `{title} - {episode} {quality}` |
| Show 16 | Ep | 2 | Snapshot | 40s | 20s | 3000 | 1 | 6 | enabled | Mặc định snapshot (cả `{season}x{episode:00}` lẫn `S{season:00}E{episode:00}` đều bị bỏ) |
| Common TV no Probe Pagi High Quality | Ep | 2 | Snapshot | 60s | 28s | 2000 | 1 | 7 | enabled | `{title}` |
| Show K | Ep | 2 | Snapshot | 40s | 15s | 2000 | 1 | 3 | enabled | Mặc định snapshot |
| Korra_Pack | Pack | 2 | Snapshot | 60s | 20s | 2000 | 1 | 3 | enabled | `{title} {quality}` |
| TV Recipe Pack High Quality | Pack | 1 | Snapshot | 120s | 10s | 2000 | 1 | 10 | enabled | `{title}` |
| Common Anime no Probe | Ep | 1 | Snapshot | 45s | **8s** | 2000 | 1 | 3 | enabled | Mặc định snapshot |
| One_Piece | Ep | 1 | Per-episode, `client.SearchAsync` | 30sˢ | 8sᶜ | **offset 0, 150 dòng** | 3 tập | 3 | enabled | `{title} {episode} {quality}`, `{title} - {episode} {quality}` |
| oregairu | Ep | 1 | Per-episode, `client.SearchAsync` | 30sˢ | 8sᶜ | **offset 0, 150 dòng** | 3 tập | 3 | enabled | như trên + custom `OreGairu` |
| Default TV Recipe | Ep | 1 | Per-episode, `client.SearchAsync` | 30sˢ | 8sᶜ | **offset 0, 75 dòng** | 3ˢ tập | 3ˢ | enabled | 4 template `S{season:00}E{episode:00}`… |

### Movie

| Recipe | Dùng | Timeout/query | Idle | Đọc buffer | Song song | Max cand. | Query |
|---|---|---|---|---|---|---|---|
| Default Movie Recipe | 2 (+3 NULL) | 30sˢ | 10sᶜ | con trỏ, 3×500/chu kỳ, trần 1500 | 3 query | 3ˢ | `{title} {year} {quality} {audio}`, `{title} {quality}`, `{title} {year}` |
| Common Movie | 4 | 120s | 30s | như trên | 3 | 10 | `{title}` |
| Common Anime Movie | 3 | 30s | 10s | như trên | 3 | 10 | `{title} {quality}` |
| Common Movie No TV | 3 | 120s | 30s | như trên | 3 | 10 | `{title}` |

5 recipe đang không được gán cho show hay movie nào: *Common Anime Pack no main title*, *Default TV Recipe New*, *Common TV no Probe Pagi*, *Common Anime no main title*. *Default TV Recipe New* là recipe Ep duy nhất bật phân trang đi theo đường FJS.

Cách snapshot sinh query: template chứa `{season}`, `{season:00}`, `{episode}` hoặc `{episode:00}` bị lọc bỏ. Nếu sau khi lọc không còn template nào, app dùng `DefaultSnapshotTemplates` = `{title} {year}`, `{title} {quality}`, `{title}`, nhân với tối đa 1 + 4 tên thay thế (`QueryTemplateSelection.cs:24-31`, `QueryTokenCatalog.cs:225-230`).

---

## 3. Bằng chứng từ log

Trong cửa sổ log còn giữ, hunt chỉ chạy 4 lần, tất cả cho recipe **Common Anime no Probe Pagination** (snapshot, timeout 60s, idle 28s, 10 plugin): 2 lần Auto-Track và 2 lần chạy cart. Tổng cộng 10 lần search snapshot:

| Ngày | Query | Status lúc dừng | Dừng vì | Dòng | Engine 0 dòng |
|---|---|---|---|---|---|
| 31/10 | Show 01 | **Running** | timeout 60s | 169 | bitsearch, solidtorrents, subsplease, therarbg, tsukihime |
| 31/10 | To Aru Anbu no ITEM | **Running** | idle 28s | 41 | 5 engine trên + eztvx |
| 31/10 | Toaru Anbu no Shoujo Kyousei | **Running** | idle 28s | 27 | 5 engine trên + eztvx, limetorrents |
| 03/11 | Show 08 | **Running** | timeout 60s | 268 | 5 engine trên |
| 03/11 | Alt Title D | **Running** | idle 28s | 259 | 5 engine trên + eztvx |
| 07/11 | Show 01 | **Running** | timeout 60s | 231 | 5 engine trên |
| 07/11 | To Aru Anbu no ITEM | **Running** | idle 28s | 49 | 5 engine trên + eztvx |
| 07/11 | Toaru Anbu no Shoujo Kyousei | **Running** | idle 28s | 48 | 5 engine trên + eztvx |
| 10/11 | Show 08 | **Running** | timeout 60s | 268 | 5 engine trên |
| 10/11 | Alt Title D | **Running** | timeout 60s | 302 | 5 engine trên |

- **10/10 lần search kết thúc khi job vẫn `Running`.** Không lần nào engine chạy xong. 4 lần chạm timeout cứng, 6 lần chạm idle.
- **5/10 plugin (bitsearch, solidtorrents, subsplease, therarbg, tsukihime) trả 0 dòng ở mọi lần search.** Trên thực tế chỉ 4–5 engine hoạt động: nyaasi, nekobt, limetorrents, torrentdownloads, eztvx.
- Số dòng mỗi engine không lớn (limetorrents 40–80, nekobt 27–50, nyaasi 33–112, torrentdownloads 46–126). Đọc code plugin trong `%LOCALAPPDATA%\qBittorrent\nova3\engines` thì thấy plugin **không** chỉ lấy trang đầu: `nyaasi` lặp **mọi trang** tới khi hết (`while True`), `subsplease` lấy 6 trang, `limetorrents` và `solidtorrents` lấy 4 trang, `torrentdownloads` theo link trang kế tiếp và mở **từng trang chi tiết** để lấy magnet. Các plugin nhiều trang này chạy chậm, nên rất có thể đang bị SRCH-01 cắt ngang giữa chừng. Đặc biệt nyaasi là engine quan trọng nhất cho anime. Cần A/B để xác nhận.
- Phễu sau search: 209 → 4, 472 → 5, 290 → 5, 505 → 5 ứng viên. Số cuối bị chặn bởi `maxCandidatesPerFetch=5`. Log không có thống kê lý do loại theo từng bước, vì candidate debug log đang tắt.
- Hunt luôn chạy **qua WARP** (`Warp.ForceTunnelForHunt=true`; log ghi "WARP already connected during hunt preflight").

Không có log nào cho One_Piece, oregairu, Common Anime, recipe pack hay movie trong cửa sổ này. Các finding về những recipe đó dựa trên code.

---

## 4. Các giả thuyết ban đầu: xác nhận hay bác bỏ

| # | Giả thuyết | Kết luận |
|---|---|---|
| H1 | Chỉ đọc `offset=0/limit` khi tắt phân trang | **Bác bỏ cho recipe snapshot** (đọc 2000–5000 dòng, buffer thật chỉ 27–302). **Xác nhận bằng code** cho 3 recipe per-episode One_Piece, oregairu, Default TV Recipe, xem SRCH-05. |
| H2 | Idle/timeout dừng sớm | **Xác nhận bằng log**: 10/10 lần dừng khi status còn `Running`, xem SRCH-01. Chưa chứng minh được là kết quả bị mất có tốt hơn, cần A/B. |
| H3 | Quá tải vì chạy song song | **Không xác nhận.** Snapshot chạy tuần tự, mỗi lần 1 job. Per-episode tối đa 3 job. Log không có cảnh báo hết slot. Chưa loại trừ được việc site giới hạn tốc độ. |
| H4 | Query hẹp hoặc sai tên | **Ngược lại với dự đoán.** Recipe snapshot dùng query **quá rộng**, chỉ có tên show, vì template tập/season bị bỏ (SRCH-03). Recipe per-episode dùng `{episode}` không đệm số 0 (`… 5 1080p` so với tên bản phát hành `- 05`). Chưa kiểm được, cần A/B. |
| H5 | Hao hụt ở các bước lọc | **Chưa đo được** vì debug log tắt. Rủi ro term khớp nhầm bị bác bỏ: `ReleaseTermMatcher` so khớp nguyên token và phân biệt hoa thường, nên `Raw` không khớp `Erai-raws` và `TS` không khớp `DTS`. Có một vấn đề xếp hạng ở đường per-episode, xem SRCH-04. |
| H6 (mới) | WARP tạo khác biệt giữa hunt và UI | **Bác bỏ như một nguyên nhân gây khác biệt.** User xác nhận: ở Việt Nam, tracker bị nhà mạng hoặc site chặn, nên user search trên UI cũng luôn bật WARP, giống hunt. WARP là bắt buộc, không phải lỗi. WARP vẫn có thể là lý do một số engine rỗng, nhưng nếu vậy thì UI cũng sẽ rỗng như nhau (xem SRCH-02). |

---

## 5. Findings

### SRCH-01 — High — Snapshot luôn dừng khi engine còn đang chạy

- **Recipe bị ảnh hưởng:** mọi recipe snapshot và pack. Nặng nhất là **Common Anime** (15 show, idle **8s**), **Common Anime Pack** (16, idle 8s), **Common Anime no Probe** (idle 8s), **Default TV Pack** (15, idle 10s). Đã quan sát trên Common Anime no Probe Pagination (idle 28s).
- **Bằng chứng:** `ShowSearchSnapshotService.cs:155-199`. Job kết thúc ở một trong ba trường hợp: hết `timeout`, quá `idle` giây không có dòng mới, hoặc status khác `Running`. Log ở mục 3 cho thấy 10/10 lần dừng khi job vẫn `Running`.
- **Cơ chế:** job search của qBittorrent chỉ chuyển sang `Stopped` khi **mọi** plugin đã thoát. Chỉ cần một plugin treo hoặc chậm (xem SRCH-02) là job không bao giờ "xong", nên lần nào cũng phải chờ tới timeout hoặc idle. Idle 8s thì quá ngắn với các plugin trả kết quả một lần ở cuối. Trên UI, user nhìn thấy kết quả tiếp tục đổ về sau thời điểm đó.
- **Đo thật (mục 5d):** app vẫn giữ được 91–100% số dòng; thiệt hại chính là **thời gian**. Engine treo giữ job tới mức 180s mà qBittorrent tự cắt, nên mỗi query luôn tốn trọn timeout.
- **Hướng sửa đề xuất:** (1) gỡ hoặc tắt các plugin luôn rỗng hay treo (SRCH-02) để job tự kết thúc sớm; (2) timeout riêng cho từng engine (hướng B); (3) ghi log số engine đã trả kết quả và thời điểm dòng cuối cùng về. ~~Đặt mức sàn idle ~30s~~: bác bỏ sau A/B.

### SRCH-02 — Medium — Một nửa số engine luôn rỗng nhưng chỉ được ghi ở mức Debug

- **Recipe bị ảnh hưởng:** Common Anime no Probe Pagination và Common Anime no Probe Parallel (cùng bộ 10 plugin chọn tay, 10 show có Auto-Track). Có khả năng ảnh hưởng mọi recipe `enabled` nếu các plugin này đang bật.
- **Bằng chứng:** log mục 3. bitsearch, solidtorrents, subsplease, therarbg, tsukihime trả 0 dòng ở 10/10 lần search. `SearchEngineDiagnostics.LogEmptyEngines` chỉ ghi `DBG`, không có cảnh báo tổng hợp.
- **Nguyên nhân có thể (chưa xác minh):** plugin hỏng; plugin chậm và bị SRCH-01 cắt; site chặn IP của WARP hoặc đòi Cloudflare challenge; hoặc plugin `subsplease` không hợp với query dạng tên show. WARP là bắt buộc ở Việt Nam và UI cũng chạy qua WARP. Vì vậy, nếu 5 engine này cũng rỗng trên UI thì chúng chỉ làm job treo (SRCH-01) chứ không phải lý do UI tốt hơn.
- **Hướng sửa:** A/B (luôn bật WARP) với job chờ tới `Stopped`, để tách nhóm engine "rỗng thật" khỏi nhóm "chậm". Engine rỗng thật thì bỏ khỏi recipe. Thêm cảnh báo khi một engine rỗng N lần liên tiếp, và hiển thị "sức khoẻ engine" trong Recipe workspace.

### SRCH-03 — High — Chế độ snapshot âm thầm bỏ mọi template có `{season}`/`{episode}`

- **Recipe bị ảnh hưởng:** mọi recipe **pack** (Default TV Pack 15 show, Common Anime Pack 16, Korra_Pack, TV Recipe Pack HQ) và các recipe tập TV snapshot có template theo tập (Common Anime 15 show, Common Anime no Probe, Show K, Show 16).
- **Bằng chứng:** `QueryTemplateSelection.cs:24-31` dùng `showLevelOnly: true` để lọc mọi template có token season/episode (`QueryTokenCatalog.cs:137-176`). Nếu không còn template nào thì dùng `{title} {year}`, `{title} {quality}`, `{title}`. `FetchSeasonPacksAsync` luôn gọi snapshot (`FetchJobService.cs:291-296`). Vì vậy các template như `{title} S{season:00} complete {quality}` trong Default TV Pack **chưa bao giờ được dùng**. UI không báo gì về việc này.
- **Hệ quả:** với show dài tập (Show 16, Show K, Show L hay anime nhiều mùa), query "Tên show" sinh ra rất nhiều trang. Plugin phải lật qua nhiều trang (nyaasi lật hết; số khác dừng ở 4–6 trang) trong khi timeout/idle cắt ngang (SRCH-01). Vì vậy season pack cũ hoặc một tập cụ thể có thể không lọt vào được. Query nhắm đích kiểu "Tên show S02 1080p" thì ít trang hơn và trúng ngay. Đó là cách user search tay trên UI.
- **Hướng sửa:** trong snapshot, cho phép template `{season}` khi biết season đích (với pack là các season đang chọn; với tập là season của các tập trong lô). Hoặc thêm một lượt "search nhắm đích" theo tập hay season khi snapshot không có ứng viên đạt. Recipe editor cần cảnh báo template nào sẽ bị bỏ trong chế độ snapshot.

### SRCH-04 — Medium — Search theo từng tập chỉ chấm điểm N kết quả nhiều seed nhất

- **Recipe bị ảnh hưởng:** Common Anime no Probe Parallel (4 show có Auto-Track), One_Piece, oregairu, Default TV Recipe.
- **Bằng chứng:** `FetchJobService.cs:671-728`. Kết quả đã sắp theo seeders. Khi đã đủ `maxCandidates` ứng viên đạt (3–5), app **không đánh giá thêm** các dòng còn lại (`continue` ở dòng 678-681 và 709-712) và **bỏ các query còn lại** (dòng 718-728). Phần xếp hạng theo chất lượng, prefer term và engine priority (dòng 740-750) chỉ diễn ra trong N ứng viên đó.
- **Hệ quả:** một bản tốt hơn theo recipe (nhóm phát hành ưu tiên như ToonsHub hay Erai-raws, engine ưu tiên, chất lượng cao hơn) nhưng ít seed hơn sẽ bị bỏ qua. Đường snapshot (`MapSnapshotCandidatesAsync`) thì đánh giá **hết** rồi mới lấy top N, nên hai đường cho kết quả không nhất quán.
- **Hướng sửa:** đánh giá toàn bộ kết quả của query rồi lấy top N theo điểm. Nếu cần dừng sớm thì chỉ dừng khi đã có ứng viên đạt "chất lượng tối đa" của recipe.

### SRCH-05 — Medium — Khi tắt phân trang chỉ đọc `ResultLimit` dòng đầu buffer

- **Recipe bị ảnh hưởng:** One_Piece (150), oregairu (150), Default TV Recipe (75). Ngoài ra mọi recipe tập TV không đặt `enableSearchPagination` (mặc định `false` cho TvEpisode, `RecipeRuntimeSettings.cs:208-209`).
- **Bằng chứng:** mỗi lần poll, `QbittorrentClient.cs:153` đều đọc `GetSearchResultsAsync(id, request.Limit, request.Offset=0)`. Buffer của qBittorrent xếp theo thứ tự kết quả về, nên các engine nhanh chiếm hết slot. Comment ở `AutomationFlowService.cs:326-329` đã mô tả đúng vấn đề này cho nhánh có phân trang. Show L là show có rất nhiều bản phát hành cho mỗi tập.
- **Hướng sửa:** bỏ đường không phân trang, hoặc cho nó dùng con trỏ đọc tiếp như `AutomationFlowService`. Đổi mặc định phân trang của TvEpisode sang `true`.

### SRCH-06 — Medium — Retry một order đi đường search khác với chạy theo lô

- **Recipe bị ảnh hưởng:** mọi recipe tập TV, rõ nhất là recipe snapshot.
- **Bằng chứng:** chạy cart theo lô gọi `FetchJobService.FetchEpisodeCandidatesAsync` (`TorrentWorkspaceViewModel.cs:1366`). Retry một order (`:692` → `SearchOrderAsync` `:1426`) lại gọi `AutomationFlowService.DryRunEpisodeAsync`. Đường này bỏ qua `useShowSnapshotSearch` và search theo từng tập. Nó dùng phân trang con trỏ (khác FJS), chạy các query song song, đánh giá hết ứng viên, và dùng idle `paginationIdleTimeoutSecondsTvSnapshot` khi recipe có cờ snapshot.
- **Hệ quả:** cùng một recipe nhưng "Run cart" và "Retry" cho kết quả khác nhau. Điều này gây khó khi so sánh với UI.
- **Hướng sửa:** gom về một bộ chạy search duy nhất (xem SRCH-07).

### SRCH-07 — Medium — Có 4 vòng polling search viết riêng và đã lệch nhau

| | `QbittorrentClient.SearchAsync` | `AutomationFlowService…Pagination` | `FetchJobService…Pagination` | `ShowSearchSnapshotService` |
|---|---|---|---|---|
| Đọc buffer | offset 0, `Limit` | con trỏ đi tiếp, không trần trang | đọc lại từ 0, tối đa `maxPages×pageSize` | offset 0, `targetResults` |
| Điều kiện "xong" | status ≠ Running | status ≠ Running **và** đã đọc hết buffer | status ≠ Running (có thể còn dòng chưa đọc) | status ≠ Running |
| Gửi `category` | có | có | có | **không** (mặc định `all`) |
| Retry khi hết slot (409) | — | **không** | có | không |
| Log `bufferTotal` | không | có | **không** | không |
| Kẹp idle | 0–120 | 0–120 | 0–120 | 3–120 |

- **Hướng sửa:** một service `SearchRunner` duy nhất, dùng con trỏ đọc tiếp, điều kiện xong rõ ràng, retry khi hết slot, log thống nhất (bufferTotal, số đã đọc, từng engine, lý do dừng). Ba đường còn lại chỉ khác nhau ở tham số.

### SRCH-08 — Medium — Search movie và retry không chịu được lỗi "hết slot search"

- **Bằng chứng:** `AutomationFlowService.SearchAsync` (`:197-244`) không bắt `QbittorrentSearchCapacityException`. Một query bị 409 làm `Task.WhenAll` ném lỗi, nên cả lượt search movie hoặc retry thất bại. `FetchJobService` thì có retry (`:815-822`). Log trong cửa sổ hiện tại chưa ghi nhận lỗi này. Rủi ro tăng khi user đang mở tab search trên UI qBittorrent cùng lúc.
- **Recipe bị ảnh hưởng:** mọi recipe Movie và mọi lần retry tập TV.
- **Hướng sửa:** dùng chung logic retry. Một query lỗi không được làm hỏng các query còn lại.

### SRCH-09 — Low — Cấu hình chết hoặc gây hiểu nhầm trong recipe

- 13/22 recipe có các key `parallelSearchCount`, `parallelSearchTimeoutSeconds` hay `searchIdleTimeoutSecondsTvParallel` nằm trong module Identity, QueryBuilder, CandidateParser hoặc CandidateFilter. Các key này **không được đọc** vì chỉ module `SearchSource` có tác dụng. Ví dụ: Identity của Common Anime no Probe Pagination đặt `parallelSearchTimeoutSeconds=30`, trong khi giá trị hiệu lực lại lấy từ `settings.json`.
- Recipe snapshot và pack có đầy đủ key pagination (`paginationIdleTimeoutSecondsTvSnapshot=20`…) nhưng hunt không dùng. Recipe tên "…Pagination" thực tế không phân trang.
- **Hướng sửa:** recipe editor chỉ hiện các key có tác dụng với đường search đang dùng. Thêm migration dọn các key lạc chỗ.

### SRCH-10 — Low — Thiếu dữ liệu để chẩn đoán

- Đường FJS không log `bufferTotal`. Snapshot không log mỗi engine trả dòng cuối lúc nào. Thống kê lý do loại theo từng bước chỉ có khi bật candidate debug log. Vì vậy chưa đo được H5.
- **Hướng sửa:** log một dòng tóm tắt cho mỗi lần hunt, gồm: đã đọc trên tổng buffer, engine rỗng, lý do dừng, và phễu (raw → qua filter → khớp tập → top N).

---

## 5b. Findings riêng cho movie

User xác nhận: anime nổi tiếng và đang chiếu đã tìm khá nhanh. Điểm đau thật sự nằm ở **movie cũ** (các thập niên trước) và **movie có nhiều bản encode cần lựa**. Trong 15 movie đang theo dõi, các movie chưa có ứng viên gồm: Movie 01, Movie Two, Movie 04, Show E the Movie – Movie Subtitle, Movie 08, Movie J0, Movie Three, Movie One và Movie 06. Số này có cả phim user chưa từng chạy hunt.

Đường search của movie là `AutomationFlowService.DryRunMovieAsync` → `SearchAsync` (phân trang con trỏ, 3 query song song). Đường này đọc buffer đầy đủ, nên **vấn đề của movie nằm ở query và bộ lọc**, không phải ở cách đọc buffer.

### SRCH-11 — High — Movie bị loại nếu tên bản phát hành không chứa đúng năm TMDB

> **User xác nhận (12/11/2026): cần sửa lớn.** Không chỉ nới thành ±1 năm, mà phải làm lại toàn bộ cách khớp năm của movie (xem "Phạm vi sửa lớn" bên dưới).

- **Bằng chứng:** `CandidateEvaluationService.EvaluateMovie` (khoảng dòng 84) dùng `!result.FileName.Contains(movie.ReleaseYear.ToString())` để trả `YearMismatch`. Tức là phải có **đúng** năm đó, không lệch ±1 và không chấp nhận tên không ghi năm.
- **Hệ quả với movie cũ:** TMDB thường lấy năm công chiếu sớm nhất (liên hoan phim), còn bản phát hành hay dùng năm ra rạp, hoặc ngược lại. Một số bản (đặc biệt là bản cũ, remux, bản từ cộng đồng) không ghi năm. Ví dụ cần xác minh bằng A/B: *Movie 01* (TMDB 2014, bản phát hành thường ghi 2013), hiện chưa có ứng viên nào.
- **Recipe bị ảnh hưởng:** mọi recipe movie.
- **Hướng sửa:** chấp nhận lệch ±1 năm (trừ điểm nhẹ). Khi tên không có năm, chỉ chấp nhận nếu tiêu đề khớp mạnh, và đánh dấu cho user kiểm.
- **Phạm vi sửa lớn:**
  - Đọc năm từ tên bằng parser (token năm hợp lệ 1900–năm hiện tại + 1), thay cho `Contains`. Cách cũ vừa dễ khớp nhầm (ví dụ `2014` nằm trong `1080p.2014x`) vừa không lệch được ±1.
  - Lấy tập năm hợp lệ từ TMDB: release date theo từng quốc gia và loại phát hành (premiere, theatrical, digital, physical), không chỉ `ReleaseYear`.
  - Chấm điểm theo bậc: khớp đúng > lệch ±1 > không có năm. Không loại cứng. Tên không có năm chỉ được nhận khi tiêu đề khớp mạnh.
  - Năm nằm **trong tiêu đề phim** (*2001: A Space Odyssey*, *1917*, *Blade Runner 2049*) không được tính là năm phát hành.
  - Xử lý remake trùng tên (ví dụ *Dune* 1984 và 2021): lệch năm lớn phải bị loại.
  - Áp dụng cùng logic cho bước search: thêm biến thể query có năm lệch hoặc không có năm khi cần.
  - Test: bộ fixture tên bản phát hành thật cho từng trường hợp trên.

### SRCH-12 — High — Recipe movie phổ biến không dùng tên thay thế (romaji, tên gốc)

- **Bằng chứng:** `SearchTitleResolver.CreateRequest` chỉ thêm tên thay thế khi `useLibraryEnglishTitles=true`. Mặc định là `false` (`RecipeRuntimeSettings.cs:595-596`). *Default Movie Recipe* (2 movie, cộng 3 movie để NULL) không đặt key này. *Common Movie* và *Common Movie No TV* đặt `False`. Hệ quả kép: tên thay thế **không được search**, và **cũng không được dùng khi khớp tiêu đề** (`EvaluateMovie` gọi cùng resolver), nên bản phát hành ghi tên gốc bị loại vì `TitleMismatch`.
- **Ví dụ trong DB:** *Movie 04* có sẵn `Alt Title A`, `Alt Title A2`…; *Movie Two* có `Alt Title B`; *Show E … Movie Subtitle* có `Show E Chikai no Finale`. Cả ba đều chưa có ứng viên.
- **Hướng sửa:** bật tên thay thế cho các recipe movie, hoặc đổi default thành `true` cho Movie. Tách hai việc "search bằng tên thay thế" và "chấp nhận tên thay thế khi khớp".

### SRCH-13 — High (nâng từ Medium theo user) — Làm sạch query thay dấu nháy đơn bằng dấu cách

> **User xác nhận (12/11/2026): cần sửa lớn.** Làm lại toàn bộ khâu chuẩn hoá tiêu đề, dùng chung cho sinh query **và** khớp tiêu đề.

- **Bằng chứng:** `QueryTemplateRenderer.Sanitize` dùng regex `[^\p{L}\p{N}\s-]` → `" "`. Vì vậy `Schindler's List` thành `Schindler s List`, `Ferris Bueller's Day Off` thành `Ferris Bueller s Day Off`. Trong khi đó bản phát hành thường ghi `Schindlers.List` hoặc giữ nguyên dấu nháy. Engine tìm theo token sẽ không khớp `Schindler` với `Schindlers`.
- **Recipe bị ảnh hưởng:** mọi recipe (`sanitizeQuery` mặc định bật). Movie cũ hay có dấu nháy trong tên.
- **Hướng sửa:** bỏ hẳn dấu nháy (nối liền) thay vì thay bằng dấu cách. Có thể sinh thêm biến thể không dấu nháy và bỏ dấu tiếng Pháp/Đức (`Léon` → `Leon`).
- **Phạm vi sửa lớn:** một bộ `TitleNormalizer` dùng chung cho sinh query, khớp tiêu đề và dedupe:
  - Dấu nháy (`'`, `’`, `` ` ``): đo thật (mục 5c) cho thấy mỗi engine hợp một dạng khác nhau. **Gửi cả hai biến thể** `Schindlers List` và `Schindler s List`, rồi dedupe kết quả.
  - Bỏ dấu thanh: `Léon` → `Leon`, `Amélie` → `Amelie`. Áp dụng cho cả query lẫn bước khớp.
  - `&` ↔ `and`; `:` và ` - ` là ranh giới phụ đề, sinh thêm biến thể chỉ lấy phần trước phụ đề.
  - Số La Mã ↔ số thường (`Part II` ↔ `Part 2`), nhưng không đổi khi chữ nằm trong từ.
  - Mạo từ đầu tên (`The`, `A`) có thể bị lược.
  - Dấu chấm và gạch dưới trong tên bản phát hành được tách thành token.
  - Bước khớp tiêu đề cũng phải dùng đúng bộ chuẩn hoá này, để `Schindlers.List.1993` khớp với `Schindler's List`.
  - Test: fixture cho các tên có dấu nháy, có dấu thanh, có `&`, có số La Mã, và có phụ đề.

### SRCH-14 — Medium (đánh đổi do user chọn) — Bộ lọc của Common Movie loại phần lớn bản của movie cũ

- **Recipe:** *Common Movie* (4 movie) dùng `minimumSeeders=10`, exclude `DTS`, `TrueHD`, `TrueHD.7.1`, `CAM`, `HDCAM`, `NO ADS`, chất lượng chỉ `2160p` và `1080p`, tối đa 50 GB. *Common Movie No TV* dùng seed ≥ 3, tối đa 20 GB.
- **Hệ quả:** movie cũ thường ít seed (dưới 10). Nhiều bản BluRay 1080p cũ dùng âm thanh DTS (`DTS` khớp `DTS-HD MA` vì so theo token). Một số phim chỉ có bản 720p hoặc DVD. Tất cả đều bị **loại hẳn** chứ không chỉ bị xếp thấp.
- **Hướng sửa:** chuyển các điều kiện "không thích" (DTS, ít seed, 720p) từ **loại** sang **trừ điểm**. Hoặc thêm một profile "movie cũ" (ví dụ phim trước năm X thì ngưỡng seed thấp hơn và cho phép 720p).

### SRCH-15 — Low (thiếu tính năng) — Ít ứng viên và không có công cụ so sánh các bản encode

- **Bằng chứng:** `maxCandidatesPerFetch` bị kẹp trong 1–10 (`RecipeRuntimeSettings.cs:95-96`). Override trong cart lên được tối đa 20 (`:9`). Các ứng viên đạt nhưng vượt mức này bị ẩn. Danh sách không gom nhóm hay lọc theo release group, codec (x264/x265/AV1), HDR/DV, âm thanh, nguồn (BluRay/WEB/Remux).
- **Hướng sửa:** với movie, hiện **mọi** ứng viên đạt, gom theo các thuộc tính đã parse, kèm chip lọc nhanh. Recipe chỉ quyết định thứ tự gợi ý.

Ngoài ra, SRCH-01 cũng áp dụng cho *Default Movie Recipe*: timeout 30s, idle 10s, chạy **toàn bộ 23 plugin đang bật** với 3 query song song, tức khoảng 69 process plugin cùng lúc. SRCH-08 (lỗi hết slot làm hỏng cả lượt search) cũng áp dụng cho mọi recipe movie.

---

## 5c. Kết quả đo thật: sức khoẻ engine và tiêu đề đặc thù (12/11/2026, WARP bật)

Công cụ: `tools/SearchProbe/engine_probe.py` (23 engine đang bật × 10 tiêu đề đặc thù, mỗi cặp một job riêng, tối đa 120s) và `plugin_direct.py` (chạy thẳng plugin để bắt lỗi thật, vì qBittorrent nuốt exception của plugin).

### Engine: chỉ 10/23 trả được kết quả

| Engine | Kết quả 10 tiêu đề | Thời gian | Nguyên nhân (đã xác minh) |
|---|---|---|---|
| limetorrents | 10/10 có dòng, 1138 dòng | ~5.5s | **Tốt nhất**: nhanh và nhiều |
| thepiratebay, piratebay | 10/10 và 8/10 | ~1s | Tốt. **Hai plugin cho cùng kết quả** (cùng nguồn), nên chạy cả hai là thừa |
| torrentdownloads | 9/10, tối đa 100 dòng | ~27–31s | Chậm: mở từng trang chi tiết để lấy magnet |
| kickasstorrents | 5/10 | trung vị 60s, **2 lần treo tới 120s** | Chậm và không ổn định |
| sktorrent | 6/10, ít dòng | ~8s | Site Slovakia |
| yts | 7/10, 2–8 dòng | ~1s | Chỉ movie, ít bản |
| nyaasi, nekobt, subsplease | chỉ có kết quả với tiêu đề anime | 1–3s | Đúng chuyên môn (anime) |
| **eztvx** | 0 dòng, **8/10 lần treo tới 120s** | — | Cloudflare challenge. **Treo, nên giữ cả job ở trạng thái `Running`.** Đây là nguyên nhân trực tiếp của SRCH-01 với recipe anime 10 plugin |
| **therarbg** | 0 | 8–14s | Cloudflare challenge, trả trang không có kết quả |
| **bitsearch**, **solidtorrents**, **torlock** | 0 | 1–22s | Python báo `CERTIFICATE_VERIFY_FAILED: certificate has expired`, trong khi curl (Windows schannel) thấy hợp lệ, tức là lỗi chuỗi chứng chỉ phía Python. `bitsearch.to` và `solidtorrents.to` đều redirect về `bitsearch.eu`, nên **là cùng một site** |
| **tsukihime**, **torrentscsv** | 0 | ~1s | Plugin nhận trang chống bot thay vì JSON. **Gọi thẳng API (`api.tsukihime.org`, `torrents-csv.com/service/search`) thì có dữ liệu** (tsukihime: 67 kết quả cho *Alt Title D*) |
| **animetosho** | 0 | ~1s | `feed.animetosho.org` không phân giải DNS được (site chính `animetosho.org` vẫn vào được) |
| **yourbittorrent** | 0 | ~1s | 403 "One moment" (trang chống bot) |
| **cloudtorrents** | 0 | ~1s | Site chết (HTTP 526) |
| **yts_mx** | 0 | ~1s | Domain `yts.mx` không phân giải được |
| onlinefix, redetorrent | 0 | ~1s | Ngoài chuyên môn (game / site Brazil), nên tắt cho hunt media |

Hệ quả cho thiết kế B/D:
- Engine native viết bằng .NET `HttpClient` dùng kho chứng chỉ Windows, nên tự hết lỗi chứng chỉ (bitsearch, torlock).
- Tsukihime và torrents-csv có API JSON hoạt động, nên viết native rất rẻ.
- Engine treo (eztvx, kickasstorrents) cần timeout riêng cho từng engine; đây chính là lý do chọn B.
- **EZTV có API JSON chính thức không qua Cloudflare** (`https://eztvx.to/api/get-torrents`, trả 200 trong khoảng 0.5s, lọc theo `imdb_id`). Chỉ trang HTML search mới bị challenge (403 "Just a moment"). Plugin `eztvx` cần OMDb API key để đổi tên sang IMDb id, rồi rơi xuống trang HTML. Engine native lấy IMDb id từ TMDB (`external_ids`) và gọi thẳng API này.
- **Không làm cơ chế vượt Cloudflare hay chống bot** (giải JS challenge hoặc CAPTCHA, giả dấu vân tay TLS, FlareSolverr). Engine chỉ có trang bị challenge mà không có API công khai (therarbg, yourbittorrent) thì tắt, hoặc giữ lại với timeout ngắn kèm circuit breaker để không bao giờ làm kẹt job.

### Tiêu đề đặc thù

| Tiêu đề | Kết quả | Kết luận |
|---|---|---|
| `Movie 01` (TMDB 2014) | 479 dòng; năm trong tên: **2013 ×322**, 2014 ×11 | **Xác nhận SRCH-11**: luật khớp năm chính xác hiện tại loại khoảng **97%** số bản. Template `{title} {year}` (= `Movie 01 2014`) còn lọc hụt ngay từ lúc search |
| `Léon The Professional` so với `Leon The Professional` | **207** so với **458** dòng; torrentdownloads 0 so với 100, piratebay 10 so với 99 | **Xác nhận SRCH-13 về dấu thanh**: bỏ dấu tăng gấp đôi kết quả. App hiện **giữ dấu** (`\p{L}`) |
| `Schindler s List` (app gửi) so với `Schindlers List` | 413 dòng / 7 engine so với 361 dòng / 5 engine | **Điều chỉnh SRCH-13 về dấu nháy**: không có biến thể nào thắng tuyệt đối. Kickass và sktorrent chỉ ra kết quả với `Schindler s`, còn limetorrents nhiều hơn với `Schindlers`. Cách sửa đúng là **gửi cả hai biến thể** và khớp tiêu đề bằng bộ chuẩn hoá chung |
| `2001 A Space Odyssey` | 552 dòng; năm trong tên: 2001 ×596, 1968 ×419 | Parser năm (SRCH-11) **phải bỏ qua số nằm trong tiêu đề** |
| `Alt Title A` (romaji) | 113 dòng / 8 engine (nyaasi 63) | Tên thay thế mang lại kết quả, ủng hộ SRCH-12 |
| `Show 16 S03` | 214 dòng / 6 engine | Query nhắm đích theo season có kết quả, ủng hộ SRCH-03 |
| `Alt Title D 06` | 38 dòng / 6 engine | Anime đang chiếu ổn, khớp nhận xét của user |
| `Movie 08` | 574 dòng, **554 URL khác nhau** | Hàng trăm bản để lựa, trong khi app chỉ giữ tối đa 10 (SRCH-15) |

---

## 5d. Kết quả A/B timeline: app dừng sớm so với chờ tới khi xong (12/11/2026, WARP bật)

Công cụ: `tools/SearchProbe/ab_timeline.py` với 7 case trong `ab_cases.json`. Mỗi case là một job dùng đúng bộ plugin và query của recipe; job chạy tới khi qBittorrent tự dừng, sau đó áp luật dừng (timeout và idle) của recipe lên dòng thời gian để biết app giữ được bao nhiêu.

| Case | Chờ tới khi xong | App giữ được | Ghi chú |
|---|---|---|---|
| Anime snapshot *Show 08* (10 plugin, 60s/idle 28) | 312 dòng, 180s | **312 (100%)**, dừng ở 60s | Mọi dòng về trong 60s đầu; 120s sau chỉ là chờ engine treo |
| Anime, cấu hình *Common Anime* (mọi plugin, 45s/idle 8) | 454, 180s | **437 (96%)** | |
| Pack *Show 16* (query snapshot) | 884, 180s | **803 (91%)** | 782 dòng đã có ở giây 30 |
| Pack `Show 16 S03 1080p` (nhắm đích) | 131, 180s | 123 (94%) | Xem SRCH-03 bên dưới |
| Movie `Movie 01 2014` (Default Movie, 30s/idle 10) | **12**, 180s | 12 (100%) | Cả 12 đều ghi 2014; **không có bản 2013 nào** |
| Movie *Movie 08* (Common Movie, 120s/idle 30) | 616, 180s | 576 (94%) | 515 dòng đã có ở giây 30 |
| *Show L 1170 1080p* | 73, **23s** | 73 (100%) | Buffer < 150 dòng, nên SRCH-05 không xảy ra với query này |

Kết luận:
- **SRCH-01 được điều chỉnh: thiệt hại chủ yếu là _thời gian_, không phải mất kết quả.** App vẫn giữ 91–100% số dòng. Nhưng 6/7 job kéo **đúng 180s**, vì qBittorrent tự cắt search sau 3 phút và có engine treo (eztvx, kickasstorrents) giữ job tới lúc đó. Hệ quả: mỗi query của hunt luôn tốn trọn timeout của recipe (45–120s). Một show snapshot có 3 query mất 2–3 phút. **Lời khuyên cũ "tăng idle lên ~30s" bị rút lại**, vì nó chỉ làm chậm thêm. Cách sửa đúng là cắt hoặc tắt engine treo và đặt timeout riêng cho từng engine.
- **SRCH-03 được xác nhận bằng số liệu:** query pack thực tế (`Show 16`) chỉ chứa **7** season pack S03. Query nhắm đích `Show 16 S03 1080p` ra **111**, trong đó **105 bản mà query tên show không có**. Gấp khoảng 15 lần.
- **SRCH-11 nặng hơn dự kiến:** template `{title} {year}` của Default Movie gửi `Movie 01 2014`, nên chỉ thu được 12 dòng. Trong khi đó `Movie 01` (mục 5c) ra 479 dòng, 322 bản ghi 2013. Năm TMDB sai làm hỏng **cả bước search lẫn bước lọc**.

---

## 6. Đề xuất sửa, xếp theo ưu tiên để user duyệt

0. **Movie (ưu tiên theo user): SRCH-12, SRCH-11, SRCH-13, rồi SRCH-14, SRCH-15.** SRCH-12 có thể thử ngay mà chưa cần sửa code: bật `useLibraryEnglishTitles` trong recipe movie. **SRCH-11 (khớp năm) và SRCH-13 (chuẩn hoá tiêu đề) được user đánh dấu là "cần sửa lớn"**: làm lại toàn bộ, có bộ fixture test riêng, không chỉ vá nhỏ. Hai việc này nên làm chung một nhánh vì đều đụng tới bước khớp tiêu đề của movie.
1. **SRCH-02 + SRCH-01: dọn engine và cắt engine treo** (A/B đã xong, mục 5c và 5d). Làm được ngay mà chưa cần sửa code: **tắt** trong qBittorrent hoặc bỏ khỏi recipe các engine hỏng (eztvx, therarbg, bitsearch, solidtorrents, torlock, tsukihime, torrentscsv, animetosho, yourbittorrent, cloudtorrents, yts_mx, onlinefix, redetorrent), và bỏ một trong hai plugin piratebay vì trùng nguồn. **Không** tăng idle; A/B cho thấy làm vậy chỉ chậm thêm.
2. **SRCH-03: snapshot dùng template season/episode, hoặc có lượt search nhắm đích.** Ảnh hưởng lớn nhất tới pack (31+ show).
3. **SRCH-04: đánh giá hết rồi mới lấy top N.** Sửa nhỏ, ảnh hưởng 4 show có Auto-Track và Show L.
4. **SRCH-05, SRCH-06, SRCH-07, SRCH-08: gom về một `SearchRunner`.** Đây là refactor lớn hơn và nên làm sau khi có số liệu A/B.
5. **SRCH-09, SRCH-10:** dọn cấu hình và bổ sung log.

---

## 7. Kế hoạch A/B (chưa chạy, cần user cho phép)

- **Chỉ search, không add torrent.** Script đặt trong scratchpad và chỉ gọi `search/start`, `search/status`, `search/results`, `search/stop`, `search/delete` trên qBittorrent cục bộ.
- Với mỗi ví dụ user đưa ra (show/tập, query gõ tay, bản phát hành tốt mà hunt bỏ lỡ), chạy cùng plugin và cùng query theo 3 cách:
  1. như app đang làm (timeout và idle theo recipe);
  2. chờ tới khi status `Stopped`, tối đa 5 phút, rồi đọc hết buffer;
  3. dùng query nhắm đích, ví dụ `Tên S02` hoặc `Tên - 05`.

  Mỗi cách ghi lại: số dòng theo từng engine, thời điểm mỗi engine trả dòng cuối, và bản phát hành tốt có xuất hiện không (ở engine nào, sau bao nhiêu giây).
- Tất cả các lượt đều chạy với **WARP bật**, đúng như cả hunt lẫn UI đang làm. Script không đụng tới WARP; nếu WARP đang tắt thì dừng lại và báo user.

## 8. Quyết định của user và kế hoạch tiếp theo (12/11/2026)

- **Rời hẳn engine search của qBittorrent.** Search do app làm bằng **indexer native (C#)**; qBittorrent chỉ còn làm việc tải torrent. Hướng B (chạy plugin Python) không còn là đích đến.
- **Giữ đường search qua qBittorrent sau một cờ cho tới hết P6**, để so sánh A/B và để quay lại được khi cần. P6 xoá hẳn đường này.
- **Không làm cơ chế vượt Cloudflare hay chống bot.** Site chỉ có trang bị challenge thì bỏ.
- Lộ trình:
  - **P1:** khớp và chấm điểm. TitleNormalizer (SRCH-13), YearMatcher (SRCH-11), tên thay thế (SRCH-12), đánh giá hết rồi mới lấy top N (SRCH-04).
  - **P2:** khung indexer gồm `ITorrentIndexer`, SearchOrchestrator, sức khoẻ indexer và circuit breaker, đường add cho file `.torrent`, cùng 3 indexer thepiratebay, limetorrents, nyaa. Bật bằng cờ.
  - **P3:** EZTV (API, tra bằng IMDb id), torrents-csv, tsukihime, subsplease, nekobt, yts, rồi torrentdownloads, bitsearch, torlock, kickass.
  - **P4:** kế hoạch query nhắm đích theo season và tập (SRCH-03), query riêng theo indexer, cache.
  - **P5:** lọc rác, gộp theo infohash, UI so sánh nhiều bản encode (SRCH-14, SRCH-15).
  - **P6:** native thành mặc định, migration recipe v2, xoá code search qBittorrent (SRCH-05 đến SRCH-10 tự hết).
- Trước khi chuyển hẳn ở P6: dùng `tools/SearchProbe` so sánh native với qBittorrent và chứng minh anime không tụt chất lượng.

### Bắt buộc: thành phần quản lý tần suất request (user yêu cầu, 12/11/2026)

Site public miễn phí sẽ **ban IP ngay** nếu bị gọi dồn dập. Mọi request ra site đều phải đi qua một thành phần trung tâm, tạm gọi là `IndexerRequestGovernor`. Không indexer nào được tự gọi `HttpClient` trực tiếp.

- **Giới hạn theo từng site:** token bucket (số request mỗi phút), số request đồng thời tối đa (thường là 1–2), khoảng cách tối thiểu giữa hai request, cộng jitter ngẫu nhiên.
- **Lùi lại khi bị từ chối:** gặp 429 hoặc 503 thì tuân theo `Retry-After` và backoff luỹ thừa. Gặp 403 hoặc trang challenge thì **ngừng ngay** site đó (circuit breaker mở), không retry dồn.
- **Ngân sách theo ngày hoặc giờ** cho từng site, có cấu hình được. Hết ngân sách thì bỏ qua site đó, ghi log và hiện trên UI.
- **Ưu tiên:** search do user bấm (cart) đi trước; Auto-Track chạy nền thì xếp hàng và dùng phần ngân sách còn lại.
- **Cache kết quả theo query** (TTL 30–60 phút, riêng EZTV tra theo IMDb id có thể lâu hơn). Gộp request trùng đang chạy đồng thời (single-flight).
- **Giữ trạng thái qua lần khởi động lại:** lượng ngân sách đã dùng, circuit breaker và lần bị chặn gần nhất được lưu xuống DB, để khởi động lại app không reset giới hạn.
- **Quan sát được:** màn hình sức khoẻ indexer hiển thị số request, số lỗi, lần bị chặn và trạng thái breaker. Có công tắc tắt nhanh từng site và tắt toàn bộ.
- **Không vượt chặn:** khi site trả challenge thì chỉ dừng lại, không thử cách khác để lách.

Thành phần này thuộc **P2** (khung indexer). Mọi indexer ở P3 trở đi phải dùng nó ngay từ đầu.

## 9. Khảo sát các trang torrent public khác để viết indexer native (12/11/2026, WARP bật)

Công cụ: `tools/SearchProbe/site_probe.py` cùng danh sách `site_candidates.json`, chỉ gửi GET, không vượt challenge. Các site Python báo lỗi chứng chỉ được kiểm lại bằng curl (Windows schannel, cùng kho chứng chỉ mà .NET dùng). Không site nào trong nhóm dùng được **cần đăng ký hay API key**.

### Dùng được (không challenge, không cần key)

| Site | Loại | Giao diện | Kết quả thử | Đánh giá |
|---|---|---|---|---|
| **Knaben** | Meta (gom nhiều tracker) | **API JSON** `api.knaben.org/v1` | 150 kết quả trong 1.7s cho `Movie 01`, tổng ≥ 10000. Nguồn: TPB, TokyoTosho, Nyaa, **1337x** | **Ứng viên số 1.** Có sẵn `hash`, `magnetUrl`, `seeders`, `bytes`, `date`, `tracker`, kể cả trường `virusDetection`. Lấy được nội dung 1337x mà không chạm vào trang 1337x bị challenge |
| **BT4G** | Chỉ mục DHT | RSS `bt4gprx.com/search?q=…&page=rss` | 15 kết quả trong 0.4s | Phủ rộng nội dung cũ (tốt cho movie cũ), nhưng bản DHT nhiều rác, nên phải qua bộ lọc rác |
| The Pirate Bay | Tổng hợp | API JSON `apibay.org` | 77 kết quả trong 0.2s | Đã có (thay cho 2 plugin piratebay trùng nhau) |
| EZTV | TV | API JSON tra theo IMDb id | 50 kết quả trong 0.5s | Đã có trong kế hoạch |
| YTS | Movie | API JSON (domain đã chuyển sang `yts.gg`) | 2 kết quả | Ít bản nhưng sạch. Phải theo redirect domain |
| torrents-csv | Tổng hợp | API JSON | 10 kết quả | Python lỗi chứng chỉ, Windows/.NET thì chạy |
| bitsearch.eu | Chỉ mục DHT | HTML | 20 hash | Python lỗi chứng chỉ, .NET thì chạy |
| rutor | Tổng hợp (Nga) | HTML | 22 magnet trong 0.8s | Tùy chọn; có nhiều movie quốc tế |
| nyaa | Anime | **RSS** | 75 kết quả trong 0.9s | Dùng RSS thay cho cào HTML |
| TokyoTosho | Anime | RSS | 149 kết quả nhưng **12.9s** | Chậm; Knaben đã gom sẵn TokyoTosho |
| AnimeTosho | Anime | HTML search | 75 magnet trong 1.3s | JSON feed trả 403; dùng HTML |
| SubsPlease, TsukiHime, nekoBT | Anime | API JSON | 21 / 50 hash / có | Đã có trong kế hoạch |
| bangumi.moe, Mikan, dmhy | Anime (nguồn Trung Quốc) | API / RSS | 23 / 23 / 321 magnet | Chủ yếu raw và phụ đề Trung; ưu tiên thấp |

### Không dùng được

| Site | Lý do |
|---|---|
| 1337x (cả mirror), ext.to, uindex | 403 kèm Cloudflare challenge (lấy nội dung 1337x qua Knaben) |
| TorrentGalaxy (`.to`, `.one`), torrentz2 | Domain không phân giải được (có thể đã chết) |
| magnetdl | 522 (origin chết) |
| btdig, glodls | Timeout |
| anidex | Từ chối kết nối |
| erai-raws RSS | 403, cần token thành viên |
| showRSS, snowfl, torrentproject, idope | Cần id show, giao diện chạy bằng JS, cần mở từng trang chi tiết, hoặc redirect lạ; công sức cao so với giá trị |

### Đề xuất thứ tự indexer (cập nhật P2/P3)

- **P2:** Knaben (meta, phủ rộng nhất trên mỗi request), apibay (TPB), nyaa RSS, limetorrents.
- **P3:** EZTV (IMDb id), BT4G (movie cũ), YTS, torrents-csv, TsukiHime, SubsPlease, nekoBT, AnimeTosho, bitsearch, torrentdownloads; tùy chọn rutor, kickass.
- Knaben là meta và gom lại một phần TPB, Nyaa, TokyoTosho, nên phải **gộp theo infohash** (P5) để không đếm trùng.

## Ngoài phạm vi (ghi lại)

- `settings.json` lưu mật khẩu WebUI qBittorrent dạng văn bản thường. Việc này thuộc mục 4 (credentials), mục đã được bỏ khỏi đợt audit này.
