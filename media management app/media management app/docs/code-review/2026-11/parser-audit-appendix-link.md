# Phụ lục B của parser-audit: link → SourceItems → đĩa (hardlink + symlink) — finding mới

Ngày: 12/11/2026. Nhánh `claude/parser-audit-5f9a30`, chỉ đọc code (không sửa app, không commit, không đụng `<StateFolder>`, qBittorrent hay thư mục Library).
Bổ sung cho [parser-audit.md](parser-audit.md) (mỗi LINK-xx được đăng ký thành một PARSE-xx ở mục 11); **không** lặp lại PARSE-01..15. Thang mức độ theo `docs/code-review/00-method-and-scope.md`.

Mọi đường dẫn code dưới đây tính từ `media management app/media management app/`.

## Cách kiểm chứng

- **Harness end-to-end** (ngoài repo): `parser-audit-harness/linkharness/Program.cs`, output ở `parser-audit-harness/linkharness/out.txt`.
  - Dùng **code thật** của app (DLL Release do `MediaManager.App.Tests` build): `DatabaseService` (SQLite tạm), `AutoTorrentLinkService`, `HardlinkService` (tạo hardlink thật trong thư mục tạm), `LibraryPathResolver`, `SymlinkSyncService`, `TrackedShowService`, `TorrentReconciliationService`, `LibraryLinkEventHub`.
  - Chỉ giả lập: qBittorrent (`IQbittorrentClient`), `ISymlinkService` (ghi file thường thay cho symlink để khỏi cần quyền admin, có lưu map đích), logger, settings. Không có `SymlinkCoordinatorService` (event không tự đồng bộ symlink); harness gọi `SymlinkSyncService.ReconcileAll()` để mô phỏng lần khởi động/Sync Now.
  - Kịch bản A–G tương ứng các finding bên dưới.
- **Dữ liệu thật (read-only)**: bản copy DB `bản copy read-only của `media-manager.db`` (mở `mode=ro`) và danh sách 122 torrent `parser-audit-harness (`torrents_app.json`)`. Script: `parser-audit-harness/linkharness/adopt_sim2.py`, `adopt_sim3.py`, `drift.py`.

## Tóm tắt

| ID | Mức | Tin cậy | Tiêu đề |
|---|---|---|---|
| LINK-01 | High | Cao (harness A, B) | Link lại pack: "already linked" đổi cờ `IsOrphanPackSpecial` nhưng giữ S/E cũ, nên symlink chạy sang `Season Unknown` hoặc nhiều special dồn vào một `S00E00` |
| LINK-02 | High | Cao (harness F, dữ liệu thật) | Reconcile "adopt" torrent thêm tay theo **số trần** trong tên (`AAC2.0`, `DD 5 1`, CRC), và scope theo show bỏ qua hash của show/phim khác, nên torrent bị gán và link vào **tập sai** |
| LINK-03 | High | Cao (tĩnh, đối chiếu UI) | Đổi Episode Organization (episode group) không gỡ link dù hộp thoại nói "Hardlinks … and pack links will be cleared"; S/E cũ trong `SourceItems` bị so với cách đánh số mới, NFO mô tả sai tập |
| LINK-04 | High | Cao (harness D) | Reconcile khi ổ chứa download + library tạm không truy cập được thì xóa sạch `SourceItems` của ổ đó; khi ổ quay lại, link không tạo lại được ("Output already exists") |
| LINK-05 | Medium | Cao (harness C) | `ResetEpisodeForRedownload` (và `ResetMovieForRedownload`) bỏ qua lỗi gỡ link rồi vẫn xóa `SourceItem`: hardlink + symlink cũ ở lại, bản tải lại không link được, UI báo thành công |
| LINK-06 | Medium | Cao (harness E) | File trong Library bị xóa ngoài app: availability vẫn "Available", symlink Jellyfin treo (dangling) mãi; tập link từ pack không bao giờ tự link lại |
| LINK-07 | Medium | Cao (harness G, 3 cặp trong DB thật) | Hai `SourceItem` dùng chung `LinkedPath` (bản ghi legacy + bản import thư mục Library của chính app); gỡ phim chỉ xóa một dòng, lần khởi động sau symlink "sống lại" ở folder khác tên |
| LINK-08 | Medium | Trung bình–cao (tĩnh) | TMDB đổi số tập/mùa: `TrackedEpisodes` cũ không bao giờ bị xóa, `SourceItems` giữ S/E cũ; tập "ma" vẫn Available, tập mới Missing → hunt + link trùng nội dung |
| LINK-09 | Low | Cao (tĩnh) | Torrent lẻ và pack cùng tập nhưng khác đuôi (`.mkv`/`.mp4`) tạo **hai** link cho cùng một tập; gỡ pack chỉ gỡ một |

