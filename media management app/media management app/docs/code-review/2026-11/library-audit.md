# Audit Library workspace (2026-11)

- **Nhánh / commit gốc:** `claude/library-audit-a86664` @ `ff2caa3`. Không có file Library nào đổi sau 2026-09-16.
- **Phương pháp và thang mức độ:** theo [`00-method-and-scope.md`](../00-method-and-scope.md).
- **Cách làm:** chỉ đọc code tĩnh và lịch sử git. Không sửa code app, không chạy app, không đụng dữ liệu thật. Không chạy `dotnet build/test` vì không có test nào phủ vùng này (xem mục 6) và để không tranh tài nguyên với các phiên audit đang chạy song song.
- **Đường dẫn** dưới đây tính từ `media management app/media management app/`. Số dòng ứng với HEAD `ff2caa3`.
- **Trạng thái bằng chứng:**
  - **CONFIRMED (tĩnh):** đường lỗi chứng minh được bằng code, kể cả hành vi chuẩn của WPF.
  - **PLAUSIBLE:** đúng về logic nhưng phụ thuộc thứ tự sự kiện WPF lúc chạy, cần repro tay.

## 1. Tóm tắt

Library hiển thị trạng thái **lấy từ DB, nhưng một phần được tính lại từ ổ đĩa ngay trên UI thread**. Đồng bộ dựa vào việc "dựng lại toàn bộ" thay vì cập nhật từng phần, và được giữ bằng năm cờ `_suppress*`, mỗi cờ là một bản vá riêng.

Chuỗi bug lặp lại trong lịch sử (poster nháy hoặc mất, combobox status lệch, detail cũ sau reload) có chung một gốc chưa được sửa: `ApplyMediaCardFilterAndSort` gọi `MediaCards.Clear()` trong khi `ListBox.SelectedItem` bind TwoWay (**LIB-01**). Mỗi lần đổi rating, sort, search, filter hay series status, selection bị đẩy về `null`. Lúc đó detail bị xóa (poster về `null`, status về Unset) rồi dựng lại từ đầu, nên các season đang mở sụp lại và poster tải lại.

Hiệu năng với thư viện lớn bị giới hạn bởi việc quét **toàn bộ** `SourceItems` kèm `File.Exists` trên UI thread mỗi lần chọn card hoặc nhận sự kiện reconcile (**LIB-02**), kể cả khi tab Library không mở.

Ghi sai xuống DB chỉ xảy ra trong nhánh lỗi: khi load detail thất bại, rating sẽ ghi vào title trước đó (**LIB-03**). Đường bình thường không ghi sai. Rủi ro chính là **mất chỉnh sửa chưa lưu** (LIB-05, LIB-11) và **UI cũ hoặc lệch** (LIB-04, LIB-06, LIB-08, LIB-09).

| Mức độ | Số lượng | ID |
|---|---|---|
| Critical | 0 | |
| High | 2 | LIB-01, LIB-02 |
| Medium | 9 | LIB-03 … LIB-11 |
| Low | 7 | LIB-12 … LIB-18 |

## 2. Lịch sử bug và hotspot

### 2.1 Hotspot (`git log --follow`)

| File | Commit | Khoảng thời gian |
|---|---|---|
| `ViewModels/LibraryViewModel.cs` (~2.950 dòng, gần như toàn bộ logic) | 35 | 2026-06-09 → 09-16 |
| `Views/LibraryView.xaml` (2.429 dòng) | 27 | 06-09 → 09-16 |
| `ViewModels/LibrarySeasonViewModel.cs` | 11 | 06-10 → 09-15 |
| `ViewModels/LibraryEpisodeRowViewModel.cs` | 9 | 06-10 → 09-16 |
| `LibraryShowDetailViewModel.cs` / `LibraryMovieDetailViewModel.cs` | 8 / 8 | |
| `Views/LibraryView.xaml.cs` | 6 | |
| `LibraryMediaCardViewModel.cs` / `MediaCardCatalogService.cs` | 5 / 4 | |
| `PosterImageService.cs` / `MediaMetadataSyncService.cs` / `JellyfinMediaNavigationService.cs` | 3 / 3 / 3 | |

Nhiều fix thật nằm trong các commit đặt tên "update …", ví dụ `660db32`, `13dd96e`, `8657f65`, `56f64cf`. Lọc theo subject sẽ bỏ sót chúng. Các fix này được tìm bằng `git log -S` trên tên các cờ guard.

### 2.2 Bốn fix được yêu cầu

**`3881281` keep Library poster on reconcile detail refresh**
- **Triệu chứng:** đang xem detail thì poster nháy "No cover" mỗi khi có reconcile.
- **Gốc:** handler `Reconciled`/`PackReconciled` gọi `ReloadSelectedDetailAsync`, mà hàm này lúc đó set `SelectedPosterImage = null`.
- **Cách sửa:** tách riêng đường sự kiện sang `RefreshSelectedDetailAfterReconcile` (L2242), đường này không đụng poster.
- **Đánh giá:** *vá cục bộ*. Khoảng 17 lệnh khác vẫn dùng đường cũ cho tới khi `13dd96e` bỏ dòng clear. Đường clear poster **vẫn còn** qua `ApplyClearedDetail` (L2045), xem LIB-01.

