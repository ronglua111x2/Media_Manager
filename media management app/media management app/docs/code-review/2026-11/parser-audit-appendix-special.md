# Phụ lục A của parser-audit: special/OVA → TMDB Season 0 (rule + Gemini)

Mỗi SPEC-xx được đăng ký thành một PARSE-xx trong [parser-audit.md](parser-audit.md), mục 11.

Ngày: 12/11/2026. Worktree `parser-audit-5f9a30`, HEAD `f8e88de` (chỉ thêm docs so với baseline của `parser-audit.md`).
Chỉ đọc code và chạy probe offline. Không sửa code, không gọi mạng hay Gemini, không đụng `<StateFolder>`.
Thang mức độ: `docs/code-review/00-method-and-scope.md`. Các finding PARSE-01..15 **không** lặp lại ở đây. Nếu một mục có chung gốc với một PARSE cũ, mục đó ghi rõ nó khác ở chỗ nào.

Probe: bản sao ở thư mục ghi chú chung của nhóm phiên, `parser-audit-harness/special-probe/` (`Program.cs`, `special-probe.csproj`, output ở `out.txt`). Probe tham chiếu DLL Release trong `MediaManager.App.Tests/bin/x64/Release/net8.0-windows10.0.17763.0/` và chỉ gọi hàm static thuần: `PackSpecialPayloadBuilder`, `SpecialMappingCompactJson`, `SpecialEpisodeTitleMatcher`, `SpecialMappingValidator`, `PackTorrentInventoryAnalyzer`.

Đường dẫn code dưới đây tính từ `media management app/media management app/`.

## Tóm tắt

| ID | Mức | Tin cậy | Tiêu đề |
|---|---|---|---|
| SPEC-01 | Medium | Cao (đã tái hiện) | Special đánh số `- NN` (≥10 file) bị xếp sai thứ tự, nên Ordinal map sai **12/12** file |
| SPEC-02 | Medium | Cao (đã tái hiện) | Rule OVA-dash coi số `OVA - N` là số tập TMDB S00 (`S00E{N}`) |
| SPEC-03 | Medium | Cao (đã tái hiện) | Title matcher khớp nhờ stopword hoặc tên show (`the`, `of`, `OVA`) |
| SPEC-04 | Low | Cao (đã tái hiện) | `TryParseResponse` ném exception ra ngoài khi giá trị không phải int, hoặc root là mảng |
| SPEC-05 | Low | Cao (đã tái hiện) | Response thiếu `m` (`{}`, bị bọc, `{"error":…}`) được coi là thành công với 0 mapping và bị lưu cache |
| SPEC-06 | Low | Cao (đã tái hiện) | Kết quả Gemini thay hẳn rule proposal; validator không bắt lệch chỉ số hay việc ghi đè `S00Exx` có sẵn trong tên file |
| SPEC-07 | Low | Cao (tĩnh) | Quota tracker: ngày tính theo UTC chứ không theo giờ Pacific, một bộ đếm chung cho mọi model, không đếm request lỗi, nhãn không rollover |
| SPEC-08 | Low | Cao (tĩnh) | Cửa sổ progress kẹt ẩn hoặc báo "complete" khi bước Apply ném lỗi; không có cách hủy; `OperationCanceledException` bị nuốt thành lỗi |
| SPEC-09 | Low | Trung bình | Refresh model catalog bỏ qua `nextPageToken`, nên model ngoài trang đầu bị đánh dấu Deprecated |
| SPEC-10 | Low | Cao (tĩnh) | Riêng tư: payload gửi nhiều hơn những gì hộp thoại xác nhận nói; API key nằm trong query string |

Không thấy lệch thứ tự giữa `ctx.c` đã serialize và `request.Context.Candidates`. Cả hai cùng là `orderedCandidates`, và proposal cùng response đều đánh chỉ số 0-based vào đúng list đó (probe B). Không có giới hạn kích thước nào làm rơi candidate.