Thêm 7 ghi chú chưa đủ chuẩn finding ở cuối.

Không có Critical: không có trường hợp xóa file gốc hay ghi ra ngoài root được quản lý. LINK-04 gần ngưỡng "mất state ứng dụng", nhưng cần điều kiện ngoài (ổ không truy cập được), và có thể phục hồi bằng tay.

---

## Proven findings

### LINK-01 — High — Link lại pack lật cờ orphan/special nhưng giữ S/E cũ → symlink sai chỗ, availability sai

**Code**
- `Services/AutoTorrentLinkService.cs:865-876`: khi đã có `SourceItem` cùng `FilePath` và `LinkedPath` còn tồn tại, code **chỉ** chép `AutoTorrentLinkKind`, `AutoTorrentTorrentHash`, `AutoTorrentPackOwnerSeasonNumber`, `IsOrphanPackSpecial` từ bản ghi mới sang bản ghi cũ. `SeasonNumber`/`EpisodeNumber`/`EpisodeTitle` và `LinkedPath` giữ nguyên.
- `Services/AutoTorrentLinkService.cs:824-856`: orphan được tạo với `SeasonNumber = 0`, **không có** `EpisodeNumber`.
- `Services/HardlinkService.cs:37-53` (`BuildOutputPath`): orphan → `Show/Season Unknown/<FileName>`; không phải orphan → `Season {S}/… S{S}E{E}` với `EpisodeNumber.GetValueOrDefault()` = 0.
- `Services/Symlink/SymlinkSyncService.cs:52-74, 384-410`: `ReconcileAll` dựng lại đường symlink từ bản ghi, xóa symlink cũ (`CleanupDuplicateSymlinks`) rồi tạo symlink mới.
- `Services/TrackedShowService.cs:442-473`: availability dựa trên `(Mapped)Season/Episode` của mọi `SourceItem`, không xét cờ orphan.

**Khác PARSE-12 thế nào**: PARSE-12 nói "link lại không sửa được link sai" (không có gì đổi). Ở đây có thay đổi, nhưng chỉ **một nửa**: cờ orphan đổi còn S/E giữ nguyên, nên bản ghi tự mâu thuẫn và symlink bị dời.

**Kịch bản và bằng chứng (harness A, B; `out.txt`)**

A. Link bằng Gemini (special → `S00E08`), sau đó bấm "Link pack (rules only)". Theo PARSE-09, rule biến OVA M của Show E thành orphan.
- Sau lần 2: `S=0 E=8 orphan=True`, `LinkedPath=…\Season 00\Demo Show - S00E08 - OVA.mkv`. Availability S00E08 = **Available**.
- `ReconcileAll`: "created 1, removed 1". Symlink `Season 00\…S00E08 - OVA.mkv` **bị xóa**, thay bằng `Season Unknown\Demo.OVA.mkv`. Jellyfin mất S00E08, trong khi app vẫn báo có.
- Lần link rules kế tiếp: `TryMigrateLegacyOrphanPath` (dòng 895-930) thấy orphan nằm trong `Season 00` nên **xóa hardlink** rồi tạo orphan mới.

B. Ngược lại: link rules trước (2 OVA thành orphan), sau đó "AI link" (Gemini map S00E08, S00E09).
- Sau lần 2: cả hai dòng `S=0 E=(null) orphan=False`. Availability S00E08, S00E09 = **Missing**, dù đã có link.
- `ReconcileAll` #1: hai symlink `Season Unknown\…` bị xóa. Chỉ còn **một** symlink `Season 00\Demo Show - S00E00.mkv`, trỏ tới OVA M.
- `ReconcileAll` #2: "repaired 2". Hai dòng thay nhau ghi đè cùng một đường `S00E00.mkv` ở **mỗi** lần sync, và cả hai dòng lưu cùng một `SymlinkPath`.

