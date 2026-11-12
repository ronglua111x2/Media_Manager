# 07c — Audit phần search native (nhánh `feat/native-search`)

- **Baseline:** commit `a106389` trên `feat/native-search`, ngày 2026-11-12, cùng bản sửa TorrentDownloads ghi ở mục 5.
- **Phạm vi:** toàn bộ code mới của nhánh (`git diff main...HEAD`), chia 4 mảng:
  - **G — governor và HTTP:** `IndexerRequestGovernor`, `GovernedIndexerHttp`, `SearchOrchestrator`, `NativeIndexerCatalog`.
  - **P — parser indexer:** 16 file `Search/Indexers/*`, `SearchIdentity`, `CandidateDeduplicator`.
  - **A — tích hợp app:** `NativeSearchService`, các đường FetchJob, snapshot, dry-run và Auto-Track, Settings, recipe, cart picker.
  - **M — luật khớp và query:** `TitleNormalizer`, `YearMatcher`, `CandidateEvaluationService`, `JunkFilter`, `NativeQueryText`, `SearchPlanBuilder`.
- **Phương pháp:**
  - Bốn reviewer độc lập, mỗi người một mảng. Họ không được biết kết luận của người viết code.
  - Reviewer mảng M dựng một app console nhỏ biên dịch từ `MediaManager.Core` để chạy lại từng ca.
  - Người viết kiểm chứng lại các finding chính trên code. Cột "Kiểm chứng" ở bảng tóm tắt ghi mức kiểm chứng của từng finding:
    - **K**: người viết đã đọc lại đường code và xác nhận.
    - **T**: reviewer đã chạy lại ca lỗi bằng code thật.
    - **R**: reviewer đọc code, chưa ai chạy lại.
- **Không làm:** không gửi request nào tới site torrent. Không build vào thư mục `bin` của app.
- **ID:** `NS-xx`. Mức độ gồm **Cao** (nhận sai torrent, dừng cả lượt chạy, hoặc có nguy cơ bị ban IP), **Trung bình** (mất kết quả hoặc hỏng một đường chạy), và **Thấp**.

> **Kết luận chính.** Native nhanh hơn và cho nhiều bản tốt hơn qBittorrent ở movie (plan 08, lần đo 2). Tuy vậy còn **4 lỗi mức Cao** cần sửa trước khi để Native hoặc Both chạy tự động:
> 1. Hai lỗi khớp movie nhận nhầm phim khác cùng franchise (NS-01, NS-02).
> 2. TMDB bị chặn làm dừng cả lượt cart hoặc Auto-Track (NS-03).
> 3. Breaker không bao giờ mở khi site trả trang lạ mà vẫn báo 200, nên Auto-Track có thể gọi một site đang chặn mình hàng trăm lần mỗi ngày (NS-04).
>
> Ngoài ra, số đo hiện chỉ có **movie**. TV, anime và season pack là phần dùng nhiều nhất (Common Anime dùng cho 15 show, Common Anime Pack cho 16, Default TV Pack cho 15) nhưng chưa được đo theo recipe (NS-31).

---

## 1. Bảng tóm tắt

