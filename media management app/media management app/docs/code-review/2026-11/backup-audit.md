# Audit backup và restore (2026-11)

Phạm vi: `Services/Backup/*`, `ViewModels/BackupHistoryItemViewModel.cs`, phần
Backup của `SettingsViewModel` / `SystemSettingsViewModel` / `Views/SettingsView.xaml`,
restore ở `FirstRunViewModel`, `DatabaseService.CreateSafeSnapshot`/`Initialize`,
và `docs/STATE_FOLDER.md`. Mọi đường dẫn tính từ `media management app/media management app/`.

Baseline: nhánh `claude/backup-audit-e0f13c` tại `ff2caa3` (main). Mức độ theo
[`00-method-and-scope.md`](../00-method-and-scope.md). Không sửa code app, không
chạy backup/restore thật, không gọi Google Drive.

## Tóm tắt

| ID | Mức độ | Tiêu đề | Bằng chứng |
|----|--------|---------|------------|
| BAK-01 | **Critical** (đề xuất nâng AUD-003) | Restore khi có writer đang mở transaction làm hỏng DB vừa restore; hỏng được backup lên Drive mà không ai biết | Tái hiện (harness S6) + đường đi tĩnh |
| BAK-02 | High | Restore ghi đè DB đang chạy mà không validate, không giữ bản cũ, không rollback | Tái hiện (S3, S4) |
| BAK-03 | High | Scheduler tự kích hoạt lại chính nó: backup cứ ~21–24 phút một lần, 20 bản history chỉ phủ khoảng 7–8 giờ | Tái hiện (S7) |
| BAK-04 | Medium | Backup lỗi được thử lại ở mọi lần poll 3 phút, không có backoff, và mỗi lần đều ghi lại `settings.json` | Tái hiện (S8) |
| BAK-05 | Medium | Drive ID cache trong settings không bao giờ được kiểm tra lại: `latest.zip` hoặc thư mục bị xóa thì backup hỏng vĩnh viễn, thư mục bị đưa vào thùng rác thì backup rơi vào thùng rác | Tái hiện (S9) + tĩnh |
| BAK-06 | Medium | List history không phân trang: retention > 100 thì không bao giờ prune, UI restore không thấy các bản mới nhất | Tĩnh |
| BAK-07 | Medium | Connect không làm mới `DriveService` đã cache; không có Disconnect; trạng thái "Connected" chỉ dựa vào việc file tồn tại | Tĩnh |
| BAK-08 | Low | Redactor theo kiểu denylist: lọt credential nằm trong URL, `Backup.CredentialsFilePath` và chuỗi lỗi; không có test chặn trường secret mới | Tái hiện (S1) |
| BAK-09 | Low | Restore Recipes gộp chứ không thay thế, chép cả file không phải `.rcp`, và không nạp lại cache recipe | Tái hiện (S5) |
| BAK-10 | Low | Tắt app giữa lúc backup: ghi nhận là failure, gọi `Save()` trong lúc shutdown, snapshot không hủy được, snapshot thô nằm lại trong %TEMP% nếu crash | Tĩnh |
| BAK-11 | Low | Token OAuth (refresh token) lưu plaintext qua `FileDataStore`, không có DPAPI | Tĩnh |
| BAK-12 | Low | Doc drift: `STATE_FOLDER.md`, XML doc của `IGoogleDriveClient`, và dialog migration | Tĩnh |
| BAK-13 | Low | `.gitignore` `Backup*/` vẫn ẩn mọi thư mục `Backup*` khác (ví dụ test backup) | `git check-ignore` |

Tình trạng AUD-003: **vẫn mở, chưa có thay đổi nào**. Code `Services/Backup` không đổi
từ 2026-10-12, ngoài một dòng đổi version. Lần này đã tái hiện được việc hỏng DB
trên fixture tạm (xem BAK-01), nên đề xuất nâng AUD-003 lên Critical và ghép với BAK-01.

## Cách kiểm chứng

- Đọc tĩnh toàn bộ code trong phạm vi, cùng `DatabaseService`, `MigrationRunner`, `App.xaml.cs`
  (thứ tự khởi động) và XML doc của `Google.Apis` 1.75.0 (hành vi `DownloadAsync`).
- Harness console (.NET 8) tham chiếu project app của worktree này, chạy **chỉ trên thư mục
  tạm trong scratchpad** (đặt `TMP`/`TEMP` của tiến trình về đó), với `IGoogleDriveClient`
  giả (không có mạng) và `ISettingsService` giả, có `Save()` làm giống bản thật (serialize rồi
  `File.Move` đè `settings.json`). Các phần dùng thẳng code thật: `BackupService`,
  `BackupSchedulerService` (gọi `RunPollCycleAsync` bằng reflection), `DatabaseService`
  và `BackupSettingsRedactor`. Secret trong harness đều là chuỗi giả `DUMMY-SECRET-*`.
- Build: một lần build đầy đủ (lần gọi trước đó dừng sau 6 giây ở bước validate
  NETSDK1151, chưa compile), thêm một lần build incremental chỉ cho harness. Không chạy
  `dotnet test`.
- Mã nguồn và output của harness để ở thư mục notes chung của nhóm phiên:
  `side-session-notes/<phiên gốc>/backup-audit-harness/` (`Program.cs`, `run-output.txt`).
  Không đưa vào repo.

Kết quả harness (rút gọn):

