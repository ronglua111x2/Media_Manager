# Audit các luồng xóa và dọn dẹp (deletion audit)

Ngày: 12/11/2026. Nhánh `claude/deletion-audit-82f156`, gốc `ff2caa3`. Chỉ đọc code: không sửa app, không đụng `<StateFolder>` (chỉ copy DB ra scratchpad và mở `mode=ro`, đọc log), không gọi qBittorrent hay Jellyfin.

Thang mức độ và chuẩn bằng chứng theo [`../00-method-and-scope.md`](../00-method-and-scope.md). Mọi đường dẫn code tính từ `media management app/media management app/`.

Không audit lại: search của hunt (nhánh `feat/native-search`), parse/map tập và bước link (phụ lục `parser-audit-appendix-link.md` trên nhánh `claude/parser-audit-5f9a30`). Các finding LINK-xx của phụ lục đó được tham chiếu chéo khi trùng chỗ; báo cáo này **không** lặp lại chúng.

## Tóm tắt

| ID | Mức | Tin cậy | Tiêu đề |
|---|---|---|---|
| DEL-01 | High | Cao (harness H1) | "Reset Episode/Movie" xóa file trong Library, kể cả khi đó là **bản duy nhất**, trong khi hộp thoại nói "Files still on disk are not deleted" |
| DEL-02 | High | Cao (harness H7, settings thật) | Add gate kiểm lại một torrent **đã có sẵn** trong qBittorrent rồi xóa nó **kèm file** khi validate từ chối; Auto-Track làm việc này không hỏi |
| DEL-03 | Medium | Cao (harness H3) | `RemoveHardLink` coi "không thấy file" là "đã xóa": ổ Library không truy cập được thì Delete Media báo thành công, file Library thành mồ côi |
| DEL-04 | Medium | Cao (harness H4) | AUD-005 vẫn đúng, và tác động rộng hơn ghi nhận cũ: show đã xóa còn **mãi** trong Jellyfin |
| DEL-05 | Medium | Cao (harness H5) | "Xóa symlink" không kiểm tra đó có phải symlink không: file thật của user nằm đúng đường symlink bị xóa; `UnifiedRoot` không được kiểm tra chồng lấn |
| DEL-06 | Medium | Cao (harness H2) | Reset tập/phim và Cleanup pack không bền: lần reconcile kế tiếp adopt lại đúng torrent cũ và link lại |
| DEL-07 | Medium | Cao (harness H10) | Delete Media để lại `SourceItems`; thêm lại show thì tập hiện **Available** dù Library trống, Auto-Track không hunt |
| DEL-08 | Low | Cao (harness H6, tĩnh) | Guard xóa chỉ so chuỗi đường dẫn với root lấy từ settings **hiện tại**: không kiểm danh tính file, không resolve junction; tên thư mục Library nhận `.`/`..`/đường tuyệt đối và được ghi vào settings sống theo từng phím gõ |
| DEL-09 | Low | Cao (harness H8) | Dọn subtitle bỏ sót tên nhiều dấu chấm (`.eng.forced.srt`), xóa subtitle trước khi biết symlink có xóa được không |
| DEL-10 | Low | Cao (tĩnh) | Dọn NFO cho show dùng episode group xóa **mọi** `.nfo` lạ trong thư mục show (vd. `season.nfo` do Jellyfin ghi) |
| DEL-11 | Low | Cao (tĩnh) | Unlink pack và nút Link/Unlink phim xóa hardlink không hỏi xác nhận; Delete Entire Library không nguyên tử, không bắt lỗi |

**Không có Critical đã chứng minh.** DEL-01 và DEL-02 gần ngưỡng (mất dữ liệu không phục hồi được, qua thao tác bình thường), lý do chưa xếp Critical nằm trong từng mục. Không tìm thấy đường nào xóa **file gốc trong thư mục download** qua luồng gỡ link: guard `LinkedPath != FilePath` giữ được, và torrent chỉ bị xóa kèm file ở add gate (DEL-02).

Kiểm chứng:

- Test hiện có (Release/x64, 12/11/2026): `MediaManager.App.Tests` **138 passed**, `MediaManager.Core.Tests` **248 passed**, 0 failed.
- Harness: `deletion-audit-harness/Program.cs` trong thư mục ghi chú chung của nhóm phiên (ngoài repo), output ở `out.txt` cùng chỗ. Dùng DLL Release thật của app (`DatabaseService` với SQLite tạm, `HardlinkService` tạo hardlink thật, `AutoTorrentLinkService`, `LibraryManagementService`, `SymlinkSyncService`, `SymlinkService` thật cho H5, `TorrentReconciliationService`, `TorrentAddGateService` + `TorrentCleanupService` + `QbittorrentTorrentContentValidationService` thật, `SymlinkSubtitleCleanupService`). Chỉ giả lập qBittorrent (ghi lại lệnh `torrents/delete`, xóa file như qBit khi `deleteFiles=true`), logger, settings, cart, poster. Junction tạo bằng `mklink /J`. Tất cả chạy trong thư mục tạm của harness.
- Dữ liệu thật, chỉ đọc: bản copy `media-manager.db`, `settings.json`, 99 file log trong `<StateFolder>\logs` (từ 29/10/2026, vì retention 14 ngày).

---

## 1. Bảng điểm gọi API xóa

Ký hiệu cột guard: **Root** = có kiểm tra đường dẫn nằm trong root được quản lý; **Rỗng** = xử lý path rỗng/null; **Ổ** = hành vi khi root là gốc ổ đĩa; **Reparse** = xử lý junction/symlink; **Tương đối** = path tương đối; **Unicode/dài**.

### 1.1. Xóa file và thư mục trên đĩa media