**`64d7a85` fix status combobox desync**
- **Triệu chứng:** ComboBox watch status hiển thị khác `SelectedWatchStatus` sau khi panel ẩn rồi hiện lại.
- **Cách sửa:** code-behind `WatchStatusComboBox_OnIsVisibleChanged` ép `SelectedIndex` (`LibraryView.xaml.cs:125-159`).
- **Đánh giá:** *vá phía View*, chỉ sửa khi visibility đổi. Phía VM được chặn riêng bằng cờ ở `660db32`, `56f64cf`, `160232a`. Đây là lần vá thứ 4 cho cùng một chủ đề (LIB-08).

**`89eebc8` fix reset eps**
- **Triệu chứng:** reset một tập nhưng tập vẫn hiện Available.
- **Gốc:** reset chỉ xóa SourceItem khi cả file nguồn lẫn file link đều đã mất. `RefreshAvailability` coi "có SourceItem" là "có tập" (LIB-07).
- **Cách sửa:** xóa toàn bộ SourceItem tmdb của S/E đó.
- **Đánh giá:** đây là đổi chính sách ở service, chưa sửa định nghĩa "available". Biến thể cho season pack phải sửa lại ở `8ce1b1c` và `8657f65`.

**`8bd179e` add remove title rating and fix UI**
- **Triệu chứng:** không xóa được rating, và Enter không commit.
- **Gốc:** `selectedRating` là `double`, nên "chưa có rating" bị đọc thành 0.0.
- **Cách sửa:** đổi sang `double?` và thêm `CommitTitleRatingCommand`.
- **Đánh giá:** sửa có cấu trúc. Phụ tác dụng: một lần sửa ghi DB hai lần (LIB-12).

### 2.3 Chủ đề bug lặp lại

| Chủ đề | Commit | Vị trí hiện tại | Còn mở? |
|---|---|---|---|
| A. Poster mất hoặc nháy khi refresh detail | `73c1eaf`, `161b9c9`, `3881281`, `13dd96e` | `LoadSelectedMediaAsync` L2066, `ApplyClearedDetail` L2039 | **Có**: LIB-01, LIB-04 |
| B. ComboBox watch status lệch, cascade status sang item khác | `093d795`, `660db32`, `64d7a85`, `56f64cf`, `160232a` | `SetWatchProgressUi` L2473, comment L2461, code-behind | **Có**: LIB-01, LIB-08 |
| C. Detail hoặc selection cũ sau reload hay điều hướng | `177cd5b`, `13dd96e`, `660db32` | `RefreshLibraryOnTabEnterAsync` L1912, `_suppressDetailLoadOnSelection` | **Có**: LIB-01, LIB-03, LIB-15 |
| D. Bão refresh do reconcile | `3881281`, `8657f65` (debounce 250 ms), `5ce272f` (thêm trigger symlink) | L105-107, L2193-2240 | **Có**: LIB-02, LIB-05 |
| E. Trạng thái có/chưa có tập bị cũ sau reset hoặc cleanup | `89eebc8`, `8e2b3a7`, `734039c`, `8ce1b1c`, `8657f65` | `RefreshAvailabilityCore` (TrackedShowService L421) | **Có**: LIB-06, LIB-07 |
| F. Rating | `e31f562`, `c9b8674`, `63663f2`, `8bd179e` | `PersistSelectedRatingAndThought` L2522 | Một phần: LIB-12, LIB-18 |

Không có commit revert nào. Mỗi chủ đề được sửa từ 3 đến 5 lần, lần nào cũng thêm một cờ hoặc một đường đi song song, chưa lần nào sửa chỗ gốc.

## 3. Nguồn sự thật (câu hỏi 1)

| Dữ liệu hiển thị | Nguồn | Khi nào được tính lại |
|---|---|---|
| Card: `AvailableCount/TotalCount`, `OrderCount`, `SeriesStatusLabel` | DB (`TrackedShows`, `TorrentCartOrders`), giá trị `init` chỉ đọc | Chỉ khi `RefreshLibrary()` chạy (LIB-06) |
| Card: `WatchStatus`, `WatchedEpisodes`, `Rating` | DB, card được vá tại chỗ khi user sửa | Khi user sửa trên Library. Sửa từ nơi khác thì phải chờ `RefreshLibrary` |
| `TrackedEpisodes.Availability` ("Available/Missing") | **DB, suy ra từ `SourceItems`**: có item tmdb với State khác Deleted/Ignored. **Không kiểm tra đĩa** | `RefreshAvailability()` **ghi ngược xuống DB** mỗi lần vào tab, đổi series status, xóa, refresh TMDB, … |
| Trạng thái link của tập/phim (`LibraryLinkStatus`), "Linked by pack" | **Ổ đĩa**: `File.Exists(LinkedPath)` trên từng SourceItem | Mỗi lần dựng detail |
| Có symlink Jellyfin | **Ổ đĩa**: `File.Exists(SymlinkPath)` | Mỗi lần dựng detail |
| Mở trong Jellyfin | **Jellyfin API**, item id được cache theo (tmdb, S, E) | Cache chỉ bị xóa khi app chuyển sang background (LIB-13) |
| Poster | File cache `state/posters/*.jpg` kèm `.path`, nếu không có thì tải từ TMDB | Mỗi lần load detail (LIB-01, LIB-04) |
| Watch status / rating / thought ở detail | DB | Mỗi lần load detail. Lần rebuild do reconcile chỉ cập nhật phim, không cập nhật show (LIB-05) |