| Mục | Kết quả |
|-----|---------|
| S1 redactor | TMDB, Gemini, qB user/pass/apikey, Jellyfin apikey đều bị xóa. **Còn lọt**: credential trong URL qB (`http://user:pass@…`), `api_key=` trong query của Jellyfin `BaseUrl`, `Backup.CredentialsFilePath`, `Backup.LastBackupError` |
| S2 zip slip | `../x`, `Recipes/../../x`, `..\x` đều bị .NET chặn (`IOException`), không có file nào thoát ra ngoài thư mục đích |
| S3 DB hỏng trong backup | `ApplyLocalBackup` ném `SQLite Error 26: file is not a database` **sau khi** đã đè DB đang chạy. Lúc này DB đang chạy giống hệt file rác, không còn bản cũ nào |
| S4 manifest | Checksum `DEADBEEF`, `AppVersion` 99.0.0 và `MachineId` lạ đều bị bỏ qua. DB có migration `999_from_future…` vẫn được `Initialize` thật nhận mà không cảnh báo |
| S5 recipes | Sau restore còn cả `created-after-backup.rcp` (không có trong backup) và `notes.txt` |
| S6 AUD-003 | Writer mở transaction (`-journal` có mặt). `ApplyLocalBackup` trả về sau 39 ms, `File.Copy` thành công, writer `COMMIT` thành công. Sau đó `PRAGMA integrity_check` báo **`Tree 25 page 25 cell 36: 2nd reference to page 60`**. Bảng `OnlyInBackup` có mặt, nhưng `AuditMarker` lại chứa `live:3000,writer:1`: DB lai giữa hai bản, bị hỏng |
| S7 scheduler | Sau setup không đổi gì, mỗi chu kỳ debounce lại chạy thêm một backup (6 backup trên 6 chu kỳ) |
| S8 lỗi | 5 poll cho 5 lần thử thất bại, 5 warning và 5 lần ghi `settings.json` |
| S9 ID cũ | 3 lần chạy đều thất bại, `historyUploads=0`, `LatestFileId` không đổi |

---

## Lịch sử bug và lịch sử git

| Commit | Ngày | Nội dung |
|--------|------|----------|
| `94629eb` | 2026-08-11 | "add backup service": thêm `BackupSettings`, `DatabaseService.CreateSafeSnapshot` (đã dùng online backup API ngay từ đầu), UI Settings/Backup, `BackupHistoryItemViewModel`. **Toàn bộ `Services/Backup/*.cs` không được commit** vì quy tắc `Backup*/` trong `.gitignore` mẫu của Visual Studio |
| `a8431f8` | 2026-08-14 | Pill trạng thái Drive (DeviceStatus) |
| `290da3b` | 2026-10-12 | "fix: track Services/Backup sources hidden by VS gitignore Backup*/ rule": thêm `!**/Services/Backup/` và commit 13 file (1234 dòng) trong một lần. Thông điệp commit ghi rằng một clean clone **không compile được** (thiếu `IBackupService`, `IGoogleDriveClient`) |
| `60731dc` | 2026-10-12 | Manifest dùng `AppVersionInfo.Informational` thay cho assembly version |

Hệ quả:

- Khoảng 2 tháng (08-11 → 10-12) code backup thay đổi mà không có lịch sử. Không biết
  bản nào đã chạy trên máy thật trong giai đoạn đó, và không bisect được bug backup ở giai
  đoạn này.
- Không có commit nào sửa lỗi backup/restore từ sau đó. Các điểm audit trước đã nêu
  ("manifest checksums are generated but not validated by restore", AUD-003) vẫn y nguyên.
- Quy tắc `Backup*/` **vẫn còn** và chỉ được miễn cho `Services/Backup/`.
  `git check-ignore -v` xác nhận `MediaManager.App.Tests/Backup/x.cs` và
  `Views/Backup/x.xaml` vẫn bị ignore (BAK-13). Đây cũng là một lý do có thể giải thích
  vì sao repo hiện **không có test backup nào**.

---

## Trả lời các câu hỏi

### 1. Tạo backup

- **Snapshot DB nhất quán.** `CreateSafeSnapshot` dùng `SqliteConnection.BackupDatabase`
  (online backup API), không copy file thô (`Services/DatabaseService.cs:20-49`). DB chạy
  ở rollback journal mặc định: không có `PRAGMA journal_mode` ở đâu trong code, nên không
  có WAL/SHM. Backup API đọc qua pager, vì vậy hot journal và transaction đang mở được xử lý
  đúng. `STATE_FOLDER.md` ghi "WAL checkpoint + copy" là sai (BAK-12).
- **File được đưa vào backup:** snapshot `media-manager.db`, `settings.json` đã redact,
  `Recipes/*.rcp` và `manifest.json` (`BackupService.cs:363-391`).
- **File cố ý bỏ ra:** `GoogleDrive/` (gồm credentials và token, đúng là nên bỏ), `logs/`,
  `posters/` (cache), `WebView2/`, `session.alive`, `last-exit.json`, `warp-mode-restore.json`.
- **File bị thiếu mà đáng cân nhắc:** `gemini-mapping-cache.json` (kết quả AI tốn quota để tạo
  lại) và `gemini-quota.json`. Chúng không phải dữ liệu không thể tái tạo, nên không ghi
  thành finding.
- **Manifest:** gồm `AppVersion`, `TimestampUtc`, `TriggerType`, `MachineId`, `MachineName`
  và SHA-256 từng entry. **Không có schema version của DB** và **không có version của định
  dạng manifest**. Checksum chỉ được ghi ra, không bao giờ được đọc lại (BAK-02).
- **Nén:** `ZipFile.CreateFromDirectory(..., CompressionLevel.Optimal)` ghi vào
  `%TEMP%\MediaManagerBackup\<guid>`, dọn trong `finally`. Nếu crash thì snapshot DB
  không mã hóa sẽ nằm lại trong %TEMP% (BAK-10).