---

## Finding đã chứng minh

### SPEC-01: Medium. Special đánh số `- NN` (≥10 file) bị xếp sai thứ tự, nên Ordinal map sai toàn bộ

**Liên quan PARSE-01** (cùng gốc là `PackEpisodePatternInferrer.Infer` trả về pattern đã trượt validate), nhưng ở **luồng special**, mà PARSE-01 không nhắc tới. PARSE-09 nói Ordinal có thể lệch vì air date. Ở đây lỗi nằm ở bước khác: đầu vào của Ordinal (thứ tự candidate) đã sai từ trước.

Code:
- `Services/PackSpecialPayloadBuilder.cs:179`: `PackSpecialPatternInferrer.Infer(stems, specialsEpisodes.Count)`.
- `Services/PackSpecialPatternInferrer.cs:41-52`: tên file không phải `OVA - N` hay `S00E` thì rơi về `PackEpisodePatternInferrer.Infer` (fixed-width).
- `MediaManager.Core/Services/PackEpisodePatternInferrer.cs:165-173`: pattern trượt `ValidatePattern` vẫn được trả về (`?? pattern`).
- `PackSpecialPayloadBuilder.cs:446-455`: với `Opaque`, `LocalIndex = TryExtractIndex(stem, pattern)`, tức chỉ đọc **chữ số cuối**.
- `PackSpecialPayloadBuilder.cs:182-190, 207-221`: candidate được sort theo `LocalIndex`, rồi gán `GlobalSortOrder`.
- `PackSpecialPayloadBuilder.cs:400-432`: Ordinal (khi `CandidateCount == TmdbSpecialCount`) gán theo `GlobalSortOrder` và theo thứ tự air date của TMDB.
- Không có review: `AutoTorrentLinkService.cs:637` (`RequiresReview` chỉ bật khi đã dùng Gemini).

Bằng chứng (probe D3/D3b): `Show [Grp]/Specials/[Grp] Show - 01..12 [1080p].mkv`, TMDB S00 có 12 tập.
```
ctx.c[0] "- 10" local=0 | ctx.c[1] "- 01" local=1 | ctx.c[2] "- 11" local=1 | ctx.c[3] "- 02" local=2 | ctx.c[4] "- 12" local=2 | ...
RULE "- 10"→S00E01, "- 01"→S00E02, "- 11"→S00E03, "- 02"→S00E04 ... mapped=12/12, wrong=12
```
Validator vẫn pass.

Tác động:
- Ở chế độ rules-only (Gemini tắt, không có key, hoặc bấm "Link pack (rules only)"), **mọi** special được link vào sai S00Exx mà không có thông báo. Điều kiện: folder special có từ 10 file trở lên, đặt tên dạng số ở cuối, và số file bằng số tập TMDB S00.
- Ở chế độ Gemini, `ctx.c` gửi đi có thứ tự và `localIndex` sai, `p[]` chứa 12 đề xuất sai. Gemini vẫn thấy tên file nên có thể sửa lại, nhưng prompt bảo nó "review" chính các đề xuất này.
- Khi số lượng lệch thì không có Ordinal, file thành orphan (an toàn), nhưng `localIndex` sai vẫn được gửi cho Gemini.

Hướng sửa: sửa `Infer` theo PARSE-01 (không trả pattern đã trượt validate). Riêng cho special: nếu pattern không hợp lệ thì dùng `TryParseBareEpisodeIndex` hoặc sort tự nhiên (natural sort) theo tên. Không chạy Ordinal khi `LocalIndex` có trùng lặp.

### SPEC-02: Medium. Rule OVA-dash coi số `OVA - N` là số tập TMDB S00

Code: `Services/PackSpecialPayloadBuilder.cs:331-363`. Với `NamingPattern == OvaDash`, `ProposedS00E = candidate.LocalIndex` khi TMDB có tập mang số đó. Lý do được gắn là `Direct`, giống hệt một file có `S00Exx` thật. Cùng file, dòng 450 lấy `LocalIndex` từ `OVA - N`.