**Khi một nguồn thay đổi:**
- **Auto-Track tải xong / reconcile / pack link / symlink:** có sự kiện → `RefreshSelectedDetailAfterReconcile`, debounce 250 ms. Chỉ detail đang chọn được dựng lại. Card list và `RefreshAvailability` **không** chạy, nên số "x/y available" trên card cũ cho tới khi vào lại tab (LIB-06). Handler chạy cả khi đang ở tab khác (LIB-02).
- **Xóa:** `DeleteSelectedMedia` chạy `RefreshLibrary` nên đúng. Xóa từ nơi khác (Auto-Track) thì Library dựa vào lần vào tab sau. Nếu card cũ vẫn được chọn thì rơi vào LIB-03.
- **TMDB refresh từ Library:** `RefreshLibrary` rồi `ReloadSelectedDetailAsync`, nên đúng. TMDB refresh chạy nền (Auto-Track, ongoing sync) thì **không có sự kiện nào** báo về Library.
- **Ghi ngược sai xuống DB:** đường bình thường không có. Hai chỗ đáng lưu ý: (a) `RefreshAvailability` ghi `Availability` từ UI thread theo định nghĩa không nhìn đĩa (LIB-07); (b) nhánh lỗi LIB-03 ghi rating hoặc thought của user vào **title trước đó**.

## 4. Finding

### LIB-01 (High): `MediaCards.Clear()` + `SelectedItem` TwoWay xóa rồi dựng lại toàn bộ detail mỗi lần đổi rating, sort, search, filter hoặc series status

- **Đường dẫn:**
  - `ViewModels/LibraryViewModel.cs`: `ApplyMediaCardFilterAndSort` L2430-2471, `OnSelectedMediaCardChanged` L1683-1716, `LoadSelectedMediaAsync` L2066-2093, `ApplyClearedDetail` L2039-2049
  - `Views/LibraryView.xaml:1192-1195`
- **Hành vi (CONFIRMED tĩnh; repro runtime nên làm):**
  1. `MediaCards.Clear()` phát sự kiện `Reset`. `Selector` thấy item đang chọn không còn trong Items nên đặt `SelectedItem = null`, và binding TwoWay ghi `null` vào `SelectedMediaCard`.
  2. `OnSelectedMediaCardChanged(null)` không bị chặn (`_suppressDetailLoadOnSelection` chỉ bật trong `RefreshLibraryOnTabEnterAsync`). Nó đặt `ShowHiddenSeasons = false` và `_loadedDetailMediaId = null`, rồi gọi `LoadSelectedMediaAsync(null)` → `ApplyClearedDetail`: `SelectedShow = null`, `SelectedPosterImage = null`, status về Unset, rating về null, và `PersistLibraryUiState` ghi file settings với selection rỗng.
  3. Dòng L2465 chọn lại card cũ. Vì `_loadedDetailMediaId` đã là null, `LoadSelectedMediaAsync(card)` chạy lại từ đầu: quét toàn bộ SourceItems và gọi `File.Exists` (LIB-02), tải lại poster, và `PersistLibraryUiState` lưu settings lần nữa.
  4. `expandedSeasons` được lấy từ `SelectedShow`, mà lúc này đã là `null`, nên **mọi season đang mở đều sụp lại**. Panel ghi chú tập đang mở cũng bị đóng.
- **Kịch bản:**
  - Mở S01 và S03 của một show rồi bấm "+" rating title: poster nháy trống, hai season sụp, nút "Show hidden seasons" tắt.
  - Với filter status đang bật, `PersistSelectedRatingAndThought` gọi `ApplyMediaCardFilterAndSort` **hai lần** (L2540-2541 qua `SyncCardInAllMedia`), nên mỗi bước 0.1 rating gây hai lần dựng lại.
  - `CycleSelectedShowSeriesStatus` (→ `RefreshLibrary`), đổi sort, gõ search hay bấm filter cũng gây hiệu ứng tương tự.
- **Tác động:** đây là gốc chung của chủ đề A, B, C trong mục 2.3. Mất trạng thái UI, tốn I/O trên UI thread, ghi settings thừa. Không làm hỏng DB, vì các lần set trong `ApplyClearedDetail` đều đi qua cờ suppress.
- **Hướng sửa:**
  - Không gọi `Clear()` rồi `Add` lại. Dùng `ICollectionView` (Filter/SortDescriptions) trên một collection cố định, hoặc cập nhật theo diff (Move/Insert/Remove).
  - Hoặc bọc toàn bộ việc dựng lại trong một scope `_suppressDetailLoadOnSelection` (try/finally), và so sánh selection theo `(Id, Kind)` thay vì theo tham chiếu.
  - Không lấy `expandedSeasons` từ `SelectedShow` hiện tại. Lưu nó theo `(showId)`.

### LIB-02 (High): dựng detail và `RefreshLibrary` quét toàn thư viện cùng ổ đĩa trên UI thread, kể cả khi tab Library không mở

- **Đường dẫn:**
  - `BuildShowDetail` L2286: `GetLinkedEpisodeStatuses` L2755, `GetLinkedPackOwnerSeasons` L2829, `GetSymlinkedEpisodeKeys` L2800, `AppendOrphanPackRows` L2726
  - `BuildMovieDetail` L2402, `RebuildSelectedShowDetail` L2371
  - `LoadSelectedMediaAsync` L2082 (đồng bộ, không dùng `Task.Run`)
  - `RefreshLibrary` L355-363 → `TrackedShowService.RefreshAvailability` (L134-146, 421-458) và `MediaCardCatalogService.LoadCards` (L43, L61: `GetOrderCount` là một truy vấn cho **mỗi** card)
  - Trigger: L105-107 (`Reconciled`, `PackReconciled`, `SymlinkStateChanged`)
