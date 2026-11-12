# Audit Settings (2026-11)

- **Ngày:** 2026-11-12
- **Nhánh / commit gốc:** `claude/settings-audit-4d6a3b` @ `ff2caa3` (= `main`)
- **Thang mức độ:** [`../00-method-and-scope.md`](../00-method-and-scope.md)
- **Chế độ:** chỉ đọc. Không sửa code app, không đổi settings thật. Không có secret nào trong báo cáo.

## 0. Phạm vi và cách làm

Mọi đường dẫn tính từ `media management app/media management app/`.

| Vùng | File |
|---|---|
| Lưu / đọc | `Services/SettingsService.cs`, `Services/ISettingsService.cs` |
| Model | `Models/AppSettings.cs`, `MediaManager.Core/Models/AutoTorrentSettings.cs`, `Models/AutoTrackSettings.cs`, `JellyfinRefreshSettings.cs`, `BackupSettings.cs`, `GeminiSettings.cs`, `LogSettings.cs`, `AppStartupSettings.cs`, `NotificationSettings.cs`, `SymlinkSettings.cs`, `TmdbDailyBudget.cs` |
| ViewModel / View | `ViewModels/SettingsViewModel.cs` (2078 dòng), `SystemSettingsViewModel.cs`, `NotificationPreferenceItemViewModel.cs`, `GeminiModelOptionViewModel.cs`, `Views/SettingsView.xaml` (2177 dòng), `Views/SystemSettingsView*` |
| Tài liệu | `docs/STATE_FOLDER.md`, `docs/settings-modernization/*` |

Ngoài phạm vi: tham số search/recipe (đang viết lại trên `feat/native-search`), nghiệp vụ Auto-Track, xóa file, Database, Backup, TMDB/Jellyfin. Chỗ nào finding chạm vào các vùng đó thì có ghi chú "liên vùng".