### 2. Restore

- **AUD-003:** xác nhận vẫn mở, và đã tái hiện được việc hỏng DB (BAK-01).
- **Validate trước khi apply:** không có. Không kiểm checksum, version app, MachineId,
  schema hay `integrity_check`. File không phải SQLite chỉ bị phát hiện **sau khi** đã đè
  (BAK-02).
- **Backup tự động trước khi restore:** không có. File bị `File.Copy(..., overwrite: true)` đè thẳng.
- **Rollback:** không có. Thứ tự chạy là DB → Initialize → Recipes → review settings, nên lỗi ở
  giữa chừng để lại trạng thái nửa vời.
- **Đường dẫn trong settings của máy khác:** restore **không** apply `settings.json`, chỉ
  ghi ra `restored-settings.review.json`. First-run chỉ prefill một số trường do người dùng
  duyệt (`FirstRunViewModel.cs:1156-1204`) và không chép `Backup.*`, nên `MachineId` và
  Drive ID không lan sang máy khác. Ngược lại, **DB** chứa đường dẫn tuyệt đối của máy
  nguồn (`SourceItems.FilePath`, `LinkedPath`...) và được dùng nguyên trạng. Ảnh hưởng tới
  các luồng dọn dẹp/link thuộc phạm vi audit Library/Deletion, nên ở đây chỉ ghi chú.
- UI Settings chỉ liệt kê history của **máy hiện tại** (thư mục `MachineId`), nên muốn
  restore chéo máy thì phải tải zip về tay rồi chạy first-run. Đây là giới hạn tính năng,
  không phải lỗi.

### 3. Bảo mật

- **Redactor:** đủ cho các trường secret có tên riêng (TMDB, Gemini, qBittorrent
  user/pass/apikey, Jellyfin apikey). Lọt các trường kể ở BAK-08.
- **Token Google:** `FileDataStore(<StateFolder>/GoogleDrive/token, fullPath: true)`, file
  `Google.Apis.Auth.OAuth2.Responses.TokenResponse-media-manager`, JSON plaintext có
  refresh token (BAK-11). Token **không** nằm trong zip backup.
- **Scope OAuth:** `DriveService.Scope.DriveFile` (`GoogleDriveClient.cs:12`), là mức tối
  thiểu: app chỉ thấy và xóa được file do chính OAuth client này tạo ra.
- **Zip slip:** an toàn. `ZipFile.ExtractToDirectory` của .NET 8 chặn entry thoát khỏi thư mục
  đích (S2). Recipes còn được làm phẳng bằng `Path.GetFileName`. Không giới hạn kích thước giải
  nén, nhưng nguồn zip là Drive của chính người dùng hoặc file họ tự chọn, nên không ghi thành finding.

### 4. Google Drive

- **Upload lỗi:** `UploadAsync` có kiểm `progress.Exception` (`GoogleDriveClient.cs:158-174`).
  Resumable upload chỉ tạo hoặc thay file khi hoàn tất, nên không sinh file dở dang.
  `latest.zip` được upload **trước** bản history, vì vậy nếu `latest.zip` lỗi thì bản
  history cũng không được tạo (BAK-05).
- **Download lỗi:** `DownloadFileAsync` bỏ qua `IDownloadProgress` mà `DownloadAsync` trả về
  (`GoogleDriveClient.cs:208`). Theo XML doc của Google.Apis 1.75.0, khi tải lỗi thì
  *"IDownloadProgress.Exception will contain the exception"*, tức là không throw. Zip tải
  dở thiếu End of Central Directory, nên `ExtractToDirectory` sẽ ném lỗi trước khi đụng tới DB.
  Vì vậy hiện tại an toàn nhờ may mắn, nhưng thông báo lỗi gây hiểu nhầm (ghi vào BAK-02).
- **Quota:** khi hết quota, mỗi lần poll vẫn snapshot, nén và upload lại từ đầu (BAK-04).
  Retention > 100 thì không prune, nên dung lượng tăng mãi (BAK-06).
- **Token hết hạn hoặc bị thu hồi:** backup thất bại và lặp lại liên tục (BAK-04). Bấm
  Connect vẫn trả "Connected" vì `DriveService` cũ còn trong cache (BAK-07).
- **Xoay vòng và xóa:** prune chỉ chạy trong thư mục `history` của máy đó. Vì scope
  `drive.file` nên không thể xóa nhầm file người dùng tự tạo. Lưu ý: `Files.Delete` xóa
  **vĩnh viễn**, không qua thùng rác. Nếu hạ retention từ 200 xuống 1 thì 199 bản bị xóa
  vĩnh viễn ngay ở lần backup kế tiếp. Ghi ở BAK-06.
- **Trùng tên:** tên history có độ chính xác tới giây, và `_runLock` chặn chạy song song.
  `EnsureFolderAsync` lấy kết quả đầu tiên nếu có nhiều thư mục trùng tên. Khi đổi OAuth
  client hoặc project, thư mục cũ trở nên vô hình với scope `drive.file`, nên app tạo thêm một
  `MediaManagerBackups` nữa, còn ID cũ đang cache thì trả 404 (BAK-05).

### 5. Scheduler

- **Chạy trùng:** được chặn bằng `_runLock.WaitAsync(0)`. Manual và scheduled không chồng
  lên nhau. **Restore không lấy lock này** (BAK-01).
- **Máy ngủ:** `PeriodicTimer` bắn bù một tick khi máy thức. Điều kiện daily so `LastBackupUtc`
  theo ngày nên vẫn bù được.
