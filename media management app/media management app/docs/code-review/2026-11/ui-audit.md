# Audit UI/XAML và các workspace phụ (2026-11)

- **Baseline:** `ff2caa3` trên nhánh `claude/ui-audit-b26564` (cây sạch), ngày 2026-11-12.
- **Phạm vi:** News, Stats, Find/Add, phần UI của Auto-Track, Console log window, WebViewer, các dialog (`AppMessageBoxWindow`, `AddMediaLockDialog`, `EnginePickerDialog`, `PackLink*Window`, `TorrentAddDiskDialog`, `QueryTemplateBuilderDialog`), `MainWindow`, `Views/Controls/*`, `Resources/*`, `Converters/*`, `App.xaml`, cùng các ViewModel tương ứng.
- **Ngoài phạm vi:** Settings và Library (có phiên riêng). Search, Recipe workspace và Torrent workspace (đang sửa trên `feat/native-search`) chỉ được nhắc ở mức UI chung. Logic Auto-Track, xóa file, Database, lifecycle, TMDB/Jellyfin/WARP, Backup và build/CI thuộc các phiên khác; báo cáo này chỉ nhắc khi lỗi bắt nguồn từ UI.
- **Thang mức độ:** theo [`../00-method-and-scope.md`](../00-method-and-scope.md).
- **Cách làm:** đọc tĩnh XAML, code-behind và ViewModel; dò binding thủ công giữa XAML và ViewModel (có subagent hỗ trợ); đọc lịch sử git của các file trong phạm vi; đối chiếu mọi giá trị `Kind` của `PackIconLucide` với enum trong `MahApps.Metro.IconPacks.Lucide` 6.2.1.
- **Không chạy app** và không chạy `dotnet build/test`: tránh tải máy dùng chung với các phiên song song, và không có state folder giả lập sẵn. Những điểm cần xác nhận lúc chạy được ghi là *chưa xác minh runtime*.
- **Không sửa code app.**

## 1. Tóm tắt

| ID | Mức | Tiêu đề |
|---|---|---|
| UI-01 | High | Find/Add: luồng *Add to Library* đọc lại `SelectedResult` sau `await`, nên có thể thêm nhầm phim hoặc crash app |
| UI-02 | Medium | Exception thoát khỏi command hoặc handler UI làm tắt process, vì `DispatcherUnhandledException` không bao giờ được `Handled` |
| UI-03 | Medium | Đổi theme lúc chạy chỉ đổi được một phần: gần như toàn bộ brush là `StaticResource`, nên shell giữ palette cũ đến khi khởi động lại |
| UI-04 | Medium | `RunCompleted` gọi `Dispatcher.Invoke` đồng bộ từ thread scheduler: UI bị khóa khi tắt app, và exception của UI bị ghi nhầm thành lỗi scheduler |
| UI-05 | Medium | News và Auto-Track dựng lại dashboard đồng bộ trên UI thread (N+1 truy vấn DB, IO file, list không virtualize) ở mỗi lần điều hướng và mỗi lần chạy xong |
| UI-06 | Medium | Lỗi khi tải Stats (qua `UiBusyWork`) làm crash app thay vì hiện trạng thái lỗi |
| UI-07 | Low | Virtualization của heatmap Stats không có tác dụng; danh sách media trong Find/Add không virtualize |
| UI-08 | Low | `Clipboard.SetText` không được bọc ở 3 nơi nên crash khi clipboard bị khóa |
| UI-09 | Low | News lưu `settings.json` mà không bắt lỗi IO khi đổi sort, đổi view hoặc tắt reminder |
| UI-10 | Low | Kéo cửa sổ đang phóng to (MainWindow, WebViewer) trộn pixel thiết bị với DIP và ép `Top >= 0` |
| UI-11 | Low | Owner và vị trí dialog không nhất quán: Console dùng `CenterOwner` nhưng không có owner, dialog kích thước cố định có thể vượt màn hình nhỏ |
| UI-12 | Low | WebViewer: khởi tạo hỏng thì cửa sổ trắng trơn, lỗi môi trường bị cache vĩnh viễn, F5 lúc đang khởi tạo đăng ký handler hai lần |
| UI-13 | Low | Console Classic: `ScrollIntoView` theo chuỗi, luôn ép cuộn xuống cuối, làm việc thừa khi cửa sổ đang ẩn |
| UI-14 | Low | Auto-Track và News tính "tuần này" theo hai múi giờ khác nhau (fix `17c5cfa` mới áp cho News) |
| UI-15 | Low | Thiếu trạng thái rỗng ở News và Auto-Track; pill ổ đĩa trông bấm được nhưng không làm gì |
| UI-16 | Low | Bàn phím: CheckBox mất focus visual, chip "click to copy" chỉ dùng được bằng chuột |
| UI-17 | Low | Query template builder không có đường hủy: Esc và nút X đều lưu |
| UI-18 | Low | Path binding `IsSelected` hỏng trên nút overflow của sidebar (lỗi binding duy nhất trong khoảng 900 binding đã dò; chi tiết ở §4) |

Không có finding nào ở mức Critical. Thứ tự sửa đề xuất ở §6.

## 2. Lịch sử bug và hotspot

### 2.1 Số commit theo file (HEAD, theo dõi rename)

| File | Commit |
|---|---|
| `App.xaml.cs` | 51 (khoảng 8 commit liên quan UI, theme hoặc window) |
| `MainWindow.xaml` | 24 |
| `ViewModels/MainViewModel.cs` | 22 |
| `Resources/AppStyles.xaml` | 14 |
| `MainWindow.xaml.cs` | 13 |
| `ViewModels/NewsViewModel.cs` | 10 |
| `Views/NewsView.xaml` | 9 |
| `Resources/ViewTemplates.xaml` | 8 |
| `Views/AutoTrackView.xaml`, `FindAddViewModel.cs`, `WorkspaceSharedTemplates.xaml`, `App.xaml` | 7 mỗi file |
| `Views/FindAddView.xaml` | 5 |
| `WebViewerWindow.*`, `StatsView.xaml`, `StatsViewModel.cs`, `ConsoleLogWindow.xaml`, `StorageStatusViewModel.cs`, `AppThemeColors.*.xaml` | 4 mỗi file |
| Dialog, `Views/Controls/*`, converter | 1–2 mỗi file |