| ID | Mức | Mảng | Vấn đề | Kiểm chứng | Trạng thái |
|---|---|---|---|---|---|
| NS-01 | Cao | M | Movie lệch năm ±1 mà tên khớp lỏng thì nhận nhầm phần khác của cùng franchise | K, T | Đã sửa |
| NS-02 | Cao | M | Bản không có năm nhận nhầm phần tiếp theo (Dune Part Two, Frozen II); số "5.1" của audio được tính là số trong tên | K, T | Đã sửa |
| NS-03 | Cao | A | TMDB timeout khi tra IMDb id làm dừng cả lượt cart hoặc Auto-Track như thể user bấm Hủy | K | Đã sửa |
| NS-04 | Cao | G | Lỗi parse (trang lạ trả 200) không bao giờ mở breaker | K | Đã sửa |
| NS-05 | TB | G | Retry-After: giới hạn 30 phút, giá trị 0 hoặc thời điểm đã qua làm mất cooldown, 503 bị bỏ qua | K | Chưa |
| NS-06 | TB | G, A | Cache single-flight và memo query buộc mọi người gọi sau theo token, lane và thời hạn của người gọi đầu | K | Đã sửa |
| NS-07 | TB | A | Memo 15 phút lưu cả kết quả thiếu (một số site timeout hoặc bị chặn) | K | Đã sửa |
| NS-08 | TB | G, A | Cache response và memo không bao giờ dọn, bộ nhớ tăng mãi khi app chạy nhiều ngày | K | Đã sửa |
| NS-09 | TB | G | Lane interactive không được ưu tiên hơn lane background | R | Chưa |
| NS-10 | TB | M | Junk filter: chuỗi "4k" nằm bất kỳ đâu trong tên bắt áp ngưỡng 2160p; `.Bat.` bị coi là file thực thi | K, T | Đã sửa |
| NS-11 | TB | M | Movie có tên là số giống năm (1917, Wonder Woman 1984) luôn bị loại vì sai tên (có từ trước, nằm ở parser) | T | Chưa |
| NS-12 | TB | M | Anime `- 06v2` bị loại vì sai tập (có từ trước, nằm ở parser) | T | Chưa |
| NS-13 | TB | P | LimeTorrents đổi layout thì trả 0 dòng kèm trạng thái "ok"; nhiều indexer JSON cũng vậy | K | Chưa |
| NS-14 | TB | P | SubsPlease nhận query có số tập, mà API của nó chỉ khớp tên show, nên luôn trả rỗng | K | Chưa |
| NS-15 | TB | P, G | Một exception lạ ở một indexer (ví dụ timestamp ngoài phạm vi) làm mất kết quả của mọi indexer | K | Đã sửa |
| NS-16 | TB | A | Both: qBittorrent lỗi vẫn ghi record với qBittorrent = 0, nên cổng P6 có thể qua nhờ qBittorrent hỏng | K | Chưa |
| NS-17 | TB | A | Both: snapshot và dry-run hỏng hẳn khi qBittorrent tắt, dù native vẫn chạy được | K | Chưa |
| NS-18 | TB | M | Rút gọn query bỏ mất năm nằm trong tên phim (Blade Runner 2049 → "Blade Runner") | K, T | Chưa |
| NS-19 | TB | M | Tên thay thế toàn token ngắn ("IS", "Us") khớp với mọi bản | T | Chưa |
| NS-20 | TB | A | Lần tra IMDb id thất bại được nhớ tới khi tắt app, nên EZTV im lặng không search show đó | K | Đã sửa |
| NS-21 | TB | P | BT4G, TsukiHime và SubsPlease luôn báo seed = 0 (nghĩa là không biết), nên bị `MinimumSeeders` loại | R | Chưa |
| NS-22 | Thấp | A | Checkbox "Pause all" hiển thị lệch với trạng thái thật sau khi rời Settings mà không lưu | R | Chưa |
| NS-23 | Thấp | G | Một request thành công đang chạy dở đóng lại breaker vừa mở vì 403 (apibay chạy 2 request song song) | R | Đã sửa |
| NS-24 | Thấp | G | Trạng thái governor chỉ lưu khi có 2 s không có hoạt động, nên crash giữa lượt hunt làm mất breaker | R | Chưa |
| NS-25 | Thấp | A, G | Timeout từng indexer đặt trên 20 s không có tác dụng (`HttpClient.Timeout` = 20 s) | R | Chưa |
| NS-26 | Thấp | G, P | Policy LimeTorrents được nới hai lần mà không có số đo; `FullPageRows = 20` trong khi một trang thật có 40 dòng | R | Chưa |
| NS-27 | Thấp | P | Không lọc theo category: sách, nhạc và app chiếm chỗ trong 4 trang LimeTorrents | R | Chưa |
| NS-28 | Thấp | P, G | Dedupe fuzzy: dung lượng trôi dần theo bản được giữ; merge giữ size 0 dù bản khác có size | R | Chưa |
| NS-29 | Thấp | P | TsukiHime không đọc `is_adult` | R | Chưa |
| NS-30 | Thấp | nhiều | Các lỗi nhỏ khác, xem mục 3 | R | Chưa |
| NS-31 | TB | đo | Bench chỉ đo movie; TV, anime và pack chưa được đo theo recipe | K | Chưa |
| NS-32 | TB | P | Tên từ LimeTorrents và TorrentDownloads bị mojibake (UTF-8 bị đọc thành Windows-1252), nên bản Nga/Trung bị loại nhầm | K | Đã sửa |

---

## 2. Chi tiết các finding Cao và Trung bình

### NS-01 — Lệch năm ±1 cộng tên khớp lỏng (Cao)

- **Ở đâu:** `CandidateEvaluationService.cs:85-103`, `YearMatcher.cs:264-267`.
- **Lỗi:** `YearMatcher` chấp nhận năm lệch 1, mà phần khớp tên chỉ cần `min(2, n)` token có nghĩa.
  - Token "the" vẫn được tính.
  - Với tên romaji, trợ từ bị bỏ nên có khi chỉ cần một token.
  - Số trong tên (Part 1 với Part 2, Vol 1 với Vol 2) không được so.
- **Ca đã chạy lại, đều được NHẬN:**
  - "Kill Bill: Vol. 2" (2004) nhận `Kill.Bill.Vol.1.2003.1080p…`.
  - "Harry Potter and the Deathly Hallows: Part 1" (2010) nhận `…Part.2.2011.1080p…`.
  - "Detective Conan: The Million-dollar Pentagram" (2024) nhận `Detective.Conan.The.Black.Iron.Submarine.2023…`.
  - "Your Name." (alt "Kimi no Na wa.") nhận `[Group] Kimi no Koe wo Todoketai (2017) [BD 1080p]`.
- **Hướng sửa:** khi năm lệch 1 thì bắt buộc khớp tên đầy đủ (`IsCompleteTitleMatch`). Nếu tên phim và tên bản có số khác nhau (Part, Vol, số phần) thì loại.

### NS-02 — Bản không có năm nhận nhầm phần tiếp theo (Cao)

- **Ở đâu:** `CandidateEvaluationService.cs:98`, `:185-197`.
- **Lỗi:** `IsCompleteTitleMatch` chỉ kiểm tra theo một chiều: mọi token của tên phim có trong tên bản. Bản có thêm "Part Two" hay "II" vẫn khớp. Số "5.1" của audio bị tách thành "5" và "1", và hai số đó đáp ứng điều kiện "tên có số".
- **Ca đã chạy lại, đều được NHẬN:**
  - "Dune" (2021) nhận `Dune.Part.Two.1080p.WEB-DL.DDP5.1…`.
  - "Frozen" (2013) nhận `Frozen.II.1080p.BluRay…`.
  - "Deathly Hallows: Part 1" nhận `…Part.2.1080p.BluRay.DD5.1…`.
- **Vì sao nặng hơn trước:** query movie không có năm ("Dune", "Frozen") là thay đổi của nhánh này, và nó trả về đúng loại bản này.
- **Hướng sửa:**
  - Nếu bản có thêm token phần tiếp ("part", "ii", "2", "two", "chapter"…) mà tên phim không có, thì không coi là khớp đầy đủ.
  - Không lấy số trong token audio hay kênh (`DD5.1`, `2.0`, `7.1`) làm số của tên.