**Tác động**: tập special hoặc extra biến mất khỏi Jellyfin, hoặc gộp thành một file `S00E00` lật qua lật lại, mà không báo lỗi ("already linked"). Availability sai theo cả hai chiều. Bị kích hoạt bởi thao tác UI bình thường (đổi giữa nút rules và AI, hoặc retry Gemini với `bypassCache`). **Mọi bản sửa parser về sau** (ví dụ PARSE-03: tập thường đang là orphan sẽ thành `RegularEpisode`) cũng dẫn tới đúng trạng thái B khi user bấm Link lại. Dữ liệu thật có 16 orphan (14 của TMDB 154494, 2 của 94359).

**Hướng sửa**: trong nhánh "already linked", nếu `IsOrphanPackSpecial` hoặc `(Season, Episode)` khác bản ghi cũ thì đừng chỉ chép provenance. Gỡ link cũ (`RemoveHardLink` cộng event) rồi tạo lại theo bản ghi mới. Tối thiểu: không đổi cờ orphan khi không đổi S/E, và báo "mapping changed, unlink first". Gộp chung với hướng sửa PARSE-12.

### LINK-02 — High — Adopt torrent thêm tay khớp theo số trần, link vào tập sai

**Code**
- `Services/TorrentReconciliationService.cs:317-381` (`AdoptManualTorrentsAsync`): mỗi torrent chưa nằm trong `usedHashes`, nếu khớp **đúng một** mục thì được gán vào mục đó và link luôn (`AutoLinkEpisodeIfConfiguredAsync`, dòng 458-487).
- `MatchesEpisode` (dòng 524-534): tên chứa tên show **và** (`SxxEyy` **hoặc** `ContainsNumberToken(name, EpisodeNumber)`).
- `ContainsNumberToken` (dòng 588-591): regex `(?<!\d)N(?!\d)` trên **tên thô**. Các chuỗi `AAC2.0` (ra 2 và 0), `DDP5.1`/`DD 5 1` (ra 5 và 1), `x265` hay CRC `[8D3A8CEC]` (ra 8 và 3) đều khớp.
- `ContainsNormalized` (dòng 577-581) so **chuỗi con**, không so theo từ, nên show tên "You" khớp với "…With You…".
- Scope theo show: `AutoTrackService.cs:669-671` gọi `ReconcileAsync(ForMedia(TvEpisode, showId))`. Khi đó `usedHashes` (dòng 76-80, 214-236) chỉ gồm hash của **show đó** cộng pending validation. Hash của show khác và của phim đều bị coi là "chưa dùng".
- `Services/AutoTorrentLinkService.cs:1143-1148`: torrent chỉ có 1 file video thì lấy luôn file đó, **không parse S/E**.

**Bằng chứng**
- Harness F, dùng tên torrent thật `Show.G.S01E12…AAC2.0.H.264…`. S01E12 đã có hash (bản 720p cũ), S01E02 chưa có. User thêm tay bản 1080p của E12 vào qBit. Reconcile theo scope show cho kết quả `S01E02 hash=H12NEW avail=Available`, `SourceItem … S=1 E=2`. Library có file `Show G - S01E02.mkv`, **nội dung là E12**.
- Dữ liệu thật (`adopt_sim2.py`): **47/122** tên torrent trong qBit khớp số trần với một tập **khác** của chính show đó. Ví dụ `AAC2.0` khớp E02, `DD 5 1` của Show P khớp E05, CRC của Dara-san khớp E03/E08.
- `adopt_sim3.py`, mô phỏng scope theo show trên DB thật: torrent phim `Spinoff.Movie.2019…DDP5.1` là ứng viên adopt cho **12** tập Show B (S0xE01/E05). Hiện nó thoát chỉ vì có hơn 1 ứng viên. Nếu chỉ còn một tập E01/E05 chưa có hash, file phim sẽ bị link làm tập đó.

**Tác động**: tập sai được link một cách im lặng. Tập đích bị đánh dấu có torrent và Available, nên Auto-Track **không** hunt tập thật nữa. Điều kiện: torrent thêm ngoài app (hoặc torrent của show/phim khác khi chạy scope theo show), và đúng một tập chưa có hash khớp số. Điều kiện này dễ xảy ra với show sắp xong mùa.

**Hướng sửa**: bỏ nhánh `ContainsNumberToken` (chỉ nhận `SxxEyy` hoặc `SelectedCandidateName`). Nếu muốn giữ cho anime thì dùng `TorrentCandidateParser.Parse(…, allowAnimeAbsolute: true)` và so S/E. Khi chạy scope theo show, đưa hash của **mọi** episode, season và movie đang track vào `usedHashes`. Với torrent được adopt, `LinkEpisodeCoreAsync` nên parse file kể cả khi chỉ có 1 file.

