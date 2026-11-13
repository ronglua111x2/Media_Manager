# Audit bảo mật: đường nhận torrent và an toàn nội dung (2026-11)

Nhánh: `claude/security-intake-b5e427`, tách từ `release/2026-11` @ `6ff1b22`.
Thang mức độ theo `docs/code-review/00-method-and-scope.md`. ID finding: `SEC-Axx`.
Phạm vi: từ kết quả search tới file trên đĩa và Library. Mối đe dọa chính là listing giả hàng loạt: torrent mang tên phim hoặc show nhưng chứa mã độc (exe, shortcut, script, ảnh đĩa, archive có mật khẩu, "codec installer", đuôi kép), cùng việc app vô tình chạy, lộ hoặc giữ lại các file đó.

Không thuộc báo cáo này (phiên khác audit song song): parse input không tin cậy ở indexer, network và HttpClient, WebView2, secret, local system.

> Repo đang public. Báo cáo không có tên show/phim thật hay đường dẫn máy. Tên ví dụ dạng `Show.NN.S01E01`, `Movie.NN`; thư mục dạng `<SavePath>`, `<tmp>`.

## 0. Tóm tắt

- **Không có Critical.** App không bao giờ tự mở, chạy hay giải nén nội dung torrent (mục 6.3), nên chưa có đường thực thi mã. Mức cao nhất là High.
- **3 High:**
  - **SEC-A01:** reconcile nhận torrent bất kỳ trong qBittorrent theo tên, không qua kiểm nội dung hay blacklist, rồi tự link.
  - **SEC-A02:** nhiều đường thoát của gate để torrent chưa kiểm vẫn chạy mà không để lại dấu.
  - **SEC-A03:** torrent chạy tải trước khi kiểm (AUD-001). Lỗi này giờ sửa được với chi phí nhỏ bằng `stopCondition=MetadataReceived`.
- **6 Medium:**
  - **SEC-A04:** danh sách đuôi nguy hiểm còn thiếu nhiều loại (ảnh đĩa, `.url`, `.library-ms`, ClickOnce, archive).
  - **SEC-A05:** chọn payload theo độ giống tên, nên một video mồi nhỏ che được payload lớn.
  - **SEC-A06:** nhận diện torrent vừa add bằng "hash mới đầu tiên", không khóa add.
  - **SEC-A07:** blacklist chỉ theo show, fail-open, và reconcile không đọc nó.
  - **SEC-A08:** listing và torrent thật không được đối chiếu (tên, size).
  - **SEC-A09:** toàn bộ lớp kiểm phụ thuộc `settings.json` mà không có cảnh báo.
- **5 Low:** SEC-A10 đến SEC-A14.
- **S4:** ngoài reconcile (SEC-A01), mọi đường add hay dùng lại torrent đều qua gate. `AutomationFlowService.RunNowAsync` vẫn là code chết.
- **Đề xuất sửa nhanh nhất mà hiệu quả nhất:**
  1. Add với `stopCondition=MetadataReceived`, rồi `torrents/start` sau khi kiểm (SEC-A03).
  2. Đặt priority 0 cho mọi file không thuộc allow-list (video, phụ đề, ảnh, nfo) thay vì chỉ chặn theo danh sách đen (SEC-A04, SEC-A05).
  3. Cho reconcile chạy cùng bộ kiểm file-list và blacklist hash toàn cục (SEC-A01, SEC-A07).

## 1. Cách làm

- Đọc code trên worktree, với 2 subagent đọc song song: một lập bản đồ mọi đường add/adopt, một lập bản đồ link, dọn dẹp và shell launch. Các điểm chính trong báo cáo của subagent đã được đọc lại trực tiếp.
- **Harness C#** trong scratchpad, ngoài repo. Nó tham chiếu `MediaManager.Core` và đưa danh sách file giả (chỉ tên và size, không có payload) vào `TorrentContentValidationService`. Harness cũng thử hành vi đường dẫn Windows (`CON.mkv`, dấu chấm/khoảng trắng cuối, `..`, đường tuyệt đối) trong thư mục tạm. Kết quả ở mục 5.
- **Đối chiếu mã nguồn qBittorrent** (`release-4.4.5`, `4.5.0`, `4.6.0`, `5.0.0`, `5.1.0`, `5.1.2`, `5.2.0`, `master`) và libtorrent `RC_2_0` cho S3 cùng việc làm sạch tên file.
- **Ước tính báo nhầm cho S2** trên fixture indexer có sẵn trong `MediaManager.Core.Tests/Fixtures/Indexers` (309 dòng). Máy không có file dump bench `SearchBench --dump`.
- **Không** thêm torrent vào qBittorrent, không tải file nào, không đụng state folder hay Library thật, không sửa code app.

## 2. Sơ đồ luồng và các lớp phòng thủ

```text
 Indexer (native / qBittorrent plugin)
   │  IndexerResult: title, size, seeders, hash/magnet/torrentUrl, Source ("tpb:vip", "nyaa:trusted", ...)
   ▼
 [L0] Search: IndexerResultNormalizer → CandidateEvaluation (recipe) → dedupe
   │   • Knaben virusDetection ≥ 0.5 bị loại (SearchOrchestrator.cs:331)
   │   • Blacklist theo URL/hash, chỉ của show này (FetchJobService.cs:1124, AutoTrackService.cs:1214)
   │   • Cờ tin cậy (vip/trusted/verified, IndexerTrust, IsDhtIndex) KHÔNG được dùng        ← S2 / SEC-A14
   ▼
 Cart (TorrentWorkspaceViewModel)                    Auto-Track hunt (AutoTrackService)
   │ user click                                         │ timer / Run now
   ▼                                                    ▼
 [L1] TorrentAddGateService.AddPausedValidateAndResumeAsync (ITorrentAddGateService.cs:91-252)
   │  1 blacklist(show, URL)      2 ResetTorrentGuard (reset-from hash)
   │  3a existing → ApplyManagedTorrentSettings (setLocation/category/tag)   ← DEL-02
   │  3b new → QbittorrentClient.AddTorrentAsync  Paused=false               ← SEC-A03 (AUD-001)
   │        ├ plugin: search/downloadTorrent → "hash mới đầu tiên"            ← SEC-A06
   │        └ URL: resolve HTML → thử lần lượt từng magnet/.torrent           ← SEC-A06/A08
   │  4 blacklist(show, URL, hash)  5 chờ file list ≤ 90 s (5..120)
   │  6 TorrentContentValidationService (Core)                                ← SEC-A04/A05/A09
   │      đuôi nguy hiểm (settings + AlwaysDangerous), đuôi kép, payload top-1/top-3 không phải media
   │  7 reject → TorrentCleanupService (xóa kèm file, verify, pause fail-safe) + blacklist(show)
   │  mọi exception khác / cancel → torrent vẫn chạy                          ← SEC-A02
   ▼
 qBittorrent tải tiếp (đã chạy từ bước 3)
   ▼
 [L2] Reconcile (TorrentReconciliationService) ── adopt theo TÊN mọi torrent trong qBittorrent ← SEC-A01
   ▼
 [L3] Link (AutoTorrentLinkService / PackLinkCoordinatorService)
   │  chỉ file IsVideoFile (.mkv .mp4 .avi .mov .wmv .m4v .ts .m2ts .webm), complete, không sample/proof
   │  nguồn = Path.Combine(SavePath, WebAPI name), không kiểm nằm trong SavePath   ← SEC-A11
   ▼
 Library (hardlink) → UnifiedRoot (symlink) + NFO do app sinh từ TMDB → Jellyfin quét
   • file không phải video ở lại thư mục tải (không link, không dọn)          ← SEC-A04/A05
   • "Open folder" mở thư mục tải gốc trong Explorer (MainViewModel.cs:373)   ← SEC-A04 (icon/NTLM)
```

