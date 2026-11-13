# Audit tích hợp TMDB, Jellyfin và WARP (2026-11)

- **Nhánh / commit gốc:** `claude/integrations-audit-e9519e` @ `ff2caa3` (= `main`). Số dòng trong báo cáo ứng với commit này.
- **Phương pháp và thang mức độ:** theo [`00-method-and-scope.md`](../00-method-and-scope.md).
- **Đường dẫn** tính từ `media management app/media management app/`.
- **Cách làm:** đọc code tĩnh, lịch sử git, PR #7/#9/#10, log app trong `<StateFolder>\Logs` (33 file systemlog, 2026-10-29 → 2026-11-12) và log của WARP trong `C:\ProgramData\Cloudflare`. Chạy `dotnet test MediaManager.App.Tests` đúng một lần: **138/138 pass**.
- **Những gì KHÔNG làm:** không sửa code app, không bật/tắt hay đổi mode WARP, không gọi refresh Jellyfin, không gọi API TMDB. Lệnh ngoài duy nhất là một lần `warp-cli status` (chỉ đọc) để kiểm chứng INT-02.
- **Secret:** không có secret nào trong báo cáo. Log app đã quét các mẫu `Bearer …`, `Token="…"` và `api_key=`: **0 file khớp**.
- **Trạng thái bằng chứng:** **CONFIRMED** nghĩa là đường lỗi được chứng minh bằng code hoặc quan sát thật. **PLAUSIBLE** nghĩa là đúng về logic nhưng điều kiện kích hoạt chưa được quan sát.

### Quan hệ với các audit và nhánh khác

- **Network audit** (nhánh `claude/zen-antonelli-410a4f`, `docs/network-audit/`, NET-001…NET-029) đã phủ phần ống dẫn HTTP: token Bearer lọt sang Gemini, race của `DefaultRequestHeaders`, `warp-cli` không có deadline, timeout, connection pool, classifier, tunnel lúc crash và hàng đợi Jellyfin mất khi thoát. Báo cáo này **không lặp lại** các mục đó. Mục 3 chỉ tham chiếu chéo, còn finding mới (INT-xx) tập trung vào **tính đúng của dữ liệu, chính sách WARP và hành vi Jellyfin/NFO**.
- **Nhánh `fix/network-package-1`** (chưa merge, chưa push, 9 commit, ngày 2026-11-08) sửa NET-001…004, 006, 010…016 và 025. **Bản app đang cài trên máy là bản build từ nhánh này** (`1.0.7-alpha.0.8+65038d9`, theo dòng khởi động trong log 2026-11-12). Vì vậy log sau ngày 2026-11-08 phản ánh code của nhánh đó, không phải `main`. Đã kiểm tra: nhánh này **không** đụng tới các điểm của INT-01…INT-16 (cache, upsert, `WarpLogStatusWatcher`, `Retry-After`, NFO).

## 1. Tóm tắt

Đường WARP recovery cho TMDB hiện **chạy được**. Log có 12 lần `TMDB refresh ok`, và từ sau fix `warp+doh` (2026-10-31) không còn lần "still unreachable with WARP" nào. Mode của user được khôi phục 4/4 lần. Vòng lặp recovery có giới hạn: tối đa 3 lần thử mỗi show, và nếu connect thất bại thì cả cycle dừng thử.

Vấn đề lớn nhất **không nằm ở mạng mà ở dữ liệu**: refresh TMDB chỉ **upsert cộng dồn**. Tập hoặc season mà TMDB đã xóa hay đánh số lại sẽ nằm mãi trong DB ở trạng thái `Missing`, và Auto-Track săn chúng mỗi cycle (**INT-01**). Lỗi HTTP ở một season bị nuốt, khiến refresh vẫn được tính là thành công và tập mới bị trễ một tuần (**INT-03**). Show bị TMDB xóa hoặc gộp (404) được thử lại mỗi 30 phút, mãi mãi và không ai được báo (**INT-04**). Cache của provider tồn tại suốt vòng đời process và không bao giờ hết hạn (**INT-05**).

Về WARP: watcher log WARP trỏ vào **một thư mục không tồn tại** nên chưa từng chạy. Nếu chỉ sửa đường dẫn, nó sẽ tạo **vòng lặp tự kích hoạt** `warp-cli status` khoảng mỗi 3 giây, vì mỗi lần gọi `status` lại làm daemon WARP ghi một dòng chứa "Connected" (**INT-02**, đã kiểm chứng). Lease TMDB recover được giữ suốt pha hunt, nên mode `warp+doh` và tunnel kéo dài sang cả việc thêm torrent, trái với thiết kế "Hunt giữ mode của user" trong #10 (**INT-07**).

| Mức độ | Số lượng | ID |
|---|---|---|
| Critical | 0 | |
| High | 1 | INT-01 |
| Medium | 5 | INT-02 … INT-05, INT-07 |
| Low | 10 | INT-06, INT-08 … INT-16 |

## 2. Lịch sử bug và hotspot

### 2.1 Hotspot (`git log --follow`)

| File | Số commit | Khoảng thời gian | Ghi chú |
|---|---|---|---|
| `Services/TmdbMetadataProvider.cs` (1.379 dòng) | 14 | 05-16 → 11-03 | Một class gánh 3 interface: match, catalog, mapping. Có 4 cache dạng `Dictionary` sống suốt process |
| `Services/WarpCliService.cs` (917 dòng) | 8 | 06-14 → 11-03 | +271 dòng ở #10 |
| `Services/JellyfinViewerService.cs` | 7 | 08-13 → 09-11 | |
| `Services/JellyfinLibraryRefreshService.cs` | 6 | 07-27 → 10-26 | |
| `Services/JellyfinClient.cs` | 6 | 07-27 → 09-10 | |
| `Services/AutoTrackService.cs` (ngoài phạm vi, là nơi gọi) | — | — | Bị đụng ở #7, #9, #10, "fix tmdb refresh" và "fix show finised…" |

### 2.2 Chuỗi fix và đánh giá "chữa triệu chứng"