- **Hành vi (CONFIRMED tĩnh):**
  - Mỗi lần dựng detail, `_databaseService.GetSourceItems()` tải **mọi** SourceItem.
  - Trong 4 lần lọc, `File.Exists(LinkedPath/SymlinkPath)` đứng **trước** điều kiện `ProviderId == tmdbId` (ví dụ L2768-2770, L2812-2814, L2840-2842). Vì vậy app gọi `File.Exists` cho **mọi tập đã link của mọi show**, không riêng show đang xem, tức khoảng 4×N lần gọi hệ thống mỗi lần chọn card.
  - `RefreshAvailability()` duyệt O(số show × số SourceItem), cộng thêm hai truy vấn DB cho mỗi show.
  - Toàn bộ chạy trên UI thread. Riêng lần vào tab có bọc `Task.Run` cho phần dựng detail, còn `RefreshLibrary` thì không.
  - Sự kiện reconcile, pack hay symlink từ Auto-Track gọi `RebuildSelectedShowDetail` **bất kể tab nào đang mở**, cho tới khi còn card được chọn.
- **Kịch bản:** 300 show và 15.000 tập đã link, ổ thư viện là HDD đang ngủ hoặc share mạng đang offline. Bấm một card thì UI đứng vài giây, và mỗi lần `File.Exists` trên một đường dẫn mạng chết có thể chờ timeout. Đang ở tab Torrent, Auto-Track reconcile xong thì cả app khựng.
- **Hướng sửa:**
  - Đưa điều kiện provider và tmdb lên trước `File.Exists`, hoặc tốt hơn là truy vấn SourceItems theo `ProviderId` ngay ở DB.
  - Dựng detail trong `Task.Run` cho mọi đường (giống `BuildSelectedDetail` lúc vào tab), có generation hoặc cancellation.
  - Bỏ qua refresh do sự kiện khi tab Library không active (`INavigationAware` đã có sẵn), đánh dấu dirty và refresh khi `OnNavigatedTo`.
  - `LoadCards` lấy order count bằng một truy vấn GROUP BY.

### LIB-03 (Medium): load detail thất bại để lại detail của title cũ, cờ suppress kẹt, và rating ghi vào title cũ

- **Đường dẫn:** `OnSelectedMediaCardChanged` L1690-1697 (`_ = LoadSelectedMediaAsync(value)`, fire-and-forget), `LoadSelectedMediaAsync` L2069 (`_suppressWatchProgressUpdate = true`), `ApplyBuiltDetail` L2008-2013 (nhánh `IsMissing` return sớm), `PersistSelectedRatingAndThought` L2536-2553.
- **Hành vi (CONFIRMED tĩnh):** khi `BuildSelectedDetail` trả về `Missing` (title đã bị xóa ở nơi khác nhưng card cũ còn) hoặc ném exception (DB locked, I/O), thì:
  - `_loadedDetailMediaId` đã trỏ sang card mới, nhưng `SelectedShow`/`SelectedMovie`, rating và thought **vẫn là của title trước**.
  - `_suppressWatchProgressUpdate` kẹt ở `true`, vì `SetWatchProgressUi` không được gọi để lên lịch xóa cờ, nên mọi lần đổi watch status bị bỏ qua mà không báo.
  - Exception của Task bị nuốt (fire-and-forget), chỉ được log qua `UnobservedTaskException`.
- **Kịch bản:** user bấm "+" rating khi đang ở trạng thái lệch. `_trackedShowService.UpdateRating(SelectedShow.Id, …)` ghi vào **title cũ**, còn `SelectedMediaCard.ApplyRating` gán rating đó lên **card mới**. Kết quả là DB sai ở title A, còn UI sai ở card B.
- **Hướng sửa:** khi `Missing` hoặc có exception thì `ApplyClearedDetail()` (hoặc báo lỗi và bỏ chọn card). Bọc phần thân bằng try/catch kèm `finally` để luôn trả cờ suppress. `Persist*` nên kiểm tra `SelectedShow.Id == SelectedMediaCard.Id` trước khi ghi.

### LIB-04 (Medium): race poster, poster của card trước đè lên card đang chọn

- **Đường dẫn:** `LoadSelectedMediaAsync` L2089-2092, `ReloadPosterOnForegroundAsync` L2942-2952. `PosterImageService.LoadAsync` (L61-94) tải qua HTTP, timeout mặc định 100 s.
- **Hành vi (CONFIRMED tĩnh):** không có generation hay token, và không kiểm tra `card == SelectedMediaCard` sau `await`. Đường lúc vào tab (L1940) thì có token.
- **Kịch bản:** chọn A (poster chưa có cache, TMDB chậm hoặc bị chặn và đang chờ WARP) rồi chọn B (poster có cache, trả về đồng bộ). B hiện đúng, nhưng vài giây sau poster A đè lên B. Ở chủ đề A trong lịch sử, triệu chứng này dễ bị nhầm với "poster nháy".
- **Hướng sửa:** dùng `CancellationTokenSource` cho mỗi lần chọn, hủy cái cũ, và kiểm tra lại id trước khi gán.

### LIB-05 (Medium): refresh nền làm mất chỉnh sửa chưa lưu, rebuild của show và movie không đối xứng