Bảng các lớp phòng thủ:

| Lớp | Có | Thiếu |
|---|---|---|
| L0 search | Recipe, dedupe, ngưỡng virus của Knaben, blacklist theo show | Không dùng cờ tin cậy, không có từ khóa đáng ngờ, sàn size chỉ có khi recipe đặt `MinimumSizeBytes` |
| L1 gate | Kiểm đuôi, đuôi kép, payload chính, dọn có xác minh (AUD-002), reset guard | Torrent đã chạy; thiếu đuôi; mồi che payload; settings tắt được hết; không dọn khi exception hay cancel |
| L2 reconcile | Bỏ qua hash đang chờ kiểm (Auto-Track), bỏ qua hash đã reset | Không kiểm nội dung, không đọc blacklist |
| L3 link | Allow-list video, sample/proof, tên đích từ TMDB, `Sanitize` | Không kiểm nguồn nằm trong SavePath (cần qBittorrent trả tên xấu) |
| Sau link | Không mở hay chạy gì từ torrent | File lạ nằm lại thư mục tải vô thời hạn |

## 3. S4: mọi đường add hoặc dùng lại torrent

| # | Đường | Kích hoạt | Hành động trong qBittorrent | Gate (kiểm nội dung) | Blacklist | Reset guard |
|---|---|---|---|---|---|---|
| A | `TorrentWorkspaceViewModel.TryAddOrderToClientAsync` (`:1596`), gọi từ add cart `:516` và retry `:617` | User | Tìm bản có sẵn theo hash magnet (`:1605`) rồi hỏi Yes/No (`:1620`); nếu không có thì add | Có (`:1641`) | Có, trong gate | Có (`:1608`, trong gate) |
| B | `AutoTrackService.AddOrderToClientAsync` (`:1722`), gọi từ `:1241` | Timer, "Run now" | Như A nhưng dùng lại bản có sẵn **không hỏi** (`:1728`); lưu `PendingValidationHash` (`:1745-1749`) | Có | Có, lọc trước ở `:1214` | Có |
| C | `RecoverInterruptedOrdersOnceAsync` (`:1336`) → `RevalidateInterruptedOrderAsync` (`:1442`) | Hunt hoặc reconcile đầu tiên trong phiên | Tìm theo `PendingValidationHash` hoặc hash magnet (`:1411-1412`), kiểm lại | Có | Có | Có |
| D | `TorrentReconciliationService.AdoptManualTorrentsAsync` (`:423`), gọi từ `ReconcileAsync` `:85` | Reconcile nền (`AutoTrackService.cs:693`), "Reconcile existing" (`LibraryViewModel.cs:1205`, `TorrentWorkspaceViewModel.cs:744`) | Lấy **mọi** torrent, không lọc category hay tag; khớp tên với đúng 1 mục thì gắn và auto-link (`:455`, `:472`, `:495`) | **Không** | **Không** | Có (`WasResetFrom`) |
| D2 | Cùng service: `RefreshSaved*Hashes` (`:320-421`), `SyncOrdersFromTrackedItems` (`:508`) | Như D | Tin hash đã lưu; link khi xong | Không (hash đã qua A/B/C/D) | Không | — |
| E | `AutomationFlowService.RunNowAsync` (`:60`) → `AddTorrentAsync` (`:72`, `:82`) | **Không ai gọi.** Có đăng ký DI (`App.xaml.cs:330`), VM chỉ gọi `DryRunAsync` | Add thẳng | Không | Không | Không |

Kết luận S4: chỉ D (và D2 khi hash gốc đến từ D) bỏ qua gate (SEC-A01). E vẫn là code chết. Nên xóa `RunNowAsync` hoặc cho nó đi qua gate, để lần nối UI sau không mở lại lỗ (SEC-A14).

Không có đường nào gọi `ResumeTorrentsAsync`. `PauseTorrentsAsync` chỉ dùng làm fail-safe của AUD-002.

## 4. S3: kiểm danh sách file trước khi tải payload

### 4.1 Hiện trạng

- Gate add với `Paused = false` (`ITorrentAddGateService.cs:147`). `CreateAddTorrentContent` gửi cả `paused` và `stopped` (`QbittorrentClient.cs:968-970`); tài liệu `docs/qbittorrent-webapi/02`, `03` ghi "chỉ gửi `paused`" là đã cũ.
- Gate rồi chờ `torrents/files` có dữ liệu. Với qBittorrent 5.1, `filesAction()` trả mảng rỗng khi chưa có metadata, không báo lỗi (đã đọc `torrentscontroller.cpp`), nên vòng poll ở `:333-353` là đúng.
- Trong cả khoảng chờ đó torrent **đang tải**. Với `.torrent` thì tải ngay từ giây đầu. Với magnet thì tải ngay khi có metadata.

### 4.2 qBittorrent hỗ trợ gì (đã kiểm trên mã nguồn)

| Cơ chế | Phiên bản | Hành vi |
|---|---|---|
| `torrents/add` + `stopCondition=MetadataReceived` | **≥ 4.5.0** (có ở 4.5.0, 4.6.0, 5.0.0, 5.1.x; không có ở 4.4.5) | **`.torrent`:** `sessionimpl.cpp:2810-2817` (5.1.0) chuyển thành *add stopped* ngay, nên không tải byte payload nào.<br>**Magnet:** chạy để lấy metadata; khi có metadata thì `endReceivedMetadataHandling` (`torrentimpl.cpp:1851-1860`) đặt `paused` rồi `reload()`. |
| `torrents/add` + `stopped=true` | 5.x (4.x dùng `paused`) | Ổn với `.torrent`. Magnet bị dừng thì không lấy metadata, nên không dùng được cho magnet. |
| `torrents/fetchMetadata`, `torrents/parseMetadata`, add kèm metadata và file priority | **5.2.0** (WebAPI 2.15.1), changelog 2.11.9 | **Magnet:** lấy metadata mà **không add**. Bản tải metadata dùng `upload_mode` và thư mục tạm (`sessionimpl.cpp:3108-3119`, "avoid accidental file writes"), nên không ghi payload.<br>**`.torrent`:** `parseMetadata`. |
| Preference "Excluded file names" (`excluded_file_names_enabled`, `excluded_file_names`) | ≥ 4.5.0 | Tên khớp pattern được đặt priority *Ignored* lúc add `.torrent` (`sessionimpl.cpp:2861-2865`) và lúc nhận metadata magnet (`torrentimpl.cpp:1844`). Đây là setting của user trong qBittorrent, app không nên tự đổi. |
| `torrents/filePrio` (priority 0) | Mọi bản | Đặt "không tải" cho từng file sau khi có metadata. |
| `dlLimit` lúc add | Mọi bản | Giới hạn bytes/s. Có thể làm chậm cả việc lấy metadata magnet (trao đổi metadata đi qua cùng peer connection). Không khuyên dùng làm lớp chính. |

Với magnet và `stopCondition` vẫn còn một cửa sổ nhỏ. Chuỗi sự kiện là: alert metadata → `save_resume_data` (queued) → tìm file dở → `reload` paused. Tất cả đều bất đồng bộ, và trong lúc đó libtorrent đã có thể xin piece. Cửa sổ này chưa đo, ước chừng dưới vài giây. File rất nhỏ (một `.lnk` 2 KB nằm gọn trong piece đầu) có thể về kịp. Dù vậy thiệt hại vẫn nhỏ hơn hẳn hiện tại (90 s tải tự do), và gate vẫn xóa file khi từ chối.