- `git log --follow` báo 15 commit cho `WebViewerWindow.xaml.cs`. Con số này sai: ở `41877bf` git nhận nhầm file là bản sao của `MainWindow.xaml.cs` (giống khoảng 52%). Lịch sử thật là `41877bf` → `3708215` → `6aa968f` → `2a93af3`.

### 2.2 Lịch sử không ghi lại bug UI

- 148 commit trên HEAD (2026-05-14 → 2026-11-03).
- Chỉ 4 commit có tiêu đề kiểu "fix" chạm vào các file trong phạm vi: `0cd29b2`, `f72a7a9`, `2a09c78`, `d16a334`. Không commit nào trong số đó sửa bug UI.
- Không có commit revert và không có commit "hotfix".
- Bug UI thường được sửa ngay trong commit tính năng làm phát sinh ra nó. Bài học được ghi lại dưới dạng luật trong `docs/ui-polish/*.md` (`847a537`, 2026-09-09).
- Hệ quả: không thể dựa vào `git log --grep fix` để biết vùng nào hay hỏng. Các lớp bug dưới đây được rút ra bằng cách đọc diff.

### 2.3 Các lớp bug lặp lại

| Lớp bug | Commit | Tình trạng hiện tại |
|---|---|---|
| **Màu hard-code, theme, Static hay Dynamic** (lặp 3 lần) | `a79f6fb` đổi 23 mã hex sang `StaticResource`. `07b6978` thêm dark mode: bỏ 55 mã hex, thêm 86 tham chiếu Static, 0 Dynamic. `1136683` chuyển riêng ContextMenu sang `DynamicResource`. | **Fix dở dang.** ToolTip (`AppStyles.xaml:53-55`) và MenuItem (`:129`, `:139`, `:142`) vẫn dùng Static. Toàn app vẫn chủ yếu dùng Static. Xem UI-03. |
| **TwoWay trên property chỉ có getter, crash `Run.Text`** | Ghi lại trong `BINDINGS.md`. `f9761a0` thêm `Mode=OneWay` để phòng xa. | Đã ổn: cả 21 `<Run Text="{Binding}">` đều là `OneWay`. |
| **Layout nhảy hoặc phình** | Ghi lại trong `LAYOUT.md`. Đã sửa ngay trong commit tính năng. | Không thấy tái phát. |
| **Hiệu năng, đứng UI khi điều hướng** | `29cfac8` thêm `WorkspaceViewCache`. `177cd5b` thêm `OnNavigatedTo` và TTL 2 phút cho News. `13dd96e` chuyển Stats và Library sang `UiBusyWork` với `Task.Run`. | **Fix dở dang.** News và Auto-Track vẫn đồng bộ trên UI thread (UI-05). |
| **Múi giờ của "tuần này"** | `17c5cfa` đổi News sang `AppTimeZone.Today`. | **Fix dở dang.** Auto-Track vẫn dùng `DateTime.UtcNow`/`DateTime.Now` (UI-14). |
| **Kéo title bar nuốt click** | `3708215` loại trừ `ReloadButton`. `6aa968f` thêm `WarpStatusPill` vào danh sách loại trừ. | Danh sách phải tự cập nhật bằng tay mỗi khi thêm control vào header (`WebViewerWindow.xaml.cs:153-154`). Ghi chú ở §5. |
| **ComboBox hiển thị sai giá trị chọn** | `64d7a85` sửa status combobox desync trong Library. Một ngày sau, `56f64cf` đổi `ContentSite` sang `ItemTemplate`. | Ổn. |
| **Crash native PenIMC** | `f72a7a9`: tắt stylus, rồi tách DLL ra khỏi bundle. `2a09c78`: `BundleExtractionGuard`. | Ngoài phạm vi UI; ghi lại vì có chuỗi fix nối tiếp. |
| **Footer pill thay đổi liên tục** | `a8431f8` thêm `StatusPillButtonStyle` inline, `6aa968f` xóa nó. Nút Jellyfin trong News được thêm ở `41877bf` rồi bỏ ở `0655be7`. | Chỉ là thay đổi tính năng, không phải bug. |

### 2.4 Hotspot

1. **`MainWindow.xaml` và `MainWindow.xaml.cs`** (24 và 13 commit): custom chrome, sidebar overflow, footer pill, theme. Liên quan UI-03 và UI-10.
2. **`AppStyles.xaml`** (14 commit): implicit style toàn cục cho `Button`, `TextBlock`, `CheckBox`, `TextBox`, `ComboBox`. Mỗi thay đổi ở đây lan ra mọi view. Liên quan UI-03 và UI-16.
3. **`NewsViewModel.cs`** (10 commit): refresh, sort, múi giờ. Fix bị áp dở ở cả ba trục: thread (UI-05), múi giờ (UI-14) và lưu settings (UI-09).
4. **`MainViewModel.cs`** (22 commit): status timer và điều hướng. Xem ghi chú §5.

## 3. Finding

### UI-01 — Find/Add: *Add to Library* đọc lại lựa chọn hiện tại sau `await` (High)

**Code liên quan**

- `ViewModels/FindAddViewModel.cs:258-337` (`AddSelectedToLibraryAsync`).
- `ViewModels/FindAddViewModel.cs:686-699` (`CanSearch`, `CanAddSelectedToLibrary`).
- `Views/FindAddView.xaml:635-636` (`ListBox SelectedItem="{Binding SelectedResult}"`).