| Commit | Nội dung | Đánh giá |
|---|---|---|
| `9d178a9` fix tmdb refresh (07-18) | Thêm `TmdbDailyBudget` (10 refresh/ngày) | Hợp lý để tránh gọi quá nhiều, nhưng dùng thay cho việc xử lý 429 thật (INT-06). Refresh thất bại không tốn budget, nên show lỗi vĩnh viễn được thử lại mỗi tick (INT-04) |
| `d8a5060` fix jellyfin symlink bug (07-28) | `e.Item.SymlinkPath = result.TouchedSymlinkPaths[0]` khi item của event là một instance khác | **Triệu chứng.** Gốc là có hai instance `SourceItem` khác nhau cho cùng một item. Fallback lấy phần tử `[0]`, nên với nhóm nhiều item, đường dẫn báo cho Jellyfin có thể là của item khác. Ít hại, vì Jellyfin quét theo thư mục. Thuộc vùng Symlink, chỉ ghi lại |
| `a1f34df` fix show finised not refresh tmdb (09-19) | Bỏ điều kiện bỏ qua show `Finished`/`FinishedComplete` | Đúng hướng: TMDB có thể thêm special. Nhưng vấn đề gốc là refresh chỉ cộng dồn và không có tín hiệu "TMDB đã đổi". Show Finished nay refresh mỗi cycle mãi mãi; chỉ budget chặn số lần |
| `dbc95c8` #7 (10-26) | Mở rộng detector: refused, reset, DNS, timeout; thêm chờ DNS; breaker khi connect thất bại | **Triệu chứng #1** của một chuỗi: mỗi kiểu chặn mới (SSL → DNS → SNI) lại cần một bản vá ở detector |
| `779bee7` #9 (10-26) | Gom về `TmdbWarpRecovery` dùng chung; thêm backoff cho Jellyfin | Backoff chữa vòng lặp ~5 s. Gốc là pre-probe TMDB **chặn** việc báo cho Jellyfin (NET-006) thì vẫn còn: token TMDB hết hạn thì Jellyfin không bao giờ được báo |
| `d16a334` #10 (11-03) | Chuyển `doh` → `warp+doh`, khôi phục mode, marker crash, `TmdbBlockAdvisor` | **Triệu chứng #3**: ISP chặn thêm SNI. Marker crash được làm tốt. Còn hai lỗ hổng: lease kéo dài qua pha hunt (INT-07) và lúc khởi động có trường hợp không khôi phục (INT-08) |
| `fix/network-package-1` (chưa merge) | Classifier, breaker, host `api.tmdb.org`, deadline cho `warp-cli` | Đây là lời giải gốc cho chuỗi trên: một classifier duy nhất cộng breaker theo cycle. Nên merge trước khi sửa các INT liên quan tới WARP |

**Mẫu lặp lại:** cả ba PR #7, #9, #10 đều phản ứng sau khi ISP đổi cách chặn, mỗi lần vá ở từng điểm gọi riêng (Auto-Track, pre-probe Jellyfin, FirstRun), còn Library và Find/Add thì chỉ hiện gợi ý. Network-package-2 gom phần classifier lại. Phần **dữ liệu TMDB** (INT-01/03/04/05) thì chưa ai đụng tới.

### 2.3 Số liệu từ log (2026-10-29 → 2026-11-12)

| Sự kiện | Số lần |
|---|---|
| `TMDB refresh ok` (Auto-Track) | 12 |
| `Auto-track TMDB refresh failed` | 2 (cùng ngày 10-31, trước fix `warp+doh`) |
| `TMDB still unreachable with WARP` | 4 (đều ngày 10-31) |
| `TMDb returned 429/5xx` | 0 |
| `WARP mode warp+doh → doh restored` | 4/4 |
| Jellyfin notify OK (`Jellyfin accepted path notify`) | 4, cả 4 đều cần WARP ở pre-probe |
| Jellyfin flush deferred/failed | 0 |
| `WARP lease release ignored (Auto-Track TMDB recover); not held` | 440 dòng DBG (gọi Release cuối mỗi cycle dù không giữ lease; chỉ là nhiễu log) |
| `WARP log tail opened` / `log-tail confirm` | **0**, xem INT-02 |

## 3. Trả lời các câu hỏi của đề bài

### 3.1 HTTP

| Câu hỏi | Trả lời | Tham chiếu |
|---|---|---|
| Dùng lại hay tạo mới `HttpClient`? Có nguy cơ socket exhaustion? | Dùng lại. TMDB và pre-probe Jellyfin dùng `HttpClient` singleton (`App.xaml.cs:291`); `JellyfinClient` tự tạo một client riêng, cũng là singleton (`JellyfinClient.cs:22`). **Không có nguy cơ socket exhaustion.** Ngược lại, client không có `PooledConnectionLifetime` nên có thể giữ kết nối cũ qua lần chuyển WARP | NET-010 |
| Header và `BaseAddress` dùng chung | `ConfigureHeaders()` ghi `DefaultRequestHeaders` ở mỗi lần gọi (`TmdbMetadataProvider.cs:1182-1187`), `BaseAddress` được gán trong constructor (`:34`) | NET-001, NET-002, NET-025 (đã sửa trên `fix/network-package-1`) |
| Timeout | Mặc định 100 s cho mọi request; `warp-cli` không có deadline | NET-011, NET-003 |
| Cancel | Token được truyền xuyên suốt. `Task.Delay` trong retry tôn trọng token. `process.WaitForExitAsync(ct)` khi bị hủy không kill `warp-cli` | NET-003 |
| Retry/backoff có giới hạn? | Có. TMDB: 3 lần, chờ 350 ms rồi 700 ms (`:1189-1220`). WARP recovery: 3 lần thử, chờ 3 s rồi 5 s (`TmdbWarpRecovery.cs:16`). Jellyfin: 3 s → 30 s → 2 phút → 5 phút, **số lần thì không giới hạn** | NET-006, NET-012, INT-11 |
| Tôn trọng 429 và `Retry-After`? | **Không.** 429 được retry sau 350 ms hoặc 700 ms, bỏ qua header `Retry-After` | INT-06 |
| JSON thiếu field hoặc null | Hầu hết dùng `GetString`/`GetInt` có kiểm tra `ValueKind`, nên an toàn. Ngoại lệ: `SearchTvSeriesIdsAsync` dùng `GetProperty("results")` và `GetProperty("id").GetInt32()` (`:842-845`), sẽ ném lỗi nếu thiếu field, nhưng được `MatchTvSeriesAsync` bắt và trả `IsAvailable=false`. `JellyfinClient.TryGetString` gọi `GetString()` khi giá trị không phải string (`:348`), sẽ ném lỗi. Hai chỗ này để ở mức ghi chú | §5 |
| Ngôn ngữ / region | Hardcode `language=en-US`, không truyền `region`, không truyền `include_image_language`. Tiêu đề, overview và NFO đều bằng tiếng Anh; alternative title lọc theo tiếng Anh (`EnglishAlternativeTitleFilter`). Đây là lựa chọn thiết kế, không xếp thành finding | §5 |