Search plugin (`search/downloadTorrent`) không nhận `stopCondition`. Torrent do plugin add chạy theo mặc định của qBittorrent, lưu vào thư mục mặc định, rồi mới bị `setLocation`.

### 4.3 Đề xuất (chi tiết ở SEC-A03)

1. **Mọi bản ≥ 4.5:** add với `stopCondition=MetadataReceived` (không gửi `stopped=true`). Kiểm xong thì `torrents/start` (5.x) hoặc `resume` (4.x); `ResumeTorrentsAsync` đã có sẵn. Từ chối thì xóa như hiện tại.
2. **`.torrent` qua HTTP:** app đã tải và parse được bytes (`TorrentMetadataReader`, `ProbeTorrentMetadataAsync`). Kiểm file-list **trước khi add**, rồi add **đúng những bytes đó** (multipart `torrents`) để tránh TOCTOU giữa lần probe và lần add.
3. **qBittorrent ≥ 5.2:** dùng `fetchMetadata` cho magnet để kiểm trước khi add, sau đó add kèm metadata.
4. **Đường plugin:** chỉ dùng khi không còn cách nào khác. Sau khi add thì `stop` ngay theo hash đã đối chiếu (SEC-A06).
5. **Sau khi đạt:** đặt priority 0 cho mọi file ngoài allow-list (SEC-A04) **trước** khi start.

## 5. Kết quả harness

`TorrentContentValidationService` với cấu hình mặc định, trừ khi ghi khác. PASS nghĩa là torrent được nhận.

| Ca | File list (giả) | Kết quả |
|---|---|---|
| B2–B5 | `Setup.exe`, `SETUP.EXE` lồng trong `Extras/Codec/`, `*.mkv.exe`, `*.mkv.lnk` | REJECT (đúng) |
| R1–R3 | RLO `U+202E` trước `vkm.exe` / `vkm.scr`; RLI `U+2067` + `.lnk` | REJECT. Đuôi thật vẫn là `.exe`/`.scr`/`.lnk`; RLO chỉ đánh lừa mắt. Libtorrent còn lọc `U+202A–202E`, `U+200E/F` khỏi tên file. |
| T3 | `Readme.txt:x.exe` (ADS) | REJECT. Libtorrent trên Windows cũng thay `:` |
| T1, T2 | `Setup.exe ` (khoảng trắng cuối), `Setup.exe.` (chấm cuối) | **PASS.** Trên Windows, libtorrent cắt dấu chấm và khoảng trắng cuối nên tên về API đã sạch (SEC-A12) |
| X1–X3 | Video 2 GB + `Codec.Pack.iso` / `Player.img` / `Player.vhdx` 40 MB | **PASS** |
| X4–X9 | Video + `.url`, `.library-ms`, `.searchConnector-ms`, `.settingcontent-ms`, `.appref-ms`, `.application` | **PASS** |
| X10–X14 | Video + `.msp`, `.docm`, `.sct`, `.diagcab`, `.html`, `.svg` | **PASS** |
| X15 | Video + `Codec.zip`, `Subs.rar`, `Password.7z`, `PASSWORD.txt` | **PASS** |
| X16 | Video + `Setup` (PE không đuôi) | PASS. Không chạy được bằng double-click nếu không đổi tên |
| X17 | Chỉ một file `.wmv` 6 MB tên phim | PASS (kiểu "codec/DRM" cũ, xem SEC-A05) |
| P1 | `Movie.mkv` 50 KB (mồi) + `Movie.Disc.iso` 4 GB | **PASS.** Payload top-1 là file mồi vì giống tên |
| P2 | `Movie.mkv` 20 KB + `Movie.part1.rar` 2 GB | **PASS** |
| P3 | Chỉ `Movie.zip` 2 GB | REJECT (đúng) |
| P4, P5 | Pack 10 tập + `Bonus.Player.iso` 300 MB / `Codec.rar` 30 MB | **PASS.** Chỉ top-3 được kiểm |
| P6 | Pack toàn `.rar` | REJECT (đúng) |
| C1 | `settings.json` lưu `DangerousExtensions=[".scr"]` + `Setup.exe` | **PASS** |
| C2 | Danh sách chỉ có `.exe` + `.bat`, `.ps1`, `.vbs`, `.msi` | **PASS** |
| C3 | `EnableContentValidation=false` + `*.mkv.exe` | **PASS** |
| C4 | Tắt kiểm đuôi kép + `*.mkv.exe` | REJECT (vẫn bắt nhờ `.exe`) |
| U1 | `Setup.ехе` (chữ Cyrillic) | PASS. Windows không coi là exe, nên vô hại |

Thăm dò đường dẫn trên Windows 11 (thư mục `<tmp>`):

