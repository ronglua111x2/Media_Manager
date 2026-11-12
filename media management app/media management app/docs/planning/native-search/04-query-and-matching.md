# 04 — Kế hoạch query, chuẩn hoá tiêu đề, khớp năm

P1 làm phần **khớp và chấm điểm**, chạy được ngay trên đường qBittorrent hiện tại. P4 làm phần **kế hoạch query** cho đường native. Hai phần dùng chung `TitleNormalizer` và `YearMatcher`.

## 1. `TitleNormalizer` (SRCH-13, "sửa lớn")

Một bộ chuẩn hoá duy nhất, dùng cho cả ba việc: **sinh query**, **khớp tiêu đề** và **dedupe**. Nó thay `QueryTemplateRenderer.Sanitize`, phần chuẩn hoá trong `EnglishAlternativeTitleFilter.NormalizeForSearchKey` và `TorrentCandidateParser.Tokenize` (khi dùng để khớp).

| Quy tắc | Ví dụ | Số đo |
|---|---|---|
| Bỏ dấu thanh (Unicode NFD, bỏ dấu kết hợp) | `Léon` → `Leon`, `Amélie` → `Amelie` | `Léon The Professional` 207 dòng, `Leon …` **458** |
| Dấu nháy `'` `’` `` ` ``: sinh **cả hai** biến thể nối liền và tách rời | `Schindler's List` → `Schindlers List` **và** `Schindler s List` | 361/5 engine so với 413/7 engine: mỗi engine hợp một dạng |
| `&` ↔ `and` | `Fast & Furious` ↔ `Fast and Furious` | |
| Ranh giới phụ đề `:` hoặc ` - `: thêm biến thể chỉ lấy phần trước | `Spider-Man: No Way Home` → thêm `Spider-Man` (chỉ khi phần trước đủ đặc trưng) | |
| Số La Mã ↔ số thường (chỉ cho token đứng riêng) | `Part II` ↔ `Part 2` | |
| Mạo từ đầu tên được phép lược khi khớp | `The Matrix` khớp `Matrix` | |
| Token hoá tên bản phát hành: `.` `_` `-` `[]` `()` là ranh giới | `Schindlers.List.1993.1080p` | |
| Dấu gạch nối trong tên giữ ở query, khi khớp thì coi như tuỳ chọn | `Spider-Man` khớp `Spider Man`, `SpiderMan` | |

API: `IReadOnlyList<string> QueryVariants(string title)`, `TitleKey Key(string title)`, `TokenMatch Match(TitleKey expected, string releaseName)`.

## 2. `YearMatcher` (SRCH-11, "sửa lớn")

Thay `FileName.Contains(ReleaseYear)` trong `CandidateEvaluationService.EvaluateMovie`.

1. **Đọc năm bằng parser:** token 4 chữ số trong khoảng `1900..năm hiện tại+1`, ưu tiên token **đứng sau tiêu đề** và trước các thẻ chất lượng (`1080p`, `BluRay`…).
2. **Bỏ qua số thuộc tiêu đề:** số nằm trong tiêu đề mong đợi (*2001: A Space Odyssey*, *1917*, *Blade Runner 2049*) không phải năm phát hành. Số đo: tên của *2001* có cả `2001` ×596 lẫn `1968` ×419.
3. **Tập năm hợp lệ:** `ReleaseYear` của TMDB **cùng** các năm của release date theo quốc gia và loại phát hành (premiere, theatrical, digital, physical) lấy từ TMDB `release_dates`. Cache cùng metadata movie.
4. **Chấm theo bậc, không loại cứng:**
   - khớp năm trong tập hợp lệ: điểm tối đa;
   - lệch ±1: trừ nhẹ;
   - không ghi năm: chỉ nhận khi tiêu đề khớp **mạnh** (đủ token, không thừa token lạ), trừ vừa;
   - lệch ≥ 2: **loại**, vì nhiều khả năng là bản khác hoặc remake (*Dune* 1984 và 2021).
5. **Áp dụng cho cả bước search:** template `{title} {year}` phải sinh thêm biến thể **không năm**. Số đo: `Coherence 2014` chỉ ra **12** dòng, `Coherence` ra **479** dòng, trong đó 322 bản ghi 2013.