Bằng chứng (probe D1): TMDB S00 = E01 "Recap Special", E02 "Promotional Video", E03/E04 là OVA thật.
```
[Grp] Show OVA - 01 → S00E01 'Recap Special' (Rule proposal: direct S00E01.)
[Grp] Show OVA - 02 → S00E02 'Promotional Video'
```
Validator pass. Trong `ApplyProposals`, đề xuất này đứng trước Title và Ordinal, nên các bước sau không sửa lại được.

Tác động: TMDB S00 thường đánh số theo thứ tự phát sóng và gồm cả recap, PV, movie. Vì vậy "OVA 1" hiếm khi trùng S00E01. Ở chế độ rules-only, file bị link sai mà không qua review. Ở chế độ Gemini, đề xuất được gửi dưới nhãn `"direct"`, nên trông như có bằng chứng chắc chắn. PARSE-09 chỉ nói tới Flatten và Ordinal, chưa nói tới OVA-dash.

Hướng sửa: không gắn `Direct` cho OVA-dash. Chỉ đề xuất khi tiêu đề TMDB của tập đó chứa "OVA" cộng số tương ứng. Nếu không thì để Gemini hoặc review quyết, và đánh dấu `RequiresReview` cho rule.

### SPEC-03: Medium. Title matcher khớp nhờ stopword, tên show hoặc "OVA"

Code: `Services/SpecialEpisodeTitleMatcher.cs`:
- dòng 59-86: chỉ cần `overlap ≥ 2` token, hòa điểm thì lấy tập có số nhỏ nhất;
- dòng 88-114: `TitleTokens` (toàn bộ tên file đã normalize, gồm cả tên show, `the`, `of`, `OVA`, `mkv`) **không** được lọc noise. `IsNoiseToken` (dòng 116-124) chỉ áp cho token lấy từ `RawTitle`.

Đường gọi trong Link: `PackSpecialPayloadBuilder.cs:365-398`. Bước title chạy cho candidate `OvaDash` hoặc `StandardS00E` chưa có đề xuất, ví dụ số OVA không có trên TMDB, hoặc file không mang số trong một bucket mà ≥50% file có số (style được quyết theo cả bucket, `PackSpecialPatternInferrer.cs:71-103`). Luồng Inspect cũ gọi title matcher **trước tiên** cho mọi file (`PackSpecialEpisodeMapper.cs:46`).

Bằng chứng:
- Probe C: `[Grp] The Rising of the Shield Hero OVA - 05` khớp **S00E01 'Recap of the First Season'**. Token chung chỉ là `{the, of}`.
- Probe D2 (Link, rules-only): `… OVA - 05` (TMDB không có E05) thành `S00E01` với lý do "title match". `- 06` và `- 07` thì không map.

Tác động: special bị gán vào một recap hay featurette không liên quan, không qua review ở rules-only, và đề xuất sai được gửi cho Gemini. Show có tên chứa stopword tiếng Anh hoặc từ "OVA"/"Special" (rất phổ biến) đều dễ dính.

Hướng sửa:
- bỏ stopword, token của tên show (`NormalizedTitle`), `ova/special/mkv`, group tag và số khỏi tập token;
- tính điểm trên `EpisodeTitle` thay vì cả tên file;
- đòi tỉ lệ khớp, ví dụ ≥60% token có nghĩa của tiêu đề TMDB, thay cho ngưỡng tuyệt đối 2;
- hòa điểm thì không chọn.

### SPEC-04: Low. `TryParseResponse` ném exception ra ngoài với kiểu sai hoặc root là mảng

Code: `Services/SpecialMappingCompactJson.cs`:
- dòng 123: `item[0].GetInt32()` và `item[1].GetInt32()`;
- dòng 114: `root.TryGetProperty`;
- dòng 75: chỉ `catch (JsonException)`.