| Tên | Kết quả |
|---|---|
| `CON.mkv`, `NUL.mkv`, `COM1.mkv`, `AUX.mkv` | Tạo được qua `\\?\`. `File.Exists` đường thường = True. `GetFullPath` giữ nguyên tên. Windows 11 coi đây là file thường. |
| `Movie.mkv.`, `Movie.mkv ` | Tạo được qua `\\?\`, nhưng `File.Exists` đường thường = **False**; `GetFullPath` cắt về `Movie.mkv`; `IsVideoFile` = False. |
| `../outside.mkv`, `sub/../../outside.mkv` | `Path.Combine` + `GetFullPath` ra **ngoài** SavePath |
| `C:/Windows/x.mkv`, `/abs.mkv` | `Path.Combine` bỏ SavePath, ra đường tuyệt đối |

## 6. Findings

### SEC-A01 — High — Reconcile nhận torrent theo tên mà không kiểm nội dung hay blacklist, rồi tự link

**Vị trí.** `Services/TorrentReconciliationService.cs:423-505` (`AdoptManualTorrentsAsync`), `:76-79` (tập hash bị bỏ qua), `:85`. Kích hoạt từ `AutoTrackService.cs:693` (nền), `LibraryViewModel.cs:1205`, `TorrentWorkspaceViewModel.cs:744`.

**Hành vi.**
- Duyệt **mọi** torrent trong qBittorrent, không lọc category hay tag `media-manager`. Torrent nào khớp tên với đúng một tập/pack/movie chưa có hash thì được gắn (`UpdateTorrentState`) và auto-link (`AutoLinkEpisodeIfConfiguredAsync`, `TryAutoReconcilePackAsync`, `AutoLinkMovieIfConfiguredAsync`).
- Tập bỏ qua chỉ gồm `PendingValidationHash` và hash đã được theo dõi. Không gọi `ITorrentBlacklistService`, không gọi `TorrentContentValidationService`.

**Kịch bản.**
1. User tự add một listing giả trong qBittorrent (từ trình duyệt). Torrent có `Show.NN.S01E05.1080p.mkv` mồi cùng `Codec.Pack.iso` và `Setup.exe`. Reconcile nền gắn nó vào S01E05 và hardlink file video vào Library, Jellyfin quét. Không có cảnh báo, không blacklist. `Setup.exe` nằm lại thư mục tải.
2. Torrent cart bị hủy giữa lúc kiểm, hoặc gặp lỗi (SEC-A02), vẫn chạy và không có dấu vết. Lần reconcile sau nhận nó theo tên.
3. Torrent từ chối qua đường cart mà xóa thất bại (AUD-002): bị pause và blacklist theo show, nhưng order cart không có `PendingValidationHash`. Reconcile nhận lại nó cho chính tập đó, vì nó không đọc blacklist.

**Tác động.** Toàn bộ lớp L1 bị bỏ qua cho mọi torrent không do app add. Chỉ allow-list video ở L3 còn chặn, nên file nguy hiểm không vào Library. Tuy vậy torrent độc được "hợp thức hóa", tập hiện Available, và không ai nhắc user xóa phần còn lại.

**Độ tin cậy.** Cao, đọc code trực tiếp. Chưa chạy trên app thật.

**Sửa.**
- Trước khi adopt: lấy `torrents/files` (có sẵn, không tốn tải thêm) và chạy cùng `TorrentContentValidationService`, kiểm blacklist **theo hash toàn cục** (SEC-A07).
- Nếu bị từ chối: **không xóa** torrent của user (tránh lặp DEL-02). Chỉ không adopt, ghi log, và đánh dấu để UI cảnh báo.
- Khi bật auto-link cho torrent không có tag `media-manager`: cân nhắc chỉ adopt và chờ user xác nhận.
- Test: mục 7, T-A01.

### SEC-A02 — High — Gate thoát giữa chừng để lại torrent chưa kiểm vẫn chạy, nhiều khi không có dấu vết

**Vị trí.**
- `Services/ITorrentAddGateService.cs:137-252`. Không có `try/finally` quanh đoạn sau add.
- `ViewModels/TorrentWorkspaceViewModel.cs:1641` (không truyền `onTorrentAdded`), `:525-529` (cancel → Canceled), `:544-548` (`catch (Exception)` → Failed).
- `Services/AutoTrackService.cs:1287-1298` (`catch (Exception)` xóa `PendingValidationHash = string.Empty`).

**Hành vi.** Sau `AddTorrentAsync` (torrent đã chạy, SEC-A03), những lối ra sau **không** dừng hay xóa torrent:
- `OperationCanceledException` trong `WaitForTorrentFilesAsync` (user bấm dừng add cart, hoặc tắt app);
- lỗi HTTP hay timeout tạm thời của `GetTorrentFilesAsync` / `GetTorrentsAsync`;
- exception trong `ValidateFilesAsync` hoặc `onTorrentAdded` (lỗi DB khi `SaveOrder`).

Thêm vào đó:
- Đường cart (A) không lưu hash nào trước khi kiểm, nên sau crash hay cancel không còn gì để phục hồi.
- Đường Auto-Track (B) lưu hash, nhưng nhánh `catch (Exception)` xóa nó đi mà không xóa torrent.
- Recovery (C) chỉ chạy một lần mỗi phiên, và chỉ cho order Approved/Searching (`InterruptedOrderRecovery.cs:32-39`).

**Kịch bản.**
1. User add cart một listing giả. Trong 90 s chờ metadata, user bấm Stop. Order thành Canceled, torrent tiếp tục tải `Setup.exe` mãi, rồi bị reconcile nhận (SEC-A01).
2. Auto-Track add xong, qBittorrent WebUI chập chờn một nhịp. `GetTorrentFilesAsync` ném `HttpRequestException`, nhánh generic catch xóa `PendingValidationHash`. Torrent chưa kiểm vẫn chạy, và hash không còn che nó khỏi reconcile.
3. Đua với reconcile: trong lúc cart đang kiểm một bản *có sẵn và đã xong* (user chọn "manage"), reconcile chạy song song và adopt + hardlink nó. Nếu gate sau đó từ chối và xóa kèm file, hardlink trong Library vẫn còn, vì hardlink giữ dữ liệu.

**Sửa.**
- Trong gate: biến `addedByUs` cộng `finally`. Mọi lối ra không thành công mà torrent do gate add thì `stop` torrent; nếu lý do là từ chối thì xóa. Với SEC-A03 thì torrent vốn đã dừng, nên chỉ cần không start.
- Đường cart truyền `onTorrentAdded` để lưu `PendingValidationHash` như Auto-Track.
- Nhánh generic catch của Auto-Track giữ hash, hoặc chỉ xóa sau khi torrent đã dừng hay đã xóa.
- Cân nhắc cho recovery (C) chạy cả lúc reconcile thấy hash pending mồ côi, không chỉ một lần mỗi phiên.

### SEC-A03 — High — Torrent tải trước khi kiểm (AUD-001), giờ sửa được với chi phí nhỏ

**Vị trí.** `ITorrentAddGateService.cs:139-148` (`Paused = false`), `QbittorrentClient.cs:278-350`, `:960-971`.

**Hành vi.** Như AUD-001: payload tải trong lúc chờ metadata và kiểm (tới 120 s), và mãi mãi khi rơi vào SEC-A02. AUD-001 được chấp nhận vì "pause thì magnet không lấy metadata". Mục 4.2 cho thấy lý do đó không còn đúng: `stopCondition=MetadataReceived` có từ qBittorrent 4.5.0, và app đã hỗ trợ 5.1/5.2.

**Kịch bản.** Listing giả trên một indexer DHT. Add magnet; metadata về sau 3 s; file list có `Setup.exe` 3 MB cùng `Movie.mkv` 2 GB. Trong vài giây trước khi gate xóa, qBittorrent đã ghi `Setup.exe` và một phần video lên đĩa, đồng thời **seed ngược** các piece đó cho người khác (rủi ro pháp lý và băng thông). Với SEC-A02, việc này kéo dài vô hạn.

**Tác động.** File độc nằm trên đĩa, dù app không chạy nó. Windows Defender có thể bắt, có thể không. User có thể mở nó từ qBittorrent hay Explorer. Mức High giữ như AUD-001.

**Sửa.** Mục 4.3. Đề xuất mở lại AUD-001: đổi trạng thái từ "accepted trade-off" thành "fix available". Kèm theo, cập nhật `docs/qbittorrent-webapi/02-…` và `03-…` (đang ghi chỉ gửi `paused`).

### SEC-A04 — Medium — Danh sách đuôi nguy hiểm còn thiếu nhiều loại, và file lạ được giữ lại vô thời hạn

**Vị trí.** `MediaManager.Core/Services/TorrentContentValidationService.cs:11-16` (`PayloadIgnoreExtensions` có `.url`, `.m3u`, `.cue`), `:24-27` (`AlwaysDangerousExtensions`). `MediaManager.Core/Models/TorrentValidationConfig.cs` (`DangerousExtensions`; comment ghi "Archives are not listed").

**Hành vi (harness X1–X15, P4–P5).** Các đuôi sau không bị chặn khi đi kèm một video:
- ảnh đĩa: `.iso`, `.img`, `.vhd`, `.vhdx`. Double-click là mount, và file bên trong không mang Mark-of-the-Web.
- shortcut và file gây rò NTLM: `.url` (`IconFile=\\host\share` làm Explorer gửi hash NTLM chỉ khi *xem* thư mục), `.library-ms`, `.searchConnector-ms`, `.settingcontent-ms`.
- ClickOnce: `.application`, `.appref-ms`.
- `.msp`, `.mst`, `.cab`, `.diagcab`, `.wsc`, `.sct`, `.xll`, Office có macro (`.docm`, `.xlsm`, …).
- HTML smuggling: `.html`, `.htm`, `.svg`, `.mht`.
- mọi archive: `.zip`, `.rar`, `.7z`, kể cả khi đi cùng `PASSWORD.txt`.

L3 không link chúng vào Library (chỉ video). Nhưng gate cũng không dọn, nên chúng nằm trong thư mục tải tới khi user tự xóa torrent. Thư mục đó đúng là chỗ nút "Open folder" (`MainViewModel.cs:373`) mở trong Explorer.

**Kịch bản.** Listing giả "Movie.NN.2024.1080p" có `Movie.NN.2024.1080p.mkv` (video thật hoặc mồi) cùng `Watch in HD.url` (IconFile UNC) và `Codec.Pack.iso`. Gate cho qua, video được link, và hai file kia nằm trong `<SavePath>`. User bấm thẻ ổ để mở thư mục tải: Explorer hiển thị icon của `.url`, gửi NTLM ra ngoài. User double-click `.iso`, ổ được mount, bên trong là `Setup.exe` không có MotW.

**Sửa.** Hai lớp.
1. **Allow-list priority** (khuyên dùng, ít báo nhầm). Sau khi kiểm đạt và trước khi start (SEC-A03), gọi `torrents/filePrio` với priority 0 cho mọi file **không** thuộc: video (`TorrentFileRules.VideoExtensions`), phụ đề (`.srt .ass .ssa .sub .idx .sup .vtt`), `.nfo`, ảnh. File lạ sẽ không bao giờ được tải. Không cần chặn cả torrent vì một file `.zip` thừa.
2. **Mở rộng `AlwaysDangerousExtensions`** (chặn cả torrent):
   - các đuôi thực thi cốt lõi, hiện chỉ có trong list do settings lưu (SEC-A09): `.exe .scr .bat .cmd .com .pif .vbs .js .jar .ps1 .msi .msp .cpl .dll`;
   - ảnh đĩa: `.iso .img .vhd .vhdx`;
   - `.url .website .library-ms .searchConnector-ms .settingcontent-ms .application .appref-ms .xll .wsc .sct .diagcab`.

   Bỏ `.url` khỏi `PayloadIgnoreExtensions`. Commit `9db1e5e` cố ý giữ `.url` vô hại; báo cáo này đề xuất đổi, vì rủi ro NTLM khi chỉ xem thư mục. Archive thì xử lý bằng lớp 1, không chặn cứng.

### SEC-A05 — Medium — Video mồi che payload: payload được chọn theo độ giống tên, không có kiểm tỉ lệ size

**Vị trí.** `TorrentContentValidationService.cs:180-204` (`SelectPayloadFiles`: xếp theo Jaccard tên rồi mới tới size; lấy top-1, pack lấy top-3), `:137-178`.

**Hành vi (harness P1, P2, P4, P5).** Một `Movie.mkv` 20–50 KB trùng tên listing được chọn làm payload, còn `Movie.Disc.iso` 4 GB hay `*.part1.rar` 2 GB không được kiểm. Không có ngưỡng nào kiểu "video lớn nhất phải ≥ N MB" hay "byte không phải media ≤ X %". Với pack, file thứ 4 trở đi không được kiểm định dạng.

**Kịch bản.** Định dạng listing giả phổ biến: video mồi vài chục KB cùng một archive hoặc ISO lớn, kèm hướng dẫn "dùng player X / nhập mật khẩu ở trang Y". Gate cho qua. L3 cũng không có kiểm size, nên file mồi `.mkv` được link vào Library như một tập thật. Jellyfin hiện một tập hỏng, còn archive nằm lại thư mục tải.

**Sửa.**
- Tính trên toàn file list:
  - `nonMediaBytes / totalBytes` (media gồm video, phụ đề, ảnh, nfo): > 10 % hoặc > 50 MB thì từ chối, hoặc tối thiểu không tải phần đó (SEC-A04 lớp 1);
  - video lớn nhất < sàn theo loại (gợi ý: tập 60 MB, movie 300 MB, pack 60 MB mỗi tập) thì từ chối;
  - chọn payload theo **size trước**, độ giống tên chỉ để phá hòa.
- Cho pack: kiểm định dạng mọi file > 1 % tổng size, không chỉ top-3.
- `.wmv`/`.asf` nhỏ dưới sàn là dấu hiệu "codec/DRM prompt" cổ điển, và quy tắc sàn này bắt được luôn.

### SEC-A06 — Medium — Torrent vừa add được nhận diện bằng "hash mới đầu tiên"; không khóa add; resolver HTML thử lần lượt nhiều magnet

**Vị trí.**
- `QbittorrentClient.cs:305-319` (plugin → `WaitForAddedTorrentAsync(existingHashes, null)`), `:632-675` (`WaitForAddedTorrentAsync`, `TryResolveAddedTorrentAsync`).
- `:331-345` (vòng thử mọi `addSources`), `:1139-1165` (`ExtractTorrentSources`: mọi magnet và `.torrent` trong trang).
- Không có semaphore chung giữa cart và Auto-Track (`_huntLock` chỉ là của Auto-Track).

**Hành vi.**
- Với plugin (category `null`) và với qBittorrent 5.1 (response không có `added_torrent_ids`), torrent "của mình" là **torrent đầu tiên có hash chưa có trong snapshot**, không đối chiếu với hash mong đợi, kể cả khi hash đã biết từ magnet.
- Nếu một nguồn được qBittorrent nhận mà không sinh torrent mới (ví dụ đã có sẵn), vòng lặp thử **magnet tiếp theo trong trang chi tiết**. Đó có thể là "related torrents" hay quảng cáo, tức một torrent khác hẳn.

**Kịch bản.**
1. Cart add (A) và Auto-Track (B) chạy cùng lúc cùng category TV, trên qBittorrent 5.1. Mỗi bên có thể nhận nhầm torrent của bên kia. Torrent độc của bên này được kiểm như torrent của bên kia; một bên trả "đạt" cho torrent chưa kiểm.
2. User tự add một torrent trong qBittorrent đúng lúc plugin download đang chạy. Gate kiểm và có thể **xóa kèm file** torrent của user (giống DEL-02).
3. Trang chi tiết của một indexer HTML bị thay nội dung có hai magnet. Magnet đầu là bản đã có, nên app add magnet thứ hai, một torrent khác hoàn toàn với listing. Gate kiểm được nội dung, nhưng danh tính sai (SEC-A08).

**Sửa.**
- Tính hash mong đợi: từ magnet (`TorrentListingIdentity`) hoặc SHA-1 của info-dict trong bytes `.torrent` (thêm vào `TorrentMetadataReader`). Chỉ nhận torrent có đúng hash đó.
- Với 5.2 dùng `added_torrent_ids`.
- Resolver HTML: chỉ dùng nguồn đầu tiên. Nếu magnet trong trang khác hash listing thì dừng.
- Một `SemaphoreSlim` trong gate để tuần tự hóa add (giữ từ snapshot `existingHashes` tới khi nhận diện xong).

### SEC-A07 — Medium — Blacklist chỉ theo show, fail-open (AUD-006), reconcile không đọc; nên có blacklist hash toàn cục

**Vị trí.** `Services/ITorrentBlacklistService.cs:88-108`, `Services/DatabaseService.cs` (`IsTorrentBlacklisted`: `WHERE ShowId = @showId`). Người gọi: gate `:104`, `:171`; `AutoTrackService.cs:1214`; `FetchJobService.cs:1124`; thêm thủ công ở `TorrentWorkspaceViewModel.cs:950`.

**Hành vi.**
- Hash bị gate kết luận là malware cho show X vẫn được add lại cho show Y, cho movie, hay cho tập khác cùng show nếu khác `ShowId`. Movie và show còn dùng chung không gian `ShowId` (ghi chú cũ ở `03-findings-register.md`).
- Mỗi lần như vậy lại tải tối đa 120 s (SEC-A03).
- Lỗi DB thì `IsBlacklisted` trả `false` (AUD-006).
- Reconcile không kiểm blacklist (SEC-A01).

**Quan hệ với `ResetTorrentHashes` (migration 013).** Hai khái niệm tách đúng: reset nghĩa là "không gắn lại cho mục này", blacklist nghĩa là "độc, xóa kèm file". Gate kiểm blacklist URL trước, reset sau, rồi blacklist hash. Không thấy xung đột. Hai lưu ý:
- Gate xóa torrent đã reset nếu nó *mới được add lại* (`:156-163`). Đúng, vì đó là bản app vừa add.
- Bản có sẵn bị reset thì để yên. Đúng.

**Sửa.**
- Thêm phạm vi `Global` cho entry do gate kết luận malware (`ShowId = 0` hoặc cột `Scope`).
- `IsBlacklisted` kiểm `ShowId = @id OR Scope = Global`.
- Entry thủ công (`:950`, "không đúng nội dung") giữ theo show.
- Kiểm hash toàn cục **ở search** (magnet có hash) để loại trước khi add.
- Chuyển sang fail-closed cho kiểm hash toàn cục: lỗi DB thì từ chối add và báo lỗi (AUD-006).
- `ResetTorrentHashes` giữ nguyên, không trộn.

### SEC-A08 — Medium — Không đối chiếu listing với torrent thật (tên, size)

**Vị trí.** Gate (`ITorrentAddGateService.cs:137-252`) không so `addedTorrent.Name`/`TotalSize` với `order.SelectedCandidateName`/size của listing. `ProbeTorrentMetadataAsync` (`QbittorrentClient.cs:231-276`) chỉ được `FetchJobService` dùng để khớp năm, tập và mùa, và tải lại riêng so với lần add (TOCTOU).

**Hành vi.** Listing ghi 2,1 GB "Show.NN.S01E05.1080p", torrent thật có thể là 40 MB tên khác hẳn, và gate vẫn cho qua nếu đuôi sạch. Đây là dấu hiệu mạnh nhất của listing giả trên indexer HTML hay DHT (title và size do site hoặc kẻ upload tự khai).

**Sửa.** Sau khi có metadata:
- `|torrentSize − listingSize| / listingSize > 20 %` thì đánh dấu: Auto-Track từ chối, cart hỏi user;
- tên torrent hoặc tên video lớn nhất không qua `CandidateEvaluation` của recipe thì từ chối như một candidate không khớp, không blacklist.

### SEC-A09 — Medium — Toàn bộ lớp kiểm phụ thuộc `settings.json`, không có cảnh báo

**Vị trí.** `Models/TorrentValidationConfig.cs`, `Services/SettingsService.cs:75` (System.Text.Json thay cả list khi deserialize), `ITorrentAddGateService.cs:187-194`. Không có UI cho mục này.

**Hành vi (harness C1–C3).**
- `EnableContentValidation=false` tắt hết kiểm file; gate chỉ log Info.
- `DangerousExtensions` được lưu xuống `settings.json`, nên danh sách cũ, sửa tay hay bị một công cụ khác ghi đè (bỏ `.exe`) đều được dùng nguyên.
- Mặc định mới không bao giờ tới máy đã có `settings.json`. Đó chính là lý do `AlwaysDangerousExtensions` ra đời, nhưng list này chỉ có 13 đuôi và thiếu `.exe`.

**Sửa.**
- Đưa mọi đuôi thực thi vào `AlwaysDangerousExtensions` (SEC-A04); settings chỉ được **thêm**, không được bớt.
- Khi `EnableContentValidation=false`: log Warning mỗi lần add và hiện trạng thái trong UI (pill hoặc Settings).
- Cân nhắc bỏ hẳn công tắc tắt.

### SEC-A10 — Low — `AutomationFlowService.RunNowAsync` add thẳng, không qua gate (code chết)

**Vị trí.** `Services/AutomationFlowService.cs:60-90`, đăng ký DI ở `App.xaml.cs:330`. Không có người gọi (đã grep `.cs` và `.xaml`).

**Sửa.** Xóa `RunNowAsync` và `CreateAddTorrentRequest`, hoặc cho chúng gọi `ITorrentAddGateService`. Thêm một test kiến trúc để chỉ `TorrentAddGateService` được gọi `IQbittorrentClient.AddTorrentAsync`.

### SEC-A11 — Low — Đường nguồn khi link không được giữ trong SavePath

**Vị trí.** `Services/AutoTorrentLinkService.cs:1975` (`Path.Combine(torrent.SavePath, file.Name.Replace('/', '\\'))`). `HardlinkService.cs:116`, `:125-127` dùng thẳng đường này.

**Hành vi (harness).** Tên `../x.mkv` hay `C:/x.mkv` cho ra đường ngoài SavePath. Hiện tại libtorrent đã làm sạch tên (lọc `/` `\` trong một phần tử, bỏ `..`, thay `:` trên Windows), nên qBittorrent bình thường không trả tên như vậy. Chỉ khai thác được khi WebUI qBittorrent bị giả hoặc bị chiếm (thuộc phạm vi phiên network). Khi đó app có thể hardlink bất kỳ file đuôi video nào cùng ổ vào Library: lộ dữ liệu sang Jellyfin, không thực thi.

**Sửa.** Sau `Path.Combine`: `GetFullPath` rồi kiểm `StartsWith(GetFullPath(SavePath) + '\\')`; không đạt thì bỏ file và log. Bổ sung kiểm reparse point của file nguồn.

### SEC-A12 — Low — Bộ kiểm không chuẩn hóa tên trước khi lấy đuôi

**Vị trí.** `TorrentContentValidationService.cs:98-103`, `:150-152`.

**Hành vi (harness T1, T2).** `Setup.exe ` và `Setup.exe.` cho `GetExtension` = `.exe ` / rỗng, nên PASS. Trên Windows, libtorrent cắt các ký tự đó trước khi tới API, nên không khai thác được với qBittorrent Windows. Với qBittorrent chạy Linux hoặc NAS mà thư mục tải chia sẻ sang Windows, tên giữ nguyên trên đĩa, còn Win32 cắt khi mở, nên file chạy như `.exe`.

**Sửa.**
- Trước khi kiểm: `TrimEnd(' ', '.')`, chuẩn hóa NFC, bỏ ký tự điều khiển hướng (`U+202A–202E`, `U+2066–2069`, `U+200E/F`, `U+061C`).
- Nếu tên có các ký tự hướng đó thì tự nó đã là dấu hiệu, nên đánh dấu High.

### SEC-A13 — Low — Tên thiết bị và tên dài trong file extras

**Vị trí.** `HardlinkService.cs:40-53` (orphan extras dùng `SanitizeFileName(item.FileName)`), `:401-409`, `:472`, `:518-532` (`\\?\`).

**Hành vi (harness).** Trên Windows 11, `CON.mkv` là file thường và đi trót lọt qua `\\?\`. Trên Windows 10 trở về trước, tên này được hiểu là thiết bị khi gọi bằng đường thường (`File.Exists` ở `:116`), nên link thất bại có kiểm soát. Tên trên 255 ký tự thì `CreateHardLinkW` lỗi và lỗi được xử lý (`:128-133`). `Sanitize` không xử lý tên thiết bị. Một file `CON.mkv` lọt vào Library thì Jellyfin và công cụ cũ có thể không mở hay xóa được.

**Sửa.** `Sanitize`/`SanitizeFileName`: nếu stem trùng `CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³]` thì thêm `_`; cắt độ dài phần tử còn khoảng 200.

### SEC-A14 — Low — Search không dùng cờ tin cậy và dấu hiệu listing giả (S2)

**Vị trí.**
- Cờ đã đọc nhưng chỉ đi vào `SiteUrl`: `ApibayIndexer.cs:70` (`tpb:vip|trusted|member`), `NyaaIndexer.cs:59-60` (`nyaa:trusted`), `BitsearchIndexer.cs:74` (`bitsearch:verified`).
- `IndexerTrust` và `IsDhtIndex` khai báo trong `IndexerModels.cs:16-31`, đặt cho Bt4g (`Trust: Low`) và Bitsearch (`IsDhtIndex`), nhưng không ai đọc.
- `SearchOrchestrator.cs:331` chỉ lọc `VirusProbability ≥ 0.5`.

Đề xuất heuristic, và ước tính báo nhầm trên 309 dòng fixture thật (apibay 77, eztv 200, knaben 20, torrents-csv 10, yts 2). Fixture không có mẫu độc, nên **không đo được tỉ lệ bắt đúng**. Số dưới đây chỉ là chặn trên báo nhầm.

| # | Heuristic | Hành động | Dòng fixture dính | Báo nhầm ước tính |
|---|---|---|---|---|
| H1 | Title chứa token `.exe .scr .lnk .iso .zip .rar`, `password`, `codec`, `keygen`, `crack`, `setup`, `installer`, `player required`, `watch online` | Loại ở search | 0/309 | 0 % (chặn trên 95 % theo "rule of three" ≈ 1 %) |
| H2 | Size dưới sàn theo loại (tập < 60 MB; movie/pack < 300 MB), trừ khi recipe đặt `MinimumSizeBytes` thấp hơn | Loại | 18/309 (5,8 %), toàn dòng không có SxxEyy, 1–250 MB | Phần lớn là nhạc phim, sách, sample. Với recipe video, không phải báo nhầm thật; cần kiểm trên bench TV/anime trước khi bật cho anime 12–24 phút (một tập 480p có thể < 100 MB, nên sàn tập để 60 MB) |
| H3 | `seeders ≥ 500 && leechers == 0` (seed bơm) | Hạ điểm | 0/309 | 0 % |
| H4 | Nguồn `Trust.Low`/DHT (bt4g, bitsearch không verified) mà **không** có indexer khác cùng hash | Hạ điểm mạnh; Auto-Track không chọn | Chưa đo (fixture không có cột cross-indexer) | — |
| H5 | TPB `member` (không vip hay trusted) với seeders < 5 | Hạ điểm | 9/77 dòng apibay là `member`; 68/77 (88 %) là vip hoặc trusted | Chỉ hạ điểm, không loại, nên không mất kết quả |
| H6 | Ưu tiên: `nyaa:trusted`, `bitsearch:verified`, TPB vip/trusted, cùng hash trên ≥ 2 indexer | Cộng điểm | — | — |
| H7 | Knaben `virusDetection` 0,15–0,5 | Hạ điểm (hiện chỉ loại từ 0,5) | 4/20 dòng knaben ở 0,17–0,34 | Chỉ hạ điểm |

Khuyến nghị: bật H1 và H3 ngay (rẻ, không thấy báo nhầm). Bật H2 theo loại media sau khi chạy `SearchBench` với `--dump` cho cả TV và anime. Đưa H4–H7 vào điểm xếp hạng, không loại cứng. Thêm lệnh bench đếm tỉ lệ dòng bị H1/H2 loại trên dữ liệu thật.

## 7. Đã kiểm, không thấy lỗ (ghi để khỏi kiểm lại)

- **RLO và đuôi kép:** đuôi thật luôn là phần sau dấu chấm cuối, nên bộ kiểm bắt được (R1–R3, B4). Libtorrent còn lọc `U+202A–202E`, `U+200E/F`. Các ký tự isolate (`U+2066–2069`) không đảo thứ tự chữ Latin.
- **Hoa thường:** so sánh `OrdinalIgnoreCase` (B3).
- **File lồng thư mục:** `Path.GetFileName` xử lý `/` (B3).
- **ADS `:`:** bị bắt (T3), và libtorrent Windows thay `:`.
- **Chỉ video được link:** `AutoTorrentLinkService.cs:1797`, `PackLinkCoordinatorService.cs:116`, `TorrentFileRules.cs:13-14`, `ScannerService.cs:8-11`.
  - Phụ đề, NFO và ảnh từ torrent không bao giờ được copy hay link.
  - NFO do app sinh từ TMDB, escape qua `XmlWriter` (`NfoWriterService.cs:237-257`).
  - Tên đích trong Library lấy từ TMDB, trừ file extras (SEC-A13).
- **Mở hay chạy file:** không có `Process.Start` hay `file://` nào nhận đường từ torrent. `ShellLaunchGuard` chỉ mở thư mục tải gốc trong settings và URL Google Drive. Viewer WebView2 chỉ mở http/https (phần WebView2 do phiên khác audit).
- **`BundleExtractionGuard`:** chỉ giữ handle DLL của app trong `%TEMP%\.net`. Không liên quan torrent.
- **Xóa nội dung torrent:** chỉ qua API qBittorrent (`TorrentCleanupService`), dùng token timeout riêng nên cancel của user không cắt ngang việc xóa đã bắt đầu. App không tự `File.Delete` theo tên file torrent.
- **Symlink trong torrent v2 (BEP 47):** libtorrent chỉ tạo symlink trên POSIX (`TORRENT_HAS_SYMLINK` không bật cho Windows) và `sanitize_symlinks` giữ đích trong torrent.
- **Danh sách file đổi sau khi kiểm:** với một infohash cố định, info-dict bất biến. Tên trả về API đã bỏ `.!qB`. Không cần kiểm lại khi danh sách file "đổi". Rủi ro thật nằm ở SEC-A02 (kiểm không xong) và SEC-A01 (không kiểm).