**Bằng chứng**

- Lệnh dùng property `SelectedResult` và `SelectedRecipeId` ở mọi bước, kể cả sau các lần `await` gọi mạng:
  - `AddMovieAsync` (`:284`), sau đó `UpdateRecipe(movie.Id, SelectedRecipeId)` (`:285`);
  - `GetEpisodeGroupsAsync(SelectedResult.TmdbId)` (`:291`), sau đó `AddShowAsync(SelectedResult.ToShowSearchResult(), …)` (`:316`);
  - `SelectedResult.IsAlreadyAdded = true` (`:323`).
- Trong lúc chờ mạng, UI vẫn tương tác được:
  - ListBox kết quả không bị khóa;
  - `SearchCommand` chỉ kiểm tra `SearchText` nên vẫn chạy được. Search mới gọi `SearchResults.Clear()` và đặt `SelectedResult = null` (`:163-164`).
- Khối `catch` ở `:332` cũng đọc `SelectedResult.Title`.

**Kịch bản lỗi**

1. **Thêm nhầm phim.** Chọn show A, khi chưa có episode group thì không có dialog nào hiện ra. Trong lúc `GetEpisodeGroupsAsync` chờ mạng (WARP hoặc TMDB chậm vài giây), người dùng bấm vào show B. Kết quả: show **B** được thêm vào thư viện, dù dialog khóa (`AddMediaLockDialog`) đã xác nhận tên A. B nhận recipe hiện tại; A vẫn hiện "Add to Library".
2. **Gán nhầm recipe.** Với phim, nếu đổi lựa chọn trong lúc `AddMovieAsync` chạy, phim A được thêm nhưng nhận `SelectedRecipeId` của mục B. Recipe đó có thể là recipe dành cho TV.
3. **Crash.** Chạy Search mới (Enter trong ô tìm) trong lúc đang thêm. `SelectedResult` thành `null`, gây `NullReferenceException` ở `:316` hoặc `:323`. Sau đó `catch` ở `:332` ném tiếp `NullReferenceException` từ chính khối catch. Exception thoát khỏi `AsyncRelayCommand`, đi tới `DispatcherUnhandledException` không được xử lý (UI-02), và process thoát. Ở nhánh phim, bản ghi đã được ghi vào DB trước khi crash.

**Tác động:** sai dữ liệu thư viện (sửa được bằng tay) hoặc crash app. Không mất media.

**Độ tin cậy:** cao, chứng minh tĩnh. Chưa tái hiện runtime.

**Hướng sửa**

- Chụp `var target = SelectedResult; var recipeId = SelectedRecipeId;` ngay đầu lệnh và chỉ dùng hai biến này về sau, kể cả trong `catch`.
- Khóa list và `SearchCommand` khi `IsAddingToLibrary`: thêm `!IsAddingToLibrary` vào `CanSearch` và `NotifyCanExecuteChangedFor`.

### UI-02 — Exception từ UI làm tắt process (Medium)

**Code liên quan**

- `App.xaml.cs:603-615` (`OnDispatcherUnhandledException` chỉ ghi crash log, không đặt `e.Handled = true`).
- CommunityToolkit `RelayCommand` và `AsyncRelayCommand` mặc định ném lại exception trên dispatcher.

**Bằng chứng:** mọi exception thoát khỏi command, handler `Click` hoặc lambda `async void` trên UI thread đều kết thúc process. Trong phạm vi audit có các nguồn cụ thể sau:

| Nguồn | Vị trí | Điều kiện |
|---|---|---|
| Find/Add Add | UI-01 | Đổi lựa chọn hoặc search trong lúc add |
| Stats load | `Common/UiBusyWork.cs:59-79` (UI-06) | Lỗi DB hoặc lỗi build overview |
| Clipboard | UI-08 | Clipboard đang bị app khác giữ |
| News lưu settings | UI-09 | IO lỗi khi ghi `settings.json` |
| Status timer | `MainViewModel.cs:144` `Tick += async (_, _) => await RefreshStatusAsync()` | `DeviceStatusService.RefreshCoreAsync` ném lỗi, vì không có try/catch tổng |
| `ToggleWarp` | `MainViewModel.cs:314-340` | `AcquireAsync`/`ForceDisconnectAsync` ném lỗi (thuộc vùng WARP) |

**Tác động:** một lỗi cục bộ của một nút làm tắt cả app, kéo theo các tác vụ nền đang chạy. Recovery của Auto-Track có tồn tại, nhưng người dùng vẫn mất phiên làm việc.

**Độ tin cậy:** cao về cơ chế. Mỗi nguồn kích hoạt có điều kiện riêng.

**Hướng sửa (không làm đổi tính năng):** bắt lỗi ở từng command có IO hoặc mạng, rồi đưa lỗi vào `StatusMessage` hoặc `AppMessageBox`. Việc có `Handled` toàn cục hay không là quyết định của phiên lifecycle; báo cáo này chỉ nêu bề mặt lỗi phía UI.

### UI-03 — Đổi theme lúc chạy chỉ đổi được một phần (Medium)

**Code liên quan**

- `Services/ThemeService.cs:27-57` thay merged dictionary `AppThemeColors.*` lúc runtime, đồng thời đổi theme MaterialDesign, WPF-UI và LiveCharts.
- Được gọi từ `SettingsViewModel.cs:525` khi bấm Save và từ `FirstRunViewModel.cs:452` để xem trước theme.
- `Views/SettingsView.xaml:518` hứa: "Theme changes apply after you click Save".

**Bằng chứng**