### LINK-03 — High — Đổi Episode Organization không gỡ link như hộp thoại hứa

**Code**
- `ViewModels/LibraryViewModel.cs:1606-1628`: hộp thoại xác nhận nói *"Hardlinks, torrent candidates, and pack links will be cleared."*, rồi chỉ gọi `SwitchEpisodeOrganizationAsync` và `ClearCart`.
- `Services/TrackedShowService.cs:86-117`: chỉ `DeleteTrackedSeasonsAndEpisodes` (`DatabaseService.cs:827-838`) và `DeleteFetchJobsForMedia`, rồi import lại. **Không** gọi `RemoveShowLinks`, không đụng `SourceItems`. Grep xác nhận đây là caller duy nhất.
- Hậu quả:
  - `TrackedShowService.cs:421-458`: availability so `(S,E)` cũ trong `SourceItems` với đánh số mới.
  - `SymlinkCoordinatorService.cs:681-735, 811-847`: với show dùng episode group, `WriteAndCleanupEpisodeGroupNfos` tra `TrackedEpisode` theo `(S,E)` **cũ** trong bảng đánh số **mới**, rồi `NfoWriterService.WriteEpisodeNfoIfNeeded` ghi `title/season/episode` của tập đó cạnh symlink.
  - Thông tin pack của `TrackedSeasons` (`PackTorrentHash`, `SelectedPackOwnerSeasonNumber`, `LastPackLinkTorrentHash`) bị xóa, nhưng hardlink và `AutoTorrentPackOwnerSeasonNumber` vẫn còn. Cleanup pack mất `TorrentHashes` (`SeasonPackCleanupMatcher.Resolve`) và chỉ còn khớp theo owner.

**Bằng chứng**: tĩnh, chuỗi gọi ở trên. DB thật có 2 show dùng episode group (Show AB, Show J), cả hai chưa có `SourceItem`, nên **chưa dính**. `drift.py` và truy vấn đối chiếu cũng không thấy `SourceItem` nào lệch key.

**Tác động**: sau khi đổi organization, các file đã link giữ tên `SxxEyy` theo thứ tự cũ, trong khi NFO (chính là nguồn sắp xếp của show dùng episode group) mô tả **tập khác**. Jellyfin hiển thị sai tập. Availability đánh dấu sai tập là có hoặc thiếu, và Auto-Track hunt nhầm. User được báo là link đã bị xóa nên không có lý do để kiểm tra.

**Hướng sửa**: trong luồng đổi organization, gọi `RemoveShowLinks` (hoặc reset cả show) **trước** `DeleteTrackedSeasonsAndEpisodes`, rồi xóa các `SourceItem` TV của show. Cách khác là sửa nội dung hộp thoại và chặn đổi organization khi show còn link.

### LINK-04 — High — Ổ download + library tạm không truy cập được thì reconcile xóa toàn bộ `SourceItems` của ổ

**Code**
- `Services/TorrentReconciliationService.cs:92-93`: mỗi lần reconcile gọi `RefreshLinkStatus` rồi `PurgeOrphanedSourceItemsForScope`.
- `Services/AutoTorrentLinkService.cs:292-311`: `File.Exists(LinkedPath) == false` thì đặt `LinkedPath = null`, `State = Parsed`, và xóa provenance của dòng không thuộc pack. Không kiểm tra root, ổ hay thư mục có tồn tại không.
- `TorrentReconciliationService.cs:154-161`: `!File.Exists(FilePath) && LinkedPath null` thì `DeleteSourceItem`.
- `HardlinkService.cs:88-93`: hardlink luôn cùng ổ với nguồn, nên khi một ổ mất thì **cả hai** điều kiện cùng đúng với mọi dòng của ổ đó.
- Auto-Track gọi reconcile theo lịch (`AutoTrackSchedulerService.cs:283` → `AutoTrackService.cs:669`), không cần user thao tác.

**Bằng chứng (harness D)**: 2 tập đã link và có symlink. Đổi tên `dl` và `lib` (giả lập ổ offline), chạy `ReconcileAsync(All)`: "rows before: 2" → "**rows after: 0**", reconcile chỉ báo `Missing=0`. Trả thư mục về chỗ cũ: hardlink và symlink vẫn còn trên đĩa, availability = Missing, và `LinkEpisodeAsync` báo **"Output already exists."** cho mọi tập.