## 8. Ghi chú, rủi ro còn lại (không phải finding)

- **Jellyfin và Explorer phân tích file video không tin cậy.** App kích Jellyfin refresh, Jellyfin chạy ffprobe trên mọi video vừa link. Explorer tạo thumbnail khi mở thư mục tải. Một video được chế để khai thác parser là rủi ro tồn dư; giảm bằng SEC-A05 (sàn size) và SEC-A08.
- **`.m3u`, `.cue` trong `PayloadIgnoreExtensions`.** Playlist có thể trỏ tới URL hay UNC khi user mở bằng player. Xử lý bằng lớp allow-list priority ở SEC-A04 (không có trong allow-list thì không tải).
- **DEL-02 (đã có trong register).** Bổ sung từ audit này: ở đường B và C, bản có sẵn được dùng lại **không hỏi**, và bị `setLocation`, đổi category và tag (`ITorrentAddGateService.cs:125-130`) **trước** khi kiểm. Từ chối thì xóa kèm file. Sửa DEL-02 nên gồm: không đổi vị trí trước khi kiểm, và không xóa bản không có tag `media-manager`.
- **Magnet chỉ có `btmh` (v2-only).** `TorrentListingIdentity` chỉ đọc `btih`, nên blacklist trước khi add không bắt được theo hash. Hiếm gặp.
- **Metadata timeout.** Gate xóa mà không blacklist (`:197-209`), nên Auto-Track có thể thử lại cùng listing nhiều lần. Lúc chưa có metadata thì chưa tải được payload, nên chỉ tốn thời gian.