### NS-03 — TMDB timeout làm dừng cả lượt chạy (Cao)

- **Ở đâu:**
  - `NativeSearchService.cs:363-370`;
  - `TmdbMetadataProvider.cs:260-277` và `:1210-1241`;
  - `HttpClient` dùng chung khai báo ở `App.xaml.cs:291` (timeout mặc định 100 s).
- **Lỗi:**
  - Khi `HttpClient` hết thời gian, nó ném `TaskCanceledException`. Đây là một `OperationCanceledException`, nên lọt qua `catch … when (ex is not OperationCanceledException)`.
  - Cart và Auto-Track coi exception này là "user dừng". Cart ghi "Stopped by user". Auto-Track rollback và hủy cả lượt hunt.
  - Lần thất bại không được ghi là miss, nên mọi query sau chờ lại 100 s × 3 lần thử. Các query xếp hàng sau `_imdbGate`.
- **Khi nào gặp:** Native hoặc Both, TMDB bị chặn (đã biết là xảy ra ở Việt Nam khi WARP tắt hoặc sai mode), và show chưa có trong `imdb-ids.json`.
- **Hướng sửa:**
  - Tra IMDb id với thời hạn riêng ngắn (ví dụ 10 s).
  - Bắt `OperationCanceledException` khi token của người gọi chưa bị hủy, rồi coi như không có IMDb id.
  - Ghi miss tạm thời có thời hạn (xem NS-20).

### NS-04 — Lỗi parse không bao giờ mở breaker (Cao)

- **Ở đâu:** `GovernedIndexerHttp.cs:179`, `IndexerRequestGovernor.cs:230-260`, `SearchOrchestrator.cs:272-278`.
- **Lỗi:** mọi response 2xx được báo **thành công** trước khi indexer parse. Báo thành công đặt `ConsecutiveFailures = 0` và đóng breaker. Sau đó lỗi parse mới được báo `BadResponse`, nên bộ đếm chỉ lên tới 1 và không bao giờ đạt 3.
- **Ca lỗi:** một site trả trang chặn mềm với mã 200, mà `ChallengeDetector` không nhận ra (captcha tiếng khác, trang "access denied" tự làm). Mỗi request đều lỗi parse, breaker vẫn đóng, và Auto-Track gọi tiếp tới hết quota ngày (LimeTorrents: 350 request).
- **Ngược lại:** cache 45 phút giữ trang lỗi đó. Mỗi lần trúng cache được báo `BadResponse` mà không có lần "thành công" nào đứng trước, nên 3 lần trúng cache lại mở breaker dù site không bị gọi.
- **Hướng sửa:**
  - Chỉ báo thành công **sau khi** parse xong. Có thể để `GovernedIndexerHttp` trả response, rồi orchestrator báo kết quả cuối cùng.
  - Trúng cache thì không báo gì.
  - Không cache response mà parser từ chối.

### NS-05 — Retry-After (TB)

`IndexerRequestGovernor.cs:239-246`, `GovernedIndexerHttp.cs:153-171`.

- Retry-After của server bị cắt ở 30 phút. Doc 03 chỉ giới hạn backoff tự tính, không giới hạn Retry-After.
- `Retry-After: 0`, hoặc một thời điểm đã qua do lệch đồng hồ, cho cooldown ≤ 0. Rate limit cũng không bao giờ mở breaker, nên site bị gọi tiếp ngay.
- 503 không phải trang challenge thì bị coi là `ServerError`, và Retry-After của nó bị bỏ qua.
- **Hướng sửa:** tôn trọng Retry-After (có trần an toàn, ví dụ 24 h), đặt cooldown tối thiểu (ví dụ 60 s), và đọc Retry-After cho cả 503.

### NS-06 — Người gọi sau bị buộc theo người gọi đầu (TB)

`GovernedIndexerHttp.cs:51-74, 98-99`, `NativeSearchService.cs:173-178`.

- Factory của cache single-flight giữ token, lane và `maxWait` của **người gọi đầu tiên**. Memo query cũng chờ task của người khác mà không dùng token của mình.
- **Ca 1:** Auto-Track đang chờ lease cho URL X với hạn 5 phút. User search tay cùng show và nhập vào task đó. Hạn 90 s và nút Hủy của user không còn tác dụng.
- **Ca 2:** người gọi đầu bị hủy. Mọi người nhập vào nhận `OperationCanceledException`, và orchestrator ghi "timeout" rồi bỏ indexer đó.
- **Hướng sửa:** mỗi người chờ dùng `WaitAsync(token của mình)`. Nếu task chung bị hủy vì token của người khác, người còn lại tự gọi lại. Khóa memo nên có cả lane.

### NS-07 — Memo lưu kết quả thiếu (TB)

`NativeSearchService.cs:171-194`.

- Memo chỉ bỏ kết quả **rỗng hoàn toàn**. Nếu knaben và apibay timeout còn bt4g trả 4 dòng, thì trong 15 phút, Re-search, cart và Auto-Track đều nhận lại đúng 4 dòng đó.
- Bật thêm indexer trong Settings cũng không có tác dụng trong 15 phút đó.
- `TryGetValue` rồi mới gán không phải thao tác nguyên tử, nên hai người gọi cùng lúc có thể search hai lần.
- **Hướng sửa:**
  - Chỉ lưu memo khi mọi indexer chạy đều trả "ok".
  - Cho tập indexer đang bật vào khóa memo.
  - Dùng `GetOrAdd` với `Lazy`.