- **Đường dẫn:** `RefreshSelectedDetailAfterReconcile` L2242 → `RebuildSelectedMovieDetail` L2412-2428 (gọi `SetRatingThoughtUi` → `IsEditingThought = false` và ghi đè `SelectedThought`) và `RebuildSelectedShowDetail` L2371-2400 (tạo row VM mới, không gọi `CloseOpenEpisodeEditors`, không cập nhật watch/rating của title).
- **Hành vi (CONFIRMED tĩnh):**
  - **Phim:** đang gõ thought thì Auto-Track reconcile xong. Editor đóng, chữ đang gõ bị thay bằng giá trị trong DB. LostFocus sau đó gọi `CommitThought`, nhưng hàm này thoát sớm vì `IsEditingThought == false`.
  - **Show:** panel ghi chú tập đang mở (`IsNotesPanelOpen`, thought bind `PropertyChanged`) bị thay bằng row mới đọc từ DB. Chữ chưa commit bị mất. Nếu LostFocus của TextBox cũ vẫn bắn thì DB nhận chữ mới nhưng UI hiện chữ cũ, tức là lệch.
  - Show không làm mới watch status/rating sau rebuild. Phim thì có, nhưng lại reset luôn trạng thái đang sửa.
- **Hướng sửa:** khi đang sửa (`IsEditingThought` hoặc có row `IsNotesPanelOpen`) thì hoãn rebuild, hoặc commit trước rồi mới rebuild. Rebuild nên cập nhật row hiện có (theo `Id`) thay vì tạo row mới. Tách "dữ liệu link và đĩa" khỏi "dữ liệu user" để rebuild do sự kiện không đụng phần của user.

### LIB-06 (Medium): card list cũ sau link, reset, reconcile hoặc thay đổi giỏ

- **Đường dẫn:** `LibraryMediaCardViewModel` (các thuộc tính `init` L22-47). Các lệnh `LinkEpisode` L690, `ResetEpisode` L742, `ResetMovie` L1126, `LinkMovie` L1094, `UnlinkSeasonPack` L856, `RunPackLinkAsync` L1064-1069 và `ReconcileExistingTorrents` L1158 chỉ gọi `RefreshAvailability(id)` và `ReloadSelectedDetailAsync`, không gọi `RefreshLibrary`. `CartChanged` (L94) chỉ cập nhật detail.
- **Kịch bản:** reset S01E05. Detail hiện 9/10, nhưng card vẫn "10/10 available". Thêm 3 tập vào giỏ thì card vẫn "0 orders". Sort và filter tiếp tục chạy trên số liệu cũ cho tới khi vào lại tab.
- **Hướng sửa:** thêm `LibraryMediaCardViewModel.ApplyCounts(...)` (observable) và cập nhật đúng card đó sau mỗi lệnh hoặc sự kiện, thay vì `RefreshLibrary` toàn bộ.

### LIB-07 (Medium): hai định nghĩa "Available"; `RefreshAvailability` ghi xuống DB một trạng thái không nhìn đĩa

- **Đường dẫn:** `TrackedShowService.GetAvailableEpisodeKeys` L442-458 và `TrackedMovieService.RefreshAvailabilityCore` L104-119: available khi có SourceItem mà State khác Deleted/Ignored. Ngược lại, `LibraryViewModel.GetLinkedEpisodeStatuses` L2755 và `GetMovieLinkStatus` L2862 dựa vào `File.Exists(LinkedPath)`. `LibraryEpisodeRowViewModel.CanAddToCart` L122 yêu cầu `!IsAvailable`.
- **Kịch bản:** user xóa file trong thư mục thư viện bằng Explorer. Tập hiện "Available" nhưng link là "Not linked". Nút Add to cart bị khóa vì `IsAvailable`, nên user phải biết bấm Reset. Đây là gốc của chủ đề E (`89eebc8` sửa bằng cách xóa hẳn SourceItem).
- **Hướng sửa:** thống nhất một định nghĩa (đề xuất: Available = Linked và file tồn tại, hoặc thêm trạng thái thứ ba "Linked (file missing)"). Ghi chuyển trạng thái do đĩa ở một service nền, không ghi từ đường vào tab trên UI thread. Phối hợp với phiên database-audit.

### LIB-08 (Medium, PLAUSIBLE): cờ `_suppressWatchProgressUpdate` được xóa ở mức ưu tiên Background, có thể nuốt thao tác đổi status liên tiếp

- **Đường dẫn:** `SetWatchProgressUi` L2473-2498, `OnSelectedWatchStatusChanged` L1293-1301, `PersistSelectedWatchProgress` L2572 (gọi lại `SetWatchProgressUi`), `LibraryView.xaml.cs:125-159`.
- **Hành vi:** sau mỗi lần ghi, cờ suppress bật cho tới khi dispatcher chạy một item ở mức `Background`. Mức này thấp hơn `Input`. Khi UI đang bận (ví dụ LIB-01 làm một lần dựng lại nặng) hoặc user nhấn ↓ liên tục trên ComboBox đang focus, lần đổi thứ hai rơi vào lúc cờ còn bật: `SelectedWatchStatus` (và ComboBox) hiện X nhưng DB vẫn giữ giá trị trước, card cũng chưa đổi. Code-behind chỉ đồng bộ lại khi `IsVisibleChanged`.
- **Hướng sửa:** bỏ cơ chế "suppress theo thời gian". Cách 1: bind ComboBox `SelectedItem` vào một `WatchStatusOption` và chỉ persist khi user tương tác (`SelectionChanged` có `IsDropDownOpen`/`IsKeyboardFocusWithin`). Cách 2: so sánh với giá trị đã nạp cho đúng `(Id, Kind)` thay vì dùng cờ.

