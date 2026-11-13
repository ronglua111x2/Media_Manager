# Audit Auto-Track và scheduler (2026-11)

- Baseline: `ff2caa3` (nhánh `claude/autotrack-audit-6484b8`, cây sạch khi bắt đầu).
- Thang mức độ và chuẩn bằng chứng: [`../00-method-and-scope.md`](../00-method-and-scope.md).
  Finding cũ AUD-001..007: [`../03-findings-register.md`](../03-findings-register.md).
- Phạm vi (tính từ `media management app/media management app/`): `Services/AutoTrack*.cs`,
  `InterruptedOrderRecovery.cs`, `ShowWeeklyAirDay.cs`, `TrackedShowService.cs`, `TrackedMovieService.cs`,
  `AppTimeZone.cs`, phần orchestration của `FetchJobService.cs`, `ViewModels/AutoTrack*`, `Views/AutoTrackView*`,
  `Views/SetAutoTrackDialog*`, test `AutoTrack*` và `InterruptedOrderRecoveryTests`. Đọc thêm khi cần để
  lần theo đường gọi: `TorrentCartService.cs`, `TorrentReconciliationService.cs`, `DatabaseService.cs`
  (upsert tập), `PosterImageService.cs`.
- Không audit lại: search của hunt (đang viết lại trên `feat/native-search`), parse file torrent và map tập
  (báo cáo trên nhánh `claude/parser-audit-5f9a30`).
- Không sửa code app, không chạy Auto-Track thật, không ghi vào DB, qBittorrent hay Library của user.
  Harness chạy trên DB SQLite tạm trong `%TEMP%` rồi xoá.

## Tóm tắt