Dữ liệu thật: 242/299 dòng nằm trên `E:` (`<DownloadRoot>\…` → `<LibraryRoot2>`), 56 dòng trên `D:`, symlink nằm ở `<UnifiedRoot>`. Trên máy này `E:` là NVMe nội bộ (Kingston NV3), nên xác suất thấp. Điều kiện kích hoạt: ổ rời hoặc ổ mạng, BitLocker chưa mở khóa, ổ chưa mount kịp lúc app khởi động, hoặc lỗi I/O tạm thời (`File.Exists` trả `false` với mọi lỗi).

**Tác động**: mất toàn bộ state link của một ổ: provenance pack, cờ orphan, mapping Gemini đã áp. App báo Missing trong khi Jellyfin vẫn phát được. Auto-Track thử link lại ở mỗi chu kỳ và luôn lỗi "Output already exists". Muốn phục hồi, user phải tự xóa thư mục Library/Jellyfin của show rồi link lại (gọi lại Gemini).

**Hướng sửa**: trước `RefreshLinkStatus`/`Purge`, kiểm tra root của ổ hoặc thư mục `SavePath`/`LibraryRoot` có tồn tại không. Nếu không thì bỏ qua các dòng của ổ đó. Không purge khi qBit báo torrent ở trạng thái `missingFiles`. Cân nhắc chỉ đánh dấu thay vì xóa.

### LINK-05 — Medium — Reset tập/phim bỏ qua lỗi gỡ link nhưng vẫn xóa `SourceItem`

**Code**
- `Services/AutoTorrentLinkService.cs:322-325`: `unlinkResult.ErrorCount` **không** được cộng vào kết quả (chỉ cộng `LinkedCount`, `SkippedCount`, `Messages`).
- Dòng 343-347: `DeleteAcceptedTvSourceItems` xóa mọi dòng theo key **bất kể** gỡ link thành công hay không. Trong khi đó `ResetSeasonPackForRedownloadAsync` (dòng 428-436) **có** dừng lại khi có lỗi.
- `ResetMovieForRedownload` (dòng 470-494) mắc đúng lỗi này.
- `ViewModels/LibraryViewModel.cs:762-765` bỏ qua kết quả và luôn báo "reset — episode is now Missing and ready to search".
- `HardlinkService.RemoveHardLink` trả `false` khi file đang mở (dòng 170-183), hoặc khi `LinkedPath` nằm ngoài root đang cấu hình (dòng 153-159, sau khi user đổi `DriveLibraryRoots` hay `DefaultLibraryFolderName`).

**Bằng chứng (harness C)**: mở file Library với `FileShare.Read` (giống Jellyfin đang phát), rồi bấm reset.
- Kết quả trả về `errors=0 removed=1`. `SourceItems` rỗng, nhưng hardlink `Season 01\Demo Show - S01E05.mkv` và symlink Jellyfin **vẫn còn**.
- Tải lại (torrent REPACK) rồi link: "**Output already exists.**", S01E05 vẫn Missing.

**Tác động**: Jellyfin giữ bản cũ mà app không còn biết tới (không dòng nào tham chiếu nên không bao giờ dọn). Bản tải lại không link được. Auto-Track thử link lại ở mỗi chu kỳ (tập có hash nhưng Missing) và luôn lỗi.

**Hướng sửa**: giống reset pack. Nếu `unlinkResult.ErrorCount > 0` thì dừng, không xóa dòng, không xóa hash, và báo lỗi lên UI. Cộng `ErrorCount` vào kết quả.

### LINK-06 — Medium — File Library bị xóa ngoài app: availability vẫn Available, symlink treo vĩnh viễn

**Code**
- `AutoTorrentLinkService.cs:292-311`: `RefreshLinkStatus` đặt `LinkedPath = null`, `State = Parsed`, nhưng **không** phát `HardlinkRemoved` và **giữ** `SymlinkPath`.
- `SymlinkSyncService.cs:183-185`: `ReconcileAll` chỉ xét `State == Linked`, nên dòng này không bao giờ được `RemoveItem`. Symlink trỏ vào file đã mất nằm lại mãi.
- `TrackedShowService.cs:447-457`: availability chỉ cần `MatchAccepted` và `State` khác `Deleted`/`Ignored`, **không** xét `LinkedPath`. Nguồn còn thì dòng không bị purge (`TorrentReconciliationService.cs:156-157`), nên tập vẫn **Available**.
- Không có đường tự link lại cho pack: `PackLinkCoordinatorService.TryAutoReconcilePackAsync` (dòng 87-92) chỉ chạy khi torrent vừa xong, và chỉ ở chế độ Inspect. `AutoTrackService.LinkReadyAutoTrackEpisodesAsync` (dòng 762-767) bỏ qua tập Available và tập không có hash (mọi tập trong mùa pack). Chỉ tập link từ torrent lẻ, có bật auto-link, mới được `RefreshSavedEpisodeHashesAsync` link lại (vì bước này chạy **trước** `RefreshLinkStatus`).