- Số tham chiếu `StaticResource AppBrush*`: `MainWindow.xaml` 62, `NewsView` 52, `FindAddView` 51, `StatsView` 42, `AutoTrackView` 17, `FirstRunWindow` 29, các dialog 11–39, `AppStyles.xaml` 139.
- `DynamicResource` chỉ có 8 trong `AppStyles` (ContextMenu, từ `1136683`) và 72 trong `ConsoleLogWindow`.
- `StaticResource` được resolve một lần, nên khi dictionary bị thay, các phần tử đã tạo vẫn giữ brush cũ.
- `WorkspaceViewCache` giữ instance view suốt phiên, nên kể cả khi điều hướng sang tab khác rồi quay lại, palette vẫn không được làm mới.
- Các converter `ResourceKeyToBrushConverter`, `UserWatchStatusToBrushConverter` và `EpisodeRatingBandCatalog.ResolveBrush` gọi `TryFindResource` lúc convert, nên chỉ cập nhật khi binding được đánh giá lại.
- `docs/ui-polish/THEME.md` yêu cầu "Use `StaticResource AppBrush*` only". Tài liệu này mâu thuẫn với việc đổi theme lúc chạy.

**Kịch bản lỗi**

- Light → Dark, bấm Save: control của MaterialDesign và WPF-UI (TextBox, ComboBox, popup, scrollbar) chuyển sang tối, nhưng nền shell, card và chữ dùng `AppBrush*` vẫn sáng.
- Kết quả là giao diện lai: chữ màu sáng của MaterialDesign nằm trên `AppBrushSurfaceRaised` sáng, nên tương phản rất thấp.
- FirstRun đổi theme ngay khi chọn, nên cửa sổ FirstRun cũng bị lai như vậy.
- Khởi động lại thì hết lỗi.

**Lỗi cùng lớp (hex hard-code):** `Views/Controls/StatsHeatmapSeasonStrip.xaml:13` (`#88FFFFFF`) và `:44` (`#22FFFFFF`). Đây là viền và vệt sáng trắng cố định, không theo theme. Mã hex trong `ConsoleLogWindow` Classic (`#111111`, `#d6d6d6`…) là chủ ý giữ dáng terminal.

**Độ tin cậy:** cao về cơ chế. Mức độ hiển thị lai cụ thể chưa xác minh runtime.

**Hướng sửa:** chọn một trong hai.

- (a) Chuyển brush theo theme sang `DynamicResource`, bắt đầu từ `AppStyles.xaml` và shell, rồi sửa `THEME.md`.
- (b) Không hỗ trợ đổi lúc chạy: đổi câu chữ ở `SettingsView.xaml:518` thành "cần khởi động lại", và không gọi `Apply` cho đến lần khởi động sau.

Cách (b) rẻ và không đổi tính năng.

### UI-04 — `RunCompleted` gọi `Dispatcher.Invoke` đồng bộ (Medium)

**Code liên quan**

- `ViewModels/NewsViewModel.cs:41-44` và `ViewModels/AutoTrackViewModel.cs:42-45`: `Application.Current.Dispatcher.Invoke(UpdateDashboard / RefreshDashboard)`.
- Event phát từ `Services/AutoTrackSchedulerService.cs:250`, `:268`, `:290`, `:300`, bên trong `try` của vòng scheduler.
- Khi tắt app: `App.xaml.cs:267` gọi `AutoTrackSchedulerService.Dispose()` trên UI thread, hàm này chờ `_worker.Wait(8 s)` (`AutoTrackSchedulerService.cs:75`).

**Kịch bản lỗi**

1. **Tắt app đúng lúc một chu kỳ vừa xong.** Worker đang đứng trong `Dispatcher.Invoke` chờ UI thread, còn UI thread đang đứng trong `Dispose` chờ worker. Kết quả là deadlock có giới hạn: app treo khoảng 8 giây rồi log "did not stop within 8 seconds". Nếu dispatcher đã shutdown, hoặc `Application.Current` đã là `null`, thì `Invoke` ném lỗi.
2. **Exception trong handler UI bị ghi nhầm.** Ví dụ `UpdateDashboard` gặp lỗi DB. Exception đi ngược về thread scheduler, rơi vào `catch (Exception)` và được ghi thành "TMDB discovery failed" / "Reconcile failed". Khối catch lại gọi `NotifyRunCompleted` lần nữa (`:268`/`:300`). Lần này handler ném tiếp *bên ngoài* try, làm task của vòng lặp bị fault. Phiên Auto-Track nên xác nhận ảnh hưởng tới vòng lặp.

**Độ tin cậy:** trung bình–cao. Chuỗi gọi chứng minh tĩnh được; chưa tái hiện runtime.

**Hướng sửa**

- Dùng `Dispatcher.BeginInvoke` hoặc `InvokeAsync` (không chờ) và bỏ qua khi `Application.Current` là `null`.
- Bắt lỗi bên trong handler UI.

### UI-05 — Dashboard News và Auto-Track dựng lại đồng bộ trên UI thread (Medium)

**Code liên quan**

- `NewsViewModel.UpdateDashboard`: `ViewModels/NewsViewModel.cs:228-292`.
- `AutoTrackViewModel.RefreshDashboard`: `ViewModels/AutoTrackViewModel.cs:130-197`.
- Được gọi từ `OnNavigatedTo` (News có TTL 2 phút, Auto-Track thì lần nào cũng gọi), từ `RunCompleted` (UI-04), và từ `RunNow`.

**Bằng chứng**