| # | Điểm gọi | Xóa gì | Đường dẫn lấy từ | Guard | Ghi chú |
|---|---|---|---|---|---|
| F1 | `Services/HardlinkService.cs:172` `File.Delete(item.LinkedPath)` trong `RemoveHardLink` (127-184) | hardlink trong Library | cột `SourceItems.LinkedPath` (do `CreateHardLink` ghi, hoặc `= FilePath` với dòng import) | **Root**: có, `ResolveCleanupRoot` (214-228): trong root sinh ra cho ổ của `FilePath` (`LibraryPathResolver.cs:68-78`, theo settings **hiện tại**) hoặc trong `OutputLibraryFolder` cũ (máy thật: `<StateFolder>\Library`). Chặn `LinkedPath == FilePath` (139). **Rỗng**: trả lỗi (132-137). **Ổ**: nếu root sinh ra là gốc ổ (DEL-08) thì guard cho mọi file trên ổ. **Reparse**: không resolve, so chuỗi (H6). **Tương đối**: `GetFullPath` theo CWD tiến trình, DB không chứa path tương đối nên chưa gặp. **Unicode/dài**: H9 OK với đường 515 ký tự. | Không kiểm link count/danh tính file (DEL-01, DEL-08). `!File.Exists` → coi như đã xóa, trả `true` (DEL-03). |
| F2 | `HardlinkService.cs:201` `Directory.Delete(current)` trong `CleanupEmptyLibraryFolders` (186-212) | thư mục rỗng Library, đi ngược lên tới root | thư mục cha của `LinkedPath` | Không đệ quy; chỉ khi rỗng; dừng ở root; `IsPathInsideRoot`. Junction rỗng: xóa junction, không đụng đích (H6). | An toàn. Nếu root là gốc ổ thì có thể xóa cả `MediaManagerLibrary\Shows` khi rỗng; vô hại. |
| F3 | `Services/Symlink/SymlinkService.cs:88` `File.Delete` trong `TryRemoveSymlink` (76-98) | "symlink" Jellyfin | `SourceItems.SymlinkPath`, hoặc đường dựng lại từ `UnifiedRoot` + tên show hiện tại | **Không** kiểm tra reparse point, **không** kiểm tra nằm trong `UnifiedRoot`. `!File.Exists` → `true`. | Gọi từ: `SymlinkSyncService.SyncItem:87` (thay file đang chiếm chỗ), `RemoveItem:151`, `CleanupDuplicateSymlinks:399`, `SymlinkService.TryCreateFileSymlink:56`. Xem DEL-05. |
| F4 | `Services/Symlink/SymlinkSyncService.cs:529` `Directory.Delete` trong `CleanupEmptyFolders` (514-540) | thư mục rỗng Jellyfin | thư mục của symlink | Không đệ quy, chỉ khi rỗng, dừng ở `UnifiedRoot`. | An toàn. |
| F5 | `Services/Symlink/SymlinkSubtitleCleanupService.cs:96` | subtitle cạnh symlink: `{base}.{ext}` và `{base}.{1 token}.{ext}`, ext ∈ `.srt .ass .ssa .sub .vtt .sbv .json` | thư mục và tên gốc của `SymlinkPath` | Chỉ trong thư mục chứa symlink; không kiểm `UnifiedRoot`. Chỉ chạy khi `symlinkPath` còn tồn tại. | Gọi trước khi xóa symlink (`SymlinkCoordinatorService.cs:529-533, 607-611`). DEL-09. |
| F6 | `Services/NfoWriterService.cs:130` `DeleteEpisodeNfo` (115) | `{symlink}.nfo` | `SymlinkPath` | Không kiểm root. | Chỉ một file đúng tên, chấp nhận được. |
| F7 | `NfoWriterService.cs:153` `DeleteTvShowNfo` (138) | `tvshow.nfo` | thư mục cha của thư mục season tính từ symlink (với phim: chính `…\Movies`) | Chỉ khi không còn file khác dưới thư mục. | Với phim, "show folder" là `UnifiedRoot\Movies`, vô hại. |
| F8 | `NfoWriterService.cs:218` `CleanupOrphanEpisodeNfos` (180) | **mọi** `*.nfo` đệ quy trong thư mục show, trừ `tvshow.nfo` và NFO đang dùng | thư mục show tìm theo `[tmdbid-N]` dưới `UnifiedRoot\Shows` | Chỉ show dùng episode group (`SymlinkCoordinatorService.cs:681-765`). | DEL-10. |
| F9 | `Services/PosterImageService.cs:166-220` | poster/still cache | `StateFolder\posters\…` dựng từ TMDB id | Đường tự dựng, không lấy từ dữ liệu ngoài. | `DeleteAllCached` (237) `Directory.Delete(root, recursive: true)` trên `StateFolder\posters`. .NET không đi qua junction khi xóa đệ quy. Ném lỗi lên `DeleteEntireLibrary` (DEL-11). |

### 1.2. Xóa file của chính app

| # | Điểm gọi | Ghi chú |
|---|---|---|
| A1 | `Services/LogCleanupService.cs:147` | Chỉ `StateFolder\logs\*.txt` khớp regex tên session (`\d{8}_\d{6}_\d{3}_…`) hoặc `cart-debug_…`; bảo vệ file log đang ghi và settings log; theo `CreationTime` > retention (máy thật: **14 ngày**). Log thật: 38 lần "Log cleanup deleted". An toàn; hệ quả phụ là mất dấu vết điều tra các lần xóa cũ (mục 5). |
| A2 | `Services/PublishJunkCleanup.cs:42`, gọi ở `App.xaml.cs:96` với `AppContext.BaseDirectory` | Mỗi lần khởi động xóa `*.pdb .xml .ilk .exp .lib .iobj .ipdb .dbg` và `createdump.exe` ngay trong thư mục exe (không đệ quy), bỏ qua khi có debugger. Log thật: 9 lần. Rủi ro chỉ khi user chạy exe từ thư mục dùng chung (vd. Downloads) có `.xml` của thứ khác. Ghi chú, không phải finding. |
| A3 | `Services/Backup/BackupService.cs:441, 456, 404` | zip tạm, thư mục tạm `%TEMP%\MediaManagerBackup\<guid>` và `MediaManagerRestore\<guid>`, và prune bản backup cũ trên Google Drive theo `HistoryRetentionCount`. Đường tự sinh. |
| A4 | `Services/DatabaseService.cs:30` | Xóa file đích trước khi snapshot DB (đích do backup tạo). |
| A5 | `Services/RecipeService.cs:100` | `Recipes\{recipeId}.rcp`; `recipeId` đọc từ nội dung file recipe, không lọc `..`, nhưng phần mở rộng `.rcp` cố định, nên tối đa xóa một file `.rcp` khác. Ghi chú. |
| A6 | `CrashLogService.cs:392`, `WarpCliService.cs:638`, `WindowsStartupService.cs:218`, `ViewModels/FirstRunViewModel.cs:1694` | marker/alive file, XML tạm cho schtasks, file thử quyền ghi. Đường tự sinh. |

### 1.3. Xóa torrent trong qBittorrent

| # | Điểm gọi | Ghi chú |
|---|---|---|
| Q1 | `Services/QbittorrentClient.cs:1209-1232` `DeleteTorrentsAsync` (`api/v2/torrents/delete`) | Non-2xx thì ném lỗi (fix AUD-002). Hash rỗng bị lọc; danh sách rỗng thì không gửi gì. |
| Q2 | `Services/ITorrentCleanupService.cs:51-119` `DeleteTorrentAsync` | Thử 3 lần, timeout 20 s, kiểm lại bằng `GetTorrentsAsync`; không chắc đã xóa thì pause rồi ném `TorrentCleanupFailedException`. `IsTorrentDeletedAsync` trả `false` khi lỗi mạng (an toàn: tính là chưa xóa). |
| Q3 | `Services/ITorrentAddGateService.cs:233-282` `DeleteRejectedTorrentAsync`, luôn `deleteFiles: true` | **Caller duy nhất** của Q1/Q2. Ba nhánh: infohash bị blacklist (148-159), danh sách file rỗng sau timeout (174-188), validate trả `Delete` (203-224). Áp cả cho torrent có sẵn (`existingTorrent`, 107-118): DEL-02. |

Không luồng nào khác xóa torrent: Cleanup pack bảo user tự xóa file (`ViewModels/LibraryViewModel.cs:893-895`), Reset và Delete Media không đụng qBittorrent.

### 1.4. Câu `DELETE` trong DB

`Services/DatabaseService.cs`. Mọi lệnh mở **kết nối riêng, không transaction** (cả file chỉ có 4 chỗ `BeginTransaction`, không chỗ nào thuộc luồng xóa).

