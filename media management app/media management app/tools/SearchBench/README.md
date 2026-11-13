# SearchBench

Công cụ C# chạy **đúng pipeline search của app** (`MediaManager.Core`: dựng query, khớp tiêu đề/năm, chấm điểm, dedupe, indexer native với governor) trên dữ liệu thật, không cần mở UI. **Chỉ đọc dữ liệu**: DB mở ở chế độ read-only, qBittorrent chỉ được gọi `search/*`, không add torrent nào. Thứ duy nhất bench ghi là `indexer-state.json` của state folder (ngân sách của các site, xem dưới).

Dùng để đo trước/sau mỗi thay đổi search, và làm **cổng A/B trước khi chuyển hẳn sang native** (`docs/planning/native-search/06-rollout-and-migration.md` §4).

## Yêu cầu

- .NET SDK theo `global.json`. qBittorrent WebUI tại `localhost:8080` với localhost bypass (không cần mật khẩu).
- Cloudflare WARP phải bật (ở Việt Nam tracker bị chặn nếu không có WARP).
- **Ngân sách dùng chung với app (07d GOV-07):** các lệnh search native đọc `<state>/indexer-state.json` (số request hôm nay, breaker, cooldown) và ghi phần của mình vào đó khi kết thúc. Khi app đang chạy (giữ `indexer-state.lock`), hoặc một bench khác đang chạy, bench từ chối chạy; `--force 1` chạy với ngân sách riêng không được lưu (site khi đó nhận request của cả hai). Muốn thử mà không đụng state thật thì dùng `--state <SandboxFolder>\state`.
- Mỗi target (tập, pack, movie) là một search session như trong app: có trần request mỗi lượt (07d GOV-03 B), nên bench đo đúng số request app sẽ gửi.

## Lệnh

```bash
cd "media management app/media management app/tools/SearchBench"
dotnet build -c Release

# Movie thật trong DB: luật trước P1 so với luật hiện tại, search qua qBittorrent
dotnet bin/Release/net8.0/SearchBench.dll movies --max-seconds 45 --plugins "limetorrents|thepiratebay|torrentdownloads|yts|nyaasi|nekobt|sktorrent"

# Như trên nhưng search bằng indexer native
dotnet bin/Release/net8.0/SearchBench.dll movies --backend native

# Chẩn đoán: liệt kê bản luật cũ nhận mà luật mới loại (kèm lý do)
dotnet bin/Release/net8.0/SearchBench.dll movies --diag 1 --only "Movie 01,Movie 05"

# Native so với qBittorrent trên bộ tiêu đề đặc thù (../SearchProbe/special_titles.json)
dotnet bin/Release/net8.0/SearchBench.dll compare --plugins enabled --max-seconds 180

# Movie thật, cả hai backend, đếm ứng viên theo tier (Accepted/Good/Excellent).
# --native-only 1: bỏ qBittorrent (kiểm tra nhanh luật khớp). --dump f.tsv: ghi phán quyết từng dòng.
dotnet bin/Release/net8.0/SearchBench.dll movies-compare --max-seconds 45

# TV/anime thật: tập mới nhất (--episodes N) và season pack mới nhất (--packs 0 để bỏ) của mỗi show,
# query dựng bằng recipe tập/pack của chính show đó, chấm bằng luật của app (pack: không probe metadata).
# Dump: show, target, backend, verdict, detail, seeds, engine, size, name.
dotnet bin/Release/net8.0/SearchBench.dll shows-compare --max-seconds 30 --dump shows.tsv

# Offline (không mạng): chấm lại dump bằng luật hiện tại, in các dòng đổi phán quyết.
# --out verdicts.tsv ghi phán quyết mới của mọi dòng để so hai phiên bản luật.
dotnet bin/Release/net8.0/SearchBench.dll reeval-movies --dump movies.tsv
dotnet bin/Release/net8.0/SearchBench.dll reeval-shows --dump shows.tsv [--kind pack|episode] [--out verdicts.tsv]
```

Tham số chung: `--state <StateFolder>` (mặc định), `--only "a,b"` lọc theo tên movie/show, `--force 1` (xem trên).

## Đọc kết quả

- **movies**: số ứng viên đạt theo luật cũ và luật mới (sau dedupe theo infohash), cùng bản đứng đầu. Movie có số giảm thì chạy `--diag`: nếu "cũ nhận mà mới loại" bằng 0 thì phần giảm chỉ là bản trùng.
- **movies-compare / shows-compare**: mỗi dòng là số ứng viên `đạt/tốt/xuất sắc` của từng backend, bản #1 có trùng không, số bản tốt chỉ một bên có, và thời gian. `shows-compare` in thêm bảng tổng theo nhóm (anime episode, tv episode, pack), rồi tới ví dụ các bản tốt chỉ qBittorrent có.
- **compare**: với mỗi tiêu đề, số bản khác nhau (theo infohash) của native và của qBittorrent, phần giao, phần riêng của mỗi bên, thời gian, và trạng thái từng indexer native (`ok`, `trimmed`, `timeout`, `blocked`, `skipped`, `parse-error`).
- **Dòng log native** (stderr): `site:trạng thái:số dòng/thời gian/số request mạng[+lượt trúng cache]`, ví dụ `limetorrents:ok:85/6339ms/3req`, `eztv:ok:71/0ms/0req+1hit`. `trimmed` nghĩa là trần request mỗi lượt đã cắt bớt query sau.
- **reeval-shows / reeval-movies**: dump cũ không có dung lượng (và dump trước 13/11 không có cột show; thêm bằng thứ tự bảng .md), nên các phán quyết theo dung lượng bị bỏ qua.