- **App đang tắt:** không có backup ngoài tiến trình. Lần poll đầu ngay khi khởi động (DB luôn
  bị coi là "dirty" vì chưa có mốc so sánh) sẽ chạy backup bù nếu bản gần nhất đã quá 1 giờ.
  Scheduler chỉ được start khi setup đã xong và không ở `SafeTestMode` (`App.xaml.cs:107-118`).
  Nhờ vậy, restore ở first-run không phải chạy đua với các job nền.
- **Lỗi chính:** việc `RunBackupAsync` tự ghi `settings.json` lại kích hoạt chính scheduler
  (BAK-03). Ngoài ra lỗi không có backoff (BAK-04).

---

## Findings

### BAK-01 — Restore khi có writer đang mở transaction làm hỏng DB; hỏng có thể lan lên Drive (Critical, đề xuất nâng AUD-003)

Code liên quan:

- `Services/Backup/BackupService.cs:144-166` (`RestoreAsync`), `:228-236` (`ClearAllPools` + `File.Copy` + `Initialize`)
- `ViewModels/SettingsViewModel.cs:1498-1529` (`RestoreSelectedBackup`, gọi `Initialize` thêm lần nữa)
- `App.xaml.cs:114-118` (Auto-Track, symlink và backup scheduler chạy suốt thời gian sống của tiến trình)
- `Services/Backup/BackupSchedulerService.cs:170-184, 197-206` (DB đổi thì sẽ được backup)

Bằng chứng:

- Restore chỉ gọi `SqliteConnection.ClearAllPools()`. Hàm này chỉ đóng connection *rảnh*
  trong pool, không đụng tới connection đang mở hay transaction đang chạy. Restore không
  dừng scheduler nào, không lấy `_runLock` của `BackupService`, không có gate cho DB, và
  không restart app.
- Harness S6 dùng `DatabaseService` và `BackupService.ApplyLocalBackup` thật trên fixture tạm:
  1. Một connection khác `BEGIN` rồi `UPDATE` 3000 dòng, nên `media-manager.db-journal` có mặt.
  2. `ApplyLocalBackup` **thành công sau 39 ms**: `File.Copy` đè được file, vì SQLite trên
     Windows mở file với share mode read/write, và `Initialize` cũng chạy xong.
  3. Writer `COMMIT` **thành công**: nó ghi các trang của DB cũ vào file mới.
  4. `PRAGMA integrity_check` báo `2nd reference to page 60`. Bảng `OnlyInBackup` có mặt,
     nhưng dữ liệu là `live:3000,writer:1`. DB vừa không còn là bản backup, vừa bị hỏng cấu trúc.
- Sau restore không có `integrity_check` nào. File DB có mtime mới nên scheduler đánh dấu
  dirty. Cộng thêm BAK-03, trong khoảng ≤ 24 phút DB hỏng sẽ được upload đè `latest.zip`
  và vào history. Sau khoảng 20 bản (≈ 7–8 giờ) thì mọi bản history trước restore đã bị prune
  vĩnh viễn (`Files.Delete` không qua thùng rác).
- Lộ trình khác cùng gốc: backup scheduled đang `BackupDatabase` (giữ khóa đọc) đúng lúc
  restore `File.Copy`. Restore và backup không loại trừ lẫn nhau, nên snapshot có thể đọc lẫn
  trang của hai file.

Ảnh hưởng và điều kiện:

- Cần một restore do người dùng xác nhận đúng lúc có thao tác DB đang chạy: Auto-Track
  hunt/reconcile, scan Library, symlink sync hoặc một backup đang chạy. Trên máy thật các job
  này chạy theo lịch và kéo dài, nên đây là điều kiện thực tế chứ không phải chạy đua mức micro giây.
- Kết quả là DB đang chạy (bản trước restore) bị mất do bị đè, và DB sau restore bị hỏng.
  Nếu không phát hiện trong vài giờ, các bản backup tốt trên Drive bị xoay vòng mất. Chuỗi
  này đạt ngưỡng *irreversible application-state corruption* của mức Critical: bước hỏng DB
  đã được tái hiện an toàn, bước lan lên Drive được chứng minh tĩnh.
- Ngay cả khi không có writer, các ViewModel/service sống suốt tiến trình vẫn giữ dữ liệu
  cũ sau restore (theo AUD-003).

Đề xuất sửa tối thiểu (theo critical-fix gate, **chưa áp dụng**, cần user duyệt):

1. Restore chỉ **stage**: tải hoặc giải nén vào `<StateFolder>/restore-pending/`, validate
   (xem BAK-02), ghi marker `restore-pending.json`, rồi yêu cầu restart app.
2. Khi khởi động, **trước** `database.Initialize(...)` và trước khi start bất kỳ service nào:
   nếu có marker thì đổi tên `media-manager.db` (và `-journal` nếu có) thành
   `media-manager.pre-restore-<utc>.db`, chuyển DB đã stage vào, xóa marker, rồi khởi động
   như bình thường. Nếu bước nào lỗi thì giữ nguyên DB cũ và báo lỗi.
3. Trong lúc chờ có bước 1–2: tối thiểu phải lấy `_runLock`, dừng scheduler, và từ chối
   restore khi `IAutoTrackService.IsRunning` hoặc symlink/scan đang chạy. Cách này giảm rủi
   ro nhưng không loại trừ được hoàn toàn, nên chỉ là giải pháp tạm.

