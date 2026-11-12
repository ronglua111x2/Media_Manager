# 08 — Cải thiện độ phủ và chất lượng ứng viên (sau P1–P5)

> **Trạng thái:** KẾ HOẠCH, chờ user xác nhận (12/11/2026).
> **Nhánh:** tiếp tục trên `feat/native-search`.

## 1. Bối cảnh (số đo 12/11/2026)

- Native tìm ra ứng viên cho **15/15 movie**, nhanh hơn qBittorrent rất nhiều. Nhưng với **movie phương Tây phổ biến**, native có ít ứng viên đạt hơn khoảng 30–70% (*Oppenheimer* 120 so với 258, *Lego Movie 2* 65 so với 179).
- **"Nhiều ứng viên đạt" không có nghĩa là "nhiều ứng viên tốt"**:
  - *Jujutsu Kaisen 0* từng ra 168 ứng viên với qBittorrent. Gần hết là tập TV bị nhận nhầm; lỗi đã sửa ở `46fbbd4`.
  - Sau khi sửa, qBittorrent ra **21** còn native ra **22**.
  - Bench hiện chỉ đếm "đạt bộ lọc", nên chưa trả lời được "ngon hơn hay không".
- User muốn: (a) thêm nguồn và trang để native phủ ngang qBittorrent với movie phương Tây; (b) đo được **ứng viên tốt**; (c) chọn backend (`Qbittorrent`, `Native`, `Both`) được **cả trong Settings lẫn trong từng recipe**.

## 2. Hạng mục

### Q1 — Chỉ số "ứng viên tốt" trong SearchBench (làm trước, để mọi thay đổi sau đều đo được)

Với mỗi movie, đếm các ứng viên đạt theo 3 bậc:

| Bậc | Điều kiện |
|---|---|
| **Đạt** | Qua bộ lọc của recipe (như hiện nay) |
| **Tốt** | Đạt; chất lượng thuộc **2 mức cao nhất** trong allow-list của recipe; seed ≥ max(ngưỡng recipe, 5); không có cờ `Suspicious` |
| **Rất tốt** | Tốt; và có prefer term, release group ưu tiên, hoặc engine ưu tiên của recipe |

Ngoài ra bench báo thêm:
- **bản #1 có trùng nhau** giữa native và qBittorrent không, và chênh lệch điểm của bản #1;
- **số bản tốt chỉ có ở một bên** (native-only, qBit-only), kèm 3 ví dụ cho mỗi bên, để biết native đang thiếu nguồn nào.

Lệnh: `SearchBench movies --compare` chạy cả hai backend trên cùng danh sách movie và in bảng theo bậc.

### Q2 — Thêm nguồn native cho movie phương Tây

| Nguồn | Việc | Ghi chú |
|---|---|---|
| **kickasstorrents** | Indexer HTML mới | Đo ngày 12/11: plugin trả 5/10 tiêu đề, trung vị 60 s, có lần treo. Native có timeout từng request nên không còn treo. Kiểm domain hiện tại và trang challenge trước khi viết |
| **sktorrent** | Indexer HTML mới | 6/10 tiêu đề, khoảng 8 s; có movie quốc tế |
| **YTS theo IMDb id** | Bật tra IMDb cho **movie** (hiện mới tra cho TV) | YTS nhận `query_term=tt…`, cho kết quả **chính xác** thay vì search theo chữ |
| **Knaben** | Lấy tới 3 trang (thay vì 2) khi trang đầu đầy **và** là query movie | Một request Knaben phủ TPB, 1337x, YTS… cùng lúc |
| **TPB (apibay)** | apibay trả tối đa 100 dòng và không có phân trang | Bù bằng cách thêm query **tên gốc** (Q3) |

Mỗi nguồn mới phải có fixture test, chính sách governor và một dòng trong `site_candidates.json`.

### Q3 — Query movie tốt hơn

- **Tên gốc** (TMDB `original_title`) luôn có trong danh sách tên search của movie, nếu nó khác tên chính và chưa nằm trong tên thay thế.
- Với native: luôn có một query **chỉ tên** (đã có nhờ `NativeQueryText`). Thêm một query **tên + năm** chỉ khi tên ngắn hoặc dễ trùng (ví dụ *Dune*, *Heat*, *Alien*), để không bị chìm giữa hàng trăm kết quả không liên quan.

### Q4 — Chọn backend theo recipe (user yêu cầu)

- Thêm vào module `SearchSource` của recipe khoá `searchBackend` = `Default` | `Qbittorrent` | `Native` | `Both`.
- **Thứ tự ưu tiên:** recipe (nếu khác `Default`) > override theo loại media trong Settings > backend chung trong Settings.
- Recipe editor có một combo "Search backend". Cart và Auto-Track hiển thị backend đang dùng trong dòng log `Run Cart: … Mode=…`.

### Q5 — Dùng chế độ `Both` làm nguồn số liệu cho cổng P6