## 9. Test nên có

| ID | Test | Lớp |
|---|---|---|
| T-A01 | Reconcile gặp torrent không tag, khớp tên, file list có `Setup.exe` → không adopt, không link, không xóa; có cảnh báo | App, `TorrentReconciliationService` + fake qBittorrent |
| T-A01b | Reconcile gặp hash nằm trong blacklist toàn cục → không adopt | App |
| T-A02a | Gate: cancel trong `WaitForTorrentFilesAsync` → torrent do gate add bị stop hoặc delete (fake ghi lại lệnh) | App, `CleanupFakes` |
| T-A02b | Gate: `GetTorrentFilesAsync` ném `HttpRequestException` → torrent bị stop; Auto-Track giữ `PendingValidationHash` | App |
| T-A02c | Cart add lưu `PendingValidationHash` trước khi kiểm (giống `TorrentAddGatePendingHashTests`) | App |
| T-A03a | `CreateAddTorrentContent` có `stopCondition=MetadataReceived`; sau khi kiểm đạt thì gọi start/resume đúng một lần; từ chối thì không start | App |
| T-A03b | `.torrent` qua HTTP: bytes đã kiểm chính là bytes được POST (multipart `torrents`) | App |
| T-A04 | Theory: mỗi đuôi ở SEC-A04 kèm video → REJECT, hoặc nằm trong danh sách đặt priority 0; `.url` không còn bị bỏ qua | Core, như `AlwaysDangerousExtensionsTests` |
| T-A04b | Allow-list priority: file list hỗn hợp → đúng tập file nhận priority 0 | App |
| T-A05 | P1, P2, P4, P5 của harness → REJECT; một pack thật (10 tập + nfo + ảnh + phụ đề) → PASS | Core |
| T-A06a | `WaitForAddedTorrentAsync` có hai hash mới → chọn đúng hash mong đợi | App |
| T-A06b | Resolver HTML có hai magnet, magnet đầu "đã có" → không add magnet thứ hai | App |
| T-A07 | Blacklist malware của show X chặn cùng hash cho show Y và movie; lỗi DB → add bị từ chối (fail-closed) | App, DB tạm |
| T-A08 | Listing 2 GB nhưng torrent 40 MB → Auto-Track từ chối | App |
| T-A09 | `settings.json` với `DangerousExtensions=[]` vẫn chặn `.exe`; `EnableContentValidation=false` sinh Warning | Core + App |
| T-A10 | Test kiến trúc: chỉ `TorrentAddGateService` được gọi `IQbittorrentClient.AddTorrentAsync` | App |
| T-A11 | Tên file `../x.mkv`, `C:/x.mkv` từ fake qBittorrent → không được link | App, `LinkTestHost` |
| T-A12 | `Setup.exe `, `Setup.exe.`, tên có `U+2066` → REJECT | Core |
| T-A13 | Extras tên `CON.mkv` → đích `CON_.mkv` | App, `HardlinkService` |
| T-A14 | H1/H3 loại dòng chứa `.exe`/`password`; không loại dòng nào trong fixture hiện có | Core, `SearchOrchestrator` |