`GetInt32` ném `InvalidOperationException` khi gặp string hoặc null, và `FormatException` khi gặp `3.0` hoặc số vượt `int`. `TryGetProperty` trên root là mảng ném `InvalidOperationException`.

Bằng chứng (probe A):
```
{"m":[["0","3"]],"w":[]}       → THROWS InvalidOperationException
{"m":[[0,3.0]],"w":[]}         → THROWS FormatException
{"m":[[null,3]],"w":[]}        → THROWS InvalidOperationException
{"m":[[0,99999999999]],"w":[]} → THROWS FormatException
[[0,1],[1,2]]                  → THROWS InvalidOperationException
```

Tác động: một hàm `Try…` lại ném lỗi.
- Bước `TryRecoverMappingsOnly` không được chạy.
- Nhánh log "Gemini special mapping JSON parse failed" cùng progress `ParsingResponse=Failed` ở `GeminiSpecialMappingProvider.cs:213-237` bị bỏ qua.
- User thấy thông báo khó hiểu ("requires an element of type 'Number'").
- Cả lần link hỏng như PARSE-10.

`responseSchema` (kiểu integer) làm trường hợp này hiếm, nhưng fallback sang model khác vẫn gửi cùng schema, và không có gì bảo đảm model tuân theo.

Hướng sửa: dùng `TryGetInt32` và bỏ qua phần tử sai kèm warning. Kiểm tra `root.ValueKind == Object` trước. Bắt mọi exception khi parse document.

### SPEC-05: Low. Response thiếu `m` được coi là thành công với 0 mapping, và bị cache

Code:
- `SpecialMappingCompactJson.cs:106-143`: không có `m` thì `mappings` rỗng, vẫn `return true`;
- `PackSpecialPayloadBuilder.cs:102-160`: mọi candidate thành "Gemini did not map", `UsedGemini=true`;
- `SpecialMappingOrchestrator.cs:121-122`: `Validate` pass, rồi `_cache.Save`.

Bằng chứng (probe A/E): `{}`, `{"error":"quota"}` và `{"result":{"m":[[0,1],…]}}` đều cho `ok=True m=[]`. `wrapped response: ok, mapped=0/3, UsedGemini=True`.

Tác động: review hiện toàn bộ special là "Unmatched (orphan)". Bấm Accept thì cả bộ thành orphan trong `Season Unknown`. Bấm Cancel thì kết quả rỗng vẫn nằm trong cache, nên lần "Link pack (AI)" sau dùng lại nó ("Skipped API call (cached mapping)") cho tới khi user bấm Retry. PARSE-11 nói cache không được validate lại, nhưng không nói chuyện **kết quả rỗng hoặc hỏng vẫn được ghi vào cache**.

Hướng sửa: bắt buộc có `m` kiểu mảng, không có thì coi là parse fail. Không cache kết quả có 0 mapping khi có ≥1 candidate.

### SPEC-06: Low. Kết quả Gemini thay hẳn rule; validator không bắt lệch chỉ số hay việc ghi đè `S00Exx` có sẵn

Code:
- `PackSpecialPayloadBuilder.cs:102-160`: `BuildFromAiResponse` không dùng `request.Proposals`. Candidate mà Gemini không trả về thì thành unmapped, kể cả khi tên file có `S00E05` rõ ràng và rule đã đề xuất `Direct`. Index ngoài khoảng thì bị bỏ mà không có warning.
- `SpecialMappingValidator.cs:8-36`: chỉ kiểm tra trùng và tập có tồn tại.
- Prompt (`GeminiSpecialMappingProvider.cs:61-93`) không nói rõ `candidateIndex` là vị trí 0-based trong `ctx.c`. Trong khi đó mỗi dòng `ctx.c` có `globalSort` là **1-based** (`PackSpecialPayloadBuilder.cs:217`).