### NS-08 — Cache và memo không bao giờ dọn (TB)

`GovernedIndexerHttp.cs:49-60`, `NativeSearchService.cs:71, 186`.

- Mục hết hạn chỉ bị xóa khi chính khóa đó được hỏi lại. Mỗi mục giữ nguyên body HTML (LimeTorrents có 4 trang) hoặc danh sách dòng.
- Auto-Track sinh khóa mới mỗi tuần (tập mới), nên bộ nhớ của app tray tăng mãi.
- **Hướng sửa:** dọn định kỳ (ví dụ mỗi lần ghi thì quét các mục hết hạn, hoặc dùng timer), và đặt trần số mục.

### NS-09 — Lane interactive không được ưu tiên (TB)

`IndexerRequestGovernor.cs:278-345`, `:180-218`.

- Lane chỉ làm đổi giới hạn quota ngày. Hai lane dùng chung giới hạn mỗi phút, khoảng cách tối thiểu và số request đồng thời. Lease được cấp cho ai thức dậy trước.
- Doc 03 §1.6 yêu cầu interactive đi trước, và 07-phases có liệt kê test ưu tiên. Cả tính năng lẫn test đều chưa có.
- **Hướng sửa:** giữ chỗ cho interactive. Khi có request interactive đang chờ một site, background không được lấy slot của site đó.

### NS-10 — Junk filter loại nhầm (TB)

`RecipeCandidateFilter.cs:27`, `JunkFilter.cs:30-60`.

- `TorrentQuality.Detect` tìm chuỗi con, nên "4k" xuất hiện ở đâu trong tên cũng bị coi là 2160p, và ngưỡng 1 GB cho movie được áp vào.
  - `Lawrence.of.Arabia.1962.4K.Remastered.1080p…` dung lượng 900 MB bị loại.
  - `…AAC.384kbps.DVDRip.720p…` cũng bị loại.
  - Tên phim cũ hay có "4K Remaster", nên phần phim cũ của bạn bị ảnh hưởng.
- `\.(bat|scr|…)(\b|$)` khớp với `.Bat.` nằm giữa tên, nên `The.Bat.Whispers.1930.1080p…` bị loại vì "file thực thi". Tag screener `.SCR.` cũng bị bắt.
- **Hướng sửa:**
  - Junk filter dùng chất lượng do parser xác định (token đứng riêng, ưu tiên 1080p/720p khi tên có cả hai).
  - Chỉ coi là file thực thi khi đuôi nằm **ở cuối** tên (`\.(exe|…)$`), hoặc khi danh sách file của torrent có file thực thi.

### NS-11, NS-12 — Lỗi có sẵn ở parser, lộ ra nhiều hơn với native (TB)

- **NS-11:** `TorrentCandidateParser.cs:479` bỏ mọi token 19xx/20xx khỏi `TitleTokens`.
  - "1917" (2019) loại `1917.2019.1080p.BluRay…` vì sai tên.
  - "Wonder Woman 1984" loại `Wonder.Woman.1984.1080p…` vì sai năm.
  - `YearMatcher` xử lý đúng trường hợp này; chỉ bước khớp tên là sai.
- **NS-12:** regex số tập tuyệt đối `\b(\d{2,4})\b` không khớp "06v2", nên `[SubsPlease] Kanata kara - 06v2 (1080p)` bị loại vì sai tập. Fixture `01v2` có sẵn nhưng chỉ được dùng trong test parser của indexer.

### NS-13 — Indexer hỏng im lặng (TB)

- `LimeTorrentsIndexer.cs:63`: trang nào cũng có "tt-name" (từ các dòng quảng cáo), nên điều kiện kiểm tra trang lạ không bao giờ ném lỗi. Chỉ cần layout đổi nhẹ (link `//itorrents…`, khoảng trắng giữa `</a><a`, href tuyệt đối) là regex không khớp gì, và indexer trả 0 dòng với trạng thái "ok". Nguồn movie chính biến mất mà không ai biết.
- Apibay, YTS, torrents-csv, EZTV, TsukiHime và NekoBT cũng trả 0 dòng nếu tên trường hash hoặc title đổi.
- **Hướng sửa:** khi body có dấu hiệu dòng kết quả (ví dụ có `itorrents`, hoặc mảng JSON không rỗng) mà parse ra 0 dòng, thì ném `FormatException`.

### NS-14 — SubsPlease không bao giờ có kết quả cho tập (TB)

- `SubsPleaseIndexer.cs:27` gửi nguyên `query.Text`, ví dụ "Kanata Kara S01E06". Comment của chính indexer và doc 02/04 (P4-2) ghi rằng API chỉ khớp tên show.
- **Hướng sửa:** gửi tên show (bỏ marker tập), rồi lọc dòng theo `query.Episode`. Phần biến đổi query riêng cho từng indexer như doc dự kiến chưa được làm.

### NS-15 — Một exception lạ làm mất cả lượt search (TB)

- `catch` trong `SearchOrchestrator.cs:248-278` không có nhánh bắt mọi lỗi.
- Các chỗ gọi `FromUnixTimeSeconds` hay `FromUnixTimeMilliseconds` ở apibay, EZTV, YTS, torrents-csv, TsukiHime và NekoBT chỉ kiểm tra `> 0`. Một timestamp tính bằng mili giây sẽ ném `ArgumentOutOfRangeException`. Khi đó `Task.WhenAll` ném, và kết quả của mọi indexer bị bỏ.
- `PublishedUtc` hiện không được dùng ở đâu.
- **Hướng sửa:** thêm nhánh bắt mọi lỗi, ghi "parse-error" cho riêng indexer đó, và parse timestamp an toàn.