### LIB-09 (Medium): CanExecute của các lệnh theo selection không bao giờ được làm mới

- **Đường dẫn:** `DeleteSelectedMediaCommand` (L1634), `RefreshSelectedFromTmdbCommand` (L1248), `OpenSelectedTmdbPageCommand` (L1196) với `CanExecute = HasSelectedMedia`, và `CycleSelectedShowSeriesStatusCommand` (L1384) với `IsSelectedShow`. `OnSelectedMediaCardChanged` L1708-1715 và `OnSelectedShowChanged` L1868 **không** gọi `NotifyCanExecuteChanged` cho bốn lệnh này (grep: không có chỗ nào gọi). `RelayCommand` của CommunityToolkit không gắn vào `CommandManager.RequerySuggested`.
- **Kịch bản (CONFIRMED tĩnh):** lần chạy đầu thư viện rỗng. Thêm show từ Find/Add rồi quay lại thì card được chọn, nhưng nút Delete, Refresh TMDB và Open TMDB vẫn mờ cho tới khi khởi động lại app. Nếu card đầu tiên là phim, nút chuyển series status vẫn mờ khi chuyển sang show. Ngược lại, sau khi xóa card cuối, các nút vẫn sáng; bấm thì không có gì xảy ra vì có guard.
- **Hướng sửa:** thêm `[NotifyCanExecuteChangedFor]` trên `selectedMediaCard`/`selectedShow`, hoặc gom mọi lệnh phụ thuộc selection vào một hàm notify chung (hiện đã có 3 khối notify chép lặp nhau: L2051, L2180, L2273).

### LIB-10 (Medium): ghi DB đồng bộ trong handler UI không có try/catch, và handler toàn cục không đặt `Handled`

- **Đường dẫn:**
  - `OnSelectedShowSeriesStatusChanged` L1279, `PersistSelectedWatchProgress` L2572, `PersistSelectedRatingAndThought` L2522, `PersistEpisodeRatingAndThought` (EpisodeRating L297)
  - `ToggleSeasonHidden` L1890, `UpdateSeasonManagementMode` L2697 (callback từ setter `IsPackMode`), `SetAutoTrack`/`StopAutoTrack` L1491/1542, `DeleteSelectedMedia` L1635, `DeleteEntireLibrary` L1661, `ToggleAlternativeTitleRecipeSearchExclusion` L1225
  - `App.xaml.cs:603-614`: `OnDispatcherUnhandledException` chỉ log, không đặt `e.Handled = true`
- **Hành vi (CONFIRMED tĩnh):** bất kỳ exception nào từ DB (SQLite busy hoặc locked khi Auto-Track đang ghi, I/O, ổ đầy) trong các handler trên sẽ **làm app thoát**. Với toggle pack mode, setter của season VM đã đổi trước khi callback ghi DB, nên nếu được bắt lại thì UI và DB lệch nhau.
- **Hướng sửa:** bọc các thao tác ghi bằng một helper `TryPersist(...)` có báo `StatusMessage` và nạp lại giá trị từ DB khi lỗi. Việc `Handled` toàn cục thuộc phiên lifecycle-audit (tham chiếu chéo).

### LIB-11 (Medium, PLAUSIBLE): bấm sang card khác khi đang sửa có thể mất chỉnh sửa chưa commit

- **Đường dẫn:** `Resources/WorkspaceSharedTemplates.xaml:56-62`: card dùng `MouseBinding LeftClick` → `SelectMediaCardCommand`. InputBinding đánh dấu `Handled`, nên `ListBoxItem` không nhận MouseDown và không lấy focus. `LibraryView.xaml:1681` (watched episodes, `LostFocus`), `:1756` (rating, `LostFocus`), `:1808` (thought, `PropertyChanged` + commit khi LostFocus).
- **Hành vi:** focus vẫn ở TextBox khi selection đổi. `SetWatchProgressUi`/`SetRatingThoughtUi` của card mới ghi đè text đang gõ dở, và với thought thì đóng luôn editor. Commit lúc LostFocus đến muộn và không còn gì để ghi. Không có ghi sai sang title khác, chỉ mất chữ.
- **Repro tay:** gõ "8.7" vào ô rating (không nhấn Enter) rồi bấm card khác. Quay lại card cũ thì rating không phải 8.7.
- **Hướng sửa:** trong `OnSelectedMediaCardChanging`, gọi commit cho các editor đang mở của title cũ (`CommitThought`, `UpdateSource` cho các binding LostFocus), hoặc chuyển focus khỏi detail trước khi đổi selection.

### LIB-12 (Low): ghi trùng và làm việc thừa trên đường sửa rating và selection

- `TitleRatingTextBox_OnLostFocus`: binding cập nhật `SelectedRating` (L1326 persist), rồi `CommitTitleRatingCommand` persist lần nữa (L1443).
- `PersistSelectedRatingAndThought` L2539-2541 gọi `SyncCardInAllMedia` (đã có `ApplyMediaCardFilterAndSort` khi có filter) rồi lại gọi `ApplyMediaCardFilterAndSort`.
- `SyncCardInAllMedia` L2596 thừa: `_allMediaCards` và `MediaCards` chứa cùng instance.
- `PersistLibraryUiState` gọi `_settingsService.Save()` mỗi lần đổi selection, gấp đôi trong LIB-01.
- **Hướng sửa:** mỗi hành động chỉ một đường persist. Debounce việc lưu settings UI.