Bằng chứng (probe E), 3 file `Show S00E01..03`:
- Gemini trả 1-based `[[1,1],[2,2],[3,3]]`: `S00E02.mkv→S00E01`, `S00E03.mkv→S00E02`, `S00E01.mkv→null`. Validate **pass**.
- Gemini trả `[[0,3],[1,2],[2,1]]`: `Show S00E01.mkv→S00E03`. Validate **pass**.
- `[[-1,1],[5000,2]]`: parse OK, bị bỏ mà không có thông báo.

Tác động: lỗi lệch 1 hoặc model "sáng tạo" sẽ link sai mà không bị máy phát hiện. Phải nhờ user để ý ở màn review, mà màn review chỉ hiện `S00Exx`, không có tiêu đề hay air date (xem ghi chú R2). Mức độ phụ thuộc hành vi model nên giữ Low.

Hướng sửa:
- Ghép kết quả: proposal `Direct` có `S00E` thật trong tên file được ưu tiên, hoặc phải có warning khi Gemini làm khác.
- Index ngoài khoảng: thêm warning.
- Ghi rõ "candidateIndex = 0-based position in ctx.c" trong prompt, hoặc bỏ `globalSort` khỏi payload.

### SPEC-07: Low. Quota tracker không khớp cách Google tính hạn mức

Code: `Services/Gemini/GeminiQuotaTracker.cs`:
- dòng 89-104: `ResetIfNewDay` dùng `DateTime.UtcNow.ToString("yyyy-MM-dd")`. Hạn mức theo ngày của Gemini API reset lúc nửa đêm giờ Pacific. Với user ở UTC+7, bộ đếm local reset lúc 07:00, còn Google reset khoảng 14:00–15:00 giờ local.
- dòng 31-40, 105 (`AppConstants.GeminiDailyRequestLimit = 1500`): một bộ đếm **chung** cho mọi model trong chuỗi fallback, trong khi Google tính quota **theo từng model**. Con số 1500 cũng không gắn với hạn mức thực tế của model đang dùng.
- `GeminiApiClient.cs:287-290`: `RecordRequest` chỉ chạy khi HTTP 200. Các lần 429/503/400 và timeout không được đếm, và cũng không cập nhật `_lastRequestUtc` dùng cho spacing (dòng 42-50).
- dòng 20-27: getter `RequestsToday` không gọi `ResetIfNewDay`, nên nhãn ở `SettingsViewModel.cs:1638` vẫn hiện số của hôm qua cho tới request kế tiếp.
- `_sync` (dòng 11) không được dùng. `EnsureCanRequest` chạy ngoài `_requestGate` (`GeminiApiClient.cs:224-229`).
- `AppConstants.GeminiDailyWarningThreshold` (1200) không được dùng ở đâu.

Tác động: bộ đếm chỉ mang tính tham khảo. Nó có thể chặn ("daily limit reached") khi Google còn quota, hoặc cho qua khi Google đã hết. Lúc đó request nhận 429, chờ backoff 2+4+8 giây cho mỗi model, rồi thất bại. **Không** có trường hợp chặn vĩnh viễn: ngày UTC mới luôn reset bộ đếm. Có thể vòng qua bằng cách chờ.

Hướng sửa: tính ngày theo `TimeZoneInfo` "Pacific Standard Time". Đếm theo model. Tăng bộ đếm cho mọi request đã gửi đi (kể cả lỗi khác 429). Gọi `ResetIfNewDay` trong getter. Bỏ hằng số không dùng.

### SPEC-08: Low. Cửa sổ progress kẹt hoặc báo sai khi Apply lỗi; không có cách hủy