**Bằng chứng (harness E)**: link tập từ pack rồi tạo symlink. Xóa hardlink, chạy `ReconcileAsync(All)`: `state=Parsed LinkedPath=null SymlinkPath=jf\…S01E01.mkv`, availability **Available**. `ReconcileAll` báo "removed 0", symlink vẫn còn (`target exists=False`).

**Tác động**: Jellyfin thấy file hỏng (dangling), còn app báo đủ tập và không hunt hay link lại. Ghi chú trong code ở `TorrentReconciliationService.cs:90-91` ("allows episodes to transition from Available to Missing") chỉ đúng khi **cả** nguồn **và** link đều mất.

**Hướng sửa**: khi `RefreshLinkStatus` thấy link mất, phát `PublishHardlinkRemoved` (hoặc gọi `RemoveItem` symlink) và xóa `SymlinkPath`. `ReconcileAll` cũng xử lý dòng không ở trạng thái `Linked` mà còn `SymlinkPath`. Availability nên yêu cầu `State == Linked` (hoặc phân biệt "Downloaded" với "In library").

### LINK-07 — Medium — Hai `SourceItem` chung một `LinkedPath`: gỡ một dòng, symlink sống lại ở folder khác

**Code**
- `Services/MediaImportService.cs:37-39`: Preview import quét mọi file, **không** loại file nằm trong thư mục Library do app tự sinh, cũng không loại file đã có `SourceItem`.
- `Services/MediaImportService.cs:325-361` (`CommitTvItem`/`CommitMovieItem`): đặt `LinkedPath = FilePath` và `IsExternalImport = true`.
- `AutoTorrentLinkService.cs:247-256, 966-986`: `RemoveMovieLinks` (và `RemoveShowLinks`) lọc theo `ProviderId`. Dòng legacy không có `ProviderId` thì không bị gỡ. Dòng import (`LinkedPath == FilePath`) chỉ phát event rồi bị xóa.
- `SymlinkSyncService.cs:117-170`: symlink bị xóa cho cả nhóm cùng `LinkedPath`. Nhưng dòng legacy vẫn `State = Linked`, nên `ReconcileAll` (dòng 183-201) dựng lại symlink. Lần này `ApplyTrackedMetadata` (dòng 317-320) giữ nguyên dòng vì `ProviderId` null, nên đường symlink **không có** `[tmdbid-…]`.

**Bằng chứng**
- DB thật có **3 cặp** chung `LinkedPath` (đều là movie):

  | Phim | Dòng legacy (`ProviderId` NULL, `MatchAccepted` 0) | Dòng import (`FilePath` = file trong `…\MediaManagerLibrary\Movies\…`) |
  |---|---|---|
  | Movie One | 2 | 1396 |
  | Movie Two | 206 | 1395 |
  | Movie Three | 493 | 1382 |

- Harness G dựng lại đúng hình dạng cặp 206/1395 rồi gọi `RemoveMovieLinks`: symlink `Movie Two (2016) [tmdbid-000000]` bị xóa và dòng import bị xóa. `ReconcileAll` sau đó "created 1", tạo `jf\Movies\Movie Two (2016)\Movie Two (2016).mkv`, tức **phim đã gỡ quay lại Jellyfin** ở một folder mới.

**Tác động**: gỡ hay xóa phim (và show, nếu user import thư mục `MediaManagerLibrary\Shows`) không bền. Phim xuất hiện lại sau lần khởi động kế tiếp, dưới tên folder khác, và có thể bị trùng với bản gốc. Với TV, `RemoveSeasonPackLinks` xóa hardlink của dòng torrent, còn dòng import (`FilePath` là chính file đó) vẫn **Available** cho tới lần reconcile sau.