| Dòng | Câu | Caller xóa chính | Ghi chú |
|---|---|---|---|
| 325 | `DELETE FROM SourceItems WHERE State=$State` | không có caller ngoài interface | dead |
| 339 | `DELETE FROM SourceItems WHERE Id=$Id` | `RemoveLinks` (import/orphan), `RemoveOrphanItem`, `DeleteAcceptedTvSourceItems` (Reset tập), `ResetMovieForRedownload`, `ResetSeasonPackForRedownloadAsync`, `TorrentReconciliationService.PurgeOrphanedSourceItemsForScope` (LINK-04), `SourceReconciliationService` (dead, chỉ đăng ký DI ở `App.xaml.cs:317`) | |
| 833-834 | `DELETE FROM TrackedEpisodes/TrackedSeasons WHERE ShowId` | `SwitchEpisodeOrganizationAsync` (LINK-03) | |
| 845, 855 | `DELETE FROM TrackedShows` (một / tất cả) | `LibraryManagementService` | Season/Episode xóa theo `ON DELETE CASCADE` (`DatabaseService.cs:2509, 2559`); DB thật không có dòng mồ côi, nên FK đang bật. `SourceItems`, `TorrentBlacklist`, `SeriesMappings` **không** cascade (DEL-07). |
| 864, 874 | `DELETE FROM TrackedMovies` | `LibraryManagementService` | |
| 884, 897 | `DELETE FROM FetchJobs` | `LibraryManagementService` | |
| 2116-2211 | `TorrentCartOrders` / `…Candidates` | `ClearCart`, Reset, Cleanup pack, cart UI | Xóa order đang chờ validate thì mất `PendingValidationHash` (ghi chú 5). |
| 2405 | `DELETE FROM FetchJobs WHERE Id` | fetch job | |

---

## 2. Thứ tự thao tác và tính nguyên tử

| Luồng | Thứ tự | Lỗi giữa chừng để lại gì |
|---|---|---|
| **Delete Media** (`LibraryManagementService.DeleteSelectedMedia`, 74-112; UI `LibraryViewModel.cs:1635`) | (1) gỡ hardlink từng dòng (`RemoveLinks`): xóa file **trước**, rồi cập nhật dòng (`LinkedPath=null`, `Parsed`) hoặc xóa dòng (import/orphan); (2) xóa FetchJobs; (3) xóa TrackedShow/Movie (cascade); (4) xóa poster; (5) xóa cart. Symlink Jellyfin gỡ **bất đồng bộ** qua event (`SymlinkCoordinatorService.cs:154-170` → `Task.Run`). | Bước (1) lỗi **không** dừng bước (3): AUD-005, DEL-04. Bước (1) "thành công giả" khi không thấy file: DEL-03. Dòng `SourceItems` ở lại: DEL-07. Tắt app ngay sau khi xóa: event symlink bị hủy (`_shutdown`), dòng đã `Parsed` nên `ReconcileAll` (chỉ xét `Linked`, `SymlinkSyncService.cs:183-185`) không bao giờ dọn symlink đó (cùng gốc LINK-06). |
| **Delete Entire Library** (39-62) | lặp từng show/phim như trên, không bulk scope, rồi xóa hết bảng, rồi `DeleteAllCached`. | Như trên, nhân lên. `DeleteAllCached` ném lỗi thì exception lên thẳng command không có try/catch (`LibraryViewModel.cs:1661-1680`), sau khi DB đã xóa: DEL-11. |
| **Reset Episode** (`AutoTorrentLinkService.cs:317-366`) | gỡ link (xóa file) → xóa **mọi** `SourceItem` theo key S/E (bất kể gỡ được hay không, LINK-05) → xóa hash, candidate, cart order. | Lỗi gỡ link không được tính (LINK-05). File Library bị xóa dù là bản duy nhất (DEL-01). Torrent vẫn trong qBit nên bị adopt lại (DEL-06). |
| **Reset Movie** (466-507) | như Reset Episode, theo phim. | Như trên. |
| **Unlink pack** (`RemoveSeasonPackLinks`, 222-245) | gỡ link các dòng `PackOwner == S` và (`SeasonPack` hoặc orphan), giữ provenance. Có bulk scope. | Lỗi từng file được đếm và báo; không có bước DB nào sau đó nên trạng thái nhất quán. |
| **Cleanup pack** (`ResetSeasonPackForRedownloadAsync`, 368-464) | chụp danh sách id bằng `SeasonPackCleanupMatcher` → gỡ link → **nếu có lỗi thì dừng** (428-436) → xóa dòng → xóa thông tin pack của season → xóa cart order pack. | Đây là luồng duy nhất dừng đúng khi gỡ link lỗi (từ `8657f65`). Còn hở: lỗi "giả thành công" của DEL-03 không bị tính là lỗi; nếu qBit không liệt kê được file thì chỉ dùng provenance (399-400), bỏ sót dòng legacy (an toàn: sót chứ không xóa thừa). |
| **Purge khi reconcile** (`TorrentReconciliationService.cs:120-167`) | `RefreshLinkStatus` → xóa dòng mà cả `FilePath` lẫn `LinkedPath` đều "không tồn tại". | LINK-04 (ổ offline). Dòng bị xóa thẳng, không phát event, nên symlink của nó không được dọn. |
| **Add gate** (`ITorrentAddGateService.cs:87-231`) | (torrent có sẵn) `setLocation`/category/tag **trước** → validate → xóa kèm file nếu từ chối → blacklist. | DEL-02. |

**Có thể xóa nhầm file gốc trong thư mục download không?**
Qua luồng gỡ link: **không tìm ra đường nào**. `RemoveHardLink` chặn `LinkedPath == FilePath` (sau `GetFullPath`, không phân biệt hoa thường), và `LinkedPath` luôn do app sinh trong root Library. Dòng import (`LinkedPath = FilePath`, `MediaImportService.cs:329, 349`) chỉ phát event rồi xóa dòng (`AutoTorrentLinkService.cs:966-986`). Hai lỗ hổng lý thuyết đều cần dữ liệu `LinkedPath` sai, xem DEL-08:
1. root Library trùng hoặc chứa thư mục download (tên thư mục `.` hay `..`);
2. `LinkedPath` trỏ vào file thật khác (LINK-07, ghi chú 1 của phụ lục LINK).

Qua qBittorrent: **có**, xem DEL-02.

**Hardlink có link count > 1:** code không bao giờ đọc link count. Xóa `LinkedPath` khi link count = 2 chỉ bớt một tên, nội dung còn ở thư mục download. Khi link count = 1 (user đã xóa torrent kèm file, việc thường làm sau khi seed xong) thì xóa là mất hẳn (H1). Dữ liệu thật hôm nay: 295/295 hardlink Library có link count 2 (`linkcount.py`), nên **chưa ai đang ở trạng thái rủi ro**. Rủi ro xuất hiện ngay khi user dọn qBittorrent.

---

## 3. Chọn mục tiêu (target selection)