### NS-16, NS-17 — Chế độ Both (TB)

- **NS-16:** `NativeSearchService.cs:149-161` bắt lỗi của qBittorrent rồi vẫn ghi record JSONL với qBittorrent = 0. Lệnh `SearchBench gate` (`GateCommand.cs:49-57`) đếm record đó là "native ≥ 90%" và "bản #1 không tệ hơn". Lần trúng memo cũng ghi thời gian native khoảng 0 ms, làm median nhỏ đi.
  - **Hướng sửa:** thêm trường `qbitError` và `fromMemo` vào record, rồi để gate bỏ qua các record đó.
- **NS-17:** ở chế độ Both, `ShowSearchSnapshotService.cs:83-91` và `AutomationFlowService.cs:182-190` gọi `GetPluginsAsync` của qBittorrent **trước**. Nếu qBittorrent tắt thì cả snapshot hunt lẫn dry-run hỏng, dù Both trả kết quả native. Đường FetchJob theo từng tập thì vẫn chạy được.

### NS-18 — Rút gọn query bỏ năm trong tên (TB)

`NativeQueryText.cs:28, 52, 101` (`TrailingYears`) bỏ mọi số 19xx/20xx đứng sau từ đầu tiên:

- "Blade Runner 2049 2017 1080p" thành "Blade Runner".
- "Wonder Woman 1984 2020" thành "Wonder Woman".
- "Godzilla 2000 1999" thành "Godzilla".

Trên indexer HTML có phân trang, các phim cùng tên khác sẽ chiếm hết chỗ. **Hướng sửa:** truyền năm phát hành của movie vào `Reduce`, và chỉ bỏ token bằng đúng năm đó.

### NS-19 — Tên thay thế ngắn khớp mọi bản (TB)

- `SearchTitleResolver.ResolveForMatching` (`:63-82`) giờ đưa **mọi** tên thay thế của thư viện vào bước khớp. Nó bỏ qua giới hạn số tên thay thế và module Identity của recipe.
- Tên nào chỉ gồm token ≤ 2 ký tự thì `EvaluateTitleMatch` trả "khớp" cho mọi bản (`CandidateEvaluationService.cs:169-172`, `SnapshotCandidateMatcher.cs:160-164`).
  - Show "Infinite Stratos" có alt "IS", tập 5, nhận `[SubsPlease] Totally Other Show - 05`.
  - Movie "Us" (2019) nhận `Joker.2019…`.
- **Hướng sửa:** tên không có token có nghĩa thì so khớp đúng cả cụm (`"is"` phải đứng đúng vị trí tên), hoặc bỏ tên đó khỏi bước khớp.

### NS-20 — Lần tra IMDb thất bại bị nhớ tới khi tắt app (TB)

- `NativeSearchService.cs:357-375`: `_imdbMisses` giữ mọi kết quả null cho tới khi tắt app. Kể cả khi token TMDB chưa cấu hình, hoặc TMDB trả 401, 429, 5xx, hoặc mạng bị chặn.
- EZTV chỉ search theo IMDb id, nên với các show đó EZTV im lặng không trả gì cho tới khi khởi động lại.
- **Hướng sửa:** chỉ nhớ miss khi TMDB trả 200 mà không có `imdb_id`; các lỗi khác nhớ khoảng 30 phút.

### NS-21 — Seed = 0 nghĩa là "không biết" nhưng bị loại (TB)

- BT4G, TsukiHime và SubsPlease luôn báo `Seeders = 0`. Recipe mặc định có `MinimumSeeders = 1` (`RecipeService.cs:278`), nên các dòng này bị loại trừ khi được gộp với cùng hash từ indexer khác có số seed.
- Với SubsPlease, nghĩa là bản chính thức của nhóm sub lớn nhất bị loại nếu không site nào khác có nó.
- **Hướng sửa:** dùng seed = null để biểu thị "không biết", và để bộ lọc seed bỏ qua giá trị null. Hoặc lấy seed từ Nyaa theo hash.

### NS-31 — Số đo chưa đại diện cho cách dùng thật (TB)

- `SearchBench movies-compare` dùng recipe thật và chỉ đo movie (15 movie, 4 recipe movie).
- Lệnh `compare` cho TV và anime chỉ đếm số torrent khác nhau trên một bộ tiêu đề cố định, **không chấm theo recipe**.
- Recipe đang gán trong DB:

| Recipe | Số show |
|---|---|
| Common Anime (episode) | 15 |
| Common Anime no Probe Pagination / Parallel | 6 / 4 |
| Common Anime Pack | 16 |
| Default TV Pack Recipe | 15 |
| Các recipe riêng (Korra, Rick_And_Morty, One_Piece, oregairu…) | 1–2 mỗi cái |

- **Hướng sửa:** thêm lệnh `shows-compare`.
  - Với mỗi show đang theo dõi, lấy vài tập và pack còn thiếu hoặc mới nhất.
  - Dựng query bằng `BuildEpisodeQueries` hoặc `BuildSeasonPackQueries` theo đúng recipe của show.
  - Search trên cả hai backend, chấm tier như movie, và đo thời gian.
- Cổng P6 cần số này trước khi bỏ qBittorrent.

---

## 3. Finding mức Thấp