- Cả hai làm theo kiểu N+1: mỗi show một lần `GetEpisodes`, cộng thêm truy vấn cart cho từng episode qua `TryGetAutoTrackHuntBlockingEpisodeOrder` và `HasActiveManualEpisodeOrder`.
- Auto-Track gọi `GetEpisodes(show.Id)` **hai lần** cho mỗi show (`:154` và `:182`) và thêm `GetOrders` cho mỗi show (`GetLastHuntFailureDetail`).
- News chạy `_posterImageService.DeleteStillsNotIn(...)` (xóa file) trên UI thread (`:291`).
- Kết quả được đổ vào `ItemsControl` nằm trong `ScrollViewer`, không virtualize: `NewsView.xaml:242-243` và `:449-450`, `AutoTrackView.xaml:183-194` và `:429-430`. Mỗi card Auto-Track có ComboBox và editor override.
- Stats và Library đã được chuyển khỏi UI thread ở `13dd96e`. News và Auto-Track bị bỏ sót.

**Tác động:** đứng UI tăng tuyến tính theo số show (vài trăm show nghĩa là hàng nghìn truy vấn đồng bộ) mỗi lần mở tab và mỗi chu kỳ scheduler. Vì sự kiện scheduler gọi `Invoke` đồng bộ (UI-04), worker nền cũng bị giữ lại trong suốt thời gian đó.

**Độ tin cậy:** cao về cấu trúc. Chưa đo thời gian thực tế.

**Hướng sửa:** làm giống `13dd96e`. Gom truy vấn (lấy toàn bộ episode của các show auto-track bằng một query), dựng model bằng `Task.Run`, rồi chỉ gán collection trên UI thread. Chuyển `DeleteStillsNotIn` ra nền. Dùng `ItemsControl` có `ScrollViewer` và `VirtualizingStackPanel` riêng, hoặc `ListBox`.

### UI-06 — Lỗi khi tải Stats làm crash app (Medium)

**Code liên quan**

- `Common/UiBusyWork.cs:59-79`: lambda `async` truyền vào `Dispatcher.BeginInvoke(DispatcherPriority, Action)` thực chất là `async void`. Nó chỉ bắt `OperationCanceledException`.
- `ViewModels/StatsViewModel.cs:246-264`: `Task.Run` đọc DB (`GetTrackedShows`, `GetTrackedMovies`, `GetAllEpisodeUserRatings`) và chạy `PersonalRatingOverviewBuilder.Build`.

**Kịch bản lỗi:** DB bị khóa hoặc hỏng, hoặc builder ném lỗi với dữ liệu lạ. `finally` tắt overlay, rồi exception được ném lại qua `async void` → `DispatcherUnhandledException` → process thoát. Màn hình Stats không có trạng thái lỗi nào. `LibraryViewModel` dùng cùng helper, nhưng đó là vùng của phiên Library.

**Độ tin cậy:** cao về cơ chế.

**Hướng sửa:** trong `UiBusyWork.Run`, bắt `Exception`, ghi log và trả lỗi qua callback (ví dụ `onError`) để VM hiện thông báo. Dispose `cts` khi xong.

### UI-07 — Virtualization không có tác dụng (Low)

**Bằng chứng**

- `Views/StatsView.xaml:868-880`: `ItemsControl` của `HeatmapRows` khai báo `VirtualizingStackPanel` và `VirtualizationMode=Recycling`. Nhưng template chỉ có `ItemsPresenter`, đặt trong `StackPanel` của `StatsScroller` (`:330`). Panel nhận chiều cao vô hạn nên mọi dòng đều được tạo; mỗi dòng còn gọi `LoadPosterAsync` (`StatsViewModel.cs:310-316`).
- `StatsHeatmapSeasonStrip` có "pump" tự dựng cell dần dần, giúp giảm tải nhưng không thay được virtualization theo dòng.
- `Views/FindAddView.xaml:389-391`: `ExistingMediaCards` (toàn bộ thư viện) nằm trong `ItemsControl` không virtualize, và bị dựng lại sau mỗi lần thêm hoặc `OnNavigatedTo`.

**Tác động:** thời gian mở tab và bộ nhớ tăng theo kích thước thư viện.

**Hướng sửa:** không cần đổi tính năng.

- Find/Add: `ListBox`/`ItemsControl` với `ScrollViewer` riêng và `CanContentScroll=True`.
- Stats: vì cả trang là một `ScrollViewer`, hoặc chấp nhận hiện trạng và bỏ các thuộc tính virtualization gây hiểu nhầm, hoặc phân trang heatmap.

### UI-08 — `Clipboard.SetText` không được bọc (Low)

**Code liên quan:** `ViewModels/FindAddViewModel.cs:412`, `ViewModels/LibraryViewModel.cs:1220`, `Views/ConsoleLogWindow.xaml.cs:104`.

**Bằng chứng:** WPF ném `COMException` (`CLIPBRD_E_CANT_OPEN`) khi một process khác đang mở clipboard, ví dụ trình quản lý clipboard hoặc RDP. Exception này đi thẳng tới UI-02, gây crash khi bấm chip tên thay thế hoặc copy log.

**Hướng sửa:** helper thử lại 2–3 lần, bắt `ExternalException`, rồi báo bằng `StatusMessage`.

### UI-09 — News lưu settings mà không bắt lỗi IO (Low)

**Code liên quan:** `ViewModels/NewsViewModel.cs:186` (`DismissSetupReminder`), `:345` (`PersistNewsSortMode`), `:357` (`PersistTrackedShowViewMode`). `SettingsService.Save` ghi file `.tmp` rồi `File.Move` (`Services/SettingsService.cs:113-131`) và có thể ném `IOException` hoặc `UnauthorizedAccessException`, ví dụ khi antivirus hoặc trình đồng bộ đang giữ file.

**Bằng chứng:** `ConsoleLogViewModel.SaveSettings` (`:345-366`) đã bọc try/catch. News thì không, nên một lần bấm nút sort có thể làm crash app (UI-02).

**Hướng sửa:** bọc try/catch như Console.

### UI-10 — Kéo cửa sổ đang phóng to trộn pixel thiết bị với DIP (Low)