**Hướng sửa**: Preview import bỏ qua file nằm dưới mọi `LibraryRoot`/`UnifiedRoot` và file đã có `SourceItem` (theo `FilePath` hoặc `LinkedPath`). Khi gỡ link, xử lý cả nhóm cùng `LinkedPath`. Chạy migration một lần để gắn `ProviderId` cho các dòng legacy (hoặc xóa chúng).

### LINK-08 — Medium — TMDB đổi số tập: `TrackedEpisodes` cũ không bị xóa, link giữ S/E cũ

**Code**
- `Services/TrackedShowService.cs:68-84, 374-406`: `RefreshShowAsync` → `ImportShow` chỉ **upsert** từng mùa và tập.
- `DatabaseService.cs:1268-1297` (`ON CONFLICT … DO UPDATE`): không có lệnh xóa tập không còn trên TMDB. Hàm xóa duy nhất là `DeleteTrackedSeasonsAndEpisodes`, chỉ dùng khi đổi organization (LINK-03).
- `SourceItem.SeasonNumber/EpisodeNumber` được chốt lúc link (`AutoTorrentLinkService.cs:1175-1177`). Availability so theo key (`TrackedShowService.cs:429-438`).

**Kịch bản (tĩnh)**: TMDB chuyển S01E13 sang S02E01, chuyện hay gặp khi anime tách cour.
- Dòng S01E13 cũ vẫn còn, Available nhờ `SourceItem` cũ.
- Dòng S02E01 mới là Missing, nên Auto-Track hunt và tải lại cùng nội dung, rồi link `Season 02/…S02E01`.
- Jellyfin có hai bản. `Season 01` có thêm tập "ma" E13, mà metadata TMDB không còn. `EpisodeCount` của mùa (được cập nhật) cũng lệch với số dòng tập.

**Bằng chứng dữ liệu**: hiện không có mùa nào có số dòng tập khác `EpisodeCount`, và không `SourceItem` nào lệch key, nên **chưa dính**.

**Hướng sửa**: sau khi import, xóa (hoặc đánh dấu "removed upstream") các `TrackedEpisode` không còn trên TMDB. Nếu tập đó có `SourceItem` thì cảnh báo hoặc đưa vào màn review thay vì để im.

### LINK-09 — Low — Torrent lẻ và pack cùng tập nhưng khác đuôi tạo hai link

**Code**: `HardlinkService.cs:52`. Tên đích gồm `Path.GetExtension(item.FilePath)`, và chỉ chặn khi **đúng** đường đích đã tồn tại (dòng 95-100). `TryGetExistingLinkedItem` (`AutoTorrentLinkService.cs:939-951`) so theo `FilePath` nguồn, không theo `(show, S, E)`.

**Kịch bản**: S01E05 đã link từ torrent lẻ `.mkv`, sau đó link pack có bản `.mp4`. Kết quả là hai file `Show - S01E05.mkv` và `.mp4` trong cùng `Season 01`, hai symlink, hai `SourceItem` cùng key. `RemoveSeasonPackLinks` chỉ gỡ bản pack. Ngược lại, khi đuôi trùng thì pack báo lỗi "Output already exists" cho từng tập (không mất gì, nhưng log rất nhiễu). DB thật không có cặp nào như vậy.

**Hướng sửa**: trước khi tạo link, kiểm tra đã có `SourceItem` Linked cho `(ProviderId, S, E)` chưa. Nếu có thì bỏ qua với thông báo rõ, hoặc thay thế có chủ đích.

---

## Unproven review notes (chưa đủ chuẩn finding)