Code: `ViewModels/LibraryViewModel.cs:922-1091`.
- Nhánh review: dòng 1005 `progressWindow?.Hide()`, rồi `PrepareForReview` (không set `CanClose`). Nếu `ApplySeasonPackLinkAsync` ở dòng 1051 ném lỗi, `finally` (1071-1085) thấy `IsFailed=false` và `result=null`, nên không `AllowClose` hay `Close`. Cửa sổ bị **ẩn mãi** với `CanClose=false` cho tới khi app tắt. Khi app shutdown thì WPF bỏ qua cancel, nên không treo, chỉ rò rỉ cửa sổ.
- Nhánh Gemini không cần review (pack có 0 candidate special): dòng 989 `MarkComplete("Pack analysis complete.")`, rồi Apply ở dòng 1001. Nếu Apply ném lỗi, cửa sổ vẫn hiện "complete" trong khi thanh trạng thái báo "Pack link failed".
- Dòng 1019-1033: review window lỗi thì code tạo một `PackLinkProgressWindow` **mới**, còn cửa sổ cũ đang ẩn vẫn không được đóng.
- Không truyền `CancellationToken` (976-981, 1001, 1051), và `Window_Closing` chặn đóng khi `CanClose=false` (`Views/PackLinkProgressWindow.xaml.cs:20-26`). User không thể hủy lần gọi Gemini: chuỗi tới 4 model, mỗi model timeout 45 giây hoặc 4 lần thử với backoff, tức vài phút. Cửa sổ là modeless nên app vẫn dùng được.
- `GeminiApiClient.cs:196-201`: `catch (Exception)` bắt cả `OperationCanceledException`, kể cả `Task.Delay` ở dòng 181 nằm trong try. Nếu sau này có truyền token, việc hủy sẽ chuyển sang model kế tiếp rồi thành `InvalidOperationException("…failed for all configured models")` thay vì hủy thật.

Hướng sửa: đưa Apply vào `try` và gọi `MarkFailed` khi lỗi. Chỉ `MarkComplete` sau khi Apply xong. Đóng cửa sổ cũ trước khi tạo cái mới. Thêm nút Cancel nối với `CancellationTokenSource`. Thêm `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }` trước catch chung.

### SPEC-09: Low (tin cậy trung bình). Refresh catalog bỏ qua phân trang

Code:
- `Services/Gemini/GeminiModelCatalogService.cs:205-242`: một lần GET `models?key=…`, không có `pageSize` và không đọc `nextPageToken`.
- dòng 162-181: entry không có trong tập trả về thì bị `Deprecated = true`.

Endpoint ListModels mặc định trả tối đa 50 model mỗi trang (theo tài liệu API, chưa kiểm tra bằng mạng vì audit không gọi mạng).

Tác động: nếu API có hơn 50 model, các model nằm ở trang sau (kể cả model user đã chọn) bị đánh Deprecated. Chúng biến khỏi picker (`GeminiTextModelFilter.IsEligibleForFallbackPicker`), nhưng vẫn được dùng nếu đã cấu hình (`Normalize` trả về `entry.Id` khi không có `Replacement`). Chỉ ảnh hưởng UI và cấu hình.

Hướng sửa: lặp theo `nextPageToken`, hoặc truyền `pageSize=1000`.

### SPEC-10: Low. Riêng tư: payload gửi nhiều hơn những gì hộp thoại xác nhận nói; API key trong URL

Dữ liệu gửi cho Gemini (`SpecialMappingCompactJson.cs:421-476`, probe B):
- `ctx.t`: tên show;
- `ctx.id`: TMDB id;
- mỗi candidate có `RelativePath` **đầy đủ trong torrent** (gồm tên folder gốc, tức tên release và group) cộng `FileName`;
- toàn bộ S00 của TMDB (số, air date, tiêu đề).

Không gửi đường dẫn trên ổ đĩa, tên user hay save path: `file.Name` của qBittorrent là đường dẫn tương đối, `BuildSourcePath` mới ghép save path. Không gửi `Overview` hay `Thought`.

