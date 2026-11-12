# Audit khởi động, lifecycle, DI và threading (2026-11)

- Baseline: `main` @ `ff2caa3` (nhánh `claude/lifecycle-audit-ed145a`), ngày 2026-11-12.
- Phạm vi: `App.xaml.cs`, `MainWindow.xaml.cs`, `ViewModels/MainViewModel.cs`, `Services/AppLifecycleService.cs`,
  `TrayIconService.cs`, `WindowsStartupService.cs`, `CrashLogService.cs`, `ConsoleWindowService.cs`,
  `OperationProgressService.cs`, `WindowsNotificationService.cs`, `DeviceStatusService.cs`, `Services/Events/*`,
  phần start/stop/timer của `BackupSchedulerService` và `AutoTrackSchedulerService`, `FirstRun*`, `app.manifest`,
  `Directory.Build.props`, `MediaManager.App.Tests/Startup`. Có đọc thêm `AppLogger`, `SettingsService.Load`,
  `SymlinkCoordinatorService` và `SymlinkSyncService.ReconcileAll`, vì chúng nằm trên đường khởi động và tắt.
- Thang mức độ và chuẩn bằng chứng theo [00-method-and-scope.md](../00-method-and-scope.md).
- Cách làm: đọc tĩnh, đọc lịch sử git, và đọc (chỉ đọc) log thật trong `<StateFolder>\logs`
  (state folder lấy từ pointer `%LocalAppData%\MediaManager\state-path.txt`). Không build, không chạy test,
  không chạy app, không đụng autostart hay registry. Không cần harness: các hành vi then chốt đều có bằng chứng
  trong code hoặc trong lifetime trace thật.
- Mọi đường dẫn tính từ `media management app/media management app/`.

## Tóm tắt

| ID | Mức độ | Tiêu đề | Độ tin cậy |
|---|---|---|---|
| LC-01 | High | Lỗi state folder hoặc `settings.json` làm app crash mỗi lần khởi động, trước khi crash hook được gắn | High (tĩnh) |
| LC-02 | High | Symlink reconcile lúc autostart coi ổ nguồn chưa sẵn sàng là file đã mất, xóa symlink Jellyfin | High (tĩnh) |
| LC-03 | High | Exception trong subscriber `RunCompleted` làm chết vòng TMDB discovery của Auto-Track tới hết phiên | High (tĩnh) |
| LC-04 | Medium | Tắt app: chờ tuần tự đến ~24 s trên UI thread, có thể deadlock, không hủy hay chờ job do người dùng chạy | High (tĩnh) |
| LC-05 | Medium | AUD-004 vẫn mở, nhưng rủi ro thật nằm ở chỗ khác so với mô tả cũ | High (tĩnh) |
| LC-06 | Medium | Mọi exception trên UI thread đều làm app chết, không có thông báo, một sự cố sinh ba báo cáo | High (tĩnh) |
| LC-07 | Medium | Worker của `AppLogger` chết vĩnh viễn ở lỗi IO đầu tiên, sau đó mất toàn bộ log | High (tĩnh) |
| LC-08 | Medium | `testhost.exe` từng chạy toàn bộ `App.OnStartup` trên state folder thật (9 lần, 2026-11-02) | High (log thật) |
| LC-09 | Low | Instance thứ hai chỉ hiện hộp thoại; app mở từ toast có thể chạy ẩn mà không có tray | Medium |
| LC-10 | Low | Restart sau first-run nhả mutex trước khi process cũ thoát | High (tĩnh) |
| LC-11 | Low | `OperationProgressService` và `DeviceStatusService.Current` là state chung, không thread-safe | High (tĩnh) |
| LC-12 | Low | Task fire-and-forget và `async void` không bắt lỗi | High (tĩnh) |
| LC-13 | Low | Bulk scope của `LibraryLinkEventHub` dùng chung cho toàn process | Medium |
| LC-14 | Low | Thiếu chẩn đoán cuối `OnExit`; comment trỏ tới tài liệu không tồn tại | High |

Không có finding Critical. LC-01 gần ngưỡng Critical ("repeatable startup failure"), nhưng điều kiện (file
settings hỏng hoặc ổ state không mount) chưa được tái hiện, nên xếp High.

## 1. Sơ đồ khởi động