- **Show/phim khác:** mọi luồng lọc `MediaKind` + `Provider == tmdb` + `ProviderId == TmdbId`, nên không chạm show hay phim khác. Ngoại lệ đã biết: dòng legacy không có `ProviderId` không bao giờ được chọn (LINK-07), và hai dòng chung `LinkedPath` (LINK-07). Symlink: `ResolveRemovalGroupFromSnapshot` (`SymlinkCoordinatorService.cs:930-970`) gom theo `FilePath` **hoặc** `SymlinkPath` trùng, nên khi hai dòng của hai tập khác nhau tình cờ cùng `SymlinkPath` (LINK-01 kịch bản B, LINK-09) thì gỡ một dòng sẽ xóa symlink của dòng kia.
- **Mùa khác, pack khác:** `SeasonPackCleanupMatcher` (`MediaManager.Core/Services/SeasonPackCleanupMatcher.cs:67-115`) khớp khi (a) `PackOwnerSeasonNumber == owner`, hoặc (b) là dòng pack/orphan có hash thuộc pack, hoặc (c) dòng không có provenance mà `FilePath` nằm trong danh sách file của torrent pack. Loại trừ dòng import. Với (a), mọi dòng mang owner S đều bị chọn bất kể hash, kể cả dòng của một pack **cũ** cùng owner. Đây là chủ ý (cleanup theo owner), không phải lỗi. Pack nhiều mùa (owner S01, phủ S01-S03) bị dọn trọn, đúng như hộp thoại "covers". Không tìm thấy trường hợp chọn nhầm torrent lẻ. Bản `8ce1b1c` từng chọn theo số mùa (sẽ xóa cả dòng torrent lẻ và dòng import trong mùa); bản hiện tại đã sửa (mục 4).
- **File user tự thêm:** dòng import (`IsExternalImport`) bị loại khỏi cleanup pack và chỉ bị xóa *dòng* (không xóa file) khi Delete Media. File user tự đặt vào thư mục Jellyfin trùng tên symlink thì **bị xóa** (DEL-05). File `.nfo` lạ trong thư mục show episode-group bị xóa (DEL-10). Subtitle user tự tải về cạnh symlink bị xóa khi gỡ link (đúng ý đồ của `ccc4bf5`).
- **Thứ tự tập không mặc định (episode group, mapping):** gỡ link dùng `GetOutputEpisodeKey` (Mapped trước, rồi S/E) cho Reset tập, nhất quán với lúc tạo link (phụ lục LINK, mục "đã kiểm"). Cleanup pack không dùng S/E nên không bị ảnh hưởng. Lệch xảy ra khi đổi organization hoặc TMDB đổi số tập: key cũ không còn khớp nên Reset tập theo số mới **không** gỡ được link cũ (LINK-03, LINK-08). `40310d9` thêm dọn NFO và prune thư mục cho show episode-group, đúng hướng nhưng xóa quá rộng (DEL-10).
- **Legacy overlap:** commit `68a8f4c` "fix legacy overlap" và `docs/legacy-overlap/` nói về các cột cấu hình cũ (`PreferredQuality`…) chồng với recipe trong **đánh giá candidate search**, không phải luồng xóa. Đã kiểm, ngoài phạm vi; không có đường xóa nào trong commit đó.

---

## 4. Lịch sử bug và hotspot