Test hồi quy: chuyển harness S6 thành test (fixture tạm, writer giữ transaction) với kỳ vọng
DB đang chạy không đổi cho tới lần khởi động kế tiếp, và sau khi apply thì
`integrity_check == ok` và có dữ liệu của backup.

### BAK-02 — Restore đè DB đang chạy mà không validate, không giữ bản cũ, không rollback (High)

Code liên quan: `BackupService.cs:219-262`, `:144-166`; `GoogleDriveClient.cs:202-209`;
`MigrationRunner.cs:11-62`; `DatabaseService.cs:51-76`.

Bằng chứng:

- S3: zip chứa `media-manager.db` là 8 KB rác. `ApplyLocalBackup` ném
  `SQLite Error 26: file is not a database` **sau khi** `File.Copy` đã đè DB đang chạy. Sau
  đó DB đang chạy giống hệt file rác, và thư mục state không còn file nào khác.
  Với `DatabaseService` thật, `Initialize` sẽ hiện dialog "migration failed" rồi throw. Lần
  khởi động sau bị chặn (`App.xaml.cs:85-93`) cho tới khi người dùng tự khôi phục.
- S4: manifest có checksum `DEADBEEF`, `AppVersion` 99.0.0 và `MachineId` lạ đều được chấp
  nhận. Không có code nào đọc `manifest.json` khi restore.
- S4: DB đến từ bản app mới hơn (có migration `999_…` mà bản hiện tại không biết) vẫn được
  `MigrationRunner` bỏ qua mà không cảnh báo. Không có cơ chế phát hiện downgrade, nên app cũ
  chạy trên schema mới.
- Không có bản sao trước restore. Không rollback: thứ tự là DB → `Initialize` → Recipes → review file.
- Kết quả của `DownloadAsync` bị bỏ qua (xem mục 4). Zip tải dở hiện bị chặn ở bước giải nén,
  nhưng chỉ nhờ cấu trúc của định dạng zip chứ không nhờ code kiểm tra.

Ảnh hưởng: một bản backup hỏng (lỗi bit trên đường truyền, lỗi ổ đĩa, bản backup đã hỏng sẵn
vì BAK-01) hoặc lệch version sẽ phá hủy DB đang chạy, và không có đường quay lại ngoài các bản
history khác trên Drive.

Đề xuất: trước khi đụng vào DB đang chạy thì kiểm `IDownloadProgress.Status == Completed`;
đọc manifest và so SHA-256 từng entry; mở DB đã stage, chạy `PRAGMA integrity_check`, đọc
`SchemaMigrations` và từ chối nếu có migration lạ (bản mới hơn); giữ
`media-manager.pre-restore-<utc>.db`; gộp vào luồng stage + restart của BAK-01.

### BAK-03 — Scheduler tự kích hoạt lại chính nó (High)

Code liên quan: `BackupSchedulerService.cs:121-168, 219-260`; `BackupService.cs:105-108`
(`Save()` khi thành công), `:329-358` (`Save()` khi tạo thư mục), `:413-421` (`Save()` khi thất bại).

Bằng chứng:

- Scheduler phát hiện "settings thay đổi" bằng `LastWriteTimeUtc` của `settings.json` và `*.rcp`.
  `RunBackupAsync` luôn gọi `_settingsService.Save()` để ghi `LastBackupUtc` và các trường liên
  quan. Hàm này ghi lại `settings.json` vô điều kiện (`SettingsService.cs:113-131`).
- Sau một backup thành công, `RunAndResetAsync` đặt `_settingsGroupChangeDetectedAtUtc = null`
  nhưng **không** cập nhật `_settingsGroupLastWriteSeenUtc`. Tick 3 phút tiếp theo thấy mtime
  mới hơn, coi đó là thay đổi, và debounce 20 phút bắt đầu lại.
- S7: sau setup, harness không đổi gì. 5 chu kỳ liên tiếp đều thấy debounce bị kích hoạt lại
  và chạy thêm 5 backup EventDriven.

Ảnh hưởng (mặc định debounce 20 phút, poll 3 phút, retention 20):

- Khoảng 60–70 backup/ngày, mỗi lần gồm một snapshot DB đầy đủ, một file zip và hai lần upload.
- **Cửa sổ history thực tế chỉ khoảng 7–8 giờ** thay vì "20 bản" như người dùng nghĩ. Nếu lỗi
  dữ liệu được phát hiện vào hôm sau thì không còn bản tốt nào. Lỗi này khuếch đại BAK-01 và BAK-02.
- `DbThrottleHours` và giờ backup daily gần như mất tác dụng vì `LastBackupUtc` lúc nào cũng mới.

Đề xuất: sau khi backup xong (thành công hay thất bại), đặt `_settingsGroupLastWriteSeenUtc`
bằng mtime hiện tại. Tốt hơn nữa là so hash nội dung đã redact (bỏ qua `Backup.Last*`) thay
cho mtime. Thêm test cho scheduler: backup chạy xong không được kích hoạt lại debounce.

### BAK-04 — Backup lỗi được thử lại ở mọi lần poll, không backoff (Medium)

Code liên quan: `BackupSchedulerService.cs:141-153, 197-217, 219-234`; `BackupService.cs:51-54, 413-421`.

Bằng chứng: khi lỗi, `_dbDirty` không được reset và `LastBackupUtc` không đổi, nên điều kiện
DB-throttle (hoặc daily) vẫn đúng ở tick sau. S8 (Drive chưa kết nối): 5 poll cho 5 lần thử,
5 warning `LogTarget.All` và 5 lần ghi `settings.json`, tương đương khoảng 480 lần/ngày. Với lỗi
quota, token bị thu hồi hoặc 404 (BAK-05), mỗi lần thử còn snapshot và nén toàn bộ DB rồi upload.
Mỗi lần `Fail()` gọi `Save()` cũng góp phần kích hoạt BAK-03.