```
Windows logon ──(Task Scheduler "MediaManager", delay 10 s, RunLevel=Highest, IgnoreNew)──┐
User double-click / toast COM activation ─────────────────────────────────────────────────┤
                                                                                          ▼
App.OnStartup (UI thread, bên trong Dispatcher)
 1. Mutex "Local\MediaManager.SingleInstance" (trước cả parse args)
      └─ không lấy được → AppMessageBox "already running" → Shutdown()        [LC-09]
 2. AppLaunchOptions.TryParse (--state-folder, --force-first-run, --enable-background)
 3. ConfigureServices + BuildServiceProvider(ValidateOnBuild, ValidateScopes)
      (mọi service là Singleton, trừ FirstRunWindow/FirstRunViewModel Transient)
 4. ISettingsService.Load(stateFolder)        ◄── có thể throw: pointer → ổ thiếu, JSON hỏng  [LC-01]
 5. IAppLogger (worker Task.Run)              ◄── chết ở lỗi IO đầu tiên                     [LC-07]
 6. ICrashLogService: ReportUncleanShutdownIfNeeded → MarkAlive (heartbeat 60 s)
 7. StartCrashLoggingHooks (Dispatcher / AppDomain / UnobservedTask / SystemEvents)   ◄── chỉ từ đây mới có crash log
 8. Gemini catalog, Theme
 9. IDatabaseService.Initialize (migration lỗi → dialog → Shutdown)
10. LogCleanupService.Start, PublishJunkCleanup, BundleExtractionGuard (fix PenIMC #6)
11. WARP RestorePendingModeAsync (fire-and-forget)                                     [LC-12]
12. Resolve TrayIconService + AutoTrackScheduler; gắn RunCompleted → tray
13. SetupCompleted && !SafeTestMode:
      SymlinkCoordinator.Start  → reconcile NGAY (không delay)                         [LC-02]
      AutoTrackScheduler.Start  → TMDB loop (delay 5 phút) + reconcile loop (poll 2 s)
      BackupScheduler.Start     → poll cycle chạy ngay, rồi mỗi 3 phút
      WarmupPosterCache (fire-and-forget)
14. RunAtStartup → EnsureStartupTaskRegistered (Task.Run, schtasks)
15. WindowsNotificationService.Initialize
16. Gated (chưa setup) → FirstRunWindow làm MainWindow → return
17. Resolve MainWindow → MainViewModel → 8 workspace VM (subscribe AppModeChanged, RunCompleted, …)
      MainViewModel: DispatcherTimer 30 s (async lambda) + RefreshStatusAsync (fire-and-forget)
18. Toast-launched → Show ẩn | StartMinimized → Show ẩn + HideToTray | còn lại → Show
19. --force-first-run → FirstRunWindow (owner = MainWindow, job nền đã chạy)          [LC-10]
```

Thứ tự đăng ký `AppModeChanged` (quyết định handler nào bị bỏ qua khi một handler throw, xem LC-05):
TrayIconService (bước 12) → JellyfinMediaNavigationService (qua NewsViewModel) → LibraryViewModel →
TorrentWorkspaceViewModel → ConsoleWindowService → QbittorrentViewerService → JellyfinViewerService →
MainViewModel; WarpLogStatusWatcher đăng ký khi `Start()` được gọi.

## 2. Sơ đồ tắt

```
Đường vào:
 A. Nút X: CloseToTray=false → CloseForShutdown → Application.Shutdown
            CloseToTray=true  → Close → OnClosing cancel → HideToTray (không thoát)
 B. Tray "Exit" → RequestShutdown → CloseForShutdown → Application.Shutdown
 C. Windows logoff/shutdown: WM_QUERYENDSESSION → App.OnSessionEnding (ClearAlive, LogLifetime)
      → WPF tự Shutdown → WM_CLOSE → OnClosing (CloseToTray=True, cancel bị bỏ qua) → OnClosed
 D. First-run Quit/Restart, migration lỗi → Shutdown()
 Không đường nào kiểm tra có job đang chạy hay không.                                   [LC-04]

App.OnExit (UI thread, xác nhận qua trace: OnDispatcher=True)
 1. ClearAlive (xóa session.alive), LogLifetime "CleanExit" (ghi last-exit.json)
 2. StopCrashLoggingHooks        ◄── từ đây trở đi exception không còn được ghi
 3. Trong MỘT try/catch rỗng, tuần tự:
      AutoTrackScheduler.Dispose   → Cancel + _worker.Wait(8 s)   ◄── có thể chờ Dispatcher.Invoke [LC-03/04]
      BackupScheduler.Dispose      → Cancel + Wait(8 s)
      SymlinkCoordinator.Dispose   → Cancel + Wait(8 s) (chỉ startup worker; task per-event không theo dõi)
      JellyfinLibraryRefresh.Dispose → hủy debounce (đường dẫn đang chờ refresh bị bỏ)
      LogCleanupService.Dispose    → Wait(…)
      TrayIconService.Dispose
    GetService<T>() ở đây khởi tạo các service chưa từng được tạo (gated/safe mode)
 4. ServiceProvider.Dispose (dispose lại toàn bộ singleton IDisposable, AppLogger Wait 2 s)
 5. ReleaseMutex, base.OnExit
 Task.Run nào còn chạy (job người dùng, worker quá 8 s) sẽ bị kill khi process thoát.
```

Bằng chứng từ log thật (`<StateFolder>\logs\*_lifetime.txt`, 2026-11-09 → 11-12): mọi lần tắt máy đều đi
qua C, kể cả cờ `ENDSESSION_CRITICAL` (0x40000000) vào các ngày 09/11, 10/11, 11/11; `OnClosing` chạy với
`AllowClose=False, CloseToTray=True` rồi vẫn có `WM_DESTROY`, nghĩa là cancel bị WPF bỏ qua khi app đang shutdown.
Dòng log cuối của mỗi phiên luôn là "Application OnExit: WPF shutdown completed", nên không biết bước 3 mất bao
lâu và có hoàn tất hay không (LC-14).

## 3. Lịch sử bug và hotspot

Số commit theo file (`git log --follow`): `App.xaml.cs` 51, `ViewModels/MainViewModel.cs` 22, `MainWindow.xaml.cs` 13,
`App.xaml` 7, `AutoTrackSchedulerService.cs` 7, `DeviceStatusService.cs` 6, `CrashLogService.cs` 5,
`TrayIconService.cs` 5, `WindowsNotificationService.cs` 4, `WindowsStartupService.cs` 3, `ConsoleWindowService.cs` 3,
`AppLifecycleService.cs` 1, `OperationProgressService.cs` 1, `BackupSchedulerService.cs` 1, `app.manifest` 1.

Phần lớn 51 commit của `App.xaml.cs` là thêm tính năng: mỗi service mới thêm một dòng đăng ký và thường thêm một
bước khởi động. File gánh ba vai trò cùng lúc (composition root, trình tự lifecycle, chẩn đoán crash) và không có
test nào. Đây là hotspot do cấu trúc, không phải do một bug cụ thể.