| Commit | Ngày | Sửa gì | Có giải quyết tận gốc không |
|---|---|---|---|
| `5291761` "Fix deleta hardlink not deleta library folder" | 16/05 | Thêm guard "LinkedPath trong output root" và dọn thư mục rỗng ngược lên root. | Đúng cho triệu chứng (thư mục rỗng ở lại). Guard về sau đổi sang root **sinh theo ổ từ settings hiện tại** + `OutputLibraryFolder` cũ: đổi settings là link cũ thành "ngoài root", gỡ lỗi, dẫn tới AUD-005 và LINK-05. |
| `0cd29b2` "Fix delete function" | 17/05 | Thêm `SourceReconciliationService` cho màn Inbox. | Inbox không còn; service vẫn đăng ký DI nhưng không có caller (dead code chứa `RemoveHardLink` + `DeleteSourceItem`). |
| `d022363` "improve hardlink handling" | 19/05 | Tạo hardlink bằng đường `\\?\`. | Xóa vẫn dùng đường thường, nhưng .NET 8 tự xử lý đường dài: H9 xóa được đường 515 ký tự có Unicode. Không có lỗi. |
| `4983e57` "add symlink and run as admin" | 17/06 | Thêm cả subsystem symlink. | `TryRemoveSymlink` **không kiểm reparse point từ đầu**: gốc của DEL-05. |
| `8e2b3a7` "Add reset download button per eps" | 17/07 | Reset tập: gỡ link + hộp thoại "Files still on disk are not deleted". | Hộp thoại sai ngay từ đầu (DEL-01). `734039c` (04/08) chép nguyên sang phim. |
| `d8a5060` "fix jellyfin symlink bug" | 28/07 | Ghi `SymlinkPath` vào instance `SourceItem` của event (khác instance đọc từ DB). | Sửa triệu chứng. Gốc rễ (gỡ symlink dựa vào `SymlinkPath` lưu trên một instance có thể cũ, gỡ symlink bất đồng bộ sau khi DB đã đổi) vẫn còn; `40310d9` thêm fallback `ResolveSymlinkPath`. Hai lần vá cùng một chỗ là hotspot. |
| `40310d9` "fix non-default order media cleanup" | 02/08 | Xóa NFO episode trước khi xóa symlink, prune season/show; dọn NFO mồ côi của show episode-group. | Giải quyết đúng chuyện thư mục không xóa được do còn NFO. Nhưng `CleanupOrphanEpisodeNfos` xóa mọi `.nfo` lạ (DEL-10). |
| `68a8f4c` "fix legacy overlap" | 27/08 | Đánh giá candidate search. | Không liên quan xóa. |
| `8ce1b1c` "fix pack cleanup" | 03/09 | Cleanup pack xóa `SourceItems` theo **số mùa** hoặc owner (vì Unlink xóa cờ pack nên lọc theo cờ trượt). | Sửa quá tay: xóa dòng của torrent lẻ và dòng import trong mùa mà **không gỡ link**, nên hardlink thành mồ côi. Bị thay sau **12 ngày**. |
| `8657f65` "update Link/Unlink, Cleanup for pack mode" | 15/09 | `SeasonPackCleanupMatcher` (provenance + đường legacy chính xác), Unlink giữ provenance, Cleanup **dừng khi gỡ link lỗi**, bulk event. Có 231 dòng test cho matcher. | Gốc rễ của pack được giải quyết. Nhưng quy tắc "lỗi gỡ link thì dừng" **không** được áp cho Reset tập/phim (LINK-05) và Delete Media (AUD-005, DEL-04): ba luồng xóa giờ có ba hành vi khác nhau. |
| `46f8dda` "fix potential slient failed qBittorrent deletion" (AUD-002) | 16/09 | Delete API ném lỗi, retry, verify, pause, halt batch. | Giải quyết tận gốc chuyện nuốt lỗi. Cùng ngày `54b2e08` thêm `existingTorrent` vào gate: đường "kiểm lại torrent có sẵn" kế thừa nguyên `deleteFiles: true` (DEL-02). Test AUD-002 không phủ trường hợp này. |
| `ccc4bf5` "update subtitle removal for unlink service" | 01/10 | Xóa subtitle cạnh symlink khi gỡ link. | Bỏ sót tên nhiều chấm (DEL-09). |

Hotspot: `SymlinkCoordinatorService` (4 commit sửa xóa), `AutoTorrentLinkService.RemoveLinks`/Reset (3), guard root của `HardlinkService` (2). Mẫu lặp lại: mỗi lần chỉ sửa **một** luồng xóa, không có một hàm "gỡ link an toàn" dùng chung, nên quy tắc đúng ở luồng này (dừng khi lỗi, giữ provenance) không lan sang luồng khác.

---

## 5. Log của app

- Chỉ còn log từ 29/10/2026 (retention 14 ngày trong `settings.json`). Trong khoảng đó **không** có lần xóa media, reset, unlink, cleanup pack hay xóa torrent nào: không có dòng "Removed hardlink path", "Refusing to delete", "Pack cleanup", "Deleting torrent" hay "Purged … orphaned". `ReconcileAll` mỗi lần khởi động: "created 0, repaired 0, removed 0, skipped 293–295, errors 0".
- 4 lần validate gần đây đều "Validation passed" (torrent mới, không phải torrent có sẵn).
- Hệ quả: không có bằng chứng log cho các bug cũ trong commit message (tháng 5–9); mọi kết luận ở đây dựa trên code và harness. Retention 14 ngày cũng có nghĩa là bằng chứng của một lần xóa nhầm sẽ tự biến mất sau hai tuần (ghi chú 6).
- `settings.json` thật (đọc, không ghi): `TorrentValidation.DangerousExtensions` vẫn là danh sách **cũ** có `.zip .rar .7z .gz .bz2 .tar .iso .tmp`, và `AllowedMediaExtensions` thiếu `.m4v`. Code hiện tại (từ `9efbf03`, 20/08) đã bỏ archive khỏi mặc định vì "some legitimate packs include them and would false-positive", nhưng không có migration nên máy thật vẫn chạy danh sách cũ. Điều này làm DEL-02 dễ xảy ra hơn.
- `OutputLibraryFolder = <StateFolder>\Library`, `DriveLibraryRoots` rỗng, `UnifiedRoot = <UnifiedRoot>`. Library thật ở `<LibraryRoot1>` và `<LibraryRoot2>`.

---

## 6. Findings

### DEL-01 — High — Reset Episode/Movie xóa file Library, kể cả bản duy nhất, trái với hộp thoại

**Code**
- `ViewModels/LibraryViewModel.cs:750` và `:1134`: hộp thoại *"This clears all download and link data … Files still on disk are not deleted."*
- `Services/AutoTorrentLinkService.cs:322` (`RemoveEpisodeLinks`) và `:470` (`RemoveMovieLinks`) → `RemoveLinks` → `HardlinkService.RemoveHardLink` → `File.Delete(LinkedPath)` (`HardlinkService.cs:172`).
- Không chỗ nào đọc link count hay kiểm `File.Exists(FilePath)` trước khi xóa.

**Kịch bản (H1, `out.txt`)**: link S01E05 (link count 2). User xóa torrent trong qBittorrent kèm file sau khi seed xong; link count còn 1, file Library là bản duy nhất. User bấm Reset để tải bản tốt hơn, hộp thoại cam đoan không xóa file trên đĩa. Kết quả: `removed=1 errors=0`, `library file still exists = False`, trong thư mục test chỉ còn file DB. Tập mất hẳn, Jellyfin mất tập ngay lập tức chứ không phải khi bản mới về.

**Tác động**: mất media không phục hồi được, qua một thao tác UI có xác nhận mà nội dung xác nhận nói ngược lại. Cùng cơ chế ở Delete Media / Delete Entire Library / Unlink pack, nhưng các hộp thoại đó có nói "removes hardlinks", và user ít biết hardlink đã thành bản duy nhất.

**Vì sao chưa xếp Critical**: cần user đã tự xóa bản download; tác động một tập/phim mỗi lần; đúng lúc user định thay nội dung. Dữ liệu thật hôm nay 0/295 file ở link count 1. Nếu user có thói quen xóa torrent sau khi seed, nên nâng lên Critical.

**Hướng sửa**:
1. Trước khi xóa `LinkedPath`, đọc link count (`GetFileInformationByHandle.nNumberOfLinks`) hoặc so file ID với `FilePath`. Nếu link count ≤ 1 hoặc `FilePath` không tồn tại: với Reset thì **không xóa** (chỉ xóa state, hoặc đổi tên sang `*.mm-old` cho tới khi bản mới link xong); với Delete/Unlink thì hỏi riêng "đây là bản duy nhất".
2. Sửa nội dung hộp thoại Reset cho đúng ("library links are removed").

### DEL-02 — High — Add gate xóa torrent có sẵn kèm file khi validate từ chối

**Code**
- `Services/AutoTrackService.cs:1665-1689`: `existing = TryGetExistingByListingUrlAsync(url)` (infohash trong magnet trùng một torrent đang có), rồi `AddPausedValidateAndResumeAsync(…, existing)`. **Không hỏi user.** Recovery sau crash (`:1350-1356`) cũng đi đường này.
- `ViewModels/TorrentWorkspaceViewModel.cs:1588-1611`: cart hỏi *"Do you want Media Manager to manage the copy that is already there?"*, không cảnh báo copy đó có thể bị xóa.
- `Services/ITorrentAddGateService.cs:107-118`: torrent có sẵn bị `ApplyManagedTorrentSettingsAsync` (`setLocation` sang thư mục Auto-Track, đổi category, thêm tag) **trước** khi validate.
- `:148-159` (hash bị blacklist), `:173-188` (danh sách file rỗng), `:203-224` (validate trả Delete) → `DeleteRejectedTorrentAsync` → `DeleteTorrentAsync(deleteFiles: true)` (`:246-250`).
- Validator (`MediaManager.Core/Services/TorrentContentValidationService.cs`): extension "nguy hiểm", "obfuscation", và payload top-1/top-3 không phải định dạng media đều cho ra `Delete`. Settings thật còn coi archive và `.tmp` là nguy hiểm (mục 5).

**Bằng chứng (H7)**: torrent có sẵn `Demo.Show.S01.1080p.BluRay` gồm `Demo.Show.S01E05.mkv` + `Fonts/Fonts.zip` (với danh sách của `settings.json` thật), hoặc + `Extras/Menu.exe` (với danh sách mặc định). Gate gọi `setLocation` rồi `torrents/delete … deleteFiles=True`, ném `MaliciousTorrentException`. Thư mục download sau đó **trống**.

**Tác động**: mất dữ liệu user đã tải từ trước (cả pack, extras, file chưa link), seeding bị phá, Auto-Track làm việc này không cần người. Hardlink đã có trong Library còn sống vì link count. Điều kiện: candidate Auto-Track hoặc cart có cùng infohash với một torrent sẵn có, **và** validator từ chối (false positive với archive/fonts trong cấu hình thật, hoặc nội dung thật sự có `.exe`). Không xếp Critical vì cần trùng infohash, và một phần nội dung (đã link) còn sống.

**Hướng sửa**: với `existingTorrent`, không bao giờ `deleteFiles: true`. Validate **trước** khi `setLocation`; nếu từ chối thì chỉ bỏ quản lý (gỡ tag/category đã gắn, hoặc không gắn), blacklist candidate, báo user. Cân nhắc `deleteFiles: false` cho nhánh "metadata timeout". Thêm migration cho `TorrentValidation` để bỏ archive và `.tmp` khỏi danh sách đã lưu (hoặc tách "mặc định" khỏi "user thêm").

### DEL-03 — Medium — `RemoveHardLink` coi "không thấy file" là đã xóa

**Code**: `Services/HardlinkService.cs:162-168`. `!File.Exists(LinkedPath)` thì log Warning "clearing state only", phát `HardlinkRemoved` và trả `true`. `File.Exists` trả `false` cho cả ổ offline, mất quyền, lỗi I/O. Caller tính là `RemovedCount` (`AutoTorrentLinkService.cs:1004-1018`). `TryRemoveSymlink` (`SymlinkService.cs:81-84`) cũng vậy.

**Bằng chứng (H3)**: link 2 tập, đổi tên `lib` (giả lập ổ Library offline), Delete Media cho `removed=2 hardlinkErrors=0`, show bị xóa khỏi DB. Trả thư mục lại thì cả 2 hardlink vẫn còn, 2 dòng `SourceItems` ở trạng thái `Parsed`, `LinkedPath=null`. Không còn gì trong app trỏ tới hai file đó.

**Tác động**: file Library mồ côi vĩnh viễn (tốn dung lượng, Jellyfin có thể vẫn quét nếu user trỏ Jellyfin vào thư mục hardlink). UI báo thành công nên user không biết. Cleanup pack cũng tính trường hợp này là thành công nên không dừng. Cùng nguyên nhân gốc với LINK-04 (`RefreshLinkStatus`), nhưng đây là luồng xóa do user bấm.

**Hướng sửa**: phân biệt "không tồn tại" với "không truy cập được": kiểm thư mục cha và root ổ có tồn tại không (`Directory.Exists(Path.GetPathRoot(...))`, thư mục root Library). Nếu không truy cập được thì trả lỗi. Chỉ trả "đã xóa" khi thư mục cha tồn tại mà file không có.

### DEL-04 — Medium — AUD-005 vẫn đúng; show đã xóa còn mãi trong Jellyfin

**Đối chiếu AUD-005**: code `LibraryManagementService.cs:39-61` và `:74-119` **không đổi**; lỗi gỡ link chỉ được cộng vào `HardlinkErrorCount`, rồi DB vẫn bị xóa. Kết luận cũ "generated hardlinks can remain" vẫn đúng. Tác động thực tế rộng hơn ghi nhận cũ.

**Bằng chứng (H4)**: mở file Library với `FileShare.Read` (giống Jellyfin đang phát), Delete Media: `hardlinkErrors=1 shows=1`; show bị xóa, file còn. Dòng `SourceItems` vẫn `State=Linked`, còn `LinkedPath` và `SymlinkPath`. `ReconcileAll` (khởi động lần sau) cho `skipped 1`, symlink **giữ nguyên**. Vì dòng vẫn `Linked`, mọi lần khởi động sẽ duy trì (và tạo lại nếu mất) symlink của show đã xóa. Nếu show đổi tên, `ApplyTrackedMetadata` không tìm thấy show nên dựng symlink theo `MatchedTitle` cũ (`SymlinkSyncService.cs:310-350`).

**Tác động**: show user đã xóa vẫn xuất hiện trong Jellyfin cho tới khi user tự dọn cả hai thư mục; app không còn UI nào để thử lại. UI chỉ báo "N hardlink error(s)" ở status bar.

**Hướng sửa** (giữ khuyến nghị AUD-005, cụ thể hơn): áp đúng quy tắc của Cleanup pack. Nếu `ErrorCount > 0` thì **không** xóa TrackedShow/Movie, báo lỗi bằng hộp thoại, cho thử lại. Dùng chung một hàm "gỡ link rồi mới xóa state" cho Delete, Reset (LINK-05) và Cleanup.

### DEL-05 — Medium — Xóa "symlink" không kiểm tra đó là symlink

**Code**
- `Services/Symlink/SymlinkService.cs:76-98`: `TryRemoveSymlink` gọi `File.Delete` cho bất cứ thứ gì ở đường đó; không kiểm `FileAttributes.ReparsePoint`, không kiểm nằm trong `UnifiedRoot`.
- `Services/Symlink/SymlinkSyncService.cs:85-95`: trong `SyncItem`, nếu đường symlink đã có file mà không resolve được về `LinkedPath` thì xóa để "repair". Chạy ở mỗi lần khởi động (`ReconcileAll`) và mỗi lần link.
- `ViewModels/SettingsViewModel.cs:1755-1765` và `FirstRunViewModel.cs:1061`: `UnifiedRoot` nhận mọi thư mục, không kiểm trùng hoặc lồng với root Library hay thư mục download.

**Bằng chứng (H5, `SymlinkService` thật)**: đặt file thật (không phải reparse point) đúng tên `jf\Shows\Demo Show (2015) [tmdbid-105]\Season 01\Demo Show - S01E05.mkv`. `SyncItem` log "Removed symlink: …", file thật **mất**. Harness không chạy admin nên tạo symlink thất bại sau đó; app thật chạy admin (`app.manifest`) sẽ thay bằng symlink, nội dung user vẫn mất.

**Khi nào xảy ra**: (a) user tự chép file vào thư mục Jellyfin theo đúng quy ước tên Jellyfin (là quy ước app dùng); (b) `UnifiedRoot` trỏ trùng hoặc lồng với một root Library, khi đó "file đang chiếm chỗ" có thể là hardlink của một dòng khác (thường còn bản download, nhưng không chắc).

**Hướng sửa**: `TryRemoveSymlink` chỉ xóa khi `ReparsePoint` **và** đường nằm trong `UnifiedRoot`. Nếu không phải symlink thì báo lỗi "path occupied by a real file", không đụng tới. Validate `UnifiedRoot` không trùng, không chứa và không nằm trong root Library hay `SourceFolders`.

### DEL-06 — Medium — Reset/Cleanup không bền: reconcile adopt lại torrent cũ

**Code**: Reset xóa hash (`AutoTorrentLinkService.cs:350`, `:496`) và Cleanup xóa pack của season (`:447`) nhưng **để torrent trong qBittorrent**. `TorrentReconciliationService.AdoptManualTorrentsAsync` (`:317-381`) coi mọi torrent không có trong `usedHashes` là "thêm tay", khớp theo tên (`MatchesEpisode`, `MatchesSeasonPack`, `MatchesMovie`) và gắn lại, rồi auto-link nếu bật. Auto-Track gọi reconcile theo lịch (`AutoTrackService.cs:669`).

**Bằng chứng (H2)**: sau Reset: `TorrentHash=''`, `Missing`, 0 dòng. Reconcile theo scope show: `TorrentHash='H2A' availability=Available | Matched=1, Linked=1`, hardlink cũ được tạo lại.

**Tác động**: user reset để tải bản khác; trong vòng một chu kỳ Auto-Track, bản cũ quay lại, tập lại Available và Auto-Track không hunt nữa. Với pack (theo code, chưa chạy harness): `UpdateSeasonPackTorrent` gắn lại `PackTorrentHash`. Hộp thoại Cleanup bảo user tự xóa file nhưng không nói phải làm **trước** lần reconcile kế tiếp.

**Hướng sửa**: khi Reset/Cleanup, ghi hash cũ vào một danh sách "đã từ bỏ" theo media (hoặc blacklist cục bộ, không phải malware) để `AdoptManualTorrentsAsync` bỏ qua; hoặc đề nghị xóa torrent khỏi qBittorrent (không kèm file) ngay trong luồng Reset.

### DEL-07 — Medium — Delete Media để lại `SourceItems`; thêm lại show thì "Available" dù không có file

**Code**: `RemoveLinks` chỉ cập nhật dòng thường thành `Parsed`, `LinkedPath=null` (`AutoTorrentLinkService.cs:1004-1018`). `LibraryManagementService` không xóa `SourceItems`; cột này không có FK. Availability (`TrackedShowService.cs:442-473`) chỉ cần `MatchAccepted` và state khác `Deleted`/`Ignored`, không xét `LinkedPath` (gốc chung với LINK-06).

**Bằng chứng (H10)**: Delete Media thành công (`removed=1 errors=0`), dòng còn lại `state=Parsed LinkedPath=null`. Thêm lại cùng TMDB id: S01E05 **Available**, `TorrentHash=''`, Library 0 file.

**Tác động**: thêm lại show đã xóa thì app báo đủ tập, Auto-Track không hunt, Jellyfin trống. Dữ liệu thật hôm nay không có dòng mồ côi (đã kiểm), nên user chưa gặp.

**Hướng sửa**: Delete Media xóa `SourceItems` của media sau khi gỡ link thành công (trừ dòng import nếu muốn giữ, nhưng dòng import cũng nên xóa vì đã gắn `ProviderId`). Availability nên yêu cầu `State == Linked` (cùng hướng sửa LINK-06).

### DEL-08 — Low — Guard xóa chỉ so chuỗi, root lấy từ settings hiện tại

**Code**
- `HardlinkService.cs:268-275` `IsPathInsideRoot`: so tiền tố sau `GetFullPath`; không resolve junction/symlink, không so file ID với `FilePath`.
- `LibraryPathResolver.cs:68-78`: root = `Path.Combine(driveRoot, DefaultLibraryFolderName)` hoặc `DriveLibraryRoots[drive]`, đọc lúc xóa.
- `ViewModels/SettingsViewModel.cs:1018-1026` → `RefreshLibraryRootPreview` (`:1844-1849`) **ghi thẳng** `DefaultLibraryFolderName` vào `_settingsService.Current` ở **mỗi lần gõ**, trước khi bấm Save. Không chặn `.`, `..`, hay đường tuyệt đối: `Path.Combine("E:\", ".")` cho ra `E:\`, tức root là **gốc ổ**.

**Bằng chứng (H6)**: thư mục `Season 01` trong Library là junction trỏ ra ngoài. `RemoveShowLinks` xóa file ở thư mục ngoài (guard qua vì chuỗi nằm trong Library). Prune xóa junction, không xóa thư mục đích (an toàn). Trong cấu hình bình thường điều này vô hại (file ngoài chính là hardlink); nó chỉ cho thấy guard không bảo vệ được nếu `LinkedPath` sai.

**Tác động**: (1) Gõ dở tên thư mục Library trong Settings khi Auto-Track đang chạy thì link mới được tạo ở thư mục tên dở (`E:\M\Shows\…`), và link cũ bị coi là "ngoài root" nên gỡ không được (vào thẳng AUD-005, LINK-05). (2) Tên `.` hoặc `..` biến guard thành "cả ổ". Khi đó chỉ còn chặn `LinkedPath == FilePath` đứng giữa thư mục download và `File.Delete`.

**Hướng sửa**: thêm kiểm tra danh tính: chỉ xóa nếu `LinkedPath` và `FilePath` cùng file ID (cùng volume serial + file index) **hoặc** `FilePath` không còn và user đã xác nhận (DEL-01). Lưu root Library đã dùng lúc tạo link cùng dòng (hoặc suy từ `LinkedPath`) thay vì đọc settings hiện tại. Validate tên thư mục Library (không chứa dấu phân cách, `.`, `..`), và chỉ áp vào `Current` khi Save.

### DEL-09 — Low — Dọn subtitle bỏ sót tên nhiều chấm và chạy trước khi xóa symlink

**Code**: `SymlinkSubtitleCleanupService.cs:40-70` chỉ nhận `{base}.{ext}` hoặc đúng **một** token ngôn ngữ (`LastIndexOf('.')`). `SymlinkCoordinatorService.cs:529-535` xóa subtitle **trước** `RemoveItem`; nếu symlink không xóa được (đang mở) thì subtitle đã mất trong khi video còn.

**Bằng chứng (H8)**: sau `DeleteSubtitleFiles` còn lại `S - S01E01.eng.forced.srt` và `S - S01E01.en.sdh.ass` (định dạng Jellyfin và Bazarr hay dùng); đã xóa `.srt`, `.eng.srt`, `.json`; giữ đúng `.nfo`, `-thumb.jpg` và subtitle của E02.

**Tác động**: thư mục season/show Jellyfin không prune được (còn file), show "ma" có subtitle mà không có video. Không mất dữ liệu media.

**Hướng sửa**: khớp tiền tố `{base}.` với mọi số token, giới hạn trong danh sách extension; xóa subtitle **sau** khi symlink đã xóa thành công.

### DEL-10 — Low — Dọn NFO cho show episode-group xóa mọi `.nfo` lạ

**Code**: `NfoWriterService.cs:180-225` xóa đệ quy mọi `*.nfo` (trừ `tvshow.nfo` và NFO của symlink đang hoạt động) dưới thư mục show tìm theo `[tmdbid-N]`. Gọi ở mỗi lần Sync Now hoặc khởi động (`SymlinkCoordinatorService.cs:681-765`).

**Tác động**: `season.nfo` (Jellyfin ghi khi bật NFO saver), NFO của file user tự thêm hay NFO extras bị xóa ở mỗi lần đồng bộ. Mất metadata, Jellyfin tự sinh lại được. DB thật có 2 show episode-group, chưa có link nào.

**Hướng sửa**: chỉ xóa NFO có tên khớp mẫu episode do app sinh (`… - SxxEyy*.nfo`), hoặc ghi danh sách NFO đã sinh vào DB.

### DEL-11 — Low — Thiếu xác nhận và thiếu xử lý lỗi ở vài lệnh xóa

- `LibraryViewModel.cs:856-876` `UnlinkSeasonPack` và `:1094-1121` `LinkMovie` (nút đổi chức năng thành unlink khi phim đã link) xóa hardlink **không hỏi**, trong khi các lệnh xóa khác đều có hộp thoại. Kết hợp DEL-01, một cú bấm có thể xóa bản duy nhất.
- `LibraryViewModel.cs:1661-1680` `DeleteEntireLibrary` không try/catch. `LibraryManagementService.DeleteEntireLibrary` không có bulk scope (mỗi file một event, mỗi event một `Task.Run`) và không nguyên tử: lỗi ở `DeleteAllCached` (`PosterImageService.cs:232-241`) xảy ra **sau** khi DB đã xóa và ném thẳng ra dispatcher.

**Hướng sửa**: thêm xác nhận cho unlink; bọc `DeleteEntireLibrary` trong try/catch; dùng `BeginBulkMutation`; xóa poster dạng best-effort.

---

## 7. Ghi chú chưa đủ chuẩn finding

1. **Xóa chạy song song với Auto-Track**: không có khóa nào giữa Delete/Reset (UI thread) và reconcile/link nền. Nếu Auto-Track link một tập giữa `RemoveShowLinks` và `DeleteTrackedShow`, hardlink mới mồ côi ngay. Chưa tái hiện.
2. **Event symlink mất khi tắt app**: gỡ symlink chạy `Task.Run` sau khi DB đã đổi; `Dispose` hủy `_shutdown` (chờ 8 s). Event chưa chạy thì symlink treo, mà dòng đã `Parsed` nên `ReconcileAll` không bao giờ dọn (gốc chung LINK-06).
3. **`SymlinkPath` dùng chung**: `ResolveRemovalGroupFromSnapshot` gom cả dòng **khác tập** có cùng `SymlinkPath` (do LINK-01/LINK-09), nên gỡ một tập có thể xóa symlink của tập kia.
4. **`RemoveOrphanPackSpecialLink`** và **`SourceReconciliationService`** không có caller UI: dead code có quyền xóa.
5. **Xóa cart khi order đang chờ validate**: `ClearCart` trong Delete Media xóa luôn order có `PendingValidationHash`; torrent đang chạy thành "không ai quản" và có thể bị adopt theo tên (LINK-02).
6. **Retention log 14 ngày** xóa luôn dấu vết các lần xóa. Nên ghi các thao tác xóa (đường dẫn, link count, kết quả) vào một log riêng giữ lâu hơn.
7. **`PublishJunkCleanup`** xóa `*.xml`/`*.lib`/`*.pdb` trong thư mục exe ở mỗi lần khởi động; an toàn khi chạy từ thư mục cài đặt riêng, rủi ro nếu exe nằm trong thư mục dùng chung.
8. **`recipeId` không lọc `..`** trong `RecipeService.GetRecipePath`; phạm vi bị giới hạn ở file `.rcp`.

## 8. Những điểm đã kiểm và thấy ổn

- Không đường gỡ link nào xóa được `FilePath` (file gốc download) trong cấu hình hợp lệ; dòng import chỉ bị xóa dòng.
- Unicode và đường dài (515 ký tự, có `～`, tiếng Việt, CJK): link và xóa đều đúng (H9).
- Dọn thư mục rỗng (Library và Jellyfin) không đệ quy, dừng ở root, không đi xuyên junction (H6).
- `SeasonPackCleanupMatcher` không chọn show khác, torrent lẻ hay dòng import; Cleanup pack dừng khi gỡ link lỗi.
- Xóa torrent qua API (AUD-002) không còn nuốt lỗi: retry, verify, pause, ném `TorrentCleanupFailedException`; test `TorrentCleanupServiceTests` và `TorrentAddGateCleanupTests` pass.
- `LogCleanupService` chỉ xóa file khớp mẫu tên của app trong `StateFolder\logs` và bảo vệ file đang ghi.
- DB thật: không có dòng TrackedEpisode/Season, cart order, blacklist hay `SourceItems` mồ côi theo media đã xóa; 295/295 hardlink có link count 2.

## 9. Test còn thiếu

Hiện chỉ `MediaManager.App.Tests/Cleanup` (gate, cleanup service, subtitle) và `MediaManager.Core.Tests/Pack/SeasonPackCleanupMatcherTests.cs` phủ vùng này. `HardlinkService`, `LibraryManagementService`, `AutoTorrentLinkService.Remove*/Reset*`, `SymlinkService`, `SymlinkSyncService.RemoveItem`, `NfoWriterService.Cleanup*`, `TorrentReconciliationService.Purge*`, `LogCleanupService` và `PublishJunkCleanup` **không có test**. Các test nên thêm (đều chạy được trên thư mục tạm, harness này là mẫu):

1. `RemoveHardLink` từ chối khi `LinkedPath == FilePath` (khác hoa thường, có `\..\`, có dấu `\` cuối) và khi `LinkedPath` ngoài root.
2. `RemoveHardLink` khi file Library là bản duy nhất (link count 1): theo hướng sửa DEL-01.
3. `RemoveHardLink` khi thư mục hoặc ổ Library không truy cập được thì phải trả lỗi, không trả thành công (DEL-03).
4. `DeleteSelectedMedia` khi một file bị khóa: show **không** bị xóa (AUD-005/DEL-04); khi thành công thì `SourceItems` của media bị xóa (DEL-07).
5. `ResetEpisodeForRedownload`/`ResetMovieForRedownload` khi gỡ link lỗi: dừng và trả `ErrorCount` (LINK-05).
6. Reset rồi reconcile: torrent cũ **không** được adopt lại (DEL-06).
7. Add gate với `existingTorrent` bị từ chối: **không** gọi delete kèm file và không `setLocation` trước khi validate (DEL-02).
8. `SymlinkService.TryRemoveSymlink` từ chối file thường và đường ngoài `UnifiedRoot` (DEL-05).
9. `SymlinkSubtitleCleanupService` với `.eng.forced.srt`, `.en.sdh.ass`, và base name có dấu chấm (DEL-09).
10. `CleanupOrphanEpisodeNfos` giữ `season.nfo` và NFO không theo mẫu (DEL-10).
11. Cleanup/prune thư mục với junction trong Library: xóa junction, không xóa đích.
12. `LogCleanupService` chỉ xóa đúng mẫu tên, giữ log đang ghi; `PublishJunkCleanup` không chạy khi có debugger.
13. Migration settings: danh sách `DangerousExtensions` đã lưu không chứa archive và `.tmp` sau khi nâng cấp.

## 10. Thứ tự sửa đề xuất

1. **DEL-02** (xóa torrent có sẵn kèm file): nhỏ, cục bộ trong gate (không `deleteFiles` với `existingTorrent`, validate trước `setLocation`), kèm migration danh sách extension. Rủi ro mất dữ liệu lớn nhất, chạy không cần người.
2. **DEL-01** (Reset xóa bản duy nhất): kiểm link count/file ID trong `RemoveHardLink` + sửa hộp thoại. Cùng thay đổi này giải quyết luôn phần "danh tính file" của DEL-08.
3. **Một hàm gỡ link dùng chung** cho Delete Media, Reset tập/phim và Cleanup pack: gỡ link → nếu lỗi (kể cả "không truy cập được" của DEL-03) thì dừng, giữ state → nếu được thì xóa `SourceItems` + state. Giải quyết DEL-03, DEL-04 (AUD-005), DEL-07 và LINK-05 trong một lần, và chặn kiểu sửa từng luồng đã thấy ở mục 4.
4. **DEL-05**: kiểm reparse point + root trong `TryRemoveSymlink`, validate `UnifiedRoot`.
5. **DEL-06**: ghi nhớ hash đã từ bỏ khi Reset/Cleanup.
6. **DEL-08** phần settings: chỉ áp `DefaultLibraryFolderName` khi Save, validate tên.
7. **DEL-09, DEL-10, DEL-11**: dọn dẹp nhỏ, gom một PR.

Mỗi bước nên đi kèm test tương ứng ở mục 9. Theo `00-method-and-scope.md`, báo cáo này chỉ là tài liệu; không có thay đổi code app nào trong phiên này.