**Code liên quan:** `MainWindow.xaml.cs:307-324` và bản chép tại `Views/WebViewerWindow.xaml.cs:197-214` (`RestoreWindowForDrag`).

**Bằng chứng**

- `PointToScreen` trả về pixel thiết bị, còn `Left`, `Top`, `ActualWidth` và `RestoreBounds` tính bằng DIP.
- Ở 125% hoặc 150% DPI, `horizontalRatio` và vị trí `Left`/`Top` lệch theo hệ số scale. Cửa sổ có thể bật ra xa con trỏ, hoặc nằm ngoài màn hình phụ.
- `Top = Math.Max(0, …)` giả định màn hình chính có Y bằng 0. Với màn hình đặt phía trên (Y âm), cửa sổ nhảy sang màn hình khác.

**Hướng sửa:** đổi sang DIP bằng `PresentationSource.CompositionTarget.TransformFromDevice` và kẹp theo work area của màn hình chứa con trỏ. Tách thành một helper dùng chung cho hai window.

### UI-11 — Owner và vị trí dialog không nhất quán (Low)

**Bằng chứng**

- `Views/ConsoleLogWindow.xaml:12` đặt `WindowStartupLocation="CenterOwner"`, nhưng `Services/ConsoleWindowService.cs:38-48` không gán `Owner`. WPF rơi về vị trí mặc định của hệ điều hành, không căn giữa.
- 12 chỗ gán `Owner = Application.Current.MainWindow` mà không kiểm tra (ví dụ `FindAddViewModel.cs:274`, `:302`; `LibraryViewModel.cs:957`, `:1011`). Trong khi đó `AppMessageBox.ResolveOwner` (`Common/AppMessageBox.cs:81-93`) đã kiểm tra `IsLoaded && IsVisible`.
  - Nếu main window đang thu nhỏ, dialog modal thuộc owner đang thu nhỏ có thể không hiện. App trông như bị treo cho đến khi khôi phục cửa sổ.
  - Luồng PackLink có thể mở review dialog sau một tác vụ dài, đúng lúc người dùng đã thu nhỏ app.
- Dialog `ResizeMode="NoResize"` với kích thước cố định: `QueryTemplateBuilderDialog` 920×640 (MinHeight 480), `FirstRunWindow` 720×580. Trên màn hình 1366×768 ở 125% (khoảng 1093×614 DIP trừ taskbar), đáy dialog vượt work area và không kéo nhỏ được.
- Dialog thư mục WinForms (`FolderBrowserDialog`) không có owner. Thuộc phạm vi Settings và Library; nêu ở đây để thống nhất.

**Hướng sửa:** helper `DialogOwner.Resolve()` dùng chung với `AppMessageBox`. Gán owner cho Console (hoặc dùng `CenterScreen`). Đặt `MaxHeight` theo `SystemParameters.WorkArea` hoặc cho phép resize.

### UI-12 — WebViewer: lỗi khởi tạo và reload (Low)

**Code liên quan:** `Services/JellyfinViewerService.cs:184-232`, `:290-308` và bản song song trong `Services/QbittorrentViewerService.cs:195-233`, `:267-287`.

**Bằng chứng**

1. Nếu `InitializeWebViewAsync` thất bại (không có WebView2 runtime, user-data folder bị khóa), lỗi chỉ được ghi log. Cửa sổ vẫn mở nhưng trống trơn, không có thông báo hay nút thử lại.
2. `_environmentTask ??= CreateEnvironmentAsync()` cache cả task đã fault. Mọi lần mở lại hoặc F5 sau đó đều thất bại cho đến khi khởi động lại app.
3. Nhấn F5 hoặc nút Reload khi `CoreWebView2` còn `null` (đang khởi tạo) sẽ gọi `InitializeWebViewAsync` lần hai. Field `_webView` bị thay bằng instance mới, nhưng continuation của lần đầu cũng đọc field này. Kết quả là `NewWindowRequested` và các handler khác bị gắn hai lần, nên link ngoài mở hai tab trình duyệt. `ReleaseWebView` chỉ gỡ được một lần.

**Hướng sửa**

- Hiện overlay lỗi trong `BrowserHost`.
- Xóa `_environmentTask` khi task fault.
- Bỏ qua Reload khi đang khởi tạo (thêm cờ `_initializing`).

### UI-13 — Console Classic: cuộn và làm việc thừa (Low)

**Code liên quan:** `Views/ConsoleLogWindow.xaml.cs:55-67`.

**Bằng chứng**

- `ScrollIntoView(lastLogLine)` dò theo giá trị chuỗi. Nếu có dòng trùng hệt (cùng giây, cùng nội dung), danh sách cuộn tới dòng *đầu tiên* trùng.
- Mỗi dòng log đều ép cuộn xuống cuối, kể cả khi người dùng đang cuộn lên để đọc. Chế độ Pretty đã xử lý đúng việc này.
- Handler vẫn `BeginInvoke` cho mọi dòng log khi cửa sổ đang ẩn (Hide thay vì Close).
- Bộ đệm đã có giới hạn (`AppConstants.MaxUiLogLines = 500`, `AppLogger.cs:218-231`), nên log **không** tăng vô hạn. Ở chế độ Pretty đang Pause, các dòng cũ vẫn bị cắt bởi giới hạn này mà không có thông báo.

**Hướng sửa:** cuộn theo index (`Items.Count - 1`), chỉ cuộn khi đang ở cuối, và bỏ qua khi `!IsVisible`.

### UI-14 — "Tuần này" tính theo hai múi giờ (Low)

**Bằng chứng**