| Ngày | Commit | Sự kiện | Nguyên nhân gốc và bài học |
|---|---|---|---|
| 2026-05-19 | `d022363` | Thêm single-instance mutex | Mutex lấy trước khi parse args, nên bản test `--state-folder` cũng bị chặn khi app thật đang chạy (là chủ ý). |
| 2026-06-14 | `de9fb92` | Autostart bằng HKCU Run, tray | |
| 2026-06-15 | `161b9c9` | Background mode (`AppLifecycleService`) | File chỉ có 1 commit; nguồn gốc AUD-004. |
| 2026-06-17 | `4983e57` | `requireAdministrator` | HKCU Run không chạy được app cần elevation mà không có UAC, dẫn tới commit dưới. |
| 2026-07-18 | `56a2629` | "fix auto startup": chuyển sang `schtasks /rl HIGHEST` | `schtasks /create` mặc định "chỉ chạy khi cắm sạc, dừng khi chuyển sang pin, giới hạn 72 giờ". |
| 2026-08-20 | `f24d3ac` | "fix search mode change crash" | Setter trong `RecipeWorkspaceViewModel` gây vòng lặp property change; sửa bằng guard so sánh bằng nhau. Exception UI luôn làm app chết (LC-06). |
| 2026-09-03 / 10-10 | `a4198d7`, `8ac4301` | Crash log, lifetime trace | Thêm để điều tra các lần thoát không sạch khi mất điện AC. |
| 2026-10-24 | `f72a7a9` (#5) | Đăng ký task từ XML; PenIMC | Task bị dừng mỗi lần rút sạc. `PenImc_cor3.dll` trong thư mục giải nén ở `%TEMP%` bị dọn mất. |
| 2026-10-26 | `2a09c78` (#6) | `BundleExtractionGuard` giữ handle DLL | Storage Sense xóa DLL lúc app đang chạy, gây `DllNotFoundException` ở message thiết bị kế tiếp. |
| 2026-10-26 | `92737a7` (#8) | Khôi phục order Auto-Track bị ngắt | Thừa nhận việc tắt máy giết worker giữa chừng (liên quan LC-04). |
| 2026-11-03 | `d16a334` (#10) | Khôi phục WARP mode lúc khởi động | Thêm một task fire-and-forget nữa vào `OnStartup` (LC-12). |

Quy luật: mọi bug lifecycle đều chỉ lộ ra trên máy thật qua crash log. Thư mục `MediaManager.App.Tests/Startup` chỉ test
các hàm static của `WindowsStartupService` (sinh và parse XML, lý do đăng ký lại, nhận diện dev build).

## 4. Finding

### LC-01 — Lỗi state folder hoặc settings làm app crash mỗi lần khởi động, trước khi crash hook được gắn (High)

Code liên quan:
- `App.xaml.cs:66-76`: `settings.Load(...)` chạy trước `StartCrashLoggingHooks()`; `OnStartup` không có try/catch.
- `Services/SettingsService.cs:54`: `Directory.CreateDirectory(Current.StateFolder)`.
- `Services/SettingsService.cs:72-110`: `JsonSerializer.Deserialize`; lỗi được ghi vào `settings-load.log` rồi `throw`.
- `Services/SettingsService.cs:113-130`: `Save` ghi `.tmp` bằng `File.WriteAllText` rồi `File.Move(overwrite)`,
  không flush xuống đĩa.

Bằng chứng:
- `settings.json` rỗng hoặc hỏng làm `Deserialize` ném `JsonException`, `Load` ném lại. Lúc đó chưa có handler
  `DispatcherUnhandledException` hay `AppDomain`, nên process chết với lỗi .NET chưa xử lý. Không có crash file
  (crash file nằm trong state folder và cần hook) và không có thông báo cho người dùng. Lần chạy nào cũng vậy:
  autostart lúc logon thất bại im lặng cho đến khi có người sửa tay file JSON.
- Nếu pointer trỏ tới ổ không tồn tại (ổ ngoài, ổ BitLocker chưa mở khóa, đổi ký tự ổ đĩa), `CreateDirectory` ném
  `DirectoryNotFoundException` và app chết theo đúng đường trên.
- Nếu ổ có mặt nhưng thư mục không còn, `Load` lặng lẽ tạo `settings.json` mặc định với `SetupCompleted=false`, rồi
  app vào first-run có chặn (gated) trên một state folder trống. Người dùng dễ nghĩ đã mất dữ liệu, và nếu họ chạy
  setup lại thì sẽ có một DB mới tách khỏi DB cũ.
- Máy này thường xuyên tắt kiểu `ENDSESSION_CRITICAL` và mất điện AC (lifetime trace 09–11/11; xem thêm #5, #8).
  Ghi file không flush rồi rename vẫn có thể để lại file rỗng sau khi mất điện, vì NTFS chỉ ghi journal cho
  metadata.

Tác động: lỗi khởi động lặp lại, không tự phục hồi, không có chẩn đoán ngoài `settings-load.log`.
Chưa tái hiện, vì cần làm hỏng state.

Hướng sửa tối thiểu: gắn `DispatcherUnhandledException` và `AppDomain` ngay đầu `OnStartup`, ghi crash ra
`%LocalAppData%\MediaManager` khi chưa có state folder. Bọc `settings.Load` và hiện `AppMessageBox` nêu rõ đường dẫn
và lỗi. Khi JSON hỏng, giữ lại bản `.bak` (hoặc bản `.tmp` còn sót) và đề nghị khôi phục thay vì crash. Trong `Save`,
dùng `FileStream.Flush(true)` trước khi `Move` (hoặc `File.Replace` kèm backup). Phân biệt "ổ của state folder không
sẵn sàng" (chờ hoặc thử lại, rồi thông báo) với "thư mục không tồn tại" (hỏi người dùng trước khi tạo state mới).

### LC-02 — Symlink reconcile lúc autostart coi ổ nguồn chưa sẵn sàng là file đã mất (High)

Code liên quan:
- `App.xaml.cs:113-118`: `SymlinkCoordinator.Start()` chạy ngay khi khởi động. Không có delay như Auto-Track (5 phút).
- `Services/Symlink/SymlinkCoordinatorService.cs:214-231`: `RunStartupReconcileAsync` gọi `SyncNowAsync`.
- `Services/Symlink/SymlinkSyncService.cs:183-207`: với mỗi item `Linked`, nếu `File.Exists(item.LinkedPath)` là false
  (`LinkedPath` là hardlink trên ổ media) thì gọi `RemoveItem(item)`.
- `Services/Symlink/SymlinkSyncService.cs:147-167`: `RemoveItem` xóa symlink trong `UnifiedRoot` (`<UnifiedRoot>`)
  và `ClearSymlinkPath` trong DB.

Bằng chứng: không có bước kiểm tra ổ hoặc thư mục nguồn có truy cập được hay không (`DriveInfo.IsReady`, sự tồn tại của
source root). Settings thật: nguồn là `D:\…` và `E:\…` (`E:` có nhãn ổ riêng, Windows báo là Fixed; nếu là ổ NVMe
gắn qua hộp USB thì có thể enumerate sau lúc logon). Task autostart chỉ delay 10 s. Nếu `E:` chưa sẵn sàng, mọi item
trên `E:` mất symlink, `SymlinkPath` trong DB bị xóa và `NotifyJellyfinForTouchedPathsAsync` báo Jellyfin quét lại
các đường dẫn đó. Lần khởi động kế tiếp có ổ, `SyncItem` tạo lại link, nên không mất file media. Nhưng trong phiên đó
thư viện Jellyfin bị rút mất phần lớn nội dung, và metadata hoặc trạng thái phụ thuộc vào item Jellyfin có thể bị
xóa theo.

Ghi chú: trong log ngày 02/11 lúc 17:38 có dòng "Symlink startup reconcile finished … removed 0, skipped 292", tức là
reconcile chạy trước mọi thứ khác (xem LC-08). Chưa tái hiện được kịch bản ổ chưa mount.

Hướng sửa tối thiểu: trước khi gọi `RemoveItem` trong `ReconcileAll`, bỏ qua item nếu thư mục gốc hoặc ổ của
`LinkedPath` không truy cập được (ghi log "source offline, skipped"). Delay hoặc retry reconcile khởi động khi có ổ
nguồn offline. Phiên deletion/library nên xác nhận ngữ nghĩa xóa ở đây (đã để note bàn giao).

### LC-03 — Exception trong subscriber `RunCompleted` làm chết vòng TMDB discovery (High)

Code liên quan:
- `Services/AutoTrackSchedulerService.cs:233-270`: `NotifyRunCompleted(result)` nằm trong `try`. Nhánh
  `catch (Exception)` gọi `NotifyRunCompleted(FailedResult(...))` mà không có bảo vệ nào.
- `Services/AutoTrackSchedulerService.cs:127-141`: vòng lặp không bọc `await RunTmdbDiscoverySafelyAsync()`.
- Tương tự với reconcile: `RunReconcileSafelyAsync` (`:272-302`) nằm trong `RunReconcileLoopAsync`.
- Subscriber: `App.xaml.cs:111` (tray, chạy trên worker), `ViewModels/AutoTrackViewModel.cs:42-45` và
  `ViewModels/NewsViewModel.cs:41-44`, cả hai dùng `Dispatcher.Invoke(RefreshDashboard/UpdateDashboard)` đồng bộ.
  Hai hàm này đọc SQLite và thao tác `ObservableCollection` trên UI thread.

Bằng chứng: `Dispatcher.Invoke` gọi từ thread khác sẽ ném lại exception của delegate về cho caller. Nếu
`RefreshDashboard` ném lỗi (SQLite busy hoặc locked, lỗi dữ liệu, exception do binding), nhánh catch ghi log
"TMDB discovery failed: …" (gán nhầm lỗi UI thành lỗi TMDB) rồi gọi lại subscriber; nếu lần này lại ném, exception
thoát khỏi `RunTmdbDiscoverySafelyAsync` và vòng `while` kết thúc. Từ đó Auto-Track không còn phát hiện tập mới tới
hết phiên, trong khi UI không báo gì: `_worker` chỉ fault khi vòng reconcile cũng kết thúc, và chỉ được quan sát lúc
`Dispose`. Với vòng reconcile, một exception như vậy làm dừng việc poll queue tải về. Ngoài ra, `Application.Current`
bằng `null` lúc tắt app gây `NullReferenceException` trong lambda.

Tác động: workflow không người trông (Auto-Track) dừng im lặng; không mất dữ liệu.

Hướng sửa tối thiểu: `NotifyRunCompleted` bọc từng subscriber trong try/catch (dùng `GetInvocationList`) và ghi log.
Subscriber UI dùng `BeginInvoke`/`InvokeAsync` thay vì `Invoke`. Vòng lặp có try/catch ở ngoài cùng.

### LC-04 — Tắt app: chờ tuần tự trên UI thread, có thể deadlock, không hủy job người dùng (Medium)

Code liên quan: `App.xaml.cs:265-286`; `AutoTrackSchedulerService.cs:53-93`, `BackupSchedulerService.cs:51-91`,
`SymlinkCoordinatorService.cs:87-130`, `LogCleanupService.cs:39-77` (mỗi cái `Wait` 8 s);
`MainWindow.xaml.cs:45-49, 275-284`; `TrayIconService.cs:106-115`.

Bằng chứng:
- Bốn lệnh `Wait` tuần tự trên UI thread, tổng cộng tới khoảng 24–32 s. Khi tắt máy, Windows có thể kill process sớm
  hơn nhiều (máy này có cả tắt kiểu `CRITICAL`).
- Deadlock: worker của Auto-Track đang ở `Dispatcher.Invoke` (LC-03) trong khi UI thread bị chặn trong `_worker.Wait`,
  nên phải chờ hết 8 s.
- Sau khi timeout, `_shutdown.Dispose()` và `ServiceProvider.Dispose()` chạy trong lúc worker còn sống. Worker gặp
  `ObjectDisposedException` khi đọc `_shutdown.Token`, hoặc tiếp tục ghi DB rồi bị kill giữa chừng. `DatabaseService`
  mở connection theo từng lần gọi, nên transaction SQLite vẫn nguyên tử, nhưng thao tác nhiều bước (file system và
  DB) có thể dừng nửa chừng.
- Job do người dùng chạy (cart/search `FetchJobService`, pack link, xóa, backup thủ công, `SymlinkCoordinator` theo
  từng sự kiện `Task.Run`) không có registry hay token chung. `OnExit` không biết tới chúng. Nút X (khi không bật
  tray) và tray "Exit" thoát ngay, dù `DeviceStatusService.IsJobActive` đang là true.
- Tất cả các lệnh dispose nằm trong một `try`, nên một lệnh ném lỗi làm bỏ qua phần còn lại (ví dụ tray không được
  dispose, để lại icon ma). `GetService<T>()` trong `OnExit` còn khởi tạo service chưa từng được tạo (gated, safe
  mode, migration lỗi), ví dụ `BackupSchedulerService` kéo theo `BackupService` và `GoogleDriveClient`.
- Debounce của `JellyfinLibraryRefreshService` bị hủy, nên các đường dẫn đang chờ refresh bị bỏ.

Hướng sửa tối thiểu: hủy tất cả trước (gọi `Cancel` trên mọi service), rồi chờ song song với một deadline tổng
(ví dụ `Task.WhenAll(...).Wait(5 s)`). Bỏ `Dispatcher.Invoke` trên đường worker. Thêm `IShutdownCoordinator`, hoặc
dùng `IOperationProgressService.IsActive`, để hỏi xác nhận khi Exit trong lúc có job. Chỉ dispose service đã được tạo,
mỗi service một try/catch.

### LC-05 — Cập nhật AUD-004 (Medium, đề xuất hạ từ High)

Code liên quan: `Services/AppLifecycleService.cs:29-48` (không đổi kể từ khi tạo, 1 commit duy nhất);
`ViewModels/LibraryViewModel.cs:2919-2938`; `ViewModels/TorrentWorkspaceViewModel.cs:2121-2140`;
`ViewModels/MainViewModel.cs:622-632`; `Services/TrayIconService.cs:186-201`.

AUD-004 vẫn đúng: `AppModeChanged(Background)` được raise trên thread pool sau 5 phút. Đánh giá lại rủi ro:
- `DispatcherTimer.Start/Stop` (MainViewModel) có khóa nội bộ và tự đăng ký với dispatcher của timer, nên gọi từ
  thread khác không ném lỗi. Không phải rủi ro chính.
- Gán `SelectedPosterImage = null` và `card.PosterImage = null` là property vô hướng; binding engine của WPF tự
  marshal `PropertyChanged`. Không ném lỗi.
- Rủi ro thật: `foreach (var card in MediaCards)` duyệt `ObservableCollection` trên worker trong lúc UI có thể đang
  sửa nó (refresh, filter), dẫn tới `InvalidOperationException: Collection was modified`. Exception này cắt ngang
  multicast delegate. Các handler đăng ký sau (TorrentWorkspace, Console auto-close, auto-close của viewer,
  MainViewModel dừng timer) và `ScheduleTrimWorkingSet` không chạy, trong khi `CurrentMode` đã là `Background`.
  Task fault không được ai quan sát và chỉ được ghi log khi GC chạy (`UnobservedTaskException`).
- `TrayIconService.UpdateTrayTooltip` đặt `NotifyIcon.Text` từ worker. Thực tế chạy được (`Shell_NotifyIcon`), nhưng
  không nhất quán với chỗ khác.
- Các handler đã marshal đúng: `ConsoleWindowService`, `JellyfinViewerService`, `QbittorrentViewerService`
  (`RunOnUi`). `JellyfinMediaNavigationService.ClearCache` và `WarpLogStatusWatcher` an toàn với thread.

Hậu quả: không phá hủy dữ liệu, chỉ làm hỏng chuyển trạng thái nền. Vì vậy đề xuất Medium. Hướng sửa giữ nguyên:
raise sự kiện trên dispatcher của app (`Application.Current.Dispatcher.BeginInvoke`) trong `AppLifecycleService`.

### LC-06 — Mọi exception trên UI thread đều làm app chết, không thông báo (Medium)

Code liên quan: `App.xaml.cs:603-615` (ghi log nhưng không đặt `e.Handled`), `:588-601`, `:617-630`;
CommunityToolkit.Mvvm 8.4.2 với 45 phương thức `[RelayCommand] async Task` trong `ViewModels/`, không cái nào bật
`FlowExceptionsToTaskScheduler`; `MainViewModel.cs:144` (`Tick += async …`).

Bằng chứng: `AsyncRelayCommand.Execute` ném lại exception của task lên `SynchronizationContext`, dẫn tới
`DispatcherUnhandledException`. Handler không đặt `Handled`, nên exception thoát khỏi `Application.Run`. Tiếp theo
`AppDomain.UnhandledException` ghi thêm một crash file nữa và ghi đè `last-exit.json`. `ClearAlive` không chạy, nên
lần khởi động sau ghi thêm báo cáo `UncleanShutdown` thứ ba. Người dùng thấy app biến mất mà không có hộp thoại nào.
Commit "fix search mode change crash" (`f24d3ac`) thuộc đúng loại sự cố này. Để app chết là lựa chọn phòng thủ chấp
nhận được (tránh chạy tiếp ở trạng thái hỏng), nhưng hiện chưa có chính sách rõ ràng và chưa có thông báo nào.

Hướng sửa tối thiểu: quyết định chính sách thành văn bản. Đề xuất: với exception phát sinh từ command hoặc handler UI
thì ghi log, hiện `AppMessageBox` và đặt `Handled = true`. Chỉ để app chết với các loại nghiêm trọng
(`OutOfMemoryException`, `StackOverflow` không bắt được, exception khi render). Ghi dấu "đã báo cáo" để không sinh
ba báo cáo cho cùng một sự cố.

### LC-07 — Worker của `AppLogger` chết ở lỗi IO đầu tiên (Medium)

Code liên quan: `Services/AppLogger.cs:112-138` (chỉ bắt `OperationCanceledException`) và `:145-153`
(`File.AppendAllText`, `Directory.CreateDirectory` không có try).

Bằng chứng: một `IOException` (sharing violation do antivirus, đầy đĩa, ổ state rút ra) thoát khỏi `await foreach`,
làm task `_worker` fault. Từ đó không còn log file, log UI (console log window) hay log console cho tới hết phiên.
`_worker` vẫn còn được tham chiếu nên không bao giờ được GC, và `UnobservedTaskException` cũng không xảy ra: lỗi hoàn
toàn im lặng. Crash file vẫn ghi đồng bộ, nên crash vẫn có dấu vết.

Hướng sửa tối thiểu: bắt exception theo từng entry (thử lại, đổi sang file mới, hoặc ghi một dòng cảnh báo lên
console hoặc UI), không để vòng đọc thoát.

Review note (không phải finding): khi `Dispose`, `TryComplete` rồi `Cancel` ngay. Vòng `TryRead` bên trong
`ReadAllAsync` không kiểm tra token, nên thường vẫn drain hết queue. Chỉ có một khe rất hẹp ngay trước
`WaitToReadAsync` có thể làm mất vài dòng cuối.

### LC-08 — `testhost.exe` từng chạy toàn bộ `App.OnStartup` trên state folder thật (Medium)

Bằng chứng (log thật):
- `<StateFolder>\logs\20261102_1740…_crash.txt` tới `…_184151_130_crash.txt`: 9 file `UncleanShutdown` với
  `Exe: …\MediaManager.App.Tests\bin\x64\Debug\net8.0-windows10.0.17763.0\testhost.exe` và `Version: 17.8.0`
  (phiên bản của `Microsoft.NET.Test.Sdk`).
- systemlog 2026-11-02 17:38:25: "Media Manager 17.8.0 starting (pid 27252)", "State folder bootstrap source=pointer;
  folder='<StateFolder>'", khởi tạo SQLite thật, "Publish leftover cleanup deleted 7 file(s)" (trong thư mục
  bin của test), "Auto-track delayed 5 minutes", "Symlink startup reconcile finished … skipped 292", `EnsureRegistered`
  chạy `schtasks`, rồi `SettingsViewModel` và `DeviceStatus` gọi qBittorrent và Jellyfin thật.
- `ReportUncleanShutdownIfNeeded` và `MarkAlive` chỉ được gọi từ `App.OnStartup` trong mọi commit của giai đoạn đó
  (đã kiểm tra bằng `git grep` qua các revision). Vậy là toàn bộ đường khởi động đã chạy trong testhost: không có
  `--state-folder`, nên `SafeTestMode=false` và job nền chạy trên dữ liệu thật. `startedUtc` bằng `lastBeatUtc` cho
  thấy mỗi process sống chưa tới 60 s và không qua `OnExit`.
- Lần chạy thật kế tiếp (18:41, bản cài `<InstallDir>\…`) báo sai rằng phiên trước thoát không sạch.
- Không tìm ra test hay harness nào gây ra (code test hiện tại chỉ dùng fake). Nguồn có thể là một harness chạy tạm
  lúc làm tính năng Pretty console (`7f564f7`, cùng ngày).

Tác động: vi phạm an toàn khi kiểm thử (00-method: "live state folder never mutated"). Một harness sống đủ 5 phút sẽ
cho Auto-Track hunt và add torrent thật; symlink reconcile đã chạy trên `<UnifiedRoot>`.

Hướng sửa tối thiểu: trong `OnStartup`, nếu entry assembly không phải exe của app (`testhost`, `ReSharperTestRunner`…)
hoặc có biến môi trường test, thì bắt buộc `SafeTestMode` hoặc từ chối dùng pointer. Ghi rõ trong tài liệu test
harness rằng mọi lần chạy UI phải có `--state-folder`.

### LC-09 — Instance thứ hai và app mở từ toast chạy ẩn (Low)

Code liên quan: `App.xaml.cs:33-43`, `:159-171`; `Services/WindowsNotificationService.cs:140-166`.

- Instance thứ hai chỉ hiện "already running" rồi thoát, không gửi tín hiệu để kích hoạt cửa sổ đang ẩn trong tray.
  Mutex được lấy trước khi có log, nên trường hợp này không để lại dấu vết.
- Nếu app được Windows mở từ toast và cả `StartMinimized` lẫn `CloseToTray` đều false, cửa sổ được `Show()` ở trạng
  thái `Hidden` với `ShowInTaskbar=false`, tray không được khởi tạo, và việc hiện cửa sổ phụ thuộc hoàn toàn vào sự
  kiện `OnActivated`. Nếu sự kiện không tới, app chạy ẩn hoàn toàn, còn instance mới thì báo "already running". Với
  settings hiện tại (`CloseToTray=true`) thì không bị.

Hướng sửa: named pipe hoặc broadcast message để instance thứ hai kích hoạt instance thứ nhất. Khi mở từ toast, luôn
khởi tạo tray hoặc đặt một timeout để hiện cửa sổ.

### LC-10 — Restart sau first-run nhả mutex trước khi process cũ thoát (Low)

Code liên quan: `App.xaml.cs:424-448`; `ViewModels/FirstRunViewModel.cs:840-870`; `Views/FirstRunWindow.xaml.cs:20-27`.

`ReleaseSingleInstanceMutex()` chạy, rồi `Process.Start`, rồi `Shutdown()`. Process mới lấy mutex ngay, trong khi
process cũ vẫn đang chạy `OnExit` (chờ tới khoảng 24 s, LC-04). Ở chế độ gated thì job nền chưa chạy, nên rủi ro thấp.
Ở chế độ `--force-first-run` (owner là `MainWindow`), Auto-Track, Backup và Symlink đã chạy, nên hai process cùng
chạy migration, Backup poll và reconcile symlink trên cùng state. Ngoài ra, `async void OnClosed` →
`ReleaseHeldWarpLeasesAsync` có thể đua với `RestorePendingModeAsync` của process mới khi cả hai điều khiển `warp-cli`.

Hướng sửa: process mới chờ mutex có timeout (`WaitOne(30 s)`) khi được truyền cờ `--restarted`, thay vì process cũ
nhả mutex sớm.

### LC-11 — State tiến độ chung không thread-safe (Low)

`Services/OperationProgressService.cs` (toàn bộ) và `Services/DeviceStatusService.cs:51-64, 88-99, 133+`. Auto-Track
nền và search của người dùng cùng gọi `Start/Report/Finish` trên một instance, không có khóa. Thông điệp bị trộn,
`IsActive=false` khi một job xong trong lúc job kia vẫn chạy. `Current` bị thay thế theo kiểu read-modify-write từ
nhiều thread (`RefreshJobOnly`, `RefreshWarpOnly`, `RefreshCoreAsync`), nên có thể mất cập nhật. Hậu quả chỉ là hiển
thị sai, nhưng nếu LC-04 dùng `IsActive` để hỏi xác nhận khi Exit thì phải sửa chỗ này trước.

### LC-12 — Task fire-and-forget và `async void` không bắt lỗi (Low)

- `App.xaml.cs:103`: `Task.Run(() => warpCliService.RestorePendingModeAsync())`, không có catch
  (`WarpCliService.cs:415+`).
- `App.xaml.cs:521-523`: `WarmupPosterCache` dùng `catch {}` rỗng, nuốt mọi lỗi kể cả lỗi DB.
- `ViewModels/MainViewModel.cs:146, 631`: `_ = RefreshStatusAsync()`.
- `Services/AppLifecycleService.cs:29, 121`: không bắt lỗi của handler (LC-05).
- `Views/FirstRunWindow.xaml.cs:20, 29`: `async void OnClosed/OnLoaded`; lỗi ở đây đi thẳng tới LC-06.
- `Services/Symlink/SymlinkCoordinatorService.cs:134+` và `JellyfinLibraryRefreshService.cs:211`: `Task.Run` theo
  từng sự kiện, không được theo dõi khi tắt.

Lỗi chỉ hiện ra qua `UnobservedTaskException` khi GC chạy (có thể chậm nhiều phút, hoặc không bao giờ), hoặc không
hiện ra.

### LC-13 — Bulk scope của `LibraryLinkEventHub` dùng chung cho toàn process (Low)

`Services/Events/LibraryLinkEventHub.cs:33-68`. `_depth` và bộ đệm là của cả process, không gắn với từng thao tác
(`AsyncLocal`). Khi một thao tác bulk (ví dụ unlink pack) đang mở scope, mọi `PublishHardlinkCreated/Removed` của các
thao tác khác (Auto-Track reconcile) bị gom vào batch của nó và chỉ phát khi scope kia đóng. Event được raise đồng bộ
trên thread của publisher, nên handler `SymlinkCoordinator` chạy trên thread đó (nó tự `Task.Run`). Hiện chưa thấy hậu
quả sai dữ liệu. Ghi lại để phiên Library/Deletion lưu ý.

### LC-14 — Thiếu chẩn đoán cuối `OnExit`, comment trỏ tới tài liệu không tồn tại (Low)

- `OnExit` ghi `CleanExit` và `last-exit.json` ngay đầu hàm. Không có dòng nào ghi thời gian từng bước dispose hay
  việc dispose đã xong, nên không thể biết LC-04 có xảy ra trên máy thật hay không.
- `MainWindow.xaml.cs:136` và `WindowsStartupService.cs:130` nhắc tới `docs/debug/unclean-exit`, nhưng thư mục
  `docs/debug` không tồn tại.
- `WindowsStartupService.EnsureRegistered` (`:64-88`): guard dev build chỉ có tác dụng khi task đã tồn tại. Nếu task
  bị xóa, một dev build chạy trên state thật với `RunAtStartup=true` sẽ đăng ký chính nó vào autostart.
  `SetEnabled(true)` có cùng hành vi.

## 5. Review note (không đủ thành finding)

- DI: mọi service là Singleton và được resolve trên UI thread. Không có scope, nên không có captive dependency theo
  nghĩa scoped trong singleton. `FirstRunWindow` và `FirstRunViewModel` là Transient resolve từ root; chúng không
  `IDisposable` nên không rò. `ValidateOnBuild` bật, tốt. `HttpClient` singleton mặc định (timeout 100 s, không xoay
  DNS) thuộc phạm vi phiên TMDB/Jellyfin (trên nhánh khác đã có `per-destination HTTP clients`).
- Mutex `Local\…`: chỉ trong một phiên Windows. Hai user đăng nhập song song (fast user switching) có thể cùng chạy
  trên cùng `<StateFolder>` nếu cùng pointer. Pointer nằm trong `%LocalAppData%` của từng user, nên thực tế
  khó xảy ra.
- Sự kiện chưa unsubscribe: các singleton đăng ký vào singleton khác (cùng vòng đời) thì không rò. `MainWindow`
  `SidebarNavHost.SizeChanged` đăng ký một lần. WebView2 được dispose trong `CloseCore` của từng viewer, và app shutdown
  đóng mọi window. Không thấy rò window hay timer: `DispatcherTimer` trong `MainViewModel` và `StatsViewModel` sống
  cùng app.
- `BundleExtractionGuard` chạy sau DB init. Khe từ lúc process start tới lúc giữ handle chỉ vài trăm ms, chấp nhận
  được.
- `requireAdministrator` cùng task `RunLevel=Highest`: mọi instance đều elevated, nên mutex và restart nhất quán.
  `WindowsIdentity.GetCurrent()` khi elevated bằng tài khoản admin khác (UAC over-the-shoulder) sẽ đăng ký task cho
  tài khoản đó. Đây là trường hợp biên.

## 6. Test còn thiếu

1. `SettingsService.Load`: JSON rỗng hoặc hỏng, state folder trên ổ không tồn tại, thư mục bị xóa nhưng ổ còn. Kiểm
   tra không crash, có thông báo và giữ bản backup (LC-01).
2. `SymlinkSyncService.ReconcileAll` với `LinkedPath` nằm trên root không tồn tại: không gọi `RemoveItem`
   (LC-02, dùng temp dir và fake).
3. `AutoTrackSchedulerService`: subscriber `RunCompleted` ném exception thì vòng discovery và reconcile vẫn chạy tiếp
   (LC-03, `IAutoTrackService` giả với interval ngắn).
4. Dispose của các scheduler: `Dispose()` không chặn quá deadline khi worker đang kẹt; không có `ObjectDisposedException`
   sau timeout (LC-04).
5. `AppLifecycleService`: `AppModeChanged` được raise trên dispatcher của app; một handler throw không chặn các handler
   khác (LC-05, test STA với `Dispatcher`).
6. `AppLogger`: một `IOException` khi ghi file không làm dừng việc xử lý entry kế tiếp (LC-07, `ISettingsService` giả
   trỏ vào file đang bị khóa).
7. `AppLaunchOptions` và guard testhost: khi chạy trong test host thì luôn ở `SafeTestMode` (LC-08).
8. `CrashLogService`: `ReportUncleanShutdownIfNeeded`/`MarkAlive`/`ClearAlive` trên temp state folder; một sự cố chỉ
   sinh một báo cáo (LC-06).
9. `OperationProgressService` khi nhiều job chạy song song (LC-11).
10. Kiểm tra composition root: build `ServiceProvider` với `ConfigureServices` (đã có `ValidateOnBuild`) ngay trong
    unit test, để bắt lỗi đăng ký trước khi tới máy thật.

## 7. Thứ tự sửa đề xuất

1. LC-08: chặn test host và harness đụng state thật. Rẻ, và là điều kiện để test an toàn cho mọi bước sau.
2. LC-03: bọc subscriber và vòng lặp của scheduler, đổi `Invoke` sang `BeginInvoke`. Nhỏ, khôi phục độ tin cậy của
   Auto-Track.
3. LC-02: bỏ qua item có ổ nguồn offline trong startup reconcile (phối hợp với phiên Library/Deletion).
4. LC-01: gắn crash hook sớm, xử lý lỗi `Load`, ghi settings có flush và backup.
5. LC-07: logger không chết. Cần làm trước LC-04 và LC-06 để có log khi sửa chúng.
6. LC-05 (AUD-004): marshal `AppModeChanged` về dispatcher.
7. LC-04 và LC-11: shutdown coordinator, chờ song song có deadline, xác nhận Exit khi có job; `OperationProgress`
   thread-safe.
8. LC-06: chính sách xử lý exception trên UI và chống báo cáo trùng.
9. LC-09, LC-10, LC-12, LC-13, LC-14: dọn dẹp theo cơ hội.

## 8. Ghi chú bàn giao

- LC-02 (xóa symlink khi ổ nguồn offline) và LC-13 thuộc vùng Library/Deletion; đã ghi note vào thư mục ghi chú dùng
  chung của nhóm phiên.
- LC-03 chạm vào vùng Auto-Track, nhưng chỉ ở phần scheduler và subscriber, không đụng nghiệp vụ.
- AUD-004 trong `03-findings-register.md` nên cập nhật bằng chứng theo LC-05 khi gộp báo cáo.