Đề xuất: backoff theo cấp số nhân, ví dụ 3 phút → 6 giờ, reset khi thành công. Riêng lỗi "chưa
kết nối" hoặc không có credential thì chỉ ghi log một lần cho tới khi trạng thái thay đổi. Kiểm
tra kết nối trước bước snapshot (hiện đã đúng thứ tự) và kiểm quota trước khi nén nếu được.

### BAK-05 — Drive ID cache không bao giờ được kiểm tra lại (Medium)

Code liên quan: `BackupService.cs:82-98, 329-361`; `GoogleDriveClient.cs:153-165`.

Bằng chứng:

- `DriveRootFolderId`, `DriveMachineFolderId`, `DriveHistoryFolderId` và `LatestFileId` chỉ được
  tạo khi đang rỗng, không bao giờ được kiểm lại hay xóa đi.
- S9: `latest.zip` bị xóa trên Drive thì `Files.Update` trả 404. Cả 3 lần chạy đều thất bại,
  `LatestFileId` không đổi, và **không có bản history nào được tạo** vì `latest.zip` được upload trước.
- Theo hành vi của Drive (phân tích tĩnh): nếu người dùng xóa thư mục, hoặc đổi OAuth client hay
  project (khi đó thư mục cũ vô hình với scope `drive.file`), thì `Create` với parent cũ trả 404
  và backup thất bại vĩnh viễn. Nếu thư mục chỉ bị đưa vào **thùng rác**, ID vẫn dùng được và
  upload vẫn thành công, nhưng file nằm trong thùng rác và bị Drive tự xóa sau 30 ngày. Backup
  báo thành công trong khi thực tế không có gì được giữ lại.

Ảnh hưởng: mất backup một cách im lặng hoặc vĩnh viễn, và người dùng chỉ có thể sửa bằng cách
chỉnh tay `settings.json`. Kết hợp với BAK-04 thì lỗi còn sinh ra spam log.

Đề xuất: khi gặp 404 (hoặc `trashed == true` khi `files.get`), xóa ID tương ứng rồi tạo lại một
lần trong cùng lần chạy. Upload history trước rồi mới cập nhật `latest.zip`, hoặc cho hai bước
độc lập để lỗi `latest.zip` không chặn history.

### BAK-06 — List history không phân trang (Medium)

Code liên quan: `GoogleDriveClient.cs:180-194`; `BackupService.cs:127-142, 393-411`;
`AppConstants.MaxHistoryRetentionCount = 200`.

Bằng chứng (tĩnh): `Files.List` không đặt `PageSize` và không đọc `NextPageToken`. Drive v3 mặc
định 100 kết quả mỗi trang và có thể trả trang thiếu ngay cả khi chưa hết danh sách. Kết quả
được sắp `createdTime` tăng dần, nên trang đầu là **100 bản cũ nhất**.

- Với retention R trong khoảng 101–200 và số file N > R: `Math.Max(0, 100 - R) = 0`, nên
  **không bao giờ prune**. History tăng mãi cho tới khi hết quota (dẫn tới BAK-04).
- Khi N > 100, `ListHistoryAsync` trả 100 bản cũ nhất rồi sắp giảm dần. **UI restore không
  hiện các bản mới nhất**, và mục được chọn sẵn (`FirstOrDefault`) không phải bản mới nhất.
  Tình huống này xảy ra dễ dàng khi cộng thêm BAK-03.
- Prune dùng `Files.Delete`, tức là xóa vĩnh viễn. Hạ retention sẽ xóa ngay, không có xác nhận.

Đề xuất: đặt `PageSize = 1000`, lặp theo `NextPageToken`, `OrderBy = "createdTime desc"` cho UI.
Prune bằng cách đặt `trashed = true` thay vì xóa vĩnh viễn, hoặc chỉ prune những file có tên
khớp mẫu `yyyy-MM-dd_HHmmss.zip`.

### BAK-07 — Connect không làm mới credential đã cache, không có Disconnect (Medium)

Code liên quan: `GoogleDriveClient.cs:39, 97-105, 211-298`; `SettingsViewModel.cs:1376-1380, 1422-1451`.

Bằng chứng (tĩnh):

- `AuthorizeAsync` trả ngay `_driveService` nếu đã có (`:228-231`). Sau lần kết nối đầu trong
  tiến trình, `ConnectAsync` không chạy OAuth lại. Token bị thu hồi, đổi tài khoản, đổi
  `credentials.json` hay đổi state folder đều không có tác dụng cho tới khi restart, trong khi
  UI vẫn báo "Connected to Google Drive."
- `HasStoredCredential` chỉ kiểm thư mục token có file hay không. Trạng thái "Connected" trong
  Settings và pill Drive không phản ánh token có còn dùng được không. XML doc của
  `TryEnsureConnectedSilentlyAsync` ghi "obtain a valid… credential (refreshing if needed)",
  nhưng hàm không refresh cũng không gọi API nào.
- Không có lệnh Disconnect hay Revoke nào trong code.

Ảnh hưởng: người dùng làm đúng hướng dẫn (bấm Connect lại) mà backup vẫn lỗi lặp lại (BAK-04),
và UI báo sai trạng thái.

Đề xuất: `ConnectAsync` phải bỏ `_driveService` đi và gọi `ReauthorizeAsync`/xóa token khi người
dùng chủ động kết nối lại. Thêm lệnh Disconnect (xóa thư mục token và gọi revoke). Trạng thái chỉ
hiện "Connected" sau một call nhẹ thành công (`about.get`).