**Kiểm chứng.** Ngoài đọc tĩnh, tôi viết 10 test probe xUnit tạm thời trong `MediaManager.App.Tests`. Probe gọi `SettingsService` thật với `Load(<thư mục tạm>)` (chế độ CLI pin nên không đọc hay ghi pointer, không chạm state folder thật). Chạy `dotnet test --filter ZzSettingsAuditProbe`: **10/10 pass**, nghĩa là cả 10 hành vi nghi vấn đều xảy ra đúng như dự đoán. Sau đó file probe đã được gỡ khỏi repo. Tóm tắt ở [Phụ lục A](#phụ-lục-a--probe-đã-chạy). Log thật trong `<StateFolder>\logs` chỉ được grep đếm số dòng khớp từ khóa, không đọc và không chép nội dung.

## 1. Tóm tắt

| ID | Mức | Tiêu đề | Độ tin cậy | Đã chứng minh |
|---|---|---|---|---|
| SET-01 | **High** | `settings.json` không durable; file rỗng hoặc hỏng làm app không khởi động được, và không có bản dự phòng | Cao | Probe 03/04/05 |
| SET-02 | **High** | Mở trang Settings gọi `Load()` và thay object `Current`, làm mất cập nhật của service nền (ngân sách TMDB) | Cao | Probe 07 |
| SET-03 | **High** | Sửa chưa lưu bị áp vào `Current` ngay (tên thư mục library theo từng phím gõ, WARP, nút Test) rồi bị các `Save()` khác ghi xuống đĩa | Cao | Tĩnh |
| SET-04 | **High** | Đổi State Folder trong UI tạo ngay một DB rỗng và chạy tiếp trên đó; không migrate, không xác nhận, không validate | Cao | Tĩnh |
| SET-05 | **High** | TMDB bearer token bị gửi kèm mọi request Gemini, do dùng chung `HttpClient` singleton có `DefaultRequestHeaders` (liên vùng) | Cao | Tĩnh |
| SET-06 | Medium | Lỗi I/O khi Save/Load trong trang Settings không được bắt, làm crash process | Cao | Tĩnh |
| SET-07 | Medium | Migrate legacy `CategoryName` và `IntervalHours` là code chết: giá trị cũ bị bỏ âm thầm | Cao | Probe 01/02 |
| SET-08 | Medium | Field lạ hoặc mới hơn bị xóa khi Save, nên build cũ hơn chạy trên state thật sẽ xóa settings của build mới | Cao | Probe 06 |
| SET-09 | Medium | Validate thiếu: URL, path, `DefaultLibraryFolderName`, một số số và enum; UI không báo lỗi | Cao | Probe 08/09 + tĩnh |
| SET-10 | Low | Secret: mật khẩu qBittorrent hiện rõ trên UI; bản copy chưa redact khi restore; redactor dạng denylist và tài liệu lệch code | Cao | Tĩnh |
| SET-11 | Low | Pointer `state-path.txt` bị ghi lại không nguyên tử ở mỗi lần Save; pointer hỏng thì âm thầm quay về thư mục mặc định | Trung bình | Tĩnh |
| SET-12 | Low | Thay đổi không lan tới service đang chạy (log max lines, chu kỳ Auto-Track); vài chỗ UI lệch dữ liệu (download folder sống lại, CLI pin) | Cao | Probe 10 + tĩnh |
| SET-13 | Low | `Save()` chỉ khóa chính nó; `Current` bị nhiều thread sửa và serialize đồng thời | Trung bình | Tĩnh (chưa tái hiện) |

Không có Critical nào được xác nhận theo đúng tiêu chí của `00-method-and-scope.md`. SET-01 và SET-05 sát ngưỡng Critical; lý do xếp High ghi ở từng mục.

## 2. Trả lời 6 câu hỏi

### 2.1 Lưu và đọc

- **Định dạng:** một file `{StateFolder}/settings.json`, System.Text.Json, `WriteIndented`. Không có `SchemaVersion`, không có `[JsonExtensionData]`. Enum lưu dưới dạng số. Tên property phân biệt hoa thường.
- **Ghi nguyên tử:** *có một nửa*. `Save()` ghi `settings.json.tmp` rồi `File.Move(..., overwrite: true)` (`SettingsService.cs:113-133`), nên app crash giữa chừng không làm hỏng file đích. Nhưng dữ liệu **không được flush xuống đĩa** (`File.WriteAllText` không có `Flush(true)` hay `WriteThrough`), và **không giữ bản `.bak`**. Mất điện sau khi rename có thể để lại file rỗng hoặc toàn byte 0 (SET-01).
- **File hỏng hoặc rỗng:** `Load()` ném `JsonException` (Probe 03/04) và **không reset âm thầm**. Exception này bay ra khỏi `App.OnStartup` (`App.xaml.cs:67`), *trước* `StartCrashLoggingHooks()` (`:76`), nên app chết ngay mỗi lần khởi động mà không có hộp thoại và không có crash log (SET-01).
- **File chứa `null` hoặc `{}`:** `Current = new AppSettings()`, sau đó `ApplyLegacySetupCompleted` coi đây là bản cài legacy đã setup xong (`SetupCompleted=true`), và Auto-Track bật theo mặc định (Probe 05). Lần Save kế tiếp ghi đè bằng toàn bộ giá trị mặc định, tức **mất cấu hình âm thầm**, kể cả `Backup.MachineId` (một GUID mới được sinh).
- **Nhiều chỗ cùng ghi:** có ít nhất 9 nơi gọi `Save()`: `SettingsViewModel` (3), `AutoTrackService` (3, chạy trên thread nền), `LibraryViewModel`, `NewsViewModel` (3), `TorrentWorkspaceViewModel`, `RecipeWorkspaceViewModel`, `ConsoleLogViewModel`, `FirstRunViewModel` (2). Mỗi lần Save serialize **toàn bộ** `Current`, nên ai lưu cuối thì thắng cho mọi section. `_saveLock` chỉ chặn hai lần Save chạy cùng lúc, không bảo vệ các lần sửa `Current` (SET-13). `Load()` không khóa và thay luôn object `Current` (SET-02).
- **Ghi lúc tắt app:** `OnExit`/`SessionEnding` không gọi `Save()`, nên mọi trạng thái chỉ nằm trong bộ nhớ (ngân sách TMDB chưa lưu, dữ liệu VM chưa Save) đều mất khi thoát. Process bị kill giữa `WriteAllText(tmp)` và `Move` thì file đích vẫn nguyên, chỉ còn lại `.tmp` (vô hại, lần sau bị ghi đè). Rủi ro thật là mất điện (SET-01).

### 2.2 Migrate

| Migrate | Hoạt động? | Ghi chú |
|---|---|---|
| `AutoTorrent.CategoryName` → `TvShowCategoryName`/`MovieCategoryName` | **Không** | Initializer của hai property mới không rỗng (`"TV Show"`/`"Movie"`). Khi JSON cũ thiếu hai property này, deserializer giữ nguyên initializer, nên điều kiện `IsNullOrWhiteSpace` không bao giờ đúng (Probe 01). Lỗi có từ `5b8fc83` (2026-06-23). SET-07. |
| `AutoTrack.IntervalHours` → `TorrentHuntIntervalMinutes` | **Không** | Cùng nguyên nhân: initializer là `60`, nên điều kiện `<= 0` không bao giờ đúng (Probe 02). Có từ `a65d0ee` (2026-06-15). SET-07. |
| `LastTmdbRefreshDayKey`/`TmdbRefreshesToday` → `DailyBudget` | Có, chạy một lần | Xóa field legacy sau khi chuyển, không lặp. |
| `Startup.SetupCompleted == null` → `true` | Có | Nhưng cũng kích hoạt khi file là `null` hoặc `{}` (xem 2.1). |
| `AutoTorrent.DownloadFolder` → `DownloadFolders` | Chạy **mỗi lần** `EnsureDefaults` | Không phải migrate một lần: folder đơn luôn được chèn lại vào danh sách (Probe 10, SET-12). |
| Clamp số trong `EnsureDefaults` | Mỗi lần Load/Save | Idempotent. Thiếu một số field (SET-09). |

Không có migrate nào **ghi đè** một giá trị mới đã có. Vấn đề nằm ở hai chiều: (a) giá trị legacy tùy chỉnh bị bỏ qua, và (b) field không biết bị xóa khi chạy ngược phiên bản (SET-08). `CategoryName` không bao giờ được dọn và luôn được ghi `"AutoTorrent"`, nhưng vô hại.

### 2.3 Validate

- **Số:** `NumericUpDown` cộng `Math.Clamp` cả trong `Apply*` lẫn `EnsureDefaults`. Clamp diễn ra âm thầm: UI tự nhảy về giá trị đã clamp sau Save mà không báo gì. Có lỗ: `AutoTrack.HuntMinHoursAfterAirDate` chỉ được clamp trong VM (0..48), không clamp khi Load, nên `-50` sửa tay trong file lọt vào service (Probe 08). Enum số bất kỳ cũng lọt (Probe 09, `Theme=99`).
- **URL:** qBittorrent WebUI URL và Jellyfin BaseUrl **không được validate khi Save**. qBittorrent chỉ ném `InvalidOperationException("…URL is invalid")` lúc dùng (`QbittorrentClient.cs:1030-1036`), nên Auto-Track lỗi ở mỗi chu kỳ chạy.
- **Path:** StateFolder, download folder, symlink root, WARP exe, qBittorrent exe và `DefaultLibraryFolderName` đều là textbox tự do. Không kiểm tra path tuyệt đối, ổ đĩa có tồn tại không, hay ký tự hợp lệ. Đặc biệt: StateFolder sai làm crash (SET-04/SET-06), và `DefaultLibraryFolderName` là path tuyệt đối thì `Path.Combine` đổi gốc library sang ổ khác (SET-03/SET-09).
- **UI báo lỗi:** không có `ValidatesOnDataErrors`/`INotifyDataErrorInfo` nào; chỉ có `StatusMessage` sau các nút Test.

### 2.4 ViewModel và UI

- **Dirty/Save/Cancel:** không có dirty tracking, không có nút Cancel, không hỏi khi rời trang. `OnNavigatedTo()` gọi `Load()` từ đĩa (`SettingsViewModel.cs:408-413`, comment ghi "Dirty-tracking lands in Sprint 6", nhưng tính năng đó chưa bao giờ được làm). Rời trang rồi quay lại thì bản nháp mất âm thầm.
- **Binding:** VM giữ bản nháp riêng (property `[ObservableProperty]`), nên nhìn chung **không** áp ngay. Ngoại lệ có thật: `DefaultLibraryFolderName` (theo từng phím gõ), `WarpExecutablePath` (áp toàn bộ section Warp), các nút *Test qBittorrent* và *Test Jellyfin* (áp toàn bộ `AutoTorrent` hoặc `AutoTrack`, gồm cả `AutoTrack.Enabled`), cùng *Connect Google Drive* và *Backup now* (gọi Save toàn file). Các giá trị này sau đó bị **writer khác** ghi xuống đĩa (SET-03). Tài liệu `settings-modernization/04` ghi "Persisted? Only after Save" là **sai**.
- **Lan tới service:** đa số service đọc `_settingsService.Current` ở mỗi lần dùng nên thay đổi có hiệu lực ngay sau Save. Ngoại lệ: `AppLogger` cache `MaxLinesPerFile` trong constructor nên cần restart; chu kỳ TMDB và reconcile của Auto-Track chỉ đổi sau khi `Task.Delay` đang chờ kết thúc (có thể tới 24h); `DatabaseService.Initialize` chạy lại ở **mỗi** lần Save (SET-04). UI không ghi rõ mục nào cần restart.
- **Desync:** khi CLI pin, textbox StateFolder hiện giá trị người dùng gõ trong khi Save ép về thư mục pin. Download folder đã xóa khỏi danh sách vẫn sống lại (Probe 10). Nếu `SetEnabled(RunAtStartup)` lỗi thì `Save()` return sớm, nên theme, tray và DB không được áp dù file đã lưu (SET-06).

### 2.5 Credentials

| Secret | Nơi lưu | Dạng | Hiện trên UI | Log/console | Backup Drive |
|---|---|---|---|---|---|
| TMDB read token | `settings.json` | plaintext | che, có nút hiện | chỉ ghi `TokenConfigured=bool` | redact |
| Gemini API key | `settings.json` | plaintext | che | không | redact |
| qBittorrent password | `settings.json` | plaintext | **hiện rõ** (TextBox thường, `SettingsView.xaml:1241`) | không | redact |
| qBittorrent username / API key | `settings.json` | plaintext | API key được che | không | redact |
| Jellyfin API key | `settings.json` | plaintext | che | không | redact |
| Google OAuth client JSON | file người dùng chọn, hoặc `GoogleDrive/credentials.json` | plaintext | chỉ hiện path | không | path **không** redact (tài liệu nói có) |
| Google refresh/access token | `GoogleDrive/token/` (FileDataStore) | plaintext | không | không | không đưa vào zip |

- Log thật: grep các mẫu `password|apikey|api_key|Bearer` trên 99 file log thật cho 0 file khớp. `WriteBootstrapLog` có ghi ra `Console.WriteLine` nhưng chỉ chứa path, số lượng và cờ bool.
- Mask hiện 4 ký tự đầu và 4 ký tự cuối của secret dài hơn 8 ký tự (`MaskSecret`), chấp nhận được.
- Gemini key nằm trong query string `?key=` (`GeminiApiClient.cs:261`, `GeminiModelCatalogService.cs:207`). Hiện không có chỗ nào log URL, nhưng đây là một cái bẫy cho code sau này.
- **Rò rỉ thật:** TMDB token đi kèm request Gemini (SET-05).
- Restore cục bộ chép nguyên `settings.json` nguồn sang `restored-settings.review.json` (`BackupService.cs:248-252`), tạo thêm một bản plaintext chứa secret nếu nguồn còn secret (SET-10).

### 2.6 Lịch sử git: xem mục 3.

## 3. Lịch sử bug và hotspot

**Số commit (theo `git log --follow`):** `SettingsViewModel.cs` 35, `SettingsView.xaml` 34, `SettingsService.cs` 26, `AppSettings.cs` 14, `AutoTorrentSettings.cs` 11, `SystemSettingsViewModel.cs` 9, `AutoTrackSettings.cs` 7.

**Commit có nhiều dòng thay đổi nhất (4 file chính):** `a632059` (895 dòng, đổi giao diện), `36beb86` (804, *restructure settings tab*), `af30c72` (691, Gemini), `a79f6fb` (575), `94629eb` (474, backup), `618286d` (177, setup window và state-folder bootstrap).

Commit message chủ yếu là "add/update feature", hầu như không có commit "fix settings". Bug được sửa lẫn vào commit tính năng. Các mẫu lặp lại:

1. **Độ bền ghi file được vá kiểu chữa cháy.** `a1dda96` (2026-07-24, "update auto track and WARP service") cùng lúc thêm `_saveLock`, tmp+rename trong `SettingsService`, và vòng retry `IOException` khi lưu ngân sách TMDB trong `AutoTrackService.cs:373-389`. Điều đó cho thấy đã từng gặp lỗi ghi đồng thời hoặc file bị khóa. Bản vá dừng ở mức nguyên tử khi crash: chưa flush, chưa có `.bak`, chưa xử lý file hỏng, và các writer khác ngoài Auto-Track vẫn không có retry (SET-01, SET-06).
2. **Bootstrap state folder bị làm lại nhiều lần.** `618286d` (2026-09-14) thêm pointer, CLI pin, phát hiện legacy và `SetupCompleted` legacy; `9d43421` dời log settings. Lớp bug lặp lại ở đây là "chạy nhầm state folder" (comment nói rõ phải tránh để test launch trỏ vào live). Logic pin hiện đã chặt, nhưng đổi StateFolder **từ UI** vẫn là đường không được bảo vệ (SET-04), và pointer vẫn ghi không nguyên tử (SET-11).
3. **Migrate và chuẩn hóa nằm rải trong `EnsureDefaults`.** Gần như mỗi tính năng (`a65d0ee`, `9d178a9`, `b144a97`, `6ca767d`, `36beb86`, `94629eb`) đều thêm clamp hoặc migrate vào `EnsureDefaults`, đồng thời thêm một clamp gần giống vào `Apply*` của VM. Hai bản đã lệch nhau (`HuntMinHoursAfterAirDate`) và hai migrate là code chết ngay từ khi viết (SET-07). Không có test nào cho `SettingsService`: chỉ có `FakeSettingsService` trong `MediaManager.App.Tests/Cleanup/CleanupFakes.cs`.
4. **Thêm một setting phải sửa 5 chỗ.** Mỗi field mới cần sửa model, `EnsureDefaults`, property VM, `LoadFromSettings`, `Apply*` và XAML. Đây là nguyên nhân chính khiến VM và XAML đứng đầu về số lần sửa, và cũng là gốc của các lỗi bất đối xứng mà `settings-modernization/04` đã liệt kê.
5. **Reload khi điều hướng.** `177cd5b` (2026-08-23, Sprint 4) thêm `Load()` vào `OnNavigatedTo` như một bước tạm chờ dirty tracking ở Sprint 6. Sprint 6 chưa diễn ra, nên bước tạm này sinh ra SET-02.
6. **Live-apply có từ rất sớm.** `RefreshLibraryRootPreview` ghi vào `Current.DefaultLibraryFolderName` từ `d044e8c` (2026-05-16). Trước Sprint 4 thì vô hại vì không có writer nền; từ khi Auto-Track Save định kỳ thì không còn vô hại (SET-03).

**Hotspot cần ưu tiên:** `SettingsService.Load/Save/EnsureDefaults` (độ bền, migrate, đồng thời) và `SettingsViewModel.Save/OnNavigatedTo/Apply*` (ranh giới giữa bản nháp và trạng thái đã commit).

## 4. Findings chi tiết

### SET-01 — High — `settings.json` không durable; file rỗng hoặc hỏng làm app không khởi động được

**Code:** `Services/SettingsService.cs:72-110` (Load: `Deserialize` rồi `throw;`), `:113-133` (Save: `WriteAllText(tmp)` + `File.Move`), `:76-80` (JSON `null` thì reset về mặc định); `App.xaml.cs:66-67` (Load), `:76` (crash hooks được gắn *sau* Load).

**Hành vi đã chứng minh:**
- File rỗng → `JsonException` (Probe 03). File toàn byte 0 (dạng thường gặp trên NTFS sau mất điện khi metadata rename đã commit mà data chưa xuống đĩa) → `JsonException` (Probe 04).
- `null`/`{}` → toàn bộ giá trị mặc định, `SetupCompleted=true`, Auto-Track bật, `SourceFolders` rỗng (Probe 05). Save kế tiếp ghi đè file.
- `Save()` không gọi `Flush(true)` và không có `FileOptions.WriteThrough`, nên tmp+rename chỉ nguyên tử khi process crash, **không** an toàn khi mất điện.

**Kịch bản:** Auto-Track gọi `Save()` sau mỗi lần chạy (`AutoTrackService.cs:1748`). Laptop sập nguồn ngay sau đó. Lần mở app sau, `Load()` ném lỗi trong `OnStartup`, process thoát mà không có thông báo hay crash log (hook chưa gắn), và lặp lại ở mọi lần khởi động. Cách duy nhất người dùng tự làm được là xóa `settings.json`. Khi đó app tạo settings mặc định với `SetupCompleted=false`, buộc qua wizard lần đầu, và mất toàn bộ cấu hình cùng secret (bản trên Drive đã bị redact).

**Vì sao High mà không Critical:** cần mất điện hoặc crash OS đúng vào cửa sổ ghi, chưa tái hiện trên phần cứng thật; DB và media không bị ảnh hưởng. Dù vậy, khi đã xảy ra thì đây là lỗi khởi động lặp lại, chỉ thoát được bằng cách mất cấu hình.

**Hướng sửa (nhỏ, không đổi tính năng):**
1. Ghi bằng `FileStream` + `Flush(flushToDisk: true)` trước khi rename; hoặc `File.Replace(tmp, target, target + ".bak")` để luôn còn bản trước đó.
2. Khi `Load` lỗi parse: thử `settings.json.bak`. Nếu cũng hỏng thì đổi tên file hỏng thành `settings.json.corrupt-<timestamp>`, hiện `AppMessageBox` giải thích, và **không** tự ghi đè.
3. Coi `null`/`{}` là file hỏng, không phải "legacy đã setup".
4. Gắn crash hook trước `settings.Load`, hoặc bọc `Load` trong try/catch có thông báo.

### SET-02 — High — Mở trang Settings gọi `Load()` và thay `Current`, làm mất cập nhật của service nền

**Code:** `ViewModels/SettingsViewModel.cs:408-413` (`OnNavigatedTo` gọi `_settingsService.Load()`); `SettingsService.cs:83` (`Current = loaded`, thay cả instance); `AutoTrackService.cs:143-153` (`settings`/`budget` giữ reference tới object cũ suốt lượt chạy), `:263-279` (`budget.TryConsume`), `:373-389` (Save cuối lượt); `MainViewModel.cs:508`.

**Hành vi đã chứng minh (Probe 07):** giữ reference `DailyBudget`, tăng `Used`, gọi `Load()`, tăng tiếp trên reference cũ, rồi `Save()`. Kết quả trên đĩa là `Used=0`: phần tăng rơi vào object mồ côi.

**Kịch bản:** lượt TMDB discovery đang chạy (có thể kéo dài nhiều phút vì WARP và retry), người dùng mở trang Settings. Toàn bộ `TryConsume` sau đó ghi vào object cũ, và Save cuối lượt ghi object mới (lấy từ đĩa, chưa có số đã dùng). Ngân sách TMDB trong ngày bị đếm thiếu, nên Auto-Track gọi TMDB vượt `MaxTmdbRefreshesPerDay`, đúng điều mà ngân sách này sinh ra để chặn (TMDB block, SNI). Các ảnh hưởng phụ:
- Mọi chỉnh sửa chỉ nằm trong bộ nhớ (ví dụ `NormalizeGeminiSettings` lúc khởi động, `LastRunSummary` chưa lưu) cũng mất.
- `Load()` không khóa, có thể chạy xen với `Save()` của thread nền.
- Nếu lúc đó `settings.json` không còn (bị xóa hoặc đồng bộ mất), nhánh "file không tồn tại" (`SettingsService.cs:56-67`) đặt `SetupCompleted=false` và **tắt Auto-Track** trên chính object đang chạy rồi Save. Lần khởi động sau bị chặn bởi wizard.
- `Load()` ném lỗi lúc điều hướng thì crash (SET-06).

**Hướng sửa:** `OnNavigatedTo` chỉ gọi `LoadFromSettings()` (đọc lại từ `Current` trong bộ nhớ, vốn là nguồn đúng sau startup) và bỏ `_settingsService.Load()`. Về lâu dài, `SettingsService` không nên thay instance `Current` sau startup; nếu cần reload thì copy giá trị vào instance hiện có dưới lock.

### SET-03 — High — Sửa chưa lưu bị áp vào `Current` ngay, rồi bị writer khác ghi xuống đĩa

**Code:**
- `SettingsViewModel.cs:1018-1026` cộng `:1844-1849`: mỗi phím gõ trong ô *Default library folder name* gọi `RefreshLibraryRootPreview()`, và hàm này **ghi** `Current.DefaultLibraryFolderName`.
- `:1048-1057`: `OnWarpExecutablePathChanged` gọi `ApplyWarpSettings()`, áp cả `Enabled`, `AutoRecoverOnSsl`, `ConfirmDisconnect…` và `ConnectTimeout`.
- `:632-634`: *Test qBittorrent* gọi `ApplyAutoTorrentSettings()`, áp URL, credential, download folders, category và chính sách restart.
- `:822-824`: *Test Jellyfin* gọi `ApplyAutoTrackSettings()`, áp **`AutoTrack.Enabled`**, các chu kỳ, `MaxTmdbRefreshesPerDay`, anchor và Search limits.
- `:1421-1426`, `:1452-1456`: *Connect Google Drive* và *Backup now* gọi Save toàn file.
- Người tiêu thụ ngay: `LibraryPathResolver.cs:68-78` dùng `HardlinkService.cs:81,147`; `AutoTrackSchedulerService.cs:223-235`.
- Writer ghi xuống đĩa: `AutoTrackService.cs:377,384,1748`, `LibraryViewModel.cs:1850`, `NewsViewModel.cs:186,345,357`, `TorrentWorkspaceViewModel.cs:2118`, `RecipeWorkspaceViewModel.cs:339`, `ConsoleLogViewModel.cs:361`.

**Kịch bản 1 (filesystem):** người dùng gõ `MediaManagerLibrary2` vào ô tên thư mục library. Giữa lúc gõ, `Current` lần lượt mang giá trị `M`, `Me`, … Một lượt auto-link hoặc hardlink chạy đúng lúc đó sẽ tạo thư mục như `D:\MediaMan\Shows\…`. Người dùng rời trang mà không Save, tưởng là đã hủy. Nhưng lượt Auto-Track kế tiếp gọi `Save()` nên giá trị dở dang **được ghi xuống đĩa**. Lần sau mọi hardlink mới đi vào thư mục sai, còn library cũ bị bỏ rơi. Thêm nữa, nếu gõ một path tuyệt đối (`E:\X`), `Path.Combine(driveRoot, "E:\X")` trả về `E:\X`, nên gốc library rời khỏi ổ nguồn (hardlink khác volume sẽ lỗi; trên cùng volume thì ghi ra ngoài vùng quản lý).

**Kịch bản 2 (Auto-Track):** người dùng bỏ tick *Auto-Track enabled* để thử, rồi bấm *Test Jellyfin* và rời trang mà không Save. `AutoTrack.Enabled=false` đã có hiệu lực ngay, và bị lần Save của Library hoặc News ghi xuống đĩa, nên Auto-Track tắt vĩnh viễn mà người dùng không biết. Chiều ngược lại cũng vậy.

**Ghi chú:** `docs/settings-modernization/04-save-load-apply-problems.md` liệt kê các live-mutation này nhưng ghi "Persisted? Only after Save". Điều đó sai từ khi có writer nền; cần sửa tài liệu.

**Hướng sửa:** preview chỉ tính từ field của VM, không ghi `Current`. Bỏ live-apply trong `OnWarpExecutablePathChanged`. Các nút Test chỉ dựng một object cấu hình tạm và truyền vào `TestConnectionAsync(overrides)` thay vì `Apply*`. Nếu muốn giữ hành vi hiện tại thì ít nhất phải chụp và khôi phục `Current` sau khi test.

### SET-04 — High — Đổi State Folder trong UI tạo ngay một DB rỗng và chạy tiếp trên đó

**Code:** `SettingsViewModel.cs:493-528`: `Current.StateFolder = StateFolder` → `_settingsService.Save()` (ghi `settings.json` vào thư mục **mới**, rồi `WritePointer(new)`) → `_databaseService.Initialize(new)`. `DatabaseService.cs:51-66` `CreateDirectory` rồi mở hoặc **tạo** `media-manager.db` và migrate. `SettingsView.xaml:383` là textbox tự do; `STATE_FOLDER.md` ghi "Customizable in Settings → Browse".

**Hành vi:** không có xác nhận, không chép DB, `Recipes/`, `posters/`, `GoogleDrive/` (token), cache Gemini hay `WebView2/`. Ngay sau Save, mọi truy vấn DB của UI và service nền (Auto-Track, Backup, Symlink) chạy trên DB rỗng, trong khi log và các file khác vẫn ở thư mục cũ. Pointer đã trỏ sang thư mục mới nên từ lần khởi động sau app luôn dùng DB rỗng.

**Rủi ro liên vùng (chưa chứng minh, cần các phiên Backup/Symlink kiểm):** Backup có thể đẩy một DB rỗng lên đúng thư mục máy trên Drive, và history retention 20 bản sẽ dần đẩy các bản tốt ra ngoài. Symlink sync chạy trên DB rỗng có thể dọn symlink.

**Validate:** gõ path tương đối thì `SettingsFilePath` bị resolve theo CWD (khi chạy cùng Windows, CWD thường là `C:\Windows\System32`). Gõ ổ không tồn tại thì `Directory.CreateDirectory` ném lỗi và crash (SET-06), trong khi `Current.StateFolder` đã bị đổi trong bộ nhớ.

**Vì sao High:** dữ liệu cũ còn nguyên nên phục hồi được bằng cách Browse về lại; nhưng app trông như bị xóa sạch và chạy tiếp ở trạng thái lệch.

**Hướng sửa (không thêm tính năng):** chỉ gọi `Initialize` khi `StateFolder` thực sự đổi. Khi đổi, hỏi xác nhận rõ ràng ("thư mục mới không có dữ liệu; app sẽ dùng thư mục mới sau khi khởi động lại") và **chỉ ghi pointer, không chuyển DB live**, để thư mục mới có hiệu lực từ lần khởi động sau. Validate path tuyệt đối và ổ có tồn tại.

### SET-05 — High — TMDB bearer token bị gửi kèm request Gemini (dùng chung `HttpClient`) — liên vùng TMDB/Gemini

**Code:** `App.xaml.cs:291` `services.AddSingleton<HttpClient>()`; `TmdbMetadataProvider.cs:34` gán `BaseAddress` cho instance dùng chung; `:1182-1187` `ConfigureHeaders()` gán `_httpClient.DefaultRequestHeaders.Authorization = Bearer <TMDB token>`, được gọi trước mỗi request TMDB (`:59,128,227,…`). `GeminiApiClient.cs:261-270` và `GeminiModelCatalogService.cs:207-208` gửi qua **cùng instance** mà không tự đặt `Authorization`.

**Hành vi:** `HttpClient` gắn `DefaultRequestHeaders` vào mọi request không tự đặt header đó. Vì vậy, sau lần gọi TMDB đầu tiên trong phiên, mọi request tới `generativelanguage.googleapis.com` đều mang `Authorization: Bearer <TMDB token>`. Các request TMDB khác (`HostScanService`, `JellyfinLibraryRefreshService`, nút Test) tự đặt header nên không bị ảnh hưởng. Thêm vào đó, việc sửa `DefaultRequestHeaders` ở mỗi request từ nhiều thread không thread-safe (theo tài liệu .NET), có thể gây `InvalidOperationException` thỉnh thoảng.

**Tác động:** secret của một dịch vụ bị gửi cho bên thứ ba (Google) qua TLS. Có thể có thêm tác động chức năng: Google API có thể từ chối request vì bearer không hợp lệ (401) dù `?key=` đúng. **Chưa xác minh**, vì log thật không có dòng Gemini nào trong khoảng thời gian log còn giữ.

**Vì sao High mà không Critical:** theo câu chữ thì đây là "credential disclosure", nhưng người nhận là endpoint TLS của một nhà cung cấp lớn, token chỉ là TMDB read-access (chỉ đọc, thu hồi được), và không có kẻ tấn công nào được lợi. Nếu chủ sở hữu muốn áp đúng câu chữ thì có thể nâng lên Critical.

**Hướng sửa:** TMDB dùng `HttpClient` riêng (named client hoặc instance riêng) và đặt `Authorization` trên `HttpRequestMessage`, không đặt trên `DefaultRequestHeaders`. Tối thiểu: Gemini dùng một `HttpClient` riêng. Cần phiên TMDB/Jellyfin xác nhận.

### SET-06 — Medium — Lỗi I/O khi Save/Load trong trang Settings làm crash process

**Code:** `SettingsViewModel.cs:493-512` (`Save` sửa `Current` qua 10 hàm `Apply*` rồi gọi `_settingsService.Save()`, không có try/catch); `:1424-1425`, `:1455-1456` (Save nằm ngoài try); `:411` (Load lúc điều hướng); `App.xaml.cs:603-615` (`OnDispatcherUnhandledException` ghi crash log nhưng **không** đặt `e.Handled = true`, nên process thoát).

**Kịch bản:** antivirus hoặc trình đồng bộ thư mục đang giữ `settings.json` → `File.Move` ném `IOException`. `AutoTrackService` đã phải thêm retry cho đúng lỗi này (`:378-388`), nên đây không phải lỗi giả định. Bấm Save trong Settings thì app tắt ngang. Nếu lỗi không làm crash (trường hợp được bắt ở nơi khác), `Current` đã chứa toàn bộ bản nháp, nên service dùng ngay và writer khác sẽ ghi xuống (SET-03). Tương tự với StateFolder sai (SET-04).

Thêm: khi `_windowsStartupService.SetEnabled` lỗi, `Save()` return sớm (`:513-521`), nên theme, tray, `Initialize` DB và library preview bị bỏ qua dù file đã lưu.

**Hướng sửa:** validate trước rồi mới áp vào `Current`; bọc `Save()` trong try/catch, retry ngắn khi gặp sharing violation, báo lỗi qua `StatusMessage`. Khi lỗi, khôi phục `Current` từ snapshot. Đây là các thay đổi nhỏ và không đổi tính năng.

### SET-07 — Medium — Migrate legacy `CategoryName` và `IntervalHours` là code chết

**Code:** `SettingsService.cs:469-489` (`MigrateAutoTorrentCategories`) với `AutoTorrentSettings.cs:25-27` (initializer `"TV Show"`/`"Movie"`); `SettingsService.cs:404-409` với `AutoTrackSettings.cs:22` (`TorrentHuntIntervalMinutes = 60`).

**Đã chứng minh:** `{"AutoTorrent":{"CategoryName":"MyCat"}}` cho ra `Tv="TV Show"`, `Movie="Movie"` (Probe 01). `{"AutoTrack":{"IntervalHours":6}}` cho ra `TorrentHuntIntervalMinutes=60` thay vì 360 (Probe 02).

**Tác động:** bản cài có category tùy chỉnh trước `5b8fc83` sẽ âm thầm chuyển sang thêm torrent vào "TV Show"/"Movie". Category trong qBittorrent thường gắn với save path, nên file tải về rơi vào thư mục khác và các bước reconcile theo category có thể bỏ sót torrent cũ. Chu kỳ hunt legacy 6h thành 60 phút, tức gọi indexer gấp 6 lần. **Tác động hiện tại thấp:** bản cài đang chạy đã có cả hai field từ tháng 6, nên chủ yếu ảnh hưởng tới settings cũ được restore hoặc chép từ máy khác.

**Hướng sửa:** phát hiện "thiếu property" thay vì "rỗng": đọc `JsonDocument`/`JsonNode` để kiểm tra key, hoặc đổi kiểu thành nullable để `null` nghĩa là thiếu. Thêm test hồi quy. Không cần đổi hành vi với file mới.

### SET-08 — Medium — Field lạ hoặc mới hơn bị xóa khi Save

**Code:** `SettingsService.cs:10` (`JsonSerializerOptions` mặc định, không có `[JsonExtensionData]` trong `AppSettings` và các class con).

**Đã chứng minh (Probe 06):** cả `FutureFeature` ở cấp gốc lẫn `Logs.FutureLogField` đều biến mất sau `Save()`.

**Kịch bản thực tế:** repo có nhiều nhánh và worktree chạy song song (`feat/native-search` đang thêm settings). Bản cài ở `<InstallDir>\…` hoặc một build cũ được chạy với state thật (không `--state-folder`) sẽ xóa mọi field mà chỉ build mới biết ngay ở lần Save đầu tiên. Lần Save đó có thể do Auto-Track tự gọi, không cần người dùng làm gì. Quay lại build mới thì các field đó về mặc định. Kèm theo: enum số không được kiểm tra (Probe 09) nên giá trị enum mới bị build cũ đọc thành số lạ.

**Hướng sửa:** thêm `[JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }` vào `AppSettings` và từng class settings con để giữ nguyên field không biết. Cân nhắc thêm `SchemaVersion` và từ chối Save (chỉ cảnh báo) khi file có version mới hơn build.

### SET-09 — Medium — Validate thiếu, UI không báo

**Code:** `SettingsViewModel.cs:1641-1699` (`ApplyAutoTorrentSettings`: URL chỉ được `Trim`), `:1767-1824` (Jellyfin `BaseUrl` chỉ `Trim().TrimEnd('/')`), `:495-500` (StateFolder và `DefaultLibraryFolderName` không kiểm tra), `SettingsService.cs:404-467` (`MigrateAutoTrackSettings` không clamp `HuntMinHoursAfterAirDate`), `QbittorrentClient.cs:1030-1036`.

**Đã chứng minh:** `HuntMinHoursAfterAirDate=-50` lọt qua Load (Probe 08); `Ui.Theme=99` lọt qua (Probe 09).

**Tác động:** URL sai được lưu bình thường, rồi Auto-Track lỗi ở mỗi chu kỳ với thông báo nằm trong log. `DefaultLibraryFolderName` chứa `\`, `..` hoặc path tuyệt đối thì đổi gốc hardlink (SET-03). Clamp âm thầm khiến người dùng không biết giá trị mình gõ đã bị đổi.

**Hướng sửa:** một hàm `SettingsValidator` dùng chung (Core) gọi trước `Apply*`: `Uri.TryCreate` với scheme http/https; path phải tuyệt đối và ổ phải tồn tại; tên thư mục không chứa ký tự cấm, `\` hay `..`; `Enum.IsDefined`. Khi lỗi thì không Save và báo qua `StatusMessage`. Thêm clamp `HuntMinHoursAfterAirDate` vào `EnsureDefaults`.

### SET-10 — Low — Secret: hiển thị, bản sao, độ lệch của redactor

- `SettingsView.xaml:1240-1242`: mật khẩu qBittorrent là `TextBox` thường, luôn hiện rõ, trong khi các secret khác đều được che. Rủi ro khi chia sẻ màn hình hoặc chụp ảnh.
- `BackupService.cs:248-252`: restore cục bộ chép nguyên `settings.json` nguồn thành `restored-settings.review.json` trong state folder đích. Nếu nguồn là state folder được copy (còn secret), ta có thêm một file plaintext chứa secret không ai dọn.
- `BackupSettingsRedactor.cs` dùng denylist: secret mới thêm sau này sẽ mặc định **bị** upload. `STATE_FOLDER.md` ghi `Backup.CredentialsFilePath` được redact nhưng code không làm vậy (lệch tài liệu; đây chỉ là path, nhưng path có thể chứa tên user Windows).
- Plaintext trong `settings.json` là hành vi đã được ghi nhận trong audit trước (`03-findings-register.md`, phần review notes). Không đưa thêm vào finding.

**Hướng sửa:** dùng `PasswordBox` hoặc mask giống API key; xóa hoặc redact file review sau khi wizard dùng xong; thêm test kiểm tra bằng reflection rằng mọi property tên `*Password|*ApiKey|*Token|*Secret` đều được redact; sửa `STATE_FOLDER.md`. Mã hóa DPAPI là tính năng mới, ngoài phạm vi.

### SET-11 — Low — Pointer `state-path.txt` ghi không nguyên tử ở mỗi lần Save

**Code:** `SettingsService.cs:131` (`WritePointer` được gọi trong **mọi** `Save()`), `:224-247` (`File.WriteAllText` trực tiếp), `:196-222` (`TryReadPointer` trả `false` khi file rỗng), `:173-194` (fallback về legacy hoặc thư mục mặc định).

**Kịch bản:** mất điện trong lúc ghi pointer (xác suất tương đương SET-01 vì nó được ghi lại sau mỗi Save) để lại pointer rỗng. Nếu state folder nằm ở nơi khác `<StateFolder>` và khác thư mục mặc định, app khởi động với `%LocalAppData%\MediaManager\State` mới tinh: wizard lần đầu, DB rỗng, và Save đầu tiên ghi pointer mới, chốt luôn việc lệch thư mục. Trên máy hiện tại, fallback legacy trùng thư mục thật nên tác động gần như bằng 0.

**Hướng sửa:** chỉ ghi pointer khi giá trị đổi; ghi qua tmp+rename; khi pointer tồn tại mà không đọc được thì cảnh báo thay vì âm thầm fallback.

### SET-12 — Low — Thay đổi không lan tới service; UI lệch dữ liệu

- `AppLogger.cs:28-31` cache `MaxLinesPerFile` trong constructor; UI không ghi "cần restart".
- `AutoTrackSchedulerService.cs:133,189`: chu kỳ mới chỉ áp sau khi `Task.Delay` hiện tại xong (tối đa 1440 phút).
- Download folder: xóa folder đang là "default" khỏi danh sách thì nó **sống lại** khi Save, vì `DownloadFolder` đơn vẫn giữ giá trị và được chèn lại cả trong `ApplyAutoTorrentSettings` (`:1659-1663`) lẫn `EnsureDefaults` (`SettingsService.cs:313-317`) (Probe 10).
- CLI pin: Save ép `StateFolder` về thư mục pin nhưng textbox vẫn hiện giá trị đã gõ; `StatusMessage` báo "Saved settings to <pin>".
- `ApplyNotificationSettings` (`:1738-1748`) dựng lại `EnabledByKind` chỉ từ catalog hiện tại, nên key của build mới bị xóa (cùng loại với SET-08).

**Hướng sửa:** thêm chú thích "cần khởi động lại" cạnh các mục tương ứng; khi xóa folder trùng `AutoTorrentDownloadFolder` thì xóa luôn field đó; dựng lại `StateFolder` từ `Current` sau Save.

### SET-13 — Low — `Current` bị sửa và serialize đồng thời

**Code:** `SettingsService.cs:113-133` (`_saveLock` chỉ bao `Save`; `EnsureDefaults` chạy trong lock nhưng sửa `Current` mà thread UI cũng đang đọc và ghi); `Load()` không lock; `AutoTrackService` gọi `Save()` trên thread nền trong khi VM trên thread UI đang chạy chuỗi `Apply*`.

**Rủi ro:** `Save()` nền chạy giữa chuỗi `Apply*` sẽ ghi một bản nửa cũ nửa mới. Bản đó thường được ghi đè ngay bởi Save của VM, nhưng nếu Save của VM lỗi (SET-06) thì nửa vời này nằm lại trên đĩa. `EnsureDefaults` thay `Notifications.EnabledByKind`, `DownloadFolders`… trên thread nền trong khi UI có thể đang enumerate. **Chưa tái hiện**, nên giữ ở mức Low.

**Hướng sửa:** dùng chung một lock cho cả "sửa `Current`", `Save` và `Load`, ví dụ API `Update(Action<AppSettings>)`; hoặc serialize một snapshot đã clone dưới lock.

## 5. Review notes (không phải finding)

- **Không có dirty tracking hay Cancel:** là thiếu tính năng (Sprint 6), không xếp mức. Nhưng nó làm nặng SET-02 và SET-03.
- `NormalizeGeminiSettings` (`App.xaml.cs:556-566`) sửa `Current` lúc khởi động mà không Save. Chấp nhận được, vì SET-03 dù sao cũng sẽ ghi nó xuống.
- `EnsureDefaults` luôn đặt `LibraryRootMode = AutoPerDrive`. `DriveLibraryRoots` không có UI nhưng được tôn trọng. Không validate giá trị override (cùng loại SET-09).
- Các tham số search (`SnapshotTargetResults`, `LocalMatchWorkers`, `FuzzyDeduplicateSizeToleranceMb`, `TorrentValidation`) không được clamp. Đang viết lại trên `feat/native-search`, nên không xét.
- `docs/settings-modernization/04` ghi số dòng cũ (`SettingsViewModel.Save()` "lines 499–536", hiện là 493-531) và có kết luận sai về việc lưu (SET-03). Nên cập nhật khi sửa.

## 6. Test còn thiếu

Hiện **không có test nào** cho `SettingsService` hay `SettingsViewModel`. Đề xuất, theo thứ tự giá trị:

1. `Load` với file rỗng, toàn byte 0, `null`, `{}` hoặc JSON cụt: không throw ra ngoài, giữ lại file hỏng, dùng `.bak` nếu có, không đánh dấu `SetupCompleted=true` (SET-01).
2. `Save` để lại `.bak`, và `.tmp` cũ không chặn lần Save sau (SET-01).
3. Reload khi điều hướng không làm mất cập nhật `DailyBudget` đang giữ reference (Probe 07 đảo điều kiện) (SET-02).
4. Preview library hoặc WARP path, nút Test qBittorrent/Jellyfin **không** làm đổi `ISettingsService.Current` (SET-03).
5. Đổi `StateFolder` không gọi `IDatabaseService.Initialize` với thư mục mới trong phiên hiện tại; path tương đối hoặc ổ không tồn tại bị từ chối (SET-04).
6. Request Gemini không mang header `Authorization` (dùng `HttpMessageHandler` giả) (SET-05).
7. `SettingsViewModel.Save` khi `ISettingsService.Save` ném `IOException`: không crash, `Current` được khôi phục (SET-06).
8. Migrate legacy `CategoryName` tùy chỉnh và `IntervalHours` (Probe 01/02 đảo điều kiện); `DailyBudget` legacy (SET-07).
9. Round-trip giữ field lạ ở cấp gốc và cấp con (SET-08).
10. Validator: URL, path, tên thư mục, enum, `HuntMinHoursAfterAirDate` (SET-09).
11. Redactor tự phát hiện property secret mới bằng reflection (SET-10).
12. Pointer: rỗng, tương đối, CLI pin không ghi pointer (SET-11).

Tất cả đều chạy được với thư mục tạm và `Load(<pin>)` giống các probe, không chạm state thật.

## 7. Thứ tự sửa đề xuất

| Bước | Finding | Lý do | Quy mô |
|---|---|---|---|
| 1 | SET-02 | Một dòng (bỏ `_settingsService.Load()` trong `OnNavigatedTo`), chặn lost-update ngân sách TMDB ngay | Rất nhỏ |
| 2 | SET-03 | Bỏ ghi `Current` trong preview, WARP path và nút Test; chặn ghi hardlink sai chỗ và Auto-Track bị tắt ngầm | Nhỏ |
| 3 | SET-01 (+ test 1-2) | `.bak`, flush, cách ly file hỏng, gắn crash hook trước `Load` | Nhỏ-vừa |
| 4 | SET-05 | Tách `HttpClient` cho TMDB (cần phối hợp phiên TMDB) | Nhỏ |
| 5 | SET-06 + SET-04 | try/catch và snapshot quanh Save; chỉ `Initialize` khi đổi folder và chỉ áp sau restart | Vừa |
| 6 | SET-08 | `[JsonExtensionData]` trước khi `feat/native-search` thêm settings mới | Nhỏ |
| 7 | SET-07, SET-09 | Phát hiện thiếu property; validator chung | Vừa |
| 8 | SET-10..13 | Dọn dẹp | Nhỏ |

Bước 1-2 nên làm trước mọi việc tách VM trong `settings-modernization`, vì chúng thay đổi giả định "VM là bản nháp, `Current` là bản đã commit" mà kế hoạch đó dựa vào.

## Phụ lục A — Probe đã chạy

Probe nằm trong file tạm `MediaManager.App.Tests/Services/ZzSettingsAuditProbeTests.cs`, đã bị xóa sau khi chạy và không commit. Mỗi probe ghi `settings.json` vào `%TEMP%\mm-settings-audit-<guid>`, gọi `new SettingsService().Load(dir)` (CLI pin), rồi xóa thư mục ở `Dispose`. Lệnh chạy:

```
dotnet test -c Debug --filter "FullyQualifiedName~ZzSettingsAuditProbe"
```

Kết quả: `Total tests: 10`, tất cả Passed (2026-11-12). Lần chạy đầu lỗi biên dịch trong file probe (thiếu `using System.IO`) và không chạy test nào.

| # | Input / thao tác | Kết quả quan sát | Finding |
|---|---|---|---|
| 01 | `{"AutoTorrent":{"CategoryName":"MyCat"}}` | `Tv='TV Show' Movie='Movie'` | SET-07 |
| 02 | `{"AutoTrack":{"IntervalHours":6}}` | `TorrentHuntIntervalMinutes=60` | SET-07 |
| 03 | file rỗng | `JsonException` | SET-01 |
| 04 | 4096 byte 0 | `JsonException` | SET-01 |
| 05 | `null` | `SetupCompleted=True`, `AutoTrack.Enabled=True`, `SourceFolders=0` | SET-01 |
| 06 | field lạ ở cấp gốc và trong `Logs`, rồi `Save()` | cả hai bị xóa | SET-08 |
| 07 | giữ ref `DailyBudget` → `Load()` → tăng `Used` → `Save()` | `Used=0` trên đĩa | SET-02 |
| 08 | `HuntMinHoursAfterAirDate=-50` | giữ nguyên `-50` | SET-09 |
| 09 | `Ui.Theme=99` | giữ nguyên `99` | SET-09 |
| 10 | `DownloadFolder="D:\DL"`, danh sách đặt lại thành `["E:\DL2"]` → `Save()` | `D:\DL\|E:\DL2` | SET-12 |
