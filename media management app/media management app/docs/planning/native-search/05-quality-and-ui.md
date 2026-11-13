# 05 — Chất lượng kết quả và UI chọn bản

## 1. `ResultNormalizer`

Biến mọi `IndexerResult` thành `NormalizedResult`:

| Trường | Cách lấy |
|---|---|
| `InfoHash` (40 hex, chữ thường) | Lấy từ trường `hash` của API, từ `xt=urn:btih:` của magnet (đổi base32 sang hex), hoặc từ file `.torrent` khi resolve |
| `Magnet` | Có sẵn; hoặc dựng từ infohash kèm danh sách tracker công khai |
| `Size`, `Seeders`, `Leechers`, `PublishedUtc`, `Uploader` | Từ indexer |
| Thuộc tính đã parse | Độ phân giải, nguồn (BluRay / WEB-DL / WEBRip / Remux / HDTV / DVD), codec (x264 / x265 / HEVC / AV1), HDR (HDR10 / DV / HLG), âm thanh (AAC / AC3 / DDP / DTS / TrueHD / Atmos, số kênh), bit depth, release group, ngôn ngữ và phụ đề, season và tập. Dùng lại `TorrentCandidateParser` cùng Sonarr.Parser hiện có, mở rộng thêm khi thiếu |
| `Sources[]` | Danh sách indexer hoặc tracker đã trả về bản này (sau khi gộp) |

## 2. `ResultMerger`: gộp theo infohash

- Khoá là `InfoHash`. Bản không có infohash (cần resolve) thì gộp tạm theo `(tên chuẩn hoá, dung lượng ±1%)`.
- Khi gộp: lấy **seed lớn nhất**, ngày sớm nhất, giữ mọi link (magnet, `.torrent`, chi tiết), cộng thêm `Sources`.
- Knaben là meta: bản Knaben trùng infohash với TPB hoặc nyaa thì **gộp**, không đếm hai lần.
- "Có nhiều nguồn" là một tín hiệu tin cậy, được cộng điểm nhẹ.

## 3. `JunkFilter`: loại rác trước khi chấm điểm

| Luật | Hành động |
|---|---|
| Tên hoặc danh sách file chứa `.exe`, `.scr`, `.bat`, `.lnk`, `password`, `codec pack`, `keygen` cho media | **Loại** (cùng nhóm với bộ kiểm nội dung sau khi add) |
| Dung lượng phi lý so với thời lượng và chất lượng (ví dụ movie 1080p dưới 400 MB, tập 1080p 24 phút dưới 80 MB, pack nhỏ hơn 1 tập) | **Loại** hoặc gắn cờ `Suspicious` |
| Seed = 0 và leech = 0, ngày đăng rất cũ, chỉ có ở 1 nguồn DHT | Trừ điểm mạnh |
| Knaben báo `virusDetection` | **Loại** |
| Dòng lỗi của indexer hoặc kết quả trống tên | Loại |
| Kết quả chỉ có từ indexer `IsDhtIndex` | Trừ điểm, không bao giờ đứng đầu nếu có lựa chọn khác tương đương |

Mọi lần loại đều ghi lý do vào telemetry và debug log, để chỉnh luật bằng số liệu.

## 4. Chấm điểm: trừ điểm thay vì loại cứng (SRCH-14)

- Các tiêu chí "không thích" của recipe (DTS/TrueHD, seed thấp, 720p, kích thước ngoài khoảng mong muốn) được **trừ điểm**.
- Chỉ những điều kiện **cứng** thực sự (sai media, sai tập, rác, blacklist, chất lượng dưới mức tối thiểu tuyệt đối) mới **loại**.
- Recipe v2 phân biệt rõ hai nhóm `Require` (loại) và `Prefer` / `Avoid` (cộng hoặc trừ điểm). Migration chuyển các `excludeTerms` hiện có sang `Avoid`, **trừ** những term rõ ràng là rác (`CAM`, `TS`, `HDCAM`) thì giữ ở `Require`.
- Profile **"movie cũ"** (tùy chọn): movie trước năm X thì hạ ngưỡng seed và cho phép 720p hoặc DVD ở mức điểm thấp.

## 5. UI chọn bản encode (SRCH-15)

- Movie (và pack) hiện **mọi** ứng viên đạt, không chỉ top 10. Thứ tự gợi ý theo điểm recipe.
- **Gom nhóm và chip lọc nhanh:** độ phân giải, nguồn, codec, HDR/DV, âm thanh, release group, dung lượng (thanh trượt), số nguồn.
- Mỗi dòng hiện: điểm và **lý do điểm** (vì sao cộng hay trừ), các nguồn, seed, ngày đăng, cờ `Suspicious` nếu có.
- Auto-Track vẫn tự chọn top 1 (hoặc top N để thử lần lượt khi add thất bại) như hiện nay.
- Số đo: *Movie 08* có **554** URL khác nhau qua 7 engine, trong khi app hiện chỉ giữ tối đa 10.

## 6. Test

- Fixture cho `ResultNormalizer`: magnet base32 và hex, tên đủ loại thuộc tính.
- `ResultMerger`: cùng infohash từ Knaben và TPB, khác số seed.
- `JunkFilter`: bộ tên rác thật (lấy từ log `engine_probe`), dung lượng phi lý.
- Snapshot test cho thứ tự chấm điểm của các case *Hero-Man*, *Movie 01*, *Show 16 S03*.