- **NS-22:** đánh dấu "Pause all" rồi rời Settings mà không lưu. Khi quay lại, checkbox hiển thị bỏ chọn nhưng governor vẫn dừng (`SettingsViewModel.cs:410-415`, `SettingsViewModel.Search.cs:59, 111-117`).
- **NS-23:** apibay chạy 2 request song song. Request A nhận 403 và mở breaker. Request B đang chạy dở nhận 200 và đóng breaker lại. Nếu cả hai cùng nhận 403 thì `OpenCount` tăng 2 lần (`IndexerRequestGovernor.cs:230-236, 355-356`).
- **NS-24:** `Change(2s)` đặt lại bộ đếm mỗi lần có thay đổi, nên trong lượt hunt dồn dập không có lần lưu nào. Nếu app crash, breaker vừa mở bị mất (`NativeSearchService.cs:455-462`). Thoát bình thường thì vẫn lưu.
- **NS-25:** `HttpClient.Timeout` = 20 s (`NativeSearchService.cs:91`), nên timeout từng indexer đặt 21–60 s trong Settings không có tác dụng. Request bị cắt ở 20 s còn bị tính là lỗi cho breaker.
- **NS-26:**
  - Policy LimeTorrents được nới từ 6 lên 10 request/phút, khoảng cách từ 5 s xuống 3 s, quota từ 300 lên 500/ngày (`7f4bbcb`), mà không có số đo chứng minh an toàn. Doc 03 cấm nới như vậy.
  - `FullPageRows = 20` trong khi một trang thật có 40 dòng, nên phát sinh một request thừa.
- **NS-27:** không indexer nào lọc theo category (apibay `cat=` rỗng, LimeTorrents `/search/all/`, Nyaa `c=0_0`). Khoảng 1/4 số dòng trang 1 của LimeTorrents là sách, nhạc hoặc app.
- **NS-28:**
  - Dedupe fuzzy (`CandidateDeduplicator.cs:118-121`) thay dung lượng của bản được giữ bằng dung lượng bản mới, nên A (1000 MB), B (1004 MB) và C (1008 MB) đều bị gộp với dung sai 5 MB.
  - `MergeGroup` giữ size 0 của bản thắng dù bản khác có size.
- **NS-29:** TsukiHime có trường `is_adult` và `sukebei_id` nhưng parser không đọc.
- **NS-30, các lỗi nhỏ khác:**
  - Bấm Refresh danh sách indexer làm mất thay đổi chưa lưu.
  - Backend được chọn lại cho từng query thay vì cho cả lượt chạy (doc 06 §1).
  - Bộ lọc cart picker có thể xóa candidate đang chọn sau khi blacklist hoặc search lại.
  - Tên bản tách dấu nháy ("Schindler s List") không khớp với tên gộp ("Schindlers").
  - Season pack đếm từ lặp hai lần ("Hunter x Hunter" nhận "Monster Hunter Stories S01").
  - Magnet của Nyaa mất tracker riêng.
  - `IndexerParsing` coi dấu phẩy là phân cách hàng nghìn ("1,5 GB" thành 15 GB, chưa site nào dùng kiểu này).
  - torrents-csv có thể chỉ trả 10 dòng vì bỏ qua con trỏ `next`.
  - Quota ngày tính theo ngày UTC, nên reset lúc 07:00 giờ Việt Nam.
  - `FoldDiacritics` làm mất dakuten trong tên tiếng Nhật.
  - `GetImdbIdAsync` sửa default header của `HttpClient` dùng chung từ thread search.
  - Dòng log `Run Cart … Mode=…` chưa ghi backend (plan 08 §Q4).
  - `ReleaseAttributes` nhận "DL" của `WEB-DL` là tên nhóm.

---

## 4. Thiếu test

- **`SearchOrchestrator`:** không có test nào cho merge, engine name của meta indexer, lọc virus, ánh xạ exception sang trạng thái, hay giữ dòng khi hết thời gian.
- **`GovernedIndexerHttp` và `IndexerResponseCache`:** không có test phân loại status, Retry-After (delta, date, 0, đã qua), single-flight khi người gọi đầu bị hủy, hay thứ tự báo thành công và lỗi parse.
- **Governor:** không có test ưu tiên lane, probe half-open thất bại, Retry-After lớn hơn 30 phút, hay 403 và 200 xảy ra đồng thời.
- **App:** không có test cho chọn backend (thứ tự ưu tiên, giá trị "Default", key theo loại), cho `SearchOrFallbackAsync` ở chế độ Both, cho memo, cho cache IMDb và miss, cho `BackgroundScope`, hay cho load, apply và pause của Settings. Không test nào dựng `FetchJobService`, `AutomationFlowService` hay `ShowSearchSnapshotService`.
- **Luật khớp:** thiếu test cho phần khác cùng franchise lệch 1 năm, phần tiếp theo không có năm, token audio "5.1", tên phim là số (1917, 1984), junk "4K Remastered" 1080p, `.Bat.`, `.SCR.`, tên tách dấu nháy, `NNv2`, tên thay thế ngắn, và năm nằm trong tên khi rút gọn query.
- **Parser:** thiếu test nhiều trang cho Knaben, LimeTorrents, SkTorrent và Kickass; `ParseSize` với dấu phẩy; dòng Nyaa không có hash; Knaben có hash base32; TsukiHime `is_adult`.

---

## 5. Đã sửa trong lúc audit

- **TorrentDownloads lọc mất season pack.** Commit `a106389` vừa thêm bộ lọc "đủ mọi từ" để chặn feed khớp bất kỳ từ nào. Nhưng nó bắt buộc cả các từ template như `season`, `3`, `complete`, `1x06`, nên `Rick.and.Morty.S03.1080p…` bị loại với query `Rick and Morty season 3`. Reviewer mảng P phát hiện lỗi này.
- **Đã sửa:** năm, số, marker tập/mùa (`S03`, `S03E05`, `1x06`) và các từ `season`, `complete`, `pack`, `batch`, `episode`, `ep` giờ là tùy chọn. Tên chỉ gồm số ("1917") vẫn bắt buộc phải có số đó. Đã thêm test cho các ca này, và 443 test Core đều pass.