### BAK-08 — Redactor theo kiểu denylist, còn lọt (Low)

Code liên quan: `BackupSettingsRedactor.cs:15-27`.

Bằng chứng (S1): credential trong `AutoTorrent.QbittorrentWebUiUrl` (`http://user:pass@…`),
`api_key=` trong `AutoTrack.Jellyfin.BaseUrl`, `Backup.CredentialsFilePath` (đường dẫn cục bộ, có
thể lộ tên user Windows; `STATE_FOLDER.md` lại ghi rằng trường này đã được redact) và
`Backup.LastBackupError` đều được upload. Không có test nào bắt buộc mọi trường secret mới phải
được redact. Hiện chưa thấy app tạo ra URL có credential, nên đây chỉ là đường lọt tiềm năng: mức Low.

Đề xuất: bỏ phần userinfo và các query `api_key`/`token` khỏi mọi URL; xóa
`Backup.CredentialsFilePath`; thêm test duyệt mọi property string có tên chứa
`Key|Token|Password|Secret|Username` và kiểm rằng chúng rỗng trong JSON đã redact.

### BAK-09 — Restore Recipes gộp chứ không thay thế (Low)

Code liên quan: `BackupService.cs:238-246`.

Bằng chứng (S5): recipe tạo sau thời điểm backup vẫn còn sau restore. File không phải `.rcp`
trong zip (`notes.txt`) cũng được chép, vì `EnumerateFiles(recipesSource)` không lọc. Không
`RecipeService` hay ViewModel nào nạp lại recipe, nên một lần lưu recipe sau đó có thể đè lên
bản vừa restore.

Đề xuất: lọc `*.rcp`; chuyển recipes hiện có vào `Recipes.pre-restore-<utc>/` rồi mới chép; gộp
vào luồng restart của BAK-01 để cache được nạp lại.

### BAK-10 — Backup bị ngắt khi tắt app (Low)

Code liên quan: `BackupSchedulerService.cs:14, 51-91, 219-221`; `BackupService.cs:114-124`; `DatabaseService.cs:20-49`.

Bằng chứng (tĩnh):

- `Dispose` hủy token và chờ 8 giây. `OperationCanceledException` bị `catch (Exception)` bắt rồi
  chuyển sang `Fail()`, nên `LastBackupSucceeded = false` với nội dung "A task was canceled" và
  `Save()` vẫn chạy giữa lúc shutdown.
- `CreateSafeSnapshot` đồng bộ và không nhận token hủy. Nếu chạy quá 8 giây thì app thoát giữa chừng.
- Nếu crash, `%TEMP%\MediaManagerBackup\<guid>\media-manager.db` (bản đầy đủ của DB, không mã
  hóa) nằm lại cho tới khi Windows dọn %TEMP%.

Đề xuất: bắt `OperationCanceledException` riêng và không ghi nó là failure; dọn
`%TEMP%\MediaManagerBackup\*` cũ khi khởi động.

### BAK-11 — Refresh token Google lưu plaintext (Low)

Code liên quan: `GoogleDriveClient.cs:34-37, 260-265`; `docs/STATE_FOLDER.md` (mục Token folder).

Bằng chứng: `FileDataStore` ghi JSON có `refresh_token` dưới dạng plaintext vào
`<StateFolder>/GoogleDrive/token/`, không dùng DPAPI. State folder mặc định cũ nằm ở
`<StateFolder>`. Thư mục tạo ở gốc ổ dữ liệu thường kế thừa ACL cho phép
"Authenticated Users" sửa (chưa đo trên máy thật, nên đây là ghi chú). Mức rủi ro ngang với các
secret plaintext trong `settings.json` (thuộc phạm vi audit Settings), nhưng refresh token cho
phép truy cập Drive kéo dài. Ảnh hưởng được giới hạn nhờ scope `drive.file`. Token không bị đưa
vào backup.

Đề xuất: dùng `IDataStore` tự viết, bọc dữ liệu bằng `ProtectedData` (DPAPI, CurrentUser).
Phối hợp với audit Settings để xử lý chung các secret.

### BAK-12 — Doc drift (Low)

- `docs/STATE_FOLDER.md`: ghi "Safe snapshot … (WAL checkpoint + copy)", thực tế là online
  backup API trên rollback journal; ghi `Backup.CredentialsFilePath` được redact, thực tế không
  (BAK-08); danh sách trường được redact thiếu `AutoTorrent.Username`; câu "Restore … does not
  overwrite live settings.json" đúng, nhưng tài liệu không nói rằng **DB bị đè ngay lập tức**
  khi tiến trình vẫn đang chạy.
- `IGoogleDriveClient.TryEnsureConnectedSilentlyAsync`: doc ghi "valid credential (refreshing if
  needed)", nhưng hàm không validate và không refresh (BAK-07).
- Dialog migration failure (`DatabaseService.cs:2665`) khuyên "restore a local snapshot created
  with CreateSafeSnapshot()", nhưng không có UI hay CLI nào tạo snapshot cục bộ.
- `STATE_FOLDER.md` liệt kê `media-manager*.db` là "Manual/automatic DB backups", nhưng không có
  code nào tạo ra các file này.

### BAK-13 — `.gitignore` `Backup*/` vẫn ẩn các thư mục backup khác (Low)