## 10. Thứ tự sửa đề xuất

Đánh số theo đợt. Mỗi đợt nên là một PR, viết test tái hiện trước.

1. **Đợt S0 — Torrent không bao giờ chạy trước khi kiểm (SEC-A03, SEC-A02).**
   - Add với `stopCondition=MetadataReceived`, start sau khi đạt.
   - `finally` trong gate để stop hoặc xóa khi thoát giữa chừng.
   - Cart lưu `PendingValidationHash`; nhánh generic catch của Auto-Track giữ hash.
   - Cập nhật `docs/qbittorrent-webapi/` và đổi AUD-001 thành "fix available".
   - Đây là sửa nhỏ, chạm `ITorrentAddGateService.cs`, `QbittorrentClient.cs`, `AutoTrackService.cs`, `TorrentWorkspaceViewModel.cs`.
2. **Đợt S1 — Reconcile qua cùng bộ kiểm (SEC-A01) và blacklist hash toàn cục (SEC-A07).**
   - Làm cùng PR, vì reconcile cần blacklist toàn cục.
   - Nên phối hợp với PR sửa DEL-02 (cụm C9), cùng triết lý "không xóa torrent không do app add".
3. **Đợt S2 — Bộ kiểm nội dung (SEC-A04, SEC-A05, SEC-A09, SEC-A12).**
   - Allow-list priority, mở rộng `AlwaysDangerousExtensions`, tỉ lệ byte và sàn size, chuẩn hóa tên.
   - Chỉ chạm Core (`TorrentContentValidationService`, `TorrentValidationConfig`) cộng một lệnh `filePrio` trong gate.