Logic anchor theo chu kỳ (sau fix #7) đúng và có test: anchor bị lỡ vẫn "đến hạn" qua ranh giới tuần,
máy ngủ hoặc tắt chỉ làm trễ tối đa một nhịp quét. Ba semaphore ngăn hai lần discovery, hai lần hunt hoặc hai
lần reconcile chồng nhau. Khôi phục order bị gián đoạn (#8) là hàm thuần, có test.

Rủi ro lớn nằm ở **những gì xảy ra sau khi hunt thất bại** và ở **trạng thái suy diễn bị ghi đè**:

1. **AT-01 (High):** tập `NoCandidates`/`Failed` không chặn hunt và không có backoff. Mỗi nhịp TMDB (mặc định
   30 phút) app tìm lại toàn bộ tập lỗi và xoá danh sách candidate đã hỏng, nên thử lại đúng các magnet chết.
   Hàng đợi hunt xếp theo tên show và chỉ lấy 3 show mỗi nhịp, nên 3 show đầu bảng chữ cái có tập không tìm
   được sẽ chiếm hết suất, các show khác không bao giờ được hunt. Đã tái hiện.
2. **AT-02 (Medium):** refresh TMDB ghi `Availability = Missing` cho mọi tập rồi mới tính lại. Nếu refresh
   dừng giữa chừng (huỷ lúc tắt app ở bước tải poster), tập đã có trong Library bị coi là thiếu và hunt tải lại.
   Đã tái hiện.
3. **AT-03/AT-04 (Medium):** tập TBA (`AirDate = null`) bị coi là đã phát sóng; ngày phát sóng TMDB bị đọc
   như ngày UTC+7, nên show Mỹ được hunt khoảng 12 giờ trước khi phát. Cả hai làm AT-01 nặng thêm.
4. Các nút thủ công (**Reset week**, **Run Now**) và đường khôi phục có những lỗ nhỏ hơn: order đang chờ
   validate bị loại khỏi khôi phục (AT-05), torrent khôi phục xong không được link cho tới lần add sau (AT-06),
   order kẹt `Approved` cả phiên sau khi cleanup thất bại (AT-07).

Test hiện có: 138/138 pass (33 thuộc Auto-Track/khôi phục). Không có test nào cho `AutoTrackService`
(1953 dòng), `AutoTrackSchedulerService` hay `TorrentCartService` ở mức hành vi.

## 1. Sơ đồ luồng

### 1.1 Scheduler

```text
App start
 └─ AutoTrackSchedulerService.Start()  ──Task.Run──►  RunAsync = WhenAll(
      ├─ RunReconcileLoopAsync                         ├─ RunTmdbDiscoveryLoopAsync
      │   once: HasPendingAutoTrackDownloadQueue()?    │   Delay 5 min (StartupDelay)
      │         → arm                                  │   loop:
      │   loop every 2 s:                              │     RunTmdbDiscoveryAsync(bypassAnchor:false)
      │     if !armed → continue                       │        [_discoveryLock, WaitAsync(0) → skip if busy]
      │     if no pending queue → disarm               │        ├─ per show (sorted by Title):
      │     RunBackgroundReconcileAsync                │        │    anchor check → refresh TMDB (daily cap)
      │        [_reconcileLock]                        │        │    → update TmdbState/LastRefresh
      │     delay ReconcileIntervalMinutes (10)        │        └─ RunTorrentHuntAsync(bypassSchedule:false)
      │                                                │             [_huntLock]
      │   arm sources: startup check,                  │     if TorrentsAdded>0 → arm reconcile
      │   discovery TorrentsAdded>0, Run Now VM        │     Delay TmdbCheckIntervalMinutes (30)
      )
Run Now (UI) ─► AutoTrackService.RunAsync() = RunTmdbDiscoveryAsync(bypassAnchor:true)
                (no CancellationToken; same _discoveryLock → "already running" if the loop is busy)
Reset week (UI) ─► ResetAutoTrackWeekSatisfaction + ReleaseAutoTrackHuntBlocks (no lock, no IsRunning check)
```

### 1.2 Một nhịp hunt

```text
RunTorrentHuntCoreAsync
 ├─ RecoverInterruptedOrdersOnceAsync (một lần mỗi phiên, xem 1.4)
 ├─ BuildHuntQueue: shows sorted by Title, có download folder
 │    FindPendingEpisodes: ≥ checkpoint, (AirDate null hoặc AirDate+4h ≤ now),
 │                         Missing, không TorrentHash, không order đang chặn
 │    nếu lịch tuần áp dụng: FilterToCycle(AirDate ≤ cycleAnchor)
 │    tối đa 5 tập/show
 ├─ batch = 3 show đầu (MaxShowsPerHuntCycle)
 ├─ preflight: WARP lease + qBittorrent WebUI (có thể restart qBittorrent)
 ├─ PrepareAutoTrackEpisodeOrder (tái dùng order Draft/NoCandidates/Failed, xoá candidate + FailedCandidateUrls)
 ├─ Searching → FetchEpisodeCandidatesAsync → CandidatesFound | NoCandidates | Failed
 ├─ AcceptSelectedCandidates → Approved
 └─ TryAddOrderWithCandidateRetriesAsync (lần lượt từng candidate)
      add gate: add (không pause, AUD-001) → PendingValidationHash lưu ngay → validate tối đa ~120 s
      → Downloading | Completed (+ link ngay) | Failed
      TorrentCleanupFailedException → dừng cả nhịp hunt
```

### 1.3 Trạng thái order Auto-Track

```text
            Prepare                 search ok              Accept                add ok
  (none) ─────────► Draft ─► Searching ─────► CandidatesFound ─────► Approved ─────────► Downloading ─► Completed
                     ▲          │ no rows / no match                     │ all candidates fail      (reconcile / link)
                     │          ▼                                         ▼
                     ├──── NoCandidates ◄───────── (nhịp sau Prepare lại, không backoff) ──── Failed
                     │                                                                         ▲
   khôi phục: Searching→Draft; Approved→CandidatesFound | Draft | re-validate     reconcile: hash mất khỏi qBittorrent
   Reset week: Draft/Searching/CandidatesFound/NoCandidates/Approved/Failed → Failed (giữ PendingValidationHash)

  Chặn hunt (TryGetAutoTrackHuntBlockingEpisodeOrder): Searching, CandidatesFound*, Approved, AddedToClient, Downloading
  (*CandidatesFound của Auto-Track được "resume" chứ không chặn). NoCandidates/Failed: KHÔNG chặn.
```

### 1.4 Trạng thái TMDB của show và vòng đời một tuần

```text
AutoTrackTmdbState: Active ─(refresh, hết tập pending, show Ongoing)─► DormantCaughtUp
                    Active ─(refresh, show Finished và đủ tập)───────► FinishedComplete
                    DormantCaughtUp ─(qua anchor mới, chưa refresh)──► Active
  Ghi chú: trạng thái gần như chỉ để hiển thị. Lọc refresh thật sự dựa vào HasSatisfiedCurrentCycle:
  "đã refresh sau cycle anchor gần nhất". FinishedComplete vẫn refresh mỗi tuần (cố ý, để bắt special).

Cycle anchor = lần (AnchorDay, AnchorTime) gần nhất ≤ now (DateTime.Now của máy).
Show đến hạn refresh khi LastTmdbRefreshLocal < cycleAnchor, hoặc khi lịch tuần tắt (luôn đến hạn, chỉ còn
daily cap chặn). Run Now bỏ qua anchor và cap, nhưng có ghi LastTmdbRefreshLocal, nên làm thỏa chu kỳ
hiện tại (đúng ý fix "run now affect custom schedule").
```

## 2. Trả lời các câu hỏi của đề bài

### 2.1 Vòng đời một tuần

| Tình huống | Hành vi hiện tại | Đánh giá |
|---|---|---|
| Anchor, rollover | `GetCurrentCycleAnchor` lấy anchor gần nhất ≤ now; refresh sau anchor làm thỏa chu kỳ tới anchor kế. `WeekKey` (tuần bắt đầu Chủ nhật) chỉ còn để hiển thị và log. | Đúng, có test (`AutoTrackAnchorCycleTests`). |
| Reset week | Xoá `LastTmdbRefreshLocal` và `WeekKey`, đánh `Failed` mọi order Auto-Track chưa tải của show. | Lỗ ở order `Approved` (AT-05). Không kiểm tra hunt đang chạy. |
| Tập "mới" | Tập ≥ checkpoint, `Missing`, không hash, không order chặn, đã phát (`AirDate + HuntMinHoursAfterAirDate ≤ now`) và, khi có lịch tuần, `AirDate ≤ cycleAnchor`. | Tập TBA lọt qua (AT-03); giờ phát bị tính theo nửa đêm UTC+7 (AT-04). |
| Tập ra trễ | Không có tìm thấy → `NoCandidates` → tìm lại mỗi 30 phút mãi mãi. | Không backoff, gây đói hàng đợi (AT-01). |
| Tập ra sớm (TMDB đổi ngày) | Upsert ghi đè `AirDate` mỗi lần refresh, nên lần refresh sau sẽ thấy. Giữa hai lần refresh (lịch tuần: tới 7 ngày) app không biết. | Chấp nhận được; ghi chú. |
| Tập đôi | Mỗi tập TMDB là một order; nếu cả hai chọn cùng một torrent, order thứ hai dùng lại torrent đã có trong qBittorrent (`TryGetExistingByListingUrlAsync`). Map file thuộc phiên parser. | Không có finding riêng ở vùng này. |
| Show nghỉ giữa mùa | Hết tập pending → `DormantCaughtUp`; vẫn refresh một lần mỗi chu kỳ. Mùa mới thêm vào TMDB với `air_date` rỗng sẽ bị hunt ngay. | AT-03. |
| Show đã kết thúc | `FinishedComplete` vẫn refresh mỗi tuần. Tập không bao giờ tìm được thì vẫn hunt mãi. | Tốn budget, nhưng chủ yếu là AT-01. |
| Timezone, DST | `AppTimeZone` cố định UTC+7, nhưng `AutoTrackService`, `AutoTrackTmdbEligibility` và VM dùng `DateTime.Now` của máy; dashboard dùng `DateTime.UtcNow` cho "tuần này". Không có DST nếu máy để giờ Việt Nam. | Không nhất quán (AT-04, AT-12). |
| Lệch ngày TMDB với giờ địa phương | `air_date` là ngày dương lịch ở nước gốc. `ShowWeeklyAirDay` và `FindPendingEpisodes` coi nó là 00:00 UTC+7. | AT-04, đã tái hiện. |

### 2.2 Scheduler

- **Chạy chồng:** `WaitAsync(0)` trên ba semaphore riêng. Discovery chiếm `_discoveryLock` suốt cả nhịp hunt
  bên trong. Run Now trong lúc nhịp lịch đang chạy trả về "already running" với `Succeeded = false`, bị lưu
  thành `LastRunSummary` và hiện như lỗi (AT-09). Hunt và reconcile có thể chạy cùng lúc; chúng chạm tới tập
  hợp order khác nhau (`Approved` và `Downloading`), trừ khi người dùng nhấn Reset week (AT-05).
- **Nhiều show cùng lúc:** xử lý tuần tự trong một nhịp; tối đa 3 show/nhịp, 5 tập/show. Thứ tự theo tên,
  không xoay vòng (AT-01, AT-08).
- **Lịch tùy chỉnh:** đổi ngày/giờ lưu ngay vào DB; anchor mới áp dụng ở nhịp sau, không cần reset.
  Đúng.
- **Máy ngủ, tắt rồi bật:** không có "chạy bù" riêng. Cơ chế anchor-cycle tự xử lý: anchor bị lỡ vẫn đến
  hạn cho tới khi refresh thành công. Sau khi bật app, discovery đầu tiên chạy sau 5 phút. Khi máy ngủ,
  `Task.Delay` không đếm thời gian ngủ, nên nhịp kế tiếp trễ tối đa một interval sau khi máy thức dậy (log:
  19-27 phút, mục 3.4). Không có hook resume.
- **Cancel giữa chừng:** chỉ có token tắt app. Search bị huỷ được rollback về `Draft`. Add bị huỷ để lại
  `Approved` cho lần khởi động sau xử lý. Run Now không nhận token nào nên không huỷ được và không dừng khi
  tắt app (scheduler chỉ chờ 8 giây cho vòng lặp của chính nó).

### 2.3 Trạng thái và khôi phục

- **Order bị gián đoạn:** chỉ order `Searching`/`Approved` của **phiên trước** được khôi phục
  (`UpdatedUtc < sessionStart`). Order kẹt trong phiên hiện tại chỉ được gỡ khi khởi động lại (AT-07).
  Torrent khôi phục xong không kích hoạt reconcile (AT-06).
- **DB lệch với qBittorrent:** reconcile coi hash vắng mặt trong `torrents/info` là đã bị xoá ngay lần đầu,
  không cần xác nhận lần hai (ghi chú R-2). Đổi episode organization xoá và tạo lại toàn bộ tập, làm mất
  liên kết với torrent đang tải (AT-10).
- **DB lệch với Library:** `Availability` được lưu nhưng là trạng thái suy diễn, bị reset ở mỗi refresh
  (AT-02).
- **Retry vô hạn hay bỏ cuộc im lặng:** cả hai đều có. Hunt thất bại thì retry vô hạn, không backoff
  (AT-01). Episode có hash nhưng không bao giờ link được thì reconcile mỗi 10 phút mãi mãi (R-3). Ngược
  lại, show đứng sau trong bảng chữ cái thì im lặng không được hunt (AT-01) hoặc không được refresh (AT-08).

### 2.4 Phụ thuộc bên ngoài

- **TMDB lỗi hoặc chậm:** mỗi show một try/catch; lỗi tăng `Failed` và không cập nhật `LastTmdbRefreshLocal`,
  nên show vẫn đến hạn ở nhịp sau. Bị chặn mạng thì thử WARP một lần mỗi nhịp; WARP không kết nối được thì
  ngừng thử trong nhịp đó (`_warpRecoverFailedThisCycle`). Không đếm budget cho refresh thất bại. Ổn.
- **TMDB thiếu dữ liệu:** `air_date` rỗng bị coi như đã phát (AT-03). Tập bị xoá hoặc đánh số lại trên TMDB
  không bao giờ bị xoá khỏi DB (`ImportShow` chỉ upsert), nên tập "ma" có thể bị hunt mãi (gộp vào AT-01).
- **qBittorrent offline:** preflight thử restart process; nếu vẫn hỏng thì huỷ cả nhịp hunt, kèm thông báo
  `AutoTrackHuntBlocked` mỗi 30 phút. Reconcile bỏ qua và thử lại sau 10 phút. Khôi phục order `Approved`
  được hoãn cho tới khi WebUI lên. Ổn, chỉ hơi ồn.

## 3. Lịch sử bug và hotspot

Nguồn: `git log --follow` trên các path trong phạm vi tại `ff2caa3`, `git show` các commit fix, và log của
app chép từ `<StateFolder>\logs` (99 file, 2026-10-29 → 11-12; log cũ hơn đã bị dọn theo retention
nên các sự cố trước #7 không còn). DB thật `<StateFolder>\media-manager.db` không được mở.

**Lưu ý baseline:** từ 2026-11-08 app thật chạy `1.0.7-alpha.0.8+65038d9`, build từ nhánh chưa merge
`fix/network-package-1`. So với `main`, nhánh này chỉ đổi ba chỗ trong phạm vi:

- breaker WARP sau 2 show (`bd6c791`);
- hunt bị huỷ khi WARP tunnel không bật (`c39aa39`);
- client HTTP và host cho poster (`d2251b2`).

Không thay đổi nào chạm tới các dòng có finding ở mục 4, nên mọi finding áp dụng cho cả hai.

### 3.1 Hotspot

| Commit | File | Hàm bị sửa nhiều nhất |
|--:|---|---|
| 41 | `DatabaseService.cs` (toàn file) | — |
| 29 | `AutoTrackService.cs` | `RunFetchAndAddPhaseAsync` 15, `RunTmdbDiscoveryCoreAsync` 11, `RunTorrentHuntCoreAsync` 8 |
| 25 | `FetchJobService.cs` | (search, ngoài phạm vi) |
| 23 | `TrackedShowService.cs` | `ImportShow` 11 |
| 11 / 9 | `AutoTrackViewModel.cs` / `AutoTrackShowCardViewModel.cs` | — |
| 7 | `AutoTrackSchedulerService.cs`, `AutoTrackTmdbEligibility.cs`, `AutoTrackSettings.cs` | `RunTmdbDiscoverySafelyAsync` 3, `ShouldRefreshTmdb` 3 |

Chỉ 8/29 commit của `AutoTrackService.cs` có nhãn fix. Nhiều thay đổi hành vi lớn đi kèm commit "feature" không
có test:

- `a65d0ee` (phase 2, +542/−152);
- `a1dda96` bỏ hẳn vòng hunt định kỳ, nên `TorrentHuntIntervalMinutes` vẫn hiện trong Settings nhưng không còn
  tác dụng;
- `9efbf03` "add malware protection" thêm nút Reset week;
- `dac3a54` thêm retry candidate trong cùng nhịp.

Project `MediaManager.App.Tests` chỉ có từ 2026-09-16 (`46f8dda`). Mọi fix Auto-Track trước 2026-10-26 không
có test. Test thêm từ #7 và #8 chỉ phủ hàm thuần.

### 3.2 Các commit fix

| Commit | Ngày | Chủ đề | Gốc hay triệu chứng | Test |
|---|---|---|---|---|
| `89eebc8` | 07-17 | fix reset eps | Triệu chứng: ghi 0 thay vì NULL vào cột NOT NULL; phần xoá mọi SourceItem của tập trong link service là sửa thật | không |
| `9d178a9` | 07-18 | fix tmdb refresh | Lẫn: `TmdbDailyBudget` tốt hơn, nhưng field cũ còn trong settings | không |
| `a7b338b` | 07-31 | fix run now affect custom schedule | Lẫn: đúng hướng (so mốc thời gian với anchor), nhưng giữ week key và state song song; 4 nhánh `if (pastAnchor)` (bị #7 gỡ) | không |
| `f4ad232` | 07-31 | update auto track service | Chỉ thêm log `Anchor check`, Info cho mọi show mỗi 30 phút: 1 788 dòng trong 2 tuần | không |
| `a1f34df` | 09-19 | fix show finished not refresh tmdb | **Triệu chứng**: xoá luôn điều kiện bỏ qua show Finished thay vì định nghĩa khi nào cần kiểm lại. `FinishedComplete` từ đó chỉ được ghi, không được đọc (AT-11) | không |
| `46f8dda` | 09-16 | silent failed qBittorrent deletion (AUD-002) | Gốc: lỗi được nêu rõ và hunt dừng. Mở ra AT-07 | có |
| `dbc95c8` #7 | 10-26 | rolling anchor cycle + WARP recovery | **Gốc** cho anchor (thay bất biến tuần lịch bằng chu kỳ trượt). Phần WARP là thêm một lớp retry | có (hàm thuần) |
| `779bee7` #9 | 10-26 | Jellyfin WARP pre-probe + backoff | Gốc cho vòng Jellyfin (có backoff). Phần Auto-Track chỉ refactor | có |
| `92737a7` #8 | 10-26 | recover interrupted orders | Lẫn: lưu `PendingValidationHash` là sửa gốc. Khôi phục chỉ quét một lần lúc khởi động, theo `UpdatedUtc < sessionStart`, nên không phủ lỗi trong phiên (AT-07) và không arm reconcile (AT-06). Commit tự ghi "chưa kiểm bằng crash thật" | có (hàm thuần) |
| `d16a334` #10 | 11-03 | warp+doh cho TMDB | Triệu chứng của chặn mạng, thêm state (đổi mode, crash marker) | có |
| `c39aa39`, `d2251b2`, `bd6c791` | 11-08 | (chưa merge) deadline warp-cli, `api.tmdb.org`, breaker | `d2251b2` né host bị chặn, tức là xử lý gốc của cả chuỗi WARP | có |

Không có commit revert nào.

### 3.3 Chuỗi fix chồng nhau

- **Chuỗi A, điều kiện refresh TMDB:** `a65d0ee` → `9d178a9` → `a7b338b` → `f4ad232` → `18a7f49` (công tắc
  lịch tuần) → `a1f34df` → `dbc95c8`. `ShouldRefreshTmdb` đi từ 5 điều kiện xuống còn 1
  (`HasSatisfiedCurrentCycle`). Phần còn sót: `AutoTrackTmdbState` và `AutoTrackLastTmdbWeekKey` vẫn được ghi
  nhưng không quyết định gì nữa (AT-11). Công tắc `EnforceGlobalWeeklySchedule=false` của `18a7f49` mở đường
  cho AT-08.
- **Chuỗi B, TMDB bị chặn và WARP:** bảy lớp trong khoảng 4 tuần (`b144a97` → #7 → #9 → #10 → `c39aa39` →
  `d2251b2` → `bd6c791`). Retry lồng nhau: 3 lần trong `TmdbMetadataProvider` × 3 lần trong
  `TmdbWarpRecovery`, cộng một lần nối WARP, cho mỗi show mỗi 30 phút. Trên `main`, cờ dừng
  `_warpRecoverFailedThisCycle` chỉ bật khi WARP *không nối được*, không bật khi nối được mà TMDB vẫn chặn
  (log 10-31 bên dưới). Breaker `bd6c791` sửa điều này, nhưng chưa vào `main`.
- **Chuỗi C, order kẹt:**
  1. `0b87346`: lưu candidate trên order;
  2. `dac3a54`: retry candidate và **xoá `FailedCandidateUrls` khi Prepare lại** (gốc của một nửa AT-01);
  3. `9efbf03`: Reset week (`ReleaseAutoTrackHuntBlocks`, gốc AT-05);
  4. `46f8dda`: halt khi cleanup thất bại;
  5. `92737a7`: khôi phục lúc khởi động.

  Mỗi bước thêm một lối thoát thủ công hoặc theo phiên thay vì có một chủ sở hữu trạng thái duy nhất cho
  order. Reset week vẫn là cách duy nhất gỡ order kẹt trong phiên hiện tại.

Nguồn gốc các dòng có finding (`git log -S`):

- `Availability = excluded.Availability` có từ `1419e7c` (2026-05-24, trước cả Auto-Track);
- `episode.AirDate is null ||` có từ `a65d0ee` (06-15);
- `existing.FailedCandidateUrls = string.Empty` có từ `dac3a54` (08-20);
- `ReleaseAutoTrackHuntBlocks` có từ `9efbf03` (08-20).

### 3.4 Sự cố trong log (2026-10-29 → 11-12)

Tổng quan: 33 phiên app, 447 nhịp discovery (438 idle), **4 tập được hunt, cả 4 ADDED ở candidate đầu tiên**,
2 nhịp discovery lỗi. Không có dòng "already running", "Recovered N/M", "Daily cap reached", "Poster cache
failed" hay `NoCandidates`. Live settings: anchor Chủ nhật 18:00, lịch tuần bật, cap 10/ngày, reconcile 5 phút,
4 show đang track (đều là anime, có anchor riêng Thứ bảy 06:00 và Thứ ba 08:30).

1. **10-31, TMDB bị chặn SNI, WARP vô ích** (build `32c8a1b`, trước #10). 09:59:56 TMDB lỗi → WARP nối →
   3 lần "still unreachable with WARP (SSL/TLS)" → refresh thất bại 10:00:22. Lặp y hệt lúc 10:30, vì cờ dừng
   không bật khi WARP nối được. Chỉ hết khi build mới chuyển sang `warp+doh` (10:55). S01E04 được add 10:57,
   khoảng 5 giờ sau anchor Thứ bảy 06:00 (app tắt tới 09:54).
2. **Trễ sau khi máy ngủ.** `Task.Delay` không đếm thời gian máy ngủ và không có hook khi máy thức dậy.
   Khoảng từ lúc thức dậy tới nhịp discovery kế tiếp:
   - 10-31: 27 phút;
   - 11-01: 21 phút;
   - 11-08: 19 phút 41 giây. Anchor Chủ nhật 18:00 rơi vào lúc máy ngủ nên refresh lúc 19:29.

   Chu kỳ trượt (#7) đảm bảo vẫn bắt kịp, chỉ trễ tối đa một nhịp. Không phải lỗi, nhưng có thể cải thiện bằng
   `SystemEvents.PowerModeChanged`.
3. **Bắt kịp sau khi tắt app hoạt động đúng sau #7.** 11-07 khởi động 12:14 (lỡ anchor Thứ bảy 06:00), discovery
   12:19, WARP cứu TMDB, S01E05 ADDED 12:22. 11-03 và 11-10: tập của show anchor Thứ ba 08:30 được add 8-13 phút
   sau anchor và link ở lượt reconcile kế tiếp.
4. **Run Now làm thỏa chu kỳ** (11-08 22:28): đúng thiết kế sau #7; show được reset đúng ở anchor sau.
5. **Ồn log:** 1 788 dòng `Anchor check` (khoảng 41% log của `AutoTrackService`) và 440 dòng "WARP lease
   release ignored; not held" (do `ReleaseAsync` luôn chạy trong `finally`, `AutoTrackService.cs:366-369`).

Vì log chỉ có 4 lần hunt và đều thành công ngay, các finding AT-01..AT-08 **không quan sát được trong log**.
Chúng được chứng minh bằng code và harness, không phải bằng sự cố đã xảy ra. AT-01 sẽ lộ ra khi có tập không
tìm được (ví dụ khi TMDB công bố mùa mới có tập TBA, AT-03).

### 3.5 Phát hiện ngoài phạm vi (cần phiên riêng)

- **Test từng chạy cả app trên state folder thật.** 2026-11-02 17:38-17:44, 9 phiên có
  `Exe: ...\MediaManager.App.Tests\bin\x64\Debug\...\testhost.exe` đã log `State folder ... '<StateFolder>'`,
  `Initializing SQLite database at <StateFolder>\media-manager.db`, `PublishJunkCleanup deleted 7 file(s)`,
  `Auto-track delayed 5 minutes after startup`, rồi bị kill, để lại 9 crash report `UncleanShutdown`. Chỉ có
  startup delay 5 phút ngăn Auto-Track chạy trên dữ liệu thật. Lần chạy `dotnet test` của audit này (bản Debug,
  2026-11-12 khoảng 18:56) **không** tái diễn: không có file log mới sau 16:56, DB thật sửa lần cuối 11-10
  08:46. Nguyên nhân 11-02 chưa xác định (chưa tìm thấy test nào khởi tạo `App`); nên audit riêng việc cô lập
  test khỏi state folder.
- **Build cũ chạy trên DB mới.** 2026-11-02 18:41 build `1.0.2-alpha+d9f7fd6` chạy khoảng 10 phút trên DB đã
  có migration 008. Không có lỗi, nhưng không có chặn downgrade.

## 4. Finding

Mức độ theo `00-method-and-scope.md`. "Tái hiện" nghĩa là chạy harness trong scratchpad trên DB tạm
(xem mục 6). "Tĩnh" nghĩa là chứng minh bằng đường gọi trong code hiện tại.

| ID | Mức độ | Finding | Tin cậy | Tái hiện |
|---|---|---|---|---|
| AT-01 | High | Hunt thất bại retry vô hạn không backoff, và làm đói các show khác | Cao | Có (S4, S6) |
| AT-02 | Medium | Refresh TMDB reset `Availability`; refresh dở dang làm hunt tải lại tập đã có | Cao | Có (S3) |
| AT-03 | Medium | Tập TBA (`AirDate = null`) bị coi là đã phát | Cao | Có (S1) |
| AT-04 | Medium | Ngày phát TMDB bị đọc như ngày UTC+7: hunt sớm khoảng 12 giờ, suy ngày phát sai | Cao | Có (S2) |
| AT-05 | Medium | Reset week biến order `Approved` thành `Failed`, torrent chưa validate bị bỏ rơi | Cao | Có (S5) |
| AT-06 | Medium | Torrent được khôi phục sau restart không kích hoạt reconcile | Cao | Tĩnh |
| AT-07 | Medium | Cleanup thất bại để các order còn lại kẹt `Approved` tới khi restart | Cao | Tĩnh + S9 |
| AT-08 | Medium | Daily cap TMDB làm đói show cuối bảng chữ cái khi tắt lịch tuần | Cao | Có (S7) |
| AT-09 | Low | Run Now bị skip được ghi nhận như lỗi; Run Now không huỷ được | Cao | Có (S8) |
| AT-10 | Medium | Đổi episode organization xoá tập đang tải, gây tải trùng và torrent không bao giờ được link | Trung bình | Tĩnh |
| AT-11 | Low | State machine TMDB ghi nhưng không dùng; thông báo "New episode" lặp | Cao | Tĩnh |
| AT-12 | Low | Dashboard đếm "pending" theo luật khác với hunt thật | Cao | Tĩnh |
| AT-13 | Low | State dùng chung trong `FetchJobService` singleton giữa hunt và cart thủ công | Trung bình | Tĩnh |

### AT-01 — Hunt thất bại retry vô hạn không backoff, và làm đói các show khác (High)

Code liên quan:

- `Services/TorrentCartService.cs:178-188`: trạng thái chặn hunt là `Searching`, `CandidatesFound`,
  `Approved`, `AddedToClient`, `Downloading`. `NoCandidates` và `Failed` không chặn.
- `Services/TorrentCartService.cs:201-218`: `PrepareAutoTrackEpisodeOrder` tái dùng order
  `Draft`/`NoCandidates`/`Failed`, xoá candidate và `FailedCandidateUrls = string.Empty`.
- `Services/AutoTrackService.cs:787-850` (`BuildHuntQueue`): show xếp theo `Title`; `:488-489` chỉ lấy
  `MaxShowsPerHuntCycle` (mặc định 3).
- `Services/AutoTrackService.cs:956-984` (`NoCandidates`), `:1081-1092` (hết candidate → `Failed`).
- `Services/AutoTrackSchedulerService.cs:129-141`: hunt chạy ở cuối mỗi nhịp discovery (mặc định 30 phút).

Bằng chứng: harness S4 cho thấy order `Failed` không chặn, và lần `Prepare` sau xoá `FailedCandidateUrls`.
Harness S6 tạo 4 show, 3 show đầu có tập `NoCandidates`: batch của nhịp gồm đúng 3 show đó, show thứ tư không
được hunt (`'Show 9103' hunted this cycle: False`).

Kịch bản lỗi:

1. Show "A Show" có một tập không bao giờ có torrent (tập TBA, tập TMDB đánh số khác tracker, recipe quá chặt).
   Tương tự với "B Show" và "C Show".
2. Mỗi 30 phút app tìm lại 3 tập đó, thất bại, và lại xếp 3 show này vào batch.
3. "D Show" có tập mới hôm nay. `BuildHuntQueue` có thấy, nhưng batch chỉ lấy 3 show đầu, nên "D Show" không
   bao giờ được hunt cho tới khi người dùng can thiệp.
4. Biến thể: tập chỉ có candidate magnet chết. Mỗi nhịp, mỗi candidate có thể giữ add gate tới khoảng 120 giây
   chờ metadata, và `_discoveryLock` bị giữ suốt thời gian đó.

Chưa quan sát trong log (2 tuần chỉ có 4 lần hunt, đều thành công ngay, mục 3.4). Một nửa của lỗi được thêm
bởi `dac3a54`.

Tác động: workflow cốt lõi chạy không người trông (tải tập mới) hỏng hẳn với các show bị đói. Không có thông
báo nào nói show bị bỏ qua. UI chỉ hiện "Last hunt failed" cho show *đang* lỗi. Thêm vào đó là tải nặng lặp lại
lên indexer và WARP. Không mất dữ liệu, có cách vòng (Stop tracking hoặc đổi checkpoint), nên xếp High chứ
không Critical.

Hướng sửa:

- Lưu `AttemptCount` và `NextAttemptUtc` trên order (hoặc tập). `NoCandidates`/`Failed` chặn hunt cho tới
  `NextAttemptUtc`, với backoff tăng dần (30 phút → 2 giờ → 6 giờ → 24 giờ, có trần).
- Không xoá `FailedCandidateUrls` khi `Prepare` lại một order `Failed`. Chỉ xoá khi kết quả search thay đổi
  hoặc khi người dùng chủ động reset.
- Xếp hàng đợi theo "lâu nhất chưa được thử" (`LastHuntAttemptUtc`) thay vì theo tên, hoặc xoay vòng con trỏ
  giữa các nhịp.

### AT-02 — Refresh TMDB reset `Availability`; refresh dở dang làm hunt tải lại tập đã có (Medium)

Code liên quan:

- `Services/DatabaseService.cs:1277-1281`: `ON CONFLICT ... DO UPDATE SET ... Availability = excluded.Availability`.
  `TrackedEpisode.Availability` mặc định là `Missing` (`MediaManager.Core/Models/TrackedEpisode.cs:31`).
- `Services/TrackedShowService.cs:68-83` (`RefreshShowAsync`): `ImportShow` (upsert mọi tập) → `await
  CachePosterAsync` → `RefreshAvailability(showId)`.
- `Services/PosterImageService.cs:372-375`: `OperationCanceledException` được ném lại. Các lỗi khác bị nuốt.
- `Services/AutoTrackTmdbEligibility.cs:129-137`: hunt chỉ dựa vào `Availability == Missing` và
  `TorrentHash` rỗng.

Bằng chứng: harness S3. Trước refresh: `S01E01=Available`. Bước poster ném `OperationCanceledException`. Sau
refresh: `S01E01=Missing`, và `FindPendingEpisodes` trả về `S01E01,S01E02`.

Kịch bản lỗi:

1. Poster của show đổi trên TMDB (cache không còn hợp lệ). Discovery refresh show: `ImportShow` đã đặt mọi
   tập về `Missing`, giờ đang tải poster qua `image.tmdb.org` (có thể treo tới timeout 100 giây nếu bị chặn
   SNI như #10).
2. Người dùng tắt app, `_shutdown.Cancel()` huỷ request. `RefreshAvailability` không chạy.
3. Lần khởi động sau, TMDB bị chặn hoặc daily cap đã hết, nên show không được refresh lại (refresh là thứ duy
   nhất tính lại availability của show này một cách tự động).
4. Hunt chạy: các tập ≥ checkpoint đã ở Library mà `TorrentHash` rỗng (nhập thủ công, hoặc torrent đã xoá
   khỏi qBittorrent sau khi seed nên `MarkTorrentRemoved` xoá hash) bị tải lại.

Thêm vào đó, kể cả khi không lỗi, giữa `ImportShow` và `RefreshAvailability` có một khoảng mà reconcile chạy
song song đọc được availability sai. `availableBefore` bị thiếu, nên sau đó bắn thông báo "Hardlinked" giả
cho tập đã có từ trước.

Hướng sửa: bỏ `Availability` khỏi danh sách `DO UPDATE SET` (giữ `TrackedEpisodes.Availability` như các cột
torrent). Chỉ `RefreshAvailabilityCore` được ghi cột này. Tuỳ chọn thêm: gói `ImportShow` và
`RefreshAvailability` trong một transaction.

### AT-03 — Tập TBA (`AirDate = null`) bị coi là đã phát (Medium)

Code: `Services/AutoTrackTmdbEligibility.cs:131` (`episode.AirDate is null || ...`) và `:72` (`FilterToCycle`
cũng cho `null` qua). Dashboard đếm tương tự ở `ViewModels/AutoTrackViewModel.cs:317`.

Bằng chứng: harness S1. Checkpoint S02E01, hai tập S02 chưa có ngày: `pending=2`, vẫn còn 2 sau lọc chu kỳ.

Kịch bản: TMDB thêm mùa mới với danh sách tập nhưng chưa có `air_date` (thường thấy khi mùa được công bố). Mọi
tập đó vào hàng đợi ngay, tìm ra `NoCandidates`, lặp lại mỗi 30 phút trong nhiều tuần hoặc nhiều tháng, chiếm
suất 5 tập/show và 3 show/nhịp (AT-01). Nguy cơ phụ: torrent giả đặt tên "S02E01" được tải về trước khi tập
tồn tại (add gate có validate nội dung, nhưng theo AUD-001 thì tải trước rồi mới validate).

Hướng sửa: tập không có `AirDate` thì không hunt tự động. Nếu muốn giữ khả năng bắt tập không có ngày, chỉ
cho phép khi tập *sau* nó đã có ngày phát trong quá khứ.

### AT-04 — Ngày phát TMDB bị đọc như ngày UTC+7 (Medium)

Code:

- `Services/AutoTrackTmdbEligibility.cs:126-131`: `AirDate.AddHours(huntDelayHours) <= now`, với `AirDate` là
  00:00 của ngày TMDB.
- `Services/ShowWeeklyAirDay.cs:67-74`: comment nói "treat as UTC+7 local midnight", nhưng code chỉ đổi
  `Kind`. Ngày trong tuần lấy thẳng từ ngày ở nước gốc.
- Đồng hồ không nhất quán: `AppTimeZone.Now` (UTC+7 cố định) và `DateTime.Now` (`AutoTrackService.cs:145`,
  `:795`, `AutoTrackTmdbEligibility.cs:126`) cùng tồn tại; dashboard dùng `DateTime.UtcNow`
  (`AutoTrackViewModel.cs:148`).

Bằng chứng: harness S2 với settings mặc định (anchor Chủ nhật 21:00, `HuntMinHoursAfterAirDate = 4`) và một
tập Mỹ có `air_date` Chủ nhật 2026-11-15. Tập đủ điều kiện hunt lúc Chủ nhật 21:00 UTC+7, trong khi giờ phát
thật là 21:00 ET = **Thứ hai 09:00 UTC+7**. `ShowWeeklyAirDay` suy ra `Sunday`.

Với 4 show anime đang track, ảnh hưởng nhỏ. Anime đêm khuya (ví dụ 01:00 JST Chủ nhật) có `air_date` Chủ nhật,
trong khi ở UTC+7 vẫn là Thứ bảy 23:00, nên hunt *muộn* hơn chứ không sớm hơn. Lỗi nặng với show ở múi giờ âm
(Mỹ, châu Âu buổi tối).

Tác động: hunt mở sớm khoảng 12 giờ (sớm tới khoảng 28 giờ nếu tắt lịch tuần, vì delay 4 giờ tính từ nửa đêm
UTC+7). Hệ quả là nhiều lần search vô ích (nuôi AT-01) và cửa sổ cho torrent giả trước giờ phát. Nếu người
dùng đặt anchor theo ngày `ShowWeeklyAirDay` gợi ý, anchor sẽ sớm hơn ngày phát thật ở UTC+7 một ngày, nên
refresh của tuần đó có thể chưa thấy tập mới cho tới tuần sau.

Hướng sửa: dùng một đồng hồ duy nhất (`AppTimeZone.Now`) trong mọi đường Auto-Track. Coi `air_date` là ngày
ở `origin_country` của show (hoặc cho mỗi show một offset giờ phát, mặc định +1 ngày cho nước ở múi giờ âm).
`HuntMinHoursAfterAirDate` nên tính từ "giờ phát ước lượng", không phải từ nửa đêm.

### AT-05 — Reset week biến order `Approved` thành `Failed`, torrent chưa validate bị bỏ rơi (Medium)

Code:

- `Services/TorrentCartService.cs:533-573` (`ReleaseAutoTrackHuntBlocks`): gồm cả `Approved` và `Searching`.
  Đặt `Failed`, xoá candidate và torrent state, **giữ `PendingValidationHash`**.
- `Services/InterruptedOrderRecovery.cs:33-41`: chỉ khôi phục `Searching`/`Approved`.
- `Services/TorrentCartService.cs:201-218`: `Prepare` lại không xoá `PendingValidationHash`.
- `Services/TorrentReconciliationService.cs:74-75`: mọi `PendingValidationHash` (kể cả của order `Failed`) bị
  chặn adopt.
- `ViewModels/AutoTrackViewModel.cs:287-301`: không kiểm tra `IsRunning`.

Bằng chứng: harness S5. Order `Approved` với `PendingValidationHash='live-hash-in-qbt'` có `IsInterrupted=True`.
Sau Reset week: `Failed`, `IsInterrupted=False`, hash vẫn nằm trong danh sách chặn adopt. Lần hunt sau
`Prepare` lại thành `Draft` mà hash cũ vẫn còn.

Kịch bản lỗi:

1. App bị tắt cứng trong lúc add gate đang validate (đúng ca #8 muốn xử lý), hoặc qBittorrent offline lúc khởi
   động nên khôi phục bị hoãn.
2. Người dùng thấy show "kẹt" và nhấn Reset week.
3. Order thành `Failed`. Torrent trong qBittorrent vẫn tải (không pause, AUD-001), không bao giờ được validate,
   và không bao giờ được adopt vì hash bị chặn mãi.
4. Hunt sau đó add torrent mới cho cùng tập. `onTorrentAdded` ghi đè `PendingValidationHash`, nên dấu vết
   torrent cũ mất hẳn. Kết quả: torrent cũ mồ côi, chưa validate, và một bản tải trùng.

Biến thể: nhấn Reset week khi hunt đang chạy. Order `Searching`/`Approved` của nhịp hiện tại bị đổi thành
`Failed` dưới chân hunt, rồi hunt lại ghi `Downloading` hoặc `Approved` đè lên (last-writer-wins, không có
version).

Hướng sửa: Reset week bỏ qua order `Approved` có `PendingValidationHash` (hoặc chuyển sang luồng
re-validate), và bị disable khi `IAutoTrackService.IsRunning`. `Prepare` nên từ chối hoặc xử lý order còn
`PendingValidationHash` thay vì âm thầm giữ lại.

### AT-06 — Torrent được khôi phục sau restart không kích hoạt reconcile (Medium)

Code:

- `Services/AutoTrackSchedulerService.cs:151-155`: lúc khởi động chỉ arm reconcile nếu *đã có* hàng chờ tải.
  Kiểm tra này chạy ngay, trước khi khôi phục (khôi phục nằm trong hunt đầu tiên, sau 5 phút).
- `Services/AutoTrackSchedulerService.cs:304-310`: chỉ arm khi `result.TorrentsAdded > 0`.
- `Services/AutoTrackService.cs:1279-1347`, `:1385-1410`: khôi phục re-validate và gắn torrent (order thành
  `Downloading`) nhưng không tăng `TorrentsAdded` và không có tham chiếu tới scheduler.

Kịch bản: app chết lúc đang validate tập X. Khởi động lại: hàng chờ tải rỗng nên reconcile không arm. Sau 5
phút, khôi phục re-validate X thành `Downloading`. Reconcile vẫn không arm, nên X tải xong mà không được
hardlink. Nó chỉ được link khi có một lần add khác thành công, hoặc ở lần khởi động sau. Nếu tải xong ngay lúc
khôi phục (`IsComplete`) thì được link trực tiếp, nên lỗi chỉ xảy ra với torrent chưa tải xong.

Hướng sửa: `RecoverInterruptedOrdersOnceAsync` trả về số torrent đã gắn. Cộng số đó vào `TorrentsAdded`
(hoặc thêm trường `TorrentsRecovered`) để scheduler arm reconcile. Hoặc cho reconcile loop kiểm tra
`HasPendingAutoTrackDownloadQueue()` định kỳ thay vì chỉ dựa vào arm.

### AT-07 — Cleanup thất bại để các order còn lại kẹt `Approved` tới khi restart (Medium)

Code: `Services/AutoTrackService.cs:1108-1113` ném lại `TorrentCleanupFailedException`, thoát cả vòng add;
`:600-614` kết thúc nhịp. Các order đã `Approved` (do `AcceptSelectedCandidates` ở `:1022-1042`) nhưng chưa
tới lượt add vẫn `Approved`. `InterruptedOrderRecovery.IsInterrupted` loại order cập nhật trong phiên hiện tại
(`InterruptedOrderRecovery.cs:41`), và `_interruptedRecoveryDone` đã là `true`.

Bằng chứng: tĩnh. Harness S9 xác nhận một order kẹt trong phiên hiện tại không được coi là gián đoạn.

Tác động: halt sau AUD-002 là đúng về an toàn. Nhưng các tập khác trong cùng batch (có thể thuộc show khác)
bị chặn hunt cho tới khi restart app, mà không có thông báo riêng. Người dùng phải biết để nhấn Reset week, và
nút đó lại có AT-05.

Hướng sửa: khi halt, đưa các order `Approved` chưa add (chưa có `PendingValidationHash`) về `CandidatesFound`
ngay trong `catch`, giống `RollBackCanceledSearches`.

### AT-08 — Daily cap TMDB làm đói show cuối bảng chữ cái khi tắt lịch tuần (Medium)

Code: `Services/AutoTrackService.cs:193` (xếp theo `Title`), `:251-257` (`break` khi hết cap),
`AutoTrackTmdbEligibility.cs:50-53` (lịch tuần tắt thì show luôn đến hạn).

Bằng chứng: harness S7 với 25 show, cap 20, `EnforceGlobalWeeklySchedule = false`, 3 nhịp. Nhịp 1 refresh
20 show; nhịp 2 và 3 đều "Daily cap reached". `Show 9220..9224` không được refresh. Vì thứ tự giữ nguyên mỗi
ngày, 5 show này **không bao giờ** được refresh.

Khi bật lịch tuần (mặc định) và hơn 20 show dùng chung anchor, các show cuối bị trễ tới khi cap reset lúc nửa
đêm. Phiền nhưng chấp nhận được.

Hướng sửa: xếp theo `AutoTrackLastTmdbRefreshLocal` tăng dần (show lâu chưa refresh nhất đi trước). Khi lịch
tuần tắt, thêm khoảng tối thiểu giữa hai lần refresh của cùng một show (ví dụ 6 giờ) để cap không bị đốt
trong một hai nhịp.

### AT-09 — Run Now bị skip được ghi nhận như lỗi; Run Now không huỷ được (Low)

Code: `Services/AutoTrackService.cs:78-92`, `:1656-1663` (`SkippedResult` là `Succeeded = false`);
`AutoTrackSchedulerService.cs:317-326` (`!Succeeded` được coi là "meaningful"); `ViewModels/AutoTrackViewModel.cs:97-123`.

Bằng chứng: harness S8. Run Now khi discovery đang chạy trả về `Succeeded=False`, `'TMDB discovery is already
running.'`, và ghi đè `LastRunSummary`.

Thêm vào đó:

- `RunAsync()` không nhận `CancellationToken`, nên không có nút huỷ, và tắt app không dừng nó.
- Lệnh được await trên UI context mà không có `ConfigureAwait(false)`, nên các vòng gọi DB đồng bộ chạy trên
  UI thread.
- `RunCompleted` gọi `Dispatcher.Invoke` (đồng bộ) từ thread của scheduler. Nếu `Dispose` chạy trên UI thread
  và đợi worker, hai bên chờ nhau tới hết timeout 8 giây khi tắt app.

Hướng sửa: tách "skipped" khỏi "failed" trong `AutoTrackRunResult`. Không persist kết quả skipped. Truyền
token cho Run Now (và nối với shutdown). Dùng `BeginInvoke`/`InvokeAsync`.

### AT-10 — Đổi episode organization xoá tập đang tải (Medium)

Code: `Services/TrackedShowService.cs:86-115` (`SwitchEpisodeOrganizationAsync`) gọi
`DeleteTrackedSeasonsAndEpisodes` (`DatabaseService.cs:827-838`) rồi import lại. Mọi `EpisodeId` mới, mọi
`TorrentHash` của tập mất. Order cũ vẫn trỏ `EpisodeId` cũ.

Kịch bản: show đang auto-track có tập X đang tải (`Downloading`). Người dùng đổi organization trong Library
(ví dụ sang episode group). X mới có `TorrentHash` rỗng và `Missing`. Order cũ không chặn tập mới (khác
`EpisodeId`). Hunt tải X lần nữa. Torrent cũ có hash nằm trong `RefreshSavedOrderHashes`, nên bị loại khỏi
adopt và không bao giờ được link. Với show không auto-track thì chỉ mất liên kết torrent.

Độ tin cậy trung bình: đường gọi rõ, nhưng chưa chạy harness cho luồng Library. Thuộc ranh giới
Library/Auto-Track.

Hướng sửa: khi rebuild, map lại order và trạng thái torrent theo `(Season, Episode)` hoặc theo mapping của
group. Tối thiểu, chặn đổi organization khi show có order Auto-Track chưa xong.

### AT-11 — State machine TMDB ghi nhưng không dùng; thông báo "New episode" lặp (Low)

Code: `AutoTrackService.cs:217-238`, `:299-323`. `AutoTrackTmdbState` chỉ ảnh hưởng dòng trạng thái trên
dashboard. `FinishedComplete` được ghi rồi refresh vẫn tiếp tục (`:236` "Don't skip refresh"). Lọc thật sự nằm
ở `HasSatisfiedCurrentCycle`. Thông báo `AutoTrackNewEpisode` (`:325-340`) bắn sau mỗi refresh còn tập pending,
kể cả khi đó là tập đã báo, và mỗi 30 phút nếu tắt lịch tuần.

Lịch sử: `a1f34df` ("fix show finished not refresh tmdb") xoá điều kiện bỏ qua thay vì định nghĩa lại vai
trò của state. Với cap thật là 10/ngày, show Finished vẫn tiêu budget mỗi tuần.

Hướng sửa: bỏ hoặc đổi tên state cho đúng vai trò hiển thị. Chỉ thông báo tập mới khi số pending tăng (so với
trước refresh).

### AT-12 — Dashboard đếm "pending" theo luật khác với hunt thật (Low)

Code: `ViewModels/AutoTrackViewModel.cs:158-164` (preview batch với `huntDelayHours: 0`, không lọc chu kỳ,
không giới hạn show/nhịp) và `:303-323` (`AirDate.Date <= today`, không delay). Hunt thật dùng delay 4 giờ,
`FilterToCycle`, 3 show/nhịp.

Tác động: dashboard nói "Hunting: S01E05" trong khi nhịp tới sẽ không hunt tập đó (chưa tới anchor, hoặc bị
đói do AT-01). Người dùng không thấy được lý do. Hướng sửa: VM gọi cùng một hàm `BuildHuntQueue` công khai và
hiển thị "queued / waiting for anchor / starved".

### AT-13 — State dùng chung trong `FetchJobService` singleton (Low)

Code: `Services/FetchJobService.cs:25-30` (cache candidate theo `EpisodeId`), `:140-145` và `:184-190`
(`_activeHuntDebugSession` là một field, không lock). `AutoTrackService` đọc kết quả qua
`GetSearchRowCount`/`GetOverrideRejectCounts` sau khi await (`AutoTrackService.cs:953-954`).

Kịch bản: người dùng chạy Run Cart cho cùng show trong lúc Auto-Track đang hunt. `ClearEpisodeCandidateCache`
của lần chạy kia xoá số dòng search giữa chừng, nên Auto-Track ghi lý do thất bại sai ("Search returned no rows"
thay vì "No recipe matches"), và debug session bị lần chạy kia lấy mất. Chỉ ảnh hưởng chẩn đoán; dữ liệu
candidate trả về qua giá trị return nên không lẫn. Để phiên viết lại search (`feat/native-search`) xử lý.

### Ghi chú review (chưa đủ bằng chứng để thành finding)

- **R-1:** `HuntMinHoursAfterAirDate` bị clamp 0..48 ở hunt (`AutoTrackService.cs:793`) nhưng settings UI
  không có giới hạn tương ứng. Chưa kiểm.
- **R-2:** reconcile coi hash vắng trong một lần `torrents/info` là bị xoá (`TorrentReconciliationService.cs:221-235`,
  `:300-313`): xoá `TorrentHash` của tập và đánh order `Failed` ngay. Nếu qBittorrent trả về danh sách rỗng
  tạm thời (vừa restart, chưa nạp xong resume data), mọi tập đang tải sẽ bị hunt lại và tải trùng. Chưa xác
  minh qBittorrent có thể trả về như vậy khi WebUI đã lên; nên thêm "vắng hai lần liên tiếp" trước khi xoá.
- **R-3:** episode có `TorrentHash` mà link thất bại vĩnh viễn (parser không map được) giữ reconcile armed
  mãi, chạy reconcile toàn bộ show mỗi 10 phút, không có trần.
- **R-4:** `ImportShow` chỉ upsert. Tập bị TMDB xoá hoặc đánh số lại vẫn nằm trong DB và có thể bị hunt mãi.
- **R-5:** `AutoTrackService` gọi `GetTorrentCartOrders()` (đọc cả bảng) nhiều lần cho mỗi tập trong
  `IsAutoTrackHuntBlocked` và `BuildHuntQueue`. Chưa đo; có thể chậm khi cart lớn.
- **R-6:** budget TMDB chỉ được lưu ở cuối nhịp (`AutoTrackService.cs:373-391`). App chết giữa nhịp thì số
  lần đã dùng bị mất (cap mềm hơn dự kiến). Ảnh hưởng nhỏ.

## 5. Test

### 5.1 Kết quả test hiện có

`dotnet test MediaManager.App.Tests/MediaManager.App.Tests.csproj -c Debug` trên `ff2caa3`:
**138/138 pass**, 7,6 giây. Trong đó 33 test case (24 method, có `[Theory]`) thuộc
`AutoTrackAnchorCycleTests` (11) và `InterruptedOrderRecoveryTests` (13 method). Cả hai chỉ test hàm thuần
(`AutoTrackWeekAnchor`, `AutoTrackTmdbEligibility.ShouldRefreshTmdb/FilterToCycle`,
`InterruptedOrderRecovery`).

### 5.2 Test còn thiếu (đề xuất, theo thứ tự giá trị)

1. `TorrentCartService.PrepareAutoTrackEpisodeOrder`: order `Failed` giữ `FailedCandidateUrls`; order có
   `PendingValidationHash` không được tái dùng âm thầm (AT-01, AT-05).
2. `AutoTrackService.BuildHuntQueue` (cần làm `internal` + `InternalsVisibleTo`, hoặc tách thành hàm thuần):
   show bị đói khi 3 show đầu lỗi; tập TBA bị loại; thứ tự xoay vòng (AT-01, AT-03).
3. `DatabaseService.UpsertTrackedEpisode` giữ `Availability` cũ; `RefreshShowAsync` bị huỷ ở bước poster
   không đổi availability (AT-02). Dùng DB SQLite tạm như harness.
4. `AutoTrackTmdbEligibility.FindPendingEpisodes` với show Mỹ, giờ phát thật và anchor mặc định (AT-04).
5. `TorrentCartService.ReleaseAutoTrackHuntBlocks` không phá order `Approved` đang chờ validate (AT-05).
6. Scheduler: khôi phục gắn torrent thì reconcile được arm (AT-06). Cần fake `IAutoTrackService`.
7. Hunt halt do `TorrentCleanupFailedException`: các order `Approved` còn lại được trả về `CandidatesFound`
   (AT-07).
8. Discovery với daily cap: show lâu chưa refresh nhất được ưu tiên (AT-08).
9. `AutoTrackRunResult` phân biệt skipped và failed; Run Now không ghi đè `LastRunSummary` khi bị skip (AT-09).
10. `TorrentReconciliationService`: hash vắng một lần không xoá hash tập (R-2), sau khi quyết định cách sửa.
11. `TmdbDailyBudget`: đổi ngày giữa nhịp, `Remaining`/`TryConsume` ở 23:59 → 00:00.
12. `ShowWeeklyAirDay.Infer`: hoà, special, `fromSeason/fromEpisode`.

## 6. Harness tái hiện

Mã nguồn nằm ngoài repo, trong thư mục ghi chú chung của các phiên audit (`side-session-notes/.../autotrack-audit-harness/`):
`Program.cs`, `atharness.csproj`, `harness-output.txt`, cùng ghi chú lịch sử và log `history-and-logs.md`. Nó tham
chiếu `media management app.dll` từ `MediaManager.App.Tests/bin/Debug/...` và dùng `DatabaseService`,
`TrackedShowService`, `TorrentCartService`, `AutoTrackService` thật trên DB SQLite trong
`%TEMP%\at-audit-*` (xoá khi xong). Các dependency mạng (TMDB catalog, poster, WARP, qBittorrent) là fake
`DispatchProxy`. Không có lệnh nào chạm tới state folder, qBittorrent hay Library của user.

Output (rút gọn):

```text
S1 TBA: pending=2 (S02E01,S02E02), afterCycleFilter=2
S2 US show, anchor Sun 21:00: Sun 21:00 huntEligible=1; actual broadcast Mon 09:00 UTC+7; ShowWeeklyAirDay=Sunday
S3 before refresh S01E01=Available; poster step OperationCanceledException; after S01E01=Missing;
   hunt-eligible now: S01E01,S01E02
S4 Failed blocking? False; re-prepared status=Draft, FailedCandidateUrls='', PendingValidationHash='abc123'
S5 Approved IsInterrupted=True → Reset week → Failed, IsInterrupted=False, pending hash still blocks adoption
S6 batch(3) = Show 9100 | 9101 | 9102; 'Show 9103' hunted this cycle: False
S7 cap 20, schedule off: cycle1 refreshed=20; cycle2/3 "Daily cap reached"; never refreshed: Show 9220..9224
S8 Run Now during discovery: Succeeded=False, LastRunSummary='TMDB discovery is already running.'
S9 Searching order from this session: IsInterrupted=False (blocks until restart)
```

## 7. Thứ tự sửa đề xuất

1. **AT-01 + AT-03:** backoff cho `NoCandidates`/`Failed`, giữ `FailedCandidateUrls`, bỏ tập TBA, xếp hàng
   đợi theo lần thử cũ nhất. Đây là nguyên nhân chính khiến show "không tự tải" mà không báo lỗi; cũng giảm
   tải cho mọi thứ khác. Thêm test 5.2 #1, #2.
2. **AT-02:** một dòng SQL (bỏ `Availability` khỏi `DO UPDATE`) cộng test. Rủi ro thấp, chặn tải trùng.
3. **AT-05 + AT-07:** cùng chỗ (`TorrentCartService` và catch halt). Giữ order chờ validate trong luồng khôi
   phục; trả order `Approved` chưa add về `CandidatesFound`. Disable Reset week khi đang chạy.
4. **AT-06:** cho khôi phục arm reconcile.
5. **AT-04:** thống nhất đồng hồ, ước lượng giờ phát theo nước gốc. Cần quyết định thiết kế (offset theo show
   hay theo `origin_country`), nên làm sau khi AT-01 đã giảm tác hại của hunt sớm.
6. **AT-08, AT-10:** ưu tiên refresh theo thời gian; chặn đổi organization khi có order đang chạy.
7. **AT-09, AT-11, AT-12, AT-13** và ghi chú R-x: dọn dẹp khi chạm tới các file đó. Kèm theo: hạ log
   `Anchor check` xuống Debug, bỏ `TorrentHuntIntervalMinutes` khỏi Settings, merge breaker `bd6c791` vào `main`.

Việc riêng, không thuộc Auto-Track nhưng nên làm trước khi sửa bất cứ gì ở trên: tìm vì sao test App từng khởi
động app trên state folder thật (mục 3.5).