- `17c5cfa` đổi News sang `AppTimeZone.Today` (UTC+7 cố định, `Services/AppTimeZone.cs`).
- `ViewModels/AutoTrackViewModel.cs:148` vẫn dùng `DateTime.UtcNow.Date.AddDays(-7)`, `:310` dùng `DateTime.Now.Date`. `AutoTrackShowCardViewModel.cs:257` cũng dùng `DateTime.Now`.
- Từ 00:00 đến 07:00 giờ Việt Nam, ngày UTC vẫn là hôm trước. Vì vậy danh sách "New this week" và số episode pending ở tab Auto có thể khác tab News.

**Hướng sửa:** dùng `AppTimeZone.Today` cho cả hai VM. Đây là fix dở của `17c5cfa`.

### UI-15 — Thiếu trạng thái rỗng, nút không làm gì (Low)

**Bằng chứng**

- `NewsView.xaml`: không có trạng thái rỗng cho "New This Week" và "Tracked shows". Khi chưa auto-track show nào, hai panel đều trống.
- `AutoTrackView.xaml` chỉ có trạng thái rỗng cho kết quả tìm kiếm (`:167`), không có cho trường hợp chưa có show hoặc không có episode mới.
- Pill ổ đĩa ở footer (`MainWindow.xaml:552-600`) luôn bật và có con trỏ tay. Nhưng `OpenDriveFolder` không làm gì khi `OpenFolderPath` là `null` (`MainViewModel.cs:359-372`), và command không có `CanExecute`.
- `StorageStatusViewModel.StatsDisplay` ra `"0 B free ()"` khi `TotalBytes <= 0` (`:41-44`).
- `MainViewModel.ApplyDeviceStatus` xóa rồi tạo lại `DriveStatuses` mỗi 30 giây (`:576-588`). Tooltip đang mở trên pill bị đóng giữa chừng.

**Hướng sửa:** thêm `TextBlock` cho trạng thái rỗng, thêm `CanExecute` theo `OpenFolderPath`, và chỉ thay item khi dữ liệu thật sự đổi.

### UI-16 — Bàn phím và khả năng truy cập (Low)

**Bằng chứng**

- Implicit style `CheckBox` (`Resources/AppStyles.xaml:564-573`) đặt `FocusVisualStyle={x:Null}` mà không có trigger `IsKeyboardFocused` thay thế, nên khi Tab tới CheckBox không thấy focus. `Button` thì có trigger viền accent trong `AppButtonControlTemplate`.
- Chip "Click to copy" (`AppStyles.xaml:453-466`, `:468-500`) dùng `MouseBinding` trên `Border` nên không focus được và không kích hoạt được bằng bàn phím.
- Nút title bar của MainWindow có `Focusable="False"`. Đây là chấp nhận được cho caption button, ghi lại để biết.
- Không có `AutomationProperties.Name` cho các nút chỉ có icon trong các view thuộc phạm vi.

**Hướng sửa:** thêm trigger focus cho CheckBox, đổi chip sang `Button` dùng style chip, và thêm `AutomationProperties.Name` hoặc `ToolTip` cho nút icon.

### UI-17 — Query template builder không có đường hủy (Low)

**Code liên quan:** `Views/QueryTemplateBuilderDialog.xaml.cs:43-64` (`Esc` và nút X đều gọi `CommitAndClose` với `DialogResult = true`). Caller ở `RecipeWorkspaceViewModel.cs:777-786` bỏ qua `DialogResult` và luôn áp `ResultTemplates`.

**Tác động:** xóa nhầm hoặc sửa nhầm template trong dialog thì không có cách bỏ thay đổi. Esc thường được hiểu là Hủy, nhưng ở đây Esc lại lưu.

**Hướng sửa:** nếu đây là chủ ý thiết kế, ghi rõ trên UI ("Changes save automatically"). Nếu không, cho Esc trả `false` và để caller tôn trọng `DialogResult`. Recipe workspace đang được sửa trên `feat/native-search`, nên cần phối hợp với nhánh đó.

## 4. Binding, converter, resource và icon

**Cách dò**

- Một subagent dựng index mọi property public, property và command sinh bởi CommunityToolkit (kể cả partial class và lớp cơ sở), record và DependencyProperty.
- Mỗi file XAML được parse thành cây; DataContext được suy ra từ `DataType`, kiểu phần tử của `ItemsSource`, `RelativeSource`/`ElementName` và nơi dùng keyed style/template.
- Những chỗ công cụ không tự suy ra được thì dò bằng tay: `ICollectionView`, `ItemsSource` gán trong code-behind, template dùng chéo file.
- Tổng cộng khoảng 900 binding trong các file thuộc phạm vi.
- Script nằm ngoài repo (scratchpad của phiên), không commit.

**Kết quả**

| Hạng mục | Kết quả |
|---|---|
| Path không tồn tại | **1 lỗi thật**, xem **UI-18** bên dưới |
| TwoWay vào property chỉ có getter hoặc setter không public | Không có. 21 `Run.Text` đều `OneWay`. DataGrid của PackLink bind vào property `init` nhưng `IsReadOnly="True"`. |
| Converter sai kiểu | Không có. 58 `BooleanToVisibilityConverter` và 5 `BoolToVisibility` đều nhận bool. Các converter `*ToBrush` đều đổ vào property Brush. Không converter nào nằm trên binding TwoWay, nên việc thiếu `ConvertBack` không gây vấn đề. |
| Key `StaticResource`/`DynamicResource` chưa định nghĩa | Không có. Thứ tự merge (`AppThemeColors` → `AppStyles` → `WorkspaceSharedTemplates` → `ViewTemplates`) không có forward reference. Bộ key của Light và Dark giống hệt nhau. |
| `PackIconLucide.Kind` không hợp lệ | Không có. Mọi giá trị trong XAML, `Setter`, `IconKind = "..."` và `PackIconLucideKind.X` trong C# đều có trong enum 6.2.1 (1641 tên). |
| `UpdateSourceTrigger` | Các ô tìm kiếm dùng `PropertyChanged`, kèm debounce (Console) hoặc chạy khi bấm Enter (Find/Add, Auto-Track). Ô rating dùng `CommitTextBoxOnEnter`. Không thấy vấn đề. |
| Code-behind thao tác state | `AddMediaLockDialog` tự làm DataContext của chính nó, chấp nhận được với dialog nhỏ. `ConsoleLogWindow` chỉ điều khiển cuộn. `EnginePickerDialog` đồng bộ `SelectedItem` của `RankListBox` bằng tay sau khi di chuyển (`KeepSelectedRankInView`). Không thấy state bị lệch giữa View và ViewModel. |