### LIB-13 (Low): cache item id Jellyfin chỉ bị xóa khi app vào background

- `JellyfinMediaNavigationService` L15, L65-79, L260-268. Sau khi đổi Episode Organization (`SwitchEpisodeOrganizationAsync`) hoặc xóa rồi thêm lại title, Jellyfin index lại với item id mới, nhưng cache vẫn mở id cũ (trang 404 hoặc sai tập). Khi Jellyfin trả 404 thì cache không tự sửa.
- **Hướng sửa:** xóa entry khi navigation thất bại hoặc trả 404. Xóa cache cho show khi organization đổi hoặc title bị xóa.

### LIB-14 (Low): `PosterImageService._memoryCache` chỉ ghi mà không đọc, không giới hạn, và không thread-safe

- Khóa `"{kind}:{tmdbId}"` được ghi ở L367 nhưng `LoadAsync` không đọc khóa này, nên ảnh đã decode bị giữ đến hết phiên. `ReleasePosterMemory` (LibraryViewModel L2933) không đụng tới cache này.
- `App.WarmupPosterCache` (`App.xaml.cs:491`, `Task.Run`) ghi cùng một `Dictionary` song song với `LoadAsync` trên UI thread. Ghi đồng thời vào `Dictionary` có thể làm hỏng cấu trúc của nó.
- **Hướng sửa:** bỏ cache in-memory cho poster, hoặc dùng `ConcurrentDictionary` có giới hạn và đọc nó thật trong `LoadAsync`.

### LIB-15 (Low): hủy tải giữa chừng khi vào tab làm selection và detail lệch nhau

- `RefreshLibraryOnTabEnterAsync` L1912-1951: `RefreshLibrary` đổi `SelectedMediaCard` trong lúc `_suppressDetailLoadOnSelection` đang bật. Nếu bị hủy (rời tab) trước `ApplyBuiltDetail`, card mới được highlight nhưng detail và `_loadedDetailMediaId` vẫn của card cũ.
- `expandedSeasons` (L1921) lấy từ show **cũ** để áp lên show mới.
- Lần vào tab sau sẽ sửa lại, nhưng nếu một sự kiện reconcile đến trong lúc lệch, `RebuildSelectedShowDetail` dựng show mới mà rating/status vẫn của show cũ (giống LIB-03, nhưng hẹp hơn).
- FEATURES §5.1 nói "keeps the last complete pane", điều này chỉ đúng một nửa.

### LIB-16 (Low): lệnh dài bị nuốt im lặng, không hủy được

- `RunImportActionAsync` L2613-2617 `return` ngay nếu `IsImportBusy`. Bấm "Change organization" hoặc "Refresh selected" trong lúc "Refresh All from TMDB" đang chạy thì không có gì xảy ra, cũng không có thông báo.
- `RefreshAllFromTmdb` (L1174, `MediaMetadataSyncService` L114-131) chạy tuần tự hàng trăm show, không có cancellation, không có tiến độ, và chạy phần DB đồng bộ giữa các `await` trên UI context.
- **Hướng sửa:** báo "đang bận" khi lệnh bị từ chối. Truyền token và đưa tiến độ vào `IOperationProgressService`.

### LIB-17 (Low): FEATURES.md (§5) lệch so với code

- §5.1 viết "Grid of tracked shows/movies", nhưng thực tế là danh sách dọc (`VirtualizingStackPanel`).
- §5.9 viết "one Library detail refresh" cho pack link, nhưng `RunPackLinkAsync` gọi `ReloadSelectedDetailAsync` và đồng thời `PackReconciled`/`SymlinkStateChanged` kích thêm một lần rebuild debounce.
- §5.5 chỉ ghi DB `TrackedShows`, trong khi rating và watch status của phim nằm ở `TrackedMovies`.
- §5.1 "keeps the last complete pane": xem LIB-15.

### LIB-18 (Low): card ẩn rating 0.0

- `LibraryMediaCardViewModel.HasRating => Rating is > 0` (L104), trong khi `RatingMin = 0.0` là giá trị hợp lệ và detail hiển thị "0.0". Nên dùng `Rating.HasValue`.

## 5. Trả lời theo câu hỏi

**(2) Lọc, sắp xếp, phân trang, tìm kiếm.**
- Library **không phân trang**. `HallOfFamePager` thuộc Stats, còn `ViewModels/Filters/*` (SourceItem filter) không được Library dùng, nên chỉ ghi nhận.
- Search chỉ so `Title.Contains`, không xét alternative titles. Đây là hành vi, không phải lỗi.
- Không có item trùng: khóa là `(Id, MediaKind)`, và show cùng movie được nối với nhau.
- Chọn item rồi đổi filter:
  - Nếu item vẫn khớp, selection được giữ nhưng detail bị xóa rồi dựng lại (LIB-01).
  - Nếu item bị lọc ra, selection về `null` và detail trống. Đây là chủ ý (comment L2461) để tránh cascade status.
  - `RefreshLibrary` lại tự chọn card đầu (`??=` L378). Hai chính sách khác nhau này nên được thống nhất.