- `Both` đã ghi log so sánh từng query. Bổ sung: mỗi lượt search ghi một dòng JSON vào `<StateFolder>/logs/search-compare.jsonl` (query, loại media, số bản và số bản **tốt** của native, của qBittorrent, của phần chung, và bản #1 của mỗi bên).
- `SearchBench gate --state …` đọc file đó và tính tiêu chí cổng ([06 §4](./06-rollout-and-migration.md#4-cổng-chuyển-hẳn-trước-khi-đổi-mặc-định-sang-native-ở-p6)) trên **search thật** của user, không cần chạy bench riêng.
- Sửa tiêu chí cổng: thay "số infohash" bằng **"số bản tốt" và "bản #1 không kém"**, vì số infohash bị đội lên bởi bản trùng và bản rác.

## 3. Thứ tự làm và nghiệm thu

| Bước | Nội dung | Nghiệm thu |
|---|---|---|
| 1 | Q1 (chỉ số) | Bảng 3 bậc cho 15 movie, chạy cả hai backend |
| 2 | Q4 (backend theo recipe) và Q5 (log JSONL, lệnh `gate`) | Test thứ tự ưu tiên; chạy `Both` một lần tạo được dòng JSONL; `gate` đọc ra bảng |
| 3 | Q3 (tên gốc, tên + năm có điều kiện) | Test planner; bench movie không giảm bản tốt ở movie nào |
| 4 | Q2 (kickass, sktorrent, YTS theo IMDb, Knaben 3 trang) | Fixture test; bench: số bản **tốt** của native ≥ 90% của qBittorrent ở movie phương Tây, bản #1 không kém ở ≥ 13/15 movie |
| 5 | Đo lại, cập nhật [07](./07-phases.md#số-liệu-sau-từng-giai-đoạn) | Số liệu trước và sau từng bước |

Ràng buộc giữ nguyên: mọi request đi qua governor; không vượt challenge; mặc định backend vẫn là `Qbittorrent` cho tới khi qua cổng P6 (user có thể bật `Both` hoặc `Native` theo recipe để thử).

## 4. Kết quả (12/11/2026)

Đã làm Q1–Q5 (`7ad12ef` và các commit sửa sau đó). Q3 "luôn search tên gốc" bị bỏ: TMDB `original_title` không được lưu riêng, và nó đã nằm trong danh sách tên thay thế, thứ mà movie mặc định search từ P1. Kickass có indexer nhưng **mặc định tắt**, vì trang kết quả không có hash hay magnet (chỉ trang chi tiết mới có).

**Lỗi đo phát hiện trên đường làm.** Plugin `thepiratebay` và `limetorrents` của qBittorrent trả về **link trang** (mô tả hoặc chi tiết) thay vì hash. Vì vậy cùng một torrent bị đếm hai lần và bị coi là "native không có". `SearchIdentity` quy các link trang đó về hash nhờ dữ liệu của indexer native; cách quy này áp dụng cho cả bench lẫn log `Both`.

`SearchBench movies-compare` (15 movie thật, đã đếm đúng cách), số **ứng viên tốt** (đạt, thuộc 2 chất lượng cao nhất của recipe, seed ≥ 5):

| Movie | qBittorrent | Native | Bản #1 |
|---|---|---|---|
| Coherence | 25 | 21 | native điểm cao hơn |
| Predestination | 25 | 24 | giống nhau |
| Your Name. | 129 | 96 | giống nhau |
| Liz and the Blue Bird | 20 | 15 | giống nhau |
| The Lego Movie 2 | 50 | 35 | giống nhau |
| Euphonium – Our Promise | 16 | 14 | giống nhau |
| El Camino | 31 | 24 | giống nhau |
| Spider-Man: No Way Home | 90 | 80 | giống nhau |
| Jujutsu Kaisen 0 | 1 | 2 | qBittorrent điểm cao hơn |
| Oppenheimer | 126 | 100 | giống nhau |
| Euphonium: Ensemble Contest | 0 | 0 | giống nhau |
| Spider-Man: Across the Spider-Verse | 59 | 51 | native điểm cao hơn |
| Golam | 0 | 5 | native điểm cao hơn |
| Cosmic Princess Kaguya! | 0 | 1 | giống nhau |
| Project Hail Mary | 88 | 70 | giống nhau |
| **Tổng** | **660** | **538 (82%)** | **Native bằng hoặc cao hơn ở 14/15** |

Thời gian mỗi movie: native 2–26 s, qBittorrent 13–242 s.

**Còn thiếu so với cổng P6.** Cổng yêu cầu số bản tốt của native ≥ 90% của qBittorrent; hiện mới đạt 82% tổng. Các bản tốt native còn thiếu chủ yếu đến từ:
- query có token chất lượng của recipe (`… 1080p`), mà native đã gộp thành query chỉ có tên;
- plugin `yts` và `torrentdownloads` trả thêm vài bản.

Bước tiếp theo:
- cho phép native giữ query `title + quality` với **indexer HTML có phân trang** (LimeTorrents);
- chạy `Both` thật vài ngày rồi dùng `SearchBench gate`.