4. **Đợt S3 — Danh tính torrent (SEC-A06, SEC-A08).** Đối chiếu hash mong đợi, khóa add, resolver HTML một nguồn, so size và tên. Chạm `QbittorrentClient.cs`: làm sau S0 để tránh đụng nhau.
5. **Đợt S4 — Search (SEC-A14, H1, H3 trước) và dọn (SEC-A10, SEC-A11, SEC-A13).**

Đợt S0 và S1 nên xong trước khi phát hành `release/2026-11` ra `main`, vì mỗi lần push lên `main` là một bản public (BLD-04).

## 11. Bảng finding

| ID | Mức | Tóm tắt | Liên quan |
|---|---|---|---|
| SEC-A01 | High | Reconcile adopt và link torrent bất kỳ theo tên, không kiểm nội dung hay blacklist | S4, DEL-02 |
| SEC-A02 | High | Cancel hay exception sau add để torrent chưa kiểm chạy tiếp; cart không lưu hash pending | AUD-001 |
| SEC-A03 | High | Torrent tải trước khi kiểm; `stopCondition=MetadataReceived` (≥ 4.5) và `fetchMetadata` (≥ 5.2) sửa được | AUD-001, S3 |
| SEC-A04 | Medium | Thiếu đuôi nguy hiểm (ảnh đĩa, `.url`, `.library-ms`, ClickOnce, archive…); file lạ nằm lại thư mục tải | S1 |
| SEC-A05 | Medium | Video mồi che payload lớn; không có tỉ lệ byte hay sàn size | S1 |
| SEC-A06 | Medium | Nhận diện torrent mới bằng "hash mới đầu tiên"; không khóa add; resolver HTML thử nhiều magnet | — |
| SEC-A07 | Medium | Blacklist theo show, fail-open, không dùng ở reconcile; cần phạm vi toàn cục | AUD-006 |
| SEC-A08 | Medium | Không so tên và size listing với torrent thật | S2 |
| SEC-A09 | Medium | Tắt hay bớt kiểm qua `settings.json` mà không cảnh báo; mặc định mới không tới máy cũ | S1 |
| SEC-A10 | Low | `AutomationFlowService.RunNowAsync` add không qua gate (code chết) | S4 |
| SEC-A11 | Low | Đường nguồn khi link không được giữ trong SavePath | Path traversal |
| SEC-A12 | Low | Không chuẩn hóa tên (chấm hay khoảng trắng cuối, ký tự hướng) trước khi lấy đuôi | S1 |
| SEC-A13 | Low | Tên thiết bị hay tên dài ở file extras | Path traversal |
| SEC-A14 | Low | Search không dùng cờ tin cậy hay dấu hiệu listing giả; đề xuất H1–H7 | S2 |

Tổng: 0 Critical, 3 High, 6 Medium, 5 Low.