### 3.2 TMDB

| Câu hỏi | Trả lời | Tham chiếu |
|---|---|---|
| Cache metadata có bị cũ? | Có hai tầng. (a) DB: refresh cộng dồn, **không bao giờ xóa** tập hoặc season mà TMDB đã bỏ hay đánh số lại → tập ma. (b) Bộ nhớ: 4 cache trong provider sống suốt process; đường match/map khi import dùng các cache này, nên số season/tập bị cũ cho tới khi khởi động lại app. Đường refresh (`GetTvShowDetailsAsync`) thì **không** dùng cache | INT-01, INT-05 |
| Show đã kết thúc | Từ `a1f34df` vẫn refresh mỗi cycle. `RefreshOngoingShowsAsync` (chỉ lấy show `Ongoing`) là code chết | INT-13 |
| Tập đổi tên hoặc đổi số | Đổi tên: được cập nhật (`Title = excluded.Title`). Đổi số: số cũ còn lại thành tập ma. Tập bị TMDB lùi ngày phát sang tương lai: bị loại khỏi response nên ngày phát cũ (đã qua) vẫn giữ trong DB, và tập có thể bị săn sớm | INT-01 |
| Refresh có ghi đè dữ liệu user tự chỉnh? | **Không.** `UpsertTrackedShow` giữ `ExcludedAlternativeTitlesJson`, `EpisodeGroup*`, recipe, quality, audio codec, seeders. `UpsertTrackedSeason` giữ `DownloadFolder`, `ManagementMode`, `IsHidden`. `UpsertTrackedEpisode` giữ toàn bộ thông tin torrent và candidate. Riêng `Availability` bị reset rồi được tính lại ngay ở `RefreshAvailability(showId)` (`TrackedShowService.cs:76-78`). Giữa hai bước không có transaction, nhưng chỉ gây nháy UI chứ không làm sai dữ liệu | — |
| TMDB trả 404 cho show bị xóa hoặc gộp | Refresh ném lỗi với thông điệp chung chung, không có trạng thái nào được ghi, và thử lại mỗi tick 30 phút mãi mãi. `GetCandidateDetailsAsync` gom **mọi** status không phải 2xx thành "không có" | INT-04 |

### 3.3 WARP

| Câu hỏi | Trả lời | Tham chiếu |
|---|---|---|
| Phát hiện chặn có báo nhầm? | Có, trong vài trường hợp: máy offline (`HostNotFound`, `NetworkUnreachable`) bị coi là bị chặn, dẫn tới thử connect WARP rồi kích hoạt breaker. Ngoài ra có so khớp chuỗi `"forcibly closed"` hay `"certificate"`. Status HTTP (404/401/5xx) **không** bị coi là chặn: đúng | NET-013 (đã sửa trên package-2) |
| Vòng recovery có lặp vô hạn? | **Không** với TMDB: tối đa 3 lần thử mỗi show, connect thất bại thì dừng thử trong cả cycle. Pre-probe Jellyfin: số lần không giới hạn nhưng cách nhau tối đa 5 phút. **Có vòng lặp tiềm ẩn** ở watcher log WARP | NET-006, NET-012, **INT-02** |
| Có bật/tắt WARP trái ý user? | Tunnel do user bật trước đó thì không bị ngắt (`_tunnelOwnedByApp`). Nhưng lease `TmdbSslRecover` được giữ qua cả pha hunt, nên `warp+doh` và tunnel kéo dài tới lúc thêm torrent xong. User có mode `proxy` cũng bị chuyển sang `warp+doh` (theo thiết kế, có thể tắt bằng `SwitchModeForTmdb`). Nếu user tự bật WARP **trong khi** app đang giữ một lease do app sở hữu, lần release cuối sẽ ngắt luôn tunnel của user | **INT-07**, NET-009, §5 |
| Khôi phục mode cũ khi app crash | Marker được ghi **trước** khi đổi mode, và lúc khởi động sẽ khôi phục nếu mode hiện tại vẫn là `warp+doh`. Tốt. Lỗ hổng: không khôi phục khi `Warp.Enabled=false`, hoặc khi một lease giành được `_cliGate` trước; tunnel do app bật cũng không được ngắt (NET-008) | **INT-08**, NET-008 |
| Nhiều recovery chạy song song | Được tuần tự hóa: `_cliGate` (SemaphoreSlim 1), lease là một set theo lý do, `_flushGate` của Jellyfin, và mỗi cycle Auto-Track chỉ một lượt. Auto-Track và Jellyfin cùng giữ lease thì tunnel chỉ ngắt khi lease cuối cùng được trả. Không thấy race nào gây hỏng trạng thái | — |
| `warp-cli` không cài | `IsAvailable` = `File.Exists(path)`. Acquire trả `(false,false)`, recovery ném `TmdbWarpConnectFailedException` rồi kích hoạt breaker; advisor bảo cài WARP. Xử lý tốt | — |
| `warp-cli` đổi định dạng output | `status`: chỉ tìm chuỗi con `"Disconnected"`/`"Connected"` (`WarpCliService.cs:857-883`), không có test. `-j settings`: đọc `settings.operation_mode`; nếu không đọc được thì không đổi mode và có ghi cảnh báo. Mức độ an toàn chấp nhận được. Nếu output `status` có chữ "Connected" ở dòng khác (ví dụ một dòng lý do) thì có thể đọc sai | §5, §6 |

### 3.4 Jellyfin