---

## 6. Đợt sửa 1 (ngày 2026-11-12)

| ID | Đã làm | Test mới |
|---|---|---|
| NS-01, NS-02 | Thêm `MovieReleaseTitle`, chỉ lấy phần tên của bản phát hành: phần đứng trước năm, ngoặc, hoặc token chất lượng/nguồn/codec/audio, và bỏ qua `[Group]` ở đầu. Khi năm lệch 1 hoặc không có năm, phần tên này phải chứa đủ mọi từ của một tên phim (`the`/`a`/`an`/`and` là tùy chọn). Nó cũng không được có marker phần tiếp theo (số, số La Mã, `part`, `vol`, `two`…) mà tên phim không có. Bản không có năm còn bị từ chối nếu có từ thừa, trừ các từ phiên bản như Extended, Remastered, The Movie. | 15 ca trong `MovieMatchingTests` (Kill Bill, Deathly Hallows, Detective Conan, Your Name/Kimi no…, Dune Part Two, Frozen II, Frozen Fever, DD5.1, kèm các ca phải được nhận) |
| NS-10 | Junk filter đọc độ phân giải từ token đứng riêng. Độ phân giải pixel thắng "4K"/"UHD", nên `4K Remastered 1080p` được hiểu là 1080p, và `384kbps` không còn bị coi là 4K. `.scr`/`.bat`/`.cmd` chỉ bị coi là file thực thi khi nằm ở cuối tên. | `QueryPlanAndJunkTests` (Lawrence of Arabia 4K, 384kbps, The Bat Whispers, `.SCR.`) |
| NS-04 | Chỉ báo "thành công" cho governor sau khi indexer parse xong (`IIndexerHttp.ReportParsed`). Response không parse được bị báo `BadResponse` và bị xóa khỏi cache. Trúng cache thì không báo gì. | `GovernedHttpAndOrchestratorTests`: 3 trang lạ trả 200 làm mở breaker; trúng cache không bị tính lỗi |
| NS-15 | Orchestrator có nhánh bắt mọi lỗi: một indexer lỗi thì ghi `parse-error` cho riêng nó. Timestamp được parse an toàn (`IndexerParsing.FromUnix*`). | indexer ném `ArgumentOutOfRangeException` mà vẫn giữ được dòng của indexer khác |
| NS-06 | Cache single-flight và memo: người gọi sau chờ bằng token của chính mình. Nếu task chung bị hủy bởi người khởi tạo, người gọi sau tự chạy lại. Khóa memo có thêm lane. | người gọi sau không bị hủy theo người gọi đầu, và tự dừng được bằng token riêng |
| NS-07 | Memo chỉ lưu khi mọi indexer được hỏi đều trả "ok" (hoặc bị bỏ qua vì đang tắt). Khóa memo có tập indexer đang bật. Dùng `GetOrAdd` nguyên tử. | `NativeSearchServiceTests.IsComplete` |
| NS-08 | Cache response và memo tự dọn mục hết hạn (cache quét mỗi 5 phút). | |
| NS-03 | Tra IMDb id có hạn 10 s. Timeout của TMDB (kể cả timeout 100 s của `HttpClient`) giờ được coi là "không có IMDb id", không còn là user hủy. | `NativeSearchServiceTests`: TMDB timeout không làm search ném lỗi, và query sau không chờ lại |
| NS-20 (một phần) | Lần tra thất bại được nhớ 10 phút; TMDB trả về không có IMDb id thì nhớ 1 giờ, thay vì tới khi tắt app. | như trên |

Kết quả: 475 test Core và 140 test App đều pass. SearchBench build được.

### 6.1 Audit lại đợt 1 (ngày 2026-11-13)

Hai reviewer mới, không biết kết luận của người sửa, đọc lại commit `4c7bf29`:
- reviewer luật khớp chạy 105 tên bản phát hành và 27 ca junk qua code thật;
- người viết lấy thêm tên thật từ bench native (lệnh `--dump`), 182 dòng bị luật mới loại.

**Luật khớp movie (NS-01/02): bản sửa đầu gây loại nhầm, đã viết lại.**

- **Lỗi của bản sửa đầu:** với bản không có năm, luật "không được có từ thừa ngoài từ phiên bản" quá chặt. Có 36 ca loại nhầm trong probe. Bench thật cũng có nhiều ca, ví dụ:
  - tên Nyaa "Romaji - English";
  - tag ngôn ngữ (FRENCH, VOSTFR);
  - "Criterion Collection";
  - "Movie 01 - …" (đánh số phim anime);
  - nhóm Trung Quốc `【…】彗星来的那一夜[中文字幕].Coherence.2013…`;
  - `[Judas] Jujutsu Kaisen - Movie 01 - Jujutsu Kaisen 0`;
  - "Charlotte's Web" (chữ "web" bị coi là tag nguồn);
  - "(500) Days of Summer".

  Ngược lại, phim lệch 1 năm mà thêm phụ đề vẫn lọt: "The Hunger Games Catching Fire" được nhận cho "The Hunger Games".
- **Bản viết lại** (`MovieReleaseTitle`) dựa trên vị trí:
  - Một tên phim phải xuất hiện đủ và đúng thứ tự. Chấp nhận cách viết gộp/tách ("Spiderman", "C era"), và coi "Vol" bằng "Volume".
  - Chữ đứng **trước** tên chỉ được là: tên nhóm trong ngoặc, chữ không phải Latin, tag, tên của studio, hoặc sở hữu cách ("Satoshi Kon's").
  - Chữ đứng **sau** tên (tới hết đoạn tên) chỉ được là: tag ngôn ngữ, phiên bản hoặc nguồn; nội dung trong ngoặc; chữ không phải Latin; một tên khác của phim; "Movie 01"; hoặc `v2`/`25th`.
  - Các dấu " - ", "/", "|" và "aka" tách các tên khác nhau.
  - Từ của chính tên phim không kết thúc phần tên, nên "Charlotte's Web" không bị cắt ở chữ "web".