Vấn đề:
- Hộp thoại (`GeminiLinkConfirmationService.cs:28-33`) chỉ nói "special/OVA file names and TMDB episode data". Thực tế còn gửi cả tên show và đường dẫn folder trong torrent, và hộp thoại tắt được (`ConfirmBeforeLink`).
- File nào bị PARSE-03 xếp nhầm vào bucket special thì tên tập thường cũng bị gửi đi.
- API key nằm ở query string (`GeminiApiClient.cs:261`, `GeminiModelCatalogService.cs:207`) thay vì header `x-goog-api-key`. Nó có thể lộ qua proxy hoặc log HTTP (không thấy app tự log URL).
- Cache `gemini-mapping-cache.json` serialize nguyên `TrackedEpisode`, gồm `Thought`, `UserRating` và `Overview` (`SpecialMappingCache.cs:34-46`). Dữ liệu cá nhân bị sao ra một file phụ. File chỉ nằm trên máy, không gửi đi.

Hướng sửa: sửa câu trong hộp thoại cho đúng, hoặc chỉ gửi `FileName` cộng tên folder cha gần nhất. Dùng header cho API key. Trong cache chỉ lưu `(SeasonNumber, EpisodeNumber, Title, AirDate)`.

---

## Ghi chú review (chưa đủ chuẩn finding)