| Câu hỏi | Trả lời | Tham chiếu |
|---|---|---|
| Refresh quá nhiều lần (spam)? | App **không** gọi refresh toàn thư viện: chỉ `POST Library/Media/Updated` theo lô đường dẫn, có debounce 3 s. Khi lỗi thì backoff, nhưng **mỗi đường dẫn mới đưa backoff về lại 3 s**. Riêng NFO của show dùng episode group bị ghi lại toàn bộ ở mỗi lần khởi động/SyncNow, và `tvshow.nfo` bị ghi N lần, có thể làm Jellyfin quét lại khi bật realtime monitor | INT-11, INT-10 |
| API key trong URL hoặc log? | **Không.** Key nằm trong header `Authorization: MediaBrowser Token=…` (`JellyfinClient.cs:394`). URL của viewer là `/web/#/details?id=…`. Log có 0 dòng khớp mẫu secret. Nếu `BaseUrl` là `http://`, key đi dạng plaintext trong LAN; đó là cấu hình của user | — |
| Tail log: rotate, khóa file, encoding | Mở bằng `FileShare.ReadWrite`, giải mã UTF-8 bằng `Decoder` có trạng thái, nên an toàn khi ký tự UTF-8 bị cắt giữa hai lần đọc. Tailer được mở mới cho mỗi lần hold (≤ 10 phút), nên rotate chỉ ảnh hưởng nếu rơi vào giữa một lần hold. Nếu user nhập đường dẫn **file** thay vì thư mục thì file bị ghim cố định, và sau rotate tính năng early-disconnect không còn tác dụng. File bị truncate thì không phát hiện được | INT-16 |
| Viewer/WebView2 | Profile riêng (`StateFolder/WebView2/Jellyfin`). Cửa sổ mới cùng origin mở trong viewer, origin khác mở bằng trình duyệt mặc định. Handler được gỡ khi release. Điều hướng ngay trong trang thì không kiểm tra origin, chỉ là ghi chú | §5 |
| NFO ghi đè file của user | NFO chỉ được ghi cho show dùng episode group, và chỉ trong symlink root do app quản lý. `File.Create` ghi đè không điều kiện và không atomic. `CleanupOrphanEpisodeNfos` xóa **mọi** `*.nfo` (trừ `tvshow.nfo`) trong cây thư mục show nếu không khớp symlink đang hoạt động, kể cả `season.nfo` do NFO saver của Jellyfin tạo | INT-09, INT-10 |

## 4. Findings

### INT-01 — Refresh TMDB chỉ cộng dồn: tập hoặc season bị TMDB xóa hay đánh số lại thành "tập ma", và Auto-Track săn chúng mãi mãi

- **Mức độ:** High · **Bằng chứng:** CONFIRMED (tĩnh); điều kiện kích hoạt chưa quan sát trong log (thư viện hiện có 4 show).
- **Vị trí:** `Services/TrackedShowService.cs:68-84` (`RefreshShowAsync`), `:374-406` (`ImportShow` chỉ upsert); `Services/DatabaseService.cs:1268-1290` (`UpsertTrackedEpisode` dùng `ON CONFLICT … DO UPDATE`, không có DELETE); `Services/TmdbMetadataProvider.cs:1009-1022` (bỏ các tập không có ngày phát hoặc phát trong tương lai); `Services/AutoTrackTmdbEligibility.cs:112-138` (`FindPendingEpisodes`: `Missing` + không có torrent → săn).
- **Hành vi:** chỉ `SwitchEpisodeOrganizationAsync` (`:100-103`) gọi `DeleteTrackedSeasonsAndEpisodes`. Refresh thường không bao giờ xóa dòng nào trong `TrackedEpisodes`/`TrackedSeasons`.
- **Kịch bản lỗi:**
  1. TMDB tách season của một anime: S01E13–E24 thành S02E01–E12. Sau refresh, DB có cả S01E13–E24 (cũ) lẫn S02E01–E12 (mới). Tập S01E13–24 vẫn `Missing` vì file đã link theo số mới, nên Auto-Track săn "S01E13" mỗi cycle. Hoặc tệ hơn, nó tải về một release đánh số cũ, tức trùng nội dung với S02E01.
  2. TMDB lùi ngày phát của S01E08 sang tuần sau. Response mới bỏ tập này vì là ngày tương lai, nên DB vẫn giữ ngày phát cũ (đã qua) và Auto-Track săn sớm.
  3. Tập TMDB xóa (trùng lặp hoặc nhập nhầm) vẫn tính vào `TotalEpisodes` và vào tiến độ.
- **Workaround:** đổi organization sang cùng route để ép rebuild (`SwitchEpisodeOrganizationAsync` xóa rồi dựng lại).
- **Hướng sửa:** khi refresh **thành công đầy đủ** (xem INT-03), so tập TMDB trả về với DB. Những dòng không còn trong TMDB và **không** có torrent, candidate hay file đã link thì xóa. Những dòng có dữ liệu thì đánh dấu `Orphaned` (cờ mới hoặc log cảnh báo) thay vì xóa, và loại khỏi `FindPendingEpisodes`. Với tập có ngày phát bị lùi: vẫn upsert tập có ngày phát tương lai (để cập nhật `AirDate`) thay vì bỏ qua, vì `FindPendingEpisodes` đã tự lọc theo ngày phát.
- **Test cần thêm:** refresh với dữ liệu giả TMDB trong đó một tập biến mất, đổi số, hoặc bị lùi ngày phát.

### INT-02 — Watcher log WARP trỏ vào thư mục không tồn tại (chưa từng chạy); nếu chỉ sửa đường dẫn sẽ thành vòng lặp `warp-cli status` tự kích hoạt