Bằng chứng: `git check-ignore -v` trả `.gitignore:291:Backup*/` cho
`MediaManager.App.Tests/Backup/x.cs` và `Views/Backup/x.xaml`. Lỗi giống hệt lỗi đã làm
`Services/Backup` thiếu suốt 2 tháng (commit `290da3b`) sẽ lặp lại với test hoặc view đầu tiên
nằm trong một thư mục tên `Backup`.

Đề xuất: thu hẹp quy tắc thành `/Backup*/` (chỉ thư mục ở gốc repo, đúng ý nghĩa "VS upgrade
backup"), hoặc thêm `!**/*Tests/**/Backup/`.

---

## Ghi chú review (không đủ bằng chứng để thành finding)

- **MachineId bị nhân bản:** `MachineId` sinh một lần rồi lưu trong `settings.json`
  (`SettingsService.cs:381-383`). Nếu người dùng trỏ máy thứ hai vào một bản sao của state
  folder (không qua luồng restore), hai cài đặt sẽ dùng chung thư mục Drive và
  `LatestFileId`, rồi đè `latest.zip` và prune history của nhau. Có thể so
  `Environment.MachineName` với lần chạy trước để cảnh báo.
- **Restore từ "copied state folder":** DB được `File.Copy` thô từ thư mục nguồn, không kèm
  `-journal`. Nếu bản sao được tạo lúc app nguồn đang ghi thì DB có thể không nhất quán.
  `integrity_check` theo BAK-02 sẽ bao luôn trường hợp này. Với nguồn là thư mục,
  `restored-settings.review.json` chứa secret plaintext và nằm lại vô thời hạn.
- **`BackupNow` gọi `ApplyBackupSettings()` mà không lưu:** `RunBackupAsync` sau đó `Save()`, nên
  các thay đổi chưa lưu ở mục Backup bị lưu ngầm. Hành vi này vô hại nhưng gây bất ngờ.
- **Đột biến `Current.Backup` trên thread nền:** `BackupService` chạy trên thread pool, đọc và
  ghi `Current.Backup` cùng lúc UI có thể `Save()`. Không có lock chung, nên chuyển cho audit
  Settings/Database.
- **Đường dẫn tuyệt đối trong DB sau restore chéo máy:** chuyển cho audit Library/Deletion.
- `RunStateChanged` được raise từ thread nền. `MainViewModel.OnDeviceStatusChanged` đã chuyển
  về Dispatcher, nên ổn.

## Đã xác nhận là ổn

- Snapshot dùng online backup API (`BackupDatabase`), không copy file thô.
- Chặn zip slip (S2), làm phẳng tên recipe khi restore.
- Scope `drive.file`; token và credentials không nằm trong zip.
- Chặn chạy chồng giữa backup manual và scheduled (`_runLock`).
- Redactor xóa đủ các trường secret có tên riêng (S1); object settings gốc không bị sửa.
- Upload kiểm `progress.Exception`; resumable upload không để lại file dở dang.
- Restore không apply `settings.json`; first-run chỉ chép secret khi người dùng đồng ý.
- First-run restore chạy trước khi các job nền được start.

## Test còn thiếu

Repo hiện không có test nào cho `Services/Backup`: `grep -il backup` trên `*.cs` của
`MediaManager.App.Tests` và `MediaManager.Core.Tests` không khớp file nào. Các test cần có:

1. `ApplyLocalBackup` khi có writer đang giữ transaction: không được làm hỏng DB (BAK-01, chuyển từ S6).
2. Restore một zip có DB rác hoặc checksum sai hoặc migration lạ: DB đang chạy phải giữ nguyên, có bản pre-restore (BAK-02).
3. Scheduler: backup thành công không được kích hoạt lại debounce (BAK-03); lỗi lặp lại phải có backoff (BAK-04).
4. `RunBackupAsync` khi `LatestFileId` hoặc folder ID trả 404 phải tự tạo lại; history không bị chặn (BAK-05).
5. Prune và list với hơn 100 file (fake client có phân trang) (BAK-06).
6. Redactor: duyệt mọi property kiểu secret, URL có userinfo (BAK-08).
7. Zip slip: giữ S2 làm test hồi quy (hiện đã ổn).
8. Restore recipes: lọc `.rcp`, không gộp (BAK-09).
9. Hủy giữa lúc backup (shutdown): không ghi failure (BAK-10).
10. Manifest: round-trip và kiểm checksum từng entry.
11. `ConnectAsync` sau khi đã có `_driveService`: phải chạy authorize lại (BAK-07).

Lưu ý: đặt test trong thư mục tên `Backup` sẽ bị ignore (BAK-13). Hãy sửa `.gitignore`
trước, hoặc đặt test ở `Services/BackupTests/`.

## Thứ tự sửa đề xuất

1. **BAK-01 cùng BAK-02** (một thay đổi): stage → validate → restart → swap trước khi khởi tạo
   DB, giữ bản pre-restore. Đóng luôn AUD-003. Thuộc critical-fix gate: **chờ user duyệt đề xuất
   ở BAK-01 trước khi sửa code.**
2. **BAK-03**: sửa ngắn (cập nhật mốc mtime sau khi backup chạy xong) nhưng khôi phục được cửa sổ
   history thật. Nên làm ngay sau hoặc cùng bước 1, vì nó quyết định backup tốt sống được bao lâu.
3. **BAK-04** (backoff), rồi **BAK-05** (tự tạo lại ID, tách `latest.zip` khỏi history).
4. **BAK-06** (phân trang, prune vào thùng rác) và **BAK-07** (reconnect/disconnect).
5. **BAK-13** (`.gitignore`), rồi viết bộ test ở mục trên.
6. Các mục Low còn lại: BAK-08, BAK-09, BAK-10, BAK-11, BAK-12.