- **R1. Inspect và Link cho kết quả special khác nhau, nhưng ảnh hưởng thấp.** Inspect (`PackLinkCoordinatorService.cs:122-128`) dùng `BuildLegacySpecialMappings` cộng `PackSpecialEpisodeMapper` (`PackTorrentInventoryAnalyzer.cs:411-447`): title match chạy trước cho mọi file, Ordinal theo `EpisodeNumber`, xử lý từng bucket riêng. Link dùng `PackSpecialPayloadBuilder`: Direct, rồi Flatten theo air date, rồi OVA-dash, rồi Title, rồi Ordinal. Inspect chỉ lưu **số lượng** (`ToContentProfile`, `ToInspectionDisplay`), và số đó giống nhau dù matched hay unmatched, nên UI không hiện mapping khác với Link. Probe D4 không thấy khác biệt trên một case đơn giản. Rủi ro tiềm ẩn: `IAutoTorrentLinkService.LinkSeasonPackFromInventoryAsync` (`AutoTorrentLinkService.cs:130-156`) sẽ link theo mapping cũ (`resolvedSpecialMappings: null`) nhưng hiện **không có caller**. Nên xóa API này hoặc chuyển nó sang orchestrator.
- **R2. Màn review chỉ đọc và thiếu thông tin để kiểm.** `PackLinkReviewWindow.xaml:86-88` đặt `IsReadOnly="True"`. Chỉ có ba nút Accept, Retry, Cancel, không sửa được từng dòng. Accept áp **đúng** `preview` đã hiển thị (`LibraryViewModel.cs:1051`), nên không có chuyện "review một đằng, link một nẻo". Nhưng `TargetLabel` chỉ là `S00Exx`, không có tiêu đề hay air date TMDB (`PackLinkReviewViewModel.cs:104-125`), nên user khó bắt được SPEC-02, 03 và 06. File `Skipped` cũng không được liệt kê.
- **R3. Key cache không gồm tập candidate.** Mở rộng của PARSE-11. `GetCompletedVideoFilesAsync` (`AutoTorrentLinkService.cs:1096-1101`) chỉ lấy file đã xong. Nếu user bỏ chọn file special trong qBittorrent, link, rồi chọn lại và link lại sau khi unlink, cache `(hash, tmdbId)` trả về `Items` cũ. File special mới không có trong `specialMappings` và rơi vào `Skipped("Excluded from episode group")` (`PackSeasonFileGrouper.cs:81-84`). Nên thêm hash của danh sách candidate vào key.
- **R4. Payload phình với CJK.** `Utf8JsonWriter` mặc định escape mọi ký tự non-ASCII thành `\uXXXX` (probe B: `"t":"\u9032\u6483…"`). Probe F: 1 candidate và 300 special TMDB cho 29.673 ký tự, trong khi tiêu đề gốc chỉ 4.992 ký tự. Mọi S00 đều được gửi, không giới hạn. Không làm rơi candidate, chỉ tốn token và đẩy gần giới hạn context. Có thể dùng `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`.
- **R5. Trích response.** `GeminiApiClient.cs:281-285` chỉ lấy phần text **đầu tiên** không rỗng và không xét `finishReason`. Khi `MAX_TOKENS` cắt giữa `w`, `TryRecoverMappingsOnly` cứu được phần `m` (probe A). Khi cắt giữa `m` thì parse fail (PARSE-10). `SAFETY`/`RECITATION` cho text rỗng, dẫn tới "empty response" rồi chuyển model mà không retry. Không chứng minh được có mất dữ liệu.
- **R6. Phân loại lỗi HTTP.** 400 luôn bị log là "unsupported for text JSON" (`GeminiApiClient.cs:156-162`), kể cả lỗi API key sai, nên code thử hết mọi model với cùng key hỏng. `Retry-After` chỉ đọc từ header, còn Gemini đặt `retryDelay` trong body. 500 không được retry. `HttpClient` singleton có `Timeout` mặc định 100 giây, nên setting `TimeoutSeconds` 101–120 không có tác dụng (`App.xaml.cs:291`).
- **R7. Model lạ bị thay thầm.** `GeminiModelCatalogService.Normalize` (66-95) đổi id không có trong catalog thành `DefaultModel` mà không cảnh báo. Fallback lạ bị loại sau khi dedupe. Log và progress ở `GeminiSpecialMappingProvider.cs:139-161` in model thô từ setting, không phải model thật được dùng.
- **R8. Style special được quyết cho cả bucket.** `PackSpecialPatternInferrer.cs:71-103`: chỉ cần `matched >= max(1, n/2)` (với 3 file thì 1 file là đủ) là cả bucket thành `OvaDash` hoặc `StandardS00E`. File khác dạng trong bucket có `LocalIndex=0` và bỏ qua Flatten (`DetectNamingPattern` dòng 464-472 trả style của bucket trước khi xét SnSnn).
- **R9. Key `m` trùng thì key cuối thắng** (probe A: `{"m":[[0,1]],"w":[],"m":[[0,2]]}` cho `(0,2)`). Prompt cấm key trùng nên chấp nhận được, nhưng nên ghi lại.
- **R10.** Dedupe warning `CountMismatch` ở `PackSpecialPayloadBuilder.cs:152-157` dựa vào `warning.Contains("vs")`. Một warning bất kỳ của Gemini có chữ "vs" (ví dụ "TVs") sẽ làm mất cảnh báo lệch số lượng. Rất nhỏ.

## Đã kiểm, không thấy lỗi

- Thứ tự `ctx.c` và `request.Context.Candidates`: cùng một list, index 0-based nhất quán giữa `p[]`, `m[]`, `ApplyProposals` và `BuildFromAiResponse` (probe B).
- Một file không thể nằm trong hai bucket (`PackSpecialBucketDetector.Detect` mỗi file một key), nên `BuildResult.ToDictionary` không ném vì trùng path. Trùng do Gemini lặp index thì đã có trong PARSE-10.
- Markdown fence, prose trước hoặc sau JSON, hai object liền nhau, root bọc trong mảng `[{…}]`, mảng con dài hơn 2 phần tử: đều parse đúng (probe A).
- Accept áp đúng preview đã review. Retry dùng `bypassCache` và ghi đè cache. Ghi cache trước khi review đã có trong PARSE-11.
- `PackLinkRegularEpisodeSummary`: file special bị `IsExcludedFromEpisodeGroup` chặn nên không làm phồng số "matched".