- **Mức độ:** Medium · **Bằng chứng:** CONFIRMED (quan sát trên máy).
- **Vị trí:** `MediaManager.Core/Common/AppConstants.cs:40-41` (`C:\ProgramData\Cloudflare\Cloudflare WARP\log`); `Services/WarpLogStatusWatcher.cs:218-251`, `:282-303`, `:317-326`; `Services/WarpCliService.cs:349-394` (`ConfirmExternalStatusAsync`).
- **Quan sát:**
  - Thư mục `C:\ProgramData\Cloudflare\Cloudflare WARP\log` **không tồn tại**. Log WARP thật nằm ngay trong `C:\ProgramData\Cloudflare\` (`cfwarp_service_log.txt`, …). Log app có 0 dòng `WARP log tail opened` và 0 dòng `log-tail confirm`. Đường dẫn này được đưa vào từ `a8431f8` (08-14) và chưa từng sửa.
  - Chạy một lần `warp-cli status` (chỉ đọc) làm daemon WARP ghi thêm vào `cfwarp_service_log.txt` các dòng `DEBUG actor_ipc::logging: Ipc response: …; Status: Connected { network_health: … }`.
  - `LooksLikeStatusLine` khớp mọi dòng chứa `"Connected"` (không phân biệt hoa thường), nên dòng trên khớp. `FileSystemWatcher` trên thư mục (không lọc file) gọi `QueueHint` ở **mọi** lần ghi của **mọi** file, kể cả `cfwarp_service_connection_stats.txt` (9 MB/ngày).
- **Kịch bản lỗi (nếu sửa đường dẫn mà không sửa bộ lọc):** poll 2 s đọc được một dòng "Status: Connected" → debounce 1 s → `warp-cli status` → daemon ghi lại dòng "Status: Connected" → lần poll sau khớp → … Kết quả là cứ khoảng 3 s sinh một tiến trình `warp-cli`, kéo dài suốt thời gian app ở foreground, và mỗi lần đều chiếm `_cliGate`. Không có dòng log nào lộ ra vòng lặp này, vì `ConfirmExternalStatusAsync` chỉ log khi trạng thái thay đổi. Đây đúng là loại lỗi "laptop nóng" mà `CLAUDE.md` của workspace cảnh báo.
- **Tác động hiện tại:** tính năng đổi pill trạng thái khi WARP được bật/tắt từ bên ngoài app **không hoạt động**. Pill chỉ cập nhật khi app tự gọi CLI.
- **Hướng sửa:** (1) Tìm thư mục bằng cách thử `C:\ProgramData\Cloudflare` và giữ đường dẫn cũ làm phương án dự phòng. (2) Chỉ tail `cfwarp_service_log.txt`. (3) Lọc bỏ các dòng `actor_ipc` (phản hồi cho chính CLI). Chỉ khớp dòng chuyển trạng thái, ví dụ `Status: Disconnected`/`Connecting`, hoặc chỉ khớp khi trạng thái **khác** `_lastKnownConnected`. (4) Bộ lọc `FileSystemWatcher` chỉ nhận file log đó. (5) Thêm giới hạn tần suất cho confirm (ví dụ ≥ 10 s).
- **Test cần thêm:** `LooksLikeStatusLine` không được khớp dòng `Ipc response … Status: Connected`.

### INT-03 — Lỗi HTTP ở một season bị nuốt; refresh vẫn được tính là thành công và đánh dấu cycle đã xong

- **Mức độ:** Medium · **Bằng chứng:** CONFIRMED (tĩnh).
- **Vị trí:** `Services/TmdbMetadataProvider.cs:911-918` (`GetTrackedSeasonDetailsAsync` gặp status không phải 2xx thì trả season rỗng), `:356-360` (season rỗng bị bỏ qua); `Services/AutoTrackService.cs:259-300` (refresh thành công thì tiêu budget và cycle coi như xong).
- **Kịch bản lỗi:** TMDB trả 503 hoặc 429 cho `tv/{id}/season/3` ở cả 3 lần thử. Season 3 bị bỏ khỏi `details.Seasons` và chỉ có một dòng Warning. `RefreshShowAsync` vẫn thành công, `AutoTrackLastTmdbRefreshLocal` được cập nhật, nên cycle tuần này coi như đã làm. Tập mới của S03 không được phát hiện cho tới anchor tuần sau, tức trễ tối đa 7 ngày. Lỗi mạng (exception) thì **không** bị nuốt mà được retry hoặc recover, nên chỉ lỗi HTTP status gặp vấn đề này.
- **Hướng sửa:** khi lỗi season là 429 hoặc 5xx thì ném lỗi (để cả refresh thất bại và được thử lại ở tick sau). 404 thì giữ nguyên hành vi bỏ qua. Đây cũng là điều kiện tiên quyết để INT-01 có thể xóa dữ liệu một cách an toàn.

### INT-04 — Show bị TMDB xóa hoặc gộp (404): thử lại mỗi 30 phút mãi mãi, không có trạng thái, không ai được báo

- **Mức độ:** Medium · **Bằng chứng:** CONFIRMED (tĩnh); chưa gặp trong log.
- **Vị trí:** `Services/TmdbMetadataProvider.cs:302-303` (`EnsureSuccessStatusCode`); `Services/AutoTrackService.cs:259-…` (refresh thất bại thì không tiêu budget, cycle chưa xong); `Services/AutoTrackSchedulerService.cs:221-224` (tick 30 phút); `Services/TmdbMetadataProvider.cs:798-802` (`GetCandidateDetailsAsync`: mọi status không phải 2xx đều thành `null`).
- **Kịch bản lỗi:** TMDB gộp show trùng và id cũ trả 404 (`status_code 34`). Cứ mỗi tick 30 phút, app gửi 1 request (không retry vì 404 không nằm trong `ShouldRetry`) và ghi `Auto-track TMDB refresh failed …: Response status code does not indicate success: 404 (Not Found)`, tức 48 dòng cảnh báo/ngày, kéo dài mãi. Library vẫn hiện dữ liệu cũ, và không có gợi ý "TMDB id không còn, hãy re-match".
- **Hướng sửa:** phân loại 404 ở provider thành một exception riêng (ví dụ `TmdbNotFoundException`). Auto-Track ghi trạng thái `TmdbMissing` và ngừng refresh show đó, hoặc chỉ thử lại mỗi tuần; UI hiện badge. `GetCandidateDetailsAsync` chỉ coi 404 là "không có", còn các status khác thì ném lỗi.

### INT-05 — Cache trong `TmdbMetadataProvider` (singleton) sống suốt process: số season/tập cũ làm match khi import bị trừ điểm

- **Mức độ:** Medium · **Bằng chứng:** CONFIRMED (tĩnh); PLAUSIBLE về tần suất (app chạy nền nhiều ngày; log cho thấy một phiên chạy từ 06:56 tới chiều).
- **Vị trí:** `Services/TmdbMetadataProvider.cs:19-22` (4 `Dictionary` không có TTL và không bao giờ `Clear`); `:791-826` (`_candidateDetailsCache`: `NumberOfSeasons`/`NumberOfEpisodes`); `:879-909` (`_seasonEpisodeCache`); `:1094-1129`, `:1160-1180` (chấm điểm); `App.xaml.cs:336` (singleton). Caller: `Services/MediaImportService.cs:200`, `:221`, `:308`.
- **Khác với NET-017:** NET-017 nói về việc cache cả kết quả **thất bại**. Ở đây kết quả **thành công** cũng không bao giờ hết hạn.
- **Kịch bản lỗi:** app khởi động vào thứ Hai và match S01E05 của show X; cache ghi `NumberOfSeasons=1` và danh sách tập S01 = 1…5. Thứ Hai tuần sau S01E06 ra và Auto-Track tải về. Lúc import, `ValidateStandardEpisodeAsync` thấy cache S01 chỉ có tới E05, nên chấm −80 ("does not contain episode 06; max episode is 05"). Confidence < 75 nên item rơi vào xét duyệt tay. Với S02E01: "parsed season 2 exceeds candidate season count 1" bị −80. Chỉ hết khi khởi động lại app.
- **Hướng sửa:** thêm TTL (ví dụ 6 giờ) hoặc xóa cache của series khi `RefreshShowAsync` chạy. Cache phải an toàn đa luồng (NET-018).

### INT-06 — 429 bị retry sau 350/700 ms, bỏ qua `Retry-After`

- **Mức độ:** Low (log có 0 lần 429 và đã có budget 10 refresh/ngày) · **Bằng chứng:** CONFIRMED (tĩnh).
- **Vị trí:** `Services/TmdbMetadataProvider.cs:1189-1226`.
- **Kịch bản lỗi:** "Refresh all library" (`MediaMetadataSyncService.RefreshAllLibraryFromTmdbAsync`) gọi tuần tự, mỗi show 2 + N season request. Với thư viện lớn có thể chạm giới hạn của TMDB (khoảng 50 req/s theo IP). Khi đó 3 lần thử trong khoảng 1 s đều nhận 429, request thất bại, và với season thì bị nuốt (INT-03).
- **Hướng sửa:** khi gặp 429 thì đọc `response.Headers.RetryAfter` (delta hoặc date), chờ `min(RetryAfter, 10 s)`, và tính lần đó vào số lần thử.

### INT-07 — Lease `TmdbSslRecover` được giữ qua cả pha hunt, nên `warp+doh` và tunnel kéo dài sang tìm kiếm và thêm torrent

- **Mức độ:** Medium · **Bằng chứng:** CONFIRMED (tĩnh).
- **Vị trí:** `Services/AutoTrackService.cs:191-368`: `try` bao cả vòng refresh TMDB **và** `RunTorrentHuntAsync` (`:360`); `ReleaseAsync(TmdbSslRecover)` chỉ được gọi ở `finally` (`:366-369`). `Services/IWarpCliService.cs:89-93` ghi "Hunt and user leases keep the user's mode".
- **Kịch bản lỗi:** user dùng `doh`. TMDB recover ở show đầu tiên chuyển mode sang `warp+doh` và connect. Mọi show còn lại, rồi cả hunt (gọi search engine, thêm torrent, qBittorrent bắt đầu tải), đều chạy qua tunnel toàn máy cho tới cuối cycle. Điều này trái với tài liệu ("Hunt … keep the user's mode") và trái ý user khi tắt `ForceTunnelForHunt` (tùy chọn mới của package-1). Log 11-08 có `WARP lease acquired (Auto-Track hunt); tunnel already up`, khớp kịch bản này.
- **Hướng sửa:** trả lease `TmdbSslRecover` ngay sau vòng refresh TMDB, trước `RunTorrentHuntAsync`. Nếu hunt cần tunnel thì tự lấy lease `Hunt` (theo chính sách package-1).

### INT-08 — Lúc khởi động có trường hợp không khôi phục mode WARP sau crash

- **Mức độ:** Low · **Bằng chứng:** CONFIRMED (tĩnh).
- **Vị trí:** `App.xaml.cs:98-104` (chỉ khôi phục khi `Warp.Enabled`), `Services/WarpCliService.cs:414-458` (`RestorePendingModeAsync` return nếu `_leases.Count > 0`; chỉ gọi đúng một lần), `:466-487` (Acquire thấy mode đang là tunnel nên không ghi `_modeSwitchedFrom`).
- **Kịch bản lỗi:** (a) App crash khi đang ở `warp+doh`, sau đó user tắt tích hợp WARP trong Settings. Marker không bao giờ được xử lý, máy ở `warp+doh` mãi. (b) Nếu một lease (ví dụ Jellyfin hold sau startup reconcile) chiếm `_cliGate` trước `RestorePendingModeAsync`, hàm này return. Acquire thấy mode đang là tunnel nên không đánh dấu đã đổi mode, và lúc release cũng không khôi phục. Mode `warp+doh` giữ nguyên suốt phiên. Khả năng (b) xảy ra thấp vì scheduler Auto-Track chờ 5 phút sau khởi động.
- **Hướng sửa:** khôi phục từ marker bất kể `Warp.Enabled` (nếu `warp-cli` tồn tại). Khi Acquire gặp marker còn tồn tại, nạp `_modeSwitchedFrom = marker.OriginalMode` để lần release sau khôi phục đúng.

### INT-09 — Dọn NFO xóa mọi `*.nfo` không do app tạo trong thư mục show dùng episode group

- **Mức độ:** Low · **Bằng chứng:** PLAUSIBLE (phụ thuộc user có bật "Save metadata as NFO" trong Jellyfin cho library trỏ vào symlink root hay không).
- **Vị trí:** `Services/NfoWriterService.cs:180-225`, được gọi từ `Services/Symlink/SymlinkCoordinatorService.cs:737-761`.
- **Kịch bản lỗi:** NFO saver của Jellyfin ghi `Season 01/season.nfo`. Lần startup reconcile tiếp theo xóa file này vì nó không khớp symlink nào, Jellyfin lại ghi lại, cứ thế lặp. NFO tập do Jellyfin ghi cũng bị app ghi đè.
- **Hướng sửa:** chỉ xóa NFO có dấu hiệu do app tạo (ví dụ comment `<!-- media-manager -->`), hoặc ít nhất bỏ qua `season.nfo`.

### INT-10 — NFO bị ghi lại toàn bộ ở mỗi lần khởi động/SyncNow; `tvshow.nfo` bị ghi N lần mỗi lượt; ghi không atomic

- **Mức độ:** Low · **Bằng chứng:** CONFIRMED (tĩnh); việc Jellyfin quét lại vì thay đổi này là PLAUSIBLE.
- **Vị trí:** `Services/Symlink/SymlinkCoordinatorService.cs:65-72` (startup/SyncNow gọi `WriteAndCleanupEpisodeGroupNfos`), `:719`, `:734` (`WriteTvShowNfo` được gọi cho **mỗi** tập); `Services/NfoWriterService.cs:44`, `:86` (`File.Create` ghi thẳng vào đích).
- **Tác động:** show 100 tập nghĩa là 100 lần ghi `tvshow.nfo` và 100 NFO tập mỗi lần khởi động, nội dung giống hệt nhưng mtime đổi. Nếu library Jellyfin bật realtime monitoring, mỗi lần khởi động có thể kích hoạt quét metadata. App crash giữa lúc ghi để lại NFO bị cắt dở.
- **Hướng sửa:** ghi vào file tạm rồi `File.Replace`/`Move`, và chỉ ghi khi nội dung khác. Gọi `WriteTvShowNfo` một lần cho mỗi thư mục show.

### INT-11 — Backoff của Jellyfin bị đưa về 3 s mỗi khi có đường dẫn mới

- **Mức độ:** Low · **Bằng chứng:** CONFIRMED (tĩnh).
- **Vị trí:** `Services/JellyfinLibraryRefreshService.cs:122-129`.
- **Kịch bản lỗi:** Jellyfin offline và backoff đã lên 5 phút. Auto-Track link thêm một tập, `_consecutiveFlushFailures = 0` và debounce 3 s, nên app thử lại ngay rồi bậc thang 3 s → 30 s → 2 phút lặp lại sau mỗi đường dẫn mới. Mức độ spam nhỏ vì số đường dẫn mới ít, nhưng làm mất tác dụng của #9.
- **Hướng sửa:** khi `_consecutiveFlushFailures > 0` thì chỉ thêm đường dẫn mà không đặt lại timer.

### INT-12 — Episode group: special chỉ nhận ra qua tên đúng "Specials"/"Special"; số tập lấy từ `order + 1`

- **Mức độ:** Low · **Bằng chứng:** PLAUSIBLE.
- **Vị trí:** `Services/TmdbMetadataProvider.cs:470-480`, `:961-966`, `:980-981`.
- **Kịch bản lỗi:** group tên "Season 0", "Specials & OVAs" hay "Extras" bị đánh là season 1, đẩy toàn bộ season thật lệch đi 1. Hệ quả là NFO, symlink và việc săn đều lệch số. Group có `order` bắt đầu từ 1 thì mọi tập lệch +1.
- **Hướng sửa:** nhận diện special theo regex (`^(season\s*0|specials?\b|ova|extras?)`) hoặc theo `season_number` của tập gốc; chuẩn hóa `order` theo giá trị nhỏ nhất trong group.

### INT-13 — Code chết: `ValidateTvSeriesMatchAsync` và `RefreshOngoingShowsAsync`

- **Mức độ:** Low · **Bằng chứng:** CONFIRMED (grep: không có caller ngoài phần khai báo interface).
- **Vị trí:** `Services/TmdbMetadataProvider.cs:106-160`, `Services/IMetadataProvider.cs:11`; `Services/MediaMetadataSyncService.cs:100-112`.
- **Rủi ro nếu dùng lại:** `ValidateTvSeriesMatchAsync` báo "no longer available" cho cả 401, 429 và 5xx (qua `GetCandidateDetailsAsync`). `RefreshOngoingShowsAsync` bỏ qua show có status `Unknown` ("Planned", "Pilot") và show `Finished` được hồi sinh.
- **Hướng sửa:** xóa đi, hoặc sửa hai điểm trên trước khi dùng.

### INT-14 — `FindItemIdByTmdbAsync` có fallback tải toàn bộ thư viện Jellyfin mà không có `Limit`

- **Mức độ:** Low · **Bằng chứng:** CONFIRMED (tĩnh).
- **Vị trí:** `Services/JellyfinClient.cs:191-193`.
- **Tác động:** mỗi lần điều hướng tới một item chưa được Jellyfin quét (lookup có mục tiêu không trả về gì), app tải `Items?Recursive=true&IncludeItemTypes=Series&HasTmdbId=true` với toàn bộ thư viện. Thư viện vài nghìn item thì mất vài MB và vài giây.
- **Hướng sửa:** thêm `Limit` hoặc phân trang, hoặc bỏ fallback và báo "Jellyfin chưa quét item này".

### INT-15 — Release lease `TmdbSslRecover` không điều kiện ở cuối mỗi cycle, gây 440 dòng log nhiễu

- **Mức độ:** Low · **Bằng chứng:** CONFIRMED (log: 440 dòng `WARP lease release ignored … not held`).
- **Vị trí:** `Services/AutoTrackService.cs:366-369`, `Services/WarpCliService.cs:218-224`.
- **Hướng sửa:** chỉ release khi cycle đã thực sự lấy lease (một cờ cục bộ), hoặc hạ dòng log này xuống Trace.

### INT-16 — Early-disconnect theo log Jellyfin không còn tác dụng sau khi log rotate nếu cấu hình là đường dẫn file

- **Mức độ:** Low · **Bằng chứng:** CONFIRMED (tĩnh).
- **Vị trí:** `Services/JellyfinLogPathValidator.cs:28-31` (nếu là file thì dùng chính file đó), `Services/AppendOnlyLogTailer.cs:54-60` (không phát hiện truncate hay rotate).
- **Kịch bản lỗi:** user chọn `log_20261101.log`. Từ ngày hôm sau Jellyfin ghi sang file mới, tailer đọc file cũ và không bao giờ thấy dòng "will be refreshed", nên mọi lần hold đều chạy hết thời gian thay vì ngắt sớm. Tunnel kéo dài thêm tới 120 s mỗi lần.
- **Hướng sửa:** khi đường dẫn là file, lấy thư mục chứa file rồi chọn file mới nhất. Phát hiện truncate khi `stream.Length < Position`.

## 5. Ghi chú review (rủi ro chưa chứng minh được, không tính là finding)

- `WarpCliService.ParseConnectedStatus` (`:857-883`) chỉ tìm chuỗi con và không có test. Nếu `warp-cli` thêm dòng lý do có chữ "Connected" thì kết quả có thể sai. Nên bắt dòng `Status update: (\w+)`.
- Nếu user tự bật WARP **trong khi** app giữ một lease do app sở hữu, `_tunnelOwnedByApp` vẫn là `true`, và lần release cuối sẽ ngắt tunnel của user (`WarpCliService.cs:140-148`, `:236-246`).
- Mode `proxy` của user bị đổi sang `warp+doh` cho TMDB (`WarpModePolicy.IsTunnelMode` coi `proxy` là DNS-only). Đây là chính sách có chủ ý; có thể tắt bằng `SwitchModeForTmdb`.
- Nếu ghi marker thất bại (`TryWriteMarker` chỉ log), app vẫn đổi mode. Crash ngay sau đó thì mode không được khôi phục.
- JSON: `SearchTvSeriesIdsAsync` (`TmdbMetadataProvider.cs:842-845`) và `JellyfinClient.TryGetString` (`:348`) sẽ ném lỗi nếu field có kiểu khác mong đợi, nhưng đều được bắt ở tầng trên.
- `ParseDate` dùng `DateTime.TryParse` theo culture hiện tại; định dạng ISO `yyyy-MM-dd` parse ổn ở mọi culture, nên không có lỗi thực tế.
- Viewer Jellyfin không chặn điều hướng ngay trong trang (`NavigationStarting`) sang origin khác; chỉ cửa sổ mới bị kiểm soát.
- Ngôn ngữ cố định `en-US` và không truyền `region`: nếu user muốn tiêu đề hoặc NFO bằng tiếng Việt thì cần một setting mới. Đó là tính năng mới, nằm ngoài phạm vi audit.

## 6. Test còn thiếu

Các test hiện có trong phạm vi chỉ phủ phần thuần (`JellyfinRetryBackoff`, `TmdbBlockAdvisor`, `TmdbDnsProbe`, `TmdbNetworkBlockDetector`, `TmdbWarpRecovery`, `WarpModePolicy`). **Không có test** cho `TmdbMetadataProvider`, `WarpCliService`, `JellyfinLibraryRefreshService`, `JellyfinClient`, `NfoWriterService`, `AppendOnlyLogTailer`, `WarpLogStatusWatcher`, `MediaMetadataSyncService`, cũng như ngữ nghĩa refresh của `TrackedShowService`. Nhánh package-1/2 có thêm `TmdbMetadataProviderRequestTests`, nhưng bộ test đó chỉ kiểm tra header và host.

| # | Test | Finding |
|---|---|---|
| 1 | `TrackedShowService.RefreshShowAsync` với dữ liệu TMDB giả: một tập biến mất, một tập đổi số, một tập bị lùi ngày phát sang tương lai → DB không còn tập ma `Missing` có thể săn | INT-01 |
| 2 | `WarpLogStatusWatcher.LooksLikeStatusLine` không khớp dòng `actor_ipc … Ipc response …; Status: Connected` | INT-02 |
| 3 | `GetTvShowDetailsAsync` khi season trả 503 hoặc 429 → ném lỗi; khi trả 404 → bỏ qua season | INT-03 |
| 4 | Provider gặp 404 ở `tv/{id}` → exception đúng loại; Auto-Track ghi trạng thái và không thử lại mỗi tick | INT-04 |
| 5 | Cache hết hạn: lần match thứ hai sau TTL phải gọi lại HTTP | INT-05 |
| 6 | `GetAsyncWithRetryAsync` với 429 có `Retry-After: 2` → chờ đủ thời gian (dùng delay giả) | INT-06 |
| 7 | Auto-Track cycle: lease `TmdbSslRecover` được release **trước** khi hunt bắt đầu | INT-07 |
| 8 | `WarpCliService` với CLI giả: (a) marker còn và lease tới trước → release vẫn khôi phục mode; (b) tunnel của user bật giữa chừng không bị ngắt; (c) `ParseConnectedStatus` với output thật của các phiên bản `warp-cli` | INT-08, §5 |
| 9 | `NfoWriterService.CleanupOrphanEpisodeNfos` không xóa `season.nfo`; ghi NFO atomic, không ghi khi nội dung giống | INT-09, INT-10 |
| 10 | `JellyfinLibraryRefreshService.EnqueuePaths` khi đang backoff không đặt lại về 3 s | INT-11 |
| 11 | `GetTvShowDetailsByEpisodeGroupAsync` với group "Season 0", "Specials & OVAs" và `order` bắt đầu từ 1 | INT-12 |
| 12 | `AppendOnlyLogTailer`: truncate, rotate, ký tự UTF-8 bị cắt giữa hai buffer | INT-16 |

## 7. Thứ tự sửa đề xuất

1. **Merge `fix/network-package-1`** (đã chạy trên bản cài, user đã OK ngày 11-08). Nó đóng NET-001/002/003/006/012/013 và giảm rủi ro cho mọi thay đổi bên dưới. Trước khi merge, cần chạy các bước kiểm tra tay còn mở của package-1.
2. **INT-03 rồi INT-01** (High): sửa INT-03 trước để một refresh "thành công" thực sự đầy đủ, sau đó mới thêm bước reconcile xóa hoặc đánh dấu tập ma. Đây là lỗi duy nhất làm Auto-Track hành động sai một cách lặng lẽ.
3. **INT-07**: trả lease TMDB trước hunt. Thay đổi một dòng, ảnh hưởng trực tiếp tới việc tôn trọng mode của user.
4. **INT-02**: sửa đường dẫn **cùng lúc** với bộ lọc dòng và giới hạn tần suất. Không được sửa đường dẫn riêng lẻ.
5. **INT-04, INT-05**: phân loại 404 và thêm TTL cho cache, vì cả hai cùng nằm trong `TmdbMetadataProvider`.
6. **INT-08, INT-11, INT-06**: các sửa nhỏ trong WARP, Jellyfin và retry.
7. **INT-09, INT-10, INT-12, INT-16**: NFO, episode group, đường dẫn log.
8. **INT-13, INT-14, INT-15**: dọn code chết, giới hạn truy vấn, giảm nhiễu log.

Các sửa ở bước 2 và 5 đụng vào `TmdbMetadataProvider`, file đã bị sửa 14 lần. Nên làm trên nhánh đã merge package-1 để tránh conflict với các thay đổi `CreateGetRequest`/host table ở đó.