### UI-18 — Path binding hỏng ở nút overflow của sidebar (Low)

- **Code liên quan:** `MainWindow.xaml:126`. `SidebarButtonStyle` có `DataTrigger Binding="{Binding IsSelected}"`.
- **Bằng chứng:** `SidebarOverflowButtonStyle` (`:146`) dùng `BasedOn` style này và được áp cho `OverflowButton` (`:421`). Nút đó nằm thẳng dưới window, nên DataContext là `MainViewModel`. `MainViewModel` không có `IsSelected`; chỉ `ShellNavigationItem` có.
- **Tác động:** mỗi lần áp style sinh một lỗi `BindingExpression path error` trong output, và trigger không bao giờ chạy trên nút này. Highlight đúng vẫn có nhờ `IsOverflowSelected` (`:148`), nên người dùng không thấy khác biệt. Đây là nhiễu khi dò lỗi binding.
- **Hướng sửa:** tách trigger `IsSelected` ra khỏi style gốc, hoặc cho `SidebarOverflowButtonStyle` kế thừa từ một style gốc không có trigger này.

**XAML chết (không phải lỗi):** `StatsView.xaml:117` `PosterCardTemplate` và `FindAddView.xaml:234` `SortSegmentButtonStyle` được định nghĩa nhưng không dùng.

## 5. Ghi chú review (chưa đủ bằng chứng để thành finding)

- **Phóng to cửa sổ custom chrome.** `MainWindow` và `WebViewerWindow` dùng `WindowStyle=None` + `WindowChrome` và chỉ đặt `ResizeBorderThickness=0` khi phóng to. Thường cần thêm margin bằng `SystemParameters.WindowResizeBorderThickness`, nếu không mép nội dung bị tràn ra ngoài màn hình khoảng 7–8 px. Chưa xác minh runtime.
- **Implicit `TextBlock` style ở cấp Application** (`AppStyles.xaml:601-604`: `Foreground=AppBrushText`, `TextWrapping=Wrap`). Style ở cấp App có thể áp vào `TextBlock` sinh ra bên trong template (ContentPresenter của Button, header DataGrid), đè `Foreground` kế thừa (ví dụ `AppBrushOnAccent` của nút primary). Cần kiểm tra runtime, nhất là theme Light.
- **Status timer** (`MainViewModel.cs:140-145`) không chống chạy chồng. Nếu probe qBittorrent, WARP và Jellyfin mất hơn 30 giây, các lần refresh chồng lên nhau.
- **Danh sách loại trừ khi kéo title bar** (`WebViewerWindow.xaml.cs:144-160`) phải cập nhật bằng tay. `MainWindow.Header_MouseLeftButtonDown` không có danh sách tương tự, nên control tương tác thêm vào header sau này có thể bị `DragMove` nuốt click.
- **ViewModel giữ tham chiếu `Window`** (`TorrentAddDiskDialogViewModel`, `PackLinkReviewViewModel`, `PackLinkProgressViewModel` nhận `Window` qua `CommandParameter` để đặt `DialogResult`). Hoạt động đúng, nhưng làm khó test và trùng với code-behind.
- **Ảnh:** poster được tải ở kích thước `w342` từ TMDB, và `PosterImageService` truyền `width`. Không thấy ảnh gốc cỡ lớn bị decode, nên không có finding.
- **`PackLinkProgressWindow`** chặn đóng bằng `CanClose` trong khi tác vụ chạy (đúng). Không có nút hủy tác vụ Gemini, nhưng đó là tính năng.
- **`AppMessageBoxWindow` và `AppMessageBox`** đều ổn: marshal sang dispatcher, kiểm tra owner, nút default và cancel đúng theo loại, Esc trả kết quả dismiss hợp lý.

## 6. Thứ tự sửa đề xuất

1. **UI-01:** chụp lựa chọn đầu lệnh và khóa Search/list khi đang thêm. Fix nhỏ, chặn được cả dữ liệu sai lẫn crash.
2. **UI-06, UI-08, UI-09:** bọc lỗi ở ba bề mặt crash rẻ nhất (`UiBusyWork`, Clipboard, News `Save`). Mỗi chỗ vài dòng.
3. **UI-04:** đổi `Dispatcher.Invoke` sang `BeginInvoke` và bắt lỗi trong handler. Gỡ cả treo khi tắt app lẫn lỗi bị ghi nhầm cho scheduler.
4. **UI-02:** quyết định chính sách exception toàn cục cùng phiên lifecycle. Sau bước 1–3, các nguồn đã biết đều được chặn tại chỗ.
5. **UI-03:** quyết định (a) Dynamic hay (b) "cần khởi động lại". Phương án (b) chỉ cần đổi một câu chữ và một lời gọi.
6. **UI-05, UI-07:** đưa News và Auto-Track ra nền theo mẫu `13dd96e`, rồi xử lý virtualization.
7. **UI-14:** đổi sang `AppTimeZone` (một dòng mỗi chỗ).
8. **UI-10, UI-11, UI-12, UI-13, UI-15, UI-16, UI-17, UI-18:** dọn dẹp UX, gom vào một PR polish. Gom chung với phần ToolTip/MenuItem còn dùng Static nếu chọn phương án (a) của UI-03.