## 3. Tên thay thế (SRCH-12)

- Movie: **mặc định dùng** tên thay thế trong `AlternativeTitlesJson` (romaji, tên gốc, tên tiếng Anh khác). Hiện tại recipe phải bật `useLibraryEnglishTitles`, mà Default Movie và Common Movie đều đang tắt.
- Tách hai việc:
  - **search bằng tên thay thế** (giới hạn số tên, theo ưu tiên của `EnglishAlternativeTitleFilter.ScoreTitleForSearch`);
  - **chấp nhận tên thay thế khi khớp**: luôn bật, vì có thêm tên để khớp không làm mất kết quả đúng.
- Ví dụ trong DB đang chưa có ứng viên: *Liz and the Blue Bird* (`Liz to Aoi Tori`, 113 dòng / 8 engine), *Your Name.* (`Kimi no Na wa.`), *Sound! Euphonium the Movie – Our Promise* (`Hibike! Euphonium Chikai no Finale`).

## 4. Đánh giá hết rồi mới lấy top N (SRCH-04)

`FetchJobService.SearchEpisodeCandidatesSequentialAsync` ngừng đánh giá sau `maxCandidates` ứng viên đạt đầu tiên (đã sắp theo seed). Phải đổi thành: đánh giá **toàn bộ** rồi sắp theo điểm và lấy top N, giống `MapSnapshotCandidatesAsync`. Nếu cần dừng sớm thì chỉ dừng khi đã có ứng viên đạt chất lượng tối đa.

## 5. `QueryPlanner` (P4, đường native)

Đầu vào là `SearchRequest`. Đầu ra là danh sách `IndexerQuery` cho **từng** indexer, có thứ tự ưu tiên (để governor cắt bớt khi thiếu ngân sách).

| Loại | Biến thể (theo thứ tự ưu tiên) |
|---|---|
| **Movie** | `title year` → `title` → `title (năm ±1)` nếu cần → tên thay thế → biến thể dấu nháy/dấu thanh/`&`/phụ đề |
| **TV, tập** | EZTV: `imdb_id` (cộng lọc season/tập phía app). Text: `title SxxEyy` → `title xxXyy` → `title Sxx` (để bắt pack) |
| **Anime, tập** | nyaa, AnimeTosho, TsukiHime: `title - 05` (có đệm số 0), `title 05`. SubsPlease: chỉ `title`. Tên romaji và tên tiếng Anh. Tập tuyệt đối khi recipe dùng `Anime absolute` |
| **Season pack** | `title S03`, `title Season 3`, `title S03 1080p`, `title complete` (show đã kết thúc). Số đo: `Rick and Morty S03 1080p` ra 111 pack S03, query chỉ có tên show chỉ ra 7 |
| **Snapshot cả show** (Auto-Track nhiều tập) | `title` cùng tên thay thế, sau đó **query nhắm đích bổ sung** cho các tập hoặc season chưa có ứng viên đạt. Thay cho cơ chế hiện nay là bỏ template `{season}`/`{episode}` (SRCH-03) |

- **Biến đổi theo indexer** do capability quyết định: indexer không hỗ trợ tìm theo tập thì chỉ nhận query cấp show; indexer DHT nhận ít biến thể hơn.
- **Dedupe query** sau khi chuẩn hoá bằng `TitleNormalizer.Key`.
- **Template recipe** vẫn được tôn trọng, nhưng không còn bị lọc âm thầm. Template nào không áp dụng cho chế độ hiện tại thì UI cảnh báo.

## 6. Test

- Fixture tên bản phát hành thật cho từng quy tắc: dấu nháy, dấu thanh, `&`, số La Mã, phụ đề, năm lệch ±1, không năm, số trong tiêu đề, remake trùng tên.
- Bộ "golden" lấy từ kết quả `engine_probe.py` thật (đã lưu JSON): *Coherence*, *Léon*, *Schindler's List*, *2001*, *Liz to Aoi Tori*, *Spider-Man No Way Home*. Test khẳng định số bản được chấp nhận tăng mà không nhận nhầm.