- **Test:** `MovieReleaseTitleTests` có 71 ca: 51 tên phải nhận và 20 tên phải loại (Call Me by Your Name, The Real Oppenheimer, Extra Scenes, tập lẻ JJK, Dune Part Two, Frozen II/Fever, Catching Fire, Twilight New Moon…). Phần lớn là tên thật lấy từ bench.
- **Vẫn còn:** phần tiếp theo **cùng năm** ("Matrix Revolutions" 2003 cho "Matrix Reloaded" 2003) vẫn đi qua luật khớp lỏng cũ, vì bước kiểm tra này chỉ chạy khi năm lệch hoặc không có năm. Tên có nhóm không ngoặc ở đầu ("AnimeKaizoku Your Name") bị loại.

**Junk filter (NS-10):** giờ đọc được `1920x1080`/`3840x2160`, `FHD` và "1080" đứng riêng. Khi tên ghi nhiều độ phân giải thì lấy độ phân giải **cao nhất**, nên bản giả `2160p 4K 1080p 720p` dung lượng 300 MB vẫn bị loại.

**Governor, cache và memo:** reviewer xác nhận NS-03/04/08/15 đã sửa đúng, không có lỗi mức Cao. Các lỗi nhẹ đã sửa thêm:
- Một lần báo thành công đến muộn không còn đóng breaker đang mở. Lỗi này cũng chính là NS-23.
- Người gọi sau bị governor từ chối vì lane hoặc hạn chờ của người khởi tạo thì tự hỏi lại với lane của mình (NS-06 xong).
- `Remove` của cache chỉ xóa response đã xong, không xóa request mới đang chạy.
- Nếu không tra được IMDb id thì câu trả lời TV không được lưu memo, vì EZTV báo "ok" mà không search.
- TMDB trả 401/429/5xx được coi là lỗi, thử lại sau 10 phút; chỉ 404 mới được nhớ 1 giờ là "không có IMDb id" (NS-20 xong).
- Chờ khóa tra IMDb tối đa 20 s.

Kết quả: 552 test Core và 140 test App đều pass.

**Kiểm chứng trên dữ liệu thật** (bench `movies-compare --native-only 1 --dump`, 15 movie):
- Your Name: số bản tốt giảm từ 88 xuống 74. Các bản **"Call Me by Your Name" (2017)** trước đây bị nhận nhầm cho "Your Name." (2016, lệch 1 năm), giờ bị loại đúng.
- Liz tăng từ 13 lên 17, Euphonium từ 14 lên 16, Coherence từ 18 lên 19. Các movie khác giữ nguyên số.
- Trong 154 dòng còn bị bước kiểm tra tên loại, chỉ có 2 ca nên nhận:
  - `Chou.Kaguya.hime.GoLTFilm.1080p.mkv` (tên nhóm dính sau tên phim);
  - `AnimeKaizoku Kimi no Na wa Your Name` (tên nhóm không có ngoặc ở đầu).

  Còn lại là phim khác, tập lẻ hoặc bản tổng hợp, bị loại đúng.

**NS-32 (mới, phát hiện khi kiểm chứng):** 30 dòng từ LimeTorrents và TorrentDownloads có tên bị mojibake. Ví dụ `Ð¡Ð²ÑÐ·ÑŒ Coherence (2013)` thay vì `Связь Coherence (2013)`, và `å½—æ˜Ÿæ¥çš„… Coherence 2013`. Chữ rác trông như chữ Latin đứng trước tên phim, nên bản đúng bị loại. `MojibakeRepair` khôi phục tên khi nó đổi ngược lại được thành UTF-8 hợp lệ; chữ Latin-1 thật như "Amélie" giữ nguyên. Bước này chạy cho mọi indexer, ngay lúc chuẩn hóa kết quả.

**Còn lại, chấp nhận được:** nếu một lượt chạy kết thúc giữa chừng (timeout, lỗi HTTP ở trang sau) thì các trang đã thành công không được báo. Lúc đó breaker đang ở HalfOpen sẽ mở lại sớm hơn. Hướng này an toàn với site, và site không bao giờ bị chặn vĩnh viễn.

## 7. Thứ tự sửa đề xuất

1. **Trước khi để Native hoặc Both chạy tự động (Auto-Track):**
   - NS-01, NS-02: khớp movie.
   - NS-03: TMDB timeout.
   - NS-04: breaker khi trang lạ trả 200.
   - NS-15: catch-all trong orchestrator.
   - NS-10: junk filter.
   - NS-06, NS-07: memo và single-flight.

   Mỗi mục sửa kèm test cho đúng ca lỗi đã chạy lại.
2. **Trước cổng P6:**
   - Governor: NS-05, NS-08, NS-09.
   - Kết quả của các site: NS-13, NS-14, NS-21.
   - Chế độ Both: NS-16, NS-17.
   - Query và khớp tên: NS-18, NS-19, NS-20.
   - Thêm lệnh `shows-compare` (NS-31) rồi đo lại TV, anime và pack theo recipe thật.
3. **Sau P6 hoặc khi tiện:** NS-11, NS-12 (sửa ở parser, ảnh hưởng cả đường qBittorrent), các mục mức Thấp, và phần thiếu test ở mục 4.