**(3) Lệnh chạy hai lần, chạy khi đang refresh, lỗi giữa chừng.**
- `AsyncRelayCommand` chặn chạy lại cùng một lệnh. Các lệnh khác nhau vẫn chạy chồng nhau được, ví dụ Link E1 trong lúc Reset E2. Cả hai đều kết thúc bằng `ReloadSelectedDetailAsync` cho card **đang** chọn tại thời điểm xong, nên không ghi sai.
- Lỗi giữa chừng:
  - Lệnh async có try/catch và báo `StatusMessage`.
  - Lệnh đồng bộ thì làm app thoát (LIB-10).
  - Toggle pack mode có thể để UI lệch DB (LIB-10).
- Lệnh "mở trong Jellyfin" không ghi gì nên không gây lệch dữ liệu, chỉ có vấn đề cache (LIB-13).
- **Ghi chú review, không phải finding:** `DeleteSelectedMedia` không bị chặn khi đang có pack link hoặc AI link (đang chờ Gemini) cho cùng show đó. Sau khi xóa, `ApplySeasonPackLinkAsync` vẫn ghi link. Điểm này cần phiên deletion-audit xác minh ở tầng service.

**(4) Hiệu năng.**
- Card list có virtualization (`VirtualizingStackPanel`, Recycling) và không tải poster, nên ổn.
- Season và episode dùng `ItemsControl` không virtualize, nằm trong `ScrollViewer`. Show dài (hơn 1000 tập) với nhiều season mở cùng lúc sẽ tạo toàn bộ row.
- Chi phí chính nằm ở LIB-02 (quét DB và đĩa), LIB-01 (dựng lại khi chỉ một item đổi) và `RefreshLibrary` dựng lại mọi card khi chỉ một field đổi (series status).
- Rò rỉ bộ nhớ: LIB-14. Mỗi row VM giữ closure `UpdateSeasonManagementMode` trỏ về VM singleton. Đây không phải rò rỉ vì detail cũ bị bỏ cả cụm.

## 6. Test còn thiếu

Hiện **không có test nào** cho `LibraryViewModel`, các season/episode/card VM, `MediaCardCatalogService` hay `PosterImageService`. `MediaManager.App.Tests` chỉ có test cho Auto-Track, WARP và TMDB. Đề xuất, xếp theo giá trị:

1. **Selection ổn định qua `ApplyMediaCardFilterAndSort`** (LIB-01): với view model có fake services, đổi rating, sort hoặc search thì `LoadSelectedMediaAsync` không chạy lại, và `SelectedShow.Seasons[i].IsExpanded` được giữ. Cần một harness `Selector`, hoặc tách logic filter ra để test thuần.
2. **Load detail thất bại** (LIB-03): `GetShows()` không chứa id của card thì detail bị xóa, cờ suppress được trả lại, và `UpdateRating` không bao giờ được gọi với id khác `SelectedMediaCard.Id`.
3. **Race poster** (LIB-04): `LoadAsync` giả lập chậm cho A và nhanh cho B thì `SelectedPosterImage` là của B.
4. **CanExecute** (LIB-09): `CanExecuteChanged` của các lệnh selection được raise khi `SelectedMediaCard` đổi từ null sang có giá trị và giữa show với movie.
5. **Định nghĩa Available** (LIB-07): bảng SourceItem state × file tồn tại × mong đợi.
6. **Lọc theo provider trước `File.Exists`** (LIB-02): đếm số lần gọi file-exists qua một abstraction (`IFileSystem`) cho detail của một show trong thư viện 2 show.
7. **Rebuild do reconcile giữ editor đang mở** (LIB-05).
8. **Watch status** (LIB-08): hai lần đổi liên tiếp trong cùng một frame dispatcher đều được persist.
9. **`JellyfinMediaNavigationService`** (LIB-13): 404 thì xóa entry khỏi cache.

## 7. Thứ tự sửa đề xuất

1. **LIB-01**: gốc của 3 chủ đề lặp lại. Sửa xong thì các cờ `_suppress*` có thể đơn giản hóa, và LIB-08, LIB-12 nhẹ đi.
2. **LIB-03 + LIB-10**: chặn ghi sai title và chặn app thoát. Thay đổi nhỏ: try/finally, kiểm tra id, helper persist.
3. **LIB-02**: đảo thứ tự điều kiện (thay đổi 4 dòng, lợi ích lớn), rồi dựng detail off-thread và bỏ refresh khi tab không active.
4. **LIB-09**: thêm `[NotifyCanExecuteChangedFor]`, rủi ro thấp.
5. **LIB-04, LIB-05, LIB-11**: gom vào một "detail load controller" có generation và commit editor trước khi đổi hoặc rebuild.
6. **LIB-06 + LIB-07**: cần quyết định định nghĩa Available, phối hợp với database-audit và autotrack-audit.
7. **LIB-08**: làm sau khi LIB-01 xong. Đổi cơ chế bind ComboBox.
8. **Low**: LIB-12 … LIB-18.

## 8. Tham chiếu chéo

- **lifecycle-audit:** `OnDispatcherUnhandledException` không đặt `Handled` (LIB-10); handler sự kiện chạy khi tab không active (LIB-02).
- **database-audit:** `RefreshAvailability` ghi trên UI thread và O(show × item) (LIB-02, LIB-07); truy vấn `GetOrderCount` cho mỗi card.
- **deletion-audit:** xóa trong lúc pack link đang chạy (mục 5.3); card cũ sau khi xóa ở nơi khác (LIB-03).
- **autotrack-audit:** reconcile hoặc symlink từ Auto-Track kích rebuild Library (LIB-02, LIB-05); TMDB refresh nền không báo về Library (mục 3).