1. **`LinkedPath` cũ bị tái dùng, làm gỡ nhầm link của dòng khác.** Dòng A (pack) có `LinkedPath = L`, nhưng file L bị xóa ngoài app. Trước lần reconcile kế tiếp, dòng B (torrent lẻ, cùng tập) tạo lại đúng L (`CreateHardLink` thấy L không tồn tại). Sau đó "Cleanup/Unlink pack" chạy `RemoveHardLink(A)` (`HardlinkService.cs:162-176`), xóa L, tức xóa **link của B**. Thêm vào đó, symlink coordinator gom nhóm theo `LinkedPath` nên xóa luôn symlink của B. Code đúng như mô tả, nhưng cửa sổ thời gian hẹp và cần người dùng thao tác ngoài app.
2. **Thứ tự event symlink khi gỡ rồi link lại ngay.** `OnHardlinkRemoved`/`OnHardlinksRemoved` và `OnHardlinkCreated` mỗi cái chạy trong một `Task.Run` riêng (`SymlinkCoordinatorService.cs:136-212`). Nếu bước "created" lấy `_syncGate` trước "removed", thì "removed" sẽ xóa symlink mà bước "created" vừa xác nhận (cùng đường dẫn) và xóa `SymlinkPath` (`SymlinkSyncService.cs:164-167`). Hệ quả: tập có hardlink nhưng không có symlink cho tới lần khởi động sau. Chưa tái hiện.
3. **`FilePath UNIQUE` của SQLite phân biệt hoa thường** (`DatabaseService.cs:84`), trong khi app so `OrdinalIgnoreCase` ở mọi chỗ. Nếu qBit trả `SavePath` khác hoa thường (đổi category hoặc default path), và link cũ đã mất, sẽ có hai dòng cho cùng một file. `ON CONFLICT(FilePath)` sẽ không gộp chúng.
4. **Đổi tên hoặc năm show trên TMDB.** `ImportShow` cập nhật `Title`/`FirstAirYear` (`DatabaseService.cs:783-786`). Đường symlink lấy tên **hiện tại** (`SymlinkSyncService.cs:310-350`), còn hardlink giữ `MatchedTitle` cũ. Link mới vào folder mới ngay lập tức, nhưng symlink cũ chỉ dời khi khởi động hoặc Sync Now (`ReconcileAll`). Trong khoảng đó Jellyfin có hai folder cùng `[tmdbid]`. Ngoài ra, phụ đề và NFO cạnh symlink cũ không được dời (`CleanupDuplicateSymlinks` chỉ xóa symlink), nên folder cũ không bao giờ trống. DB thật không có lệch tên (`drift.py`).
5. **"Already linked" bỏ qua `MediaKind`/`ProviderId`** (`AutoTorrentLinkService.cs:939-951`). Một file đang là phim (ví dụ torrent Spinoff Movie), nếu bị adopt làm tập (LINK-02), thì tập đó **không bao giờ** có `SourceItem`. Đồng thời provenance của dòng phim bị ghi đè thành `AutoTorrentLinkKind.Episode`. Đây là hệ quả phụ của LINK-02 và PARSE-12, chưa tái hiện.
6. **Link mới ghi đè cả dòng cũ cùng `FilePath`.** `UpdateSourceItem` dùng `preserveLinkedState: false` (`DatabaseService.cs:189-265`), nên một link mới cho file đã có dòng (với `LinkedPath` mất) sẽ ghi `SymlinkPath = NULL`, `MappedSeason/Episode = NULL`, `IsExternalImport = 0`. Nếu đường symlink mới khác đường cũ (ví dụ do LINK-01 hoặc đổi tên show), symlink cũ mất dấu, không ai dọn.
7. **Bulk scope của `LibraryLinkEventHub` là toàn cục** (`LibraryLinkEventHub.cs:32-45`), không gắn với từng luồng. Trong lúc một lần link pack dài đang chạy (Gemini cộng vòng link), event của link tập lẻ chạy nền cũng bị gom vào batch và chỉ được xử lý khi pack xong, nên symlink và thông báo bị trễ. Chưa thấy sai dữ liệu.

## Những điểm đã kiểm và thấy ổn

- Thứ tự ưu tiên `Mapped*` rồi `Season/Episode` **nhất quán** giữa `HardlinkService.BuildOutputPath`, `GetOutputEpisodeKey` (AutoTorrentLink và TrackedShow), `SymlinkCoordinatorService.TryResolveShowAndEpisode`/`FormatEpisodeLabel` và `LibraryLinkBatchSummary`.
- `SeasonPackCleanupMatcher` không bắt dòng import (`IsExternalImport`). Đường legacy theo path chỉ bắt dòng không có provenance pack. Không thấy cleanup xóa link của torrent khác cùng path.
- `RemoveHardLink` từ chối xóa khi `LinkedPath == FilePath` hoặc nằm ngoài root, nên không xóa file gốc.
- Link pack chạy song song với Auto-Track không đụng nhau trên cùng file: tập trong mùa pack không có `TorrentHash`, còn `TryAutoReconcilePackAsync` chỉ chạy Inspect.
- DB thật: không có `SourceItem` nào lệch key so với `TrackedEpisodes`, không có cặp trùng `FilePath`. Có 3 cặp trùng `LinkedPath`/`SymlinkPath`, chính là LINK-07.
