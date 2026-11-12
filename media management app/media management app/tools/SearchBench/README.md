# SearchBench

Công cụ C# chạy **đúng pipeline search của app** (`MediaManager.Core`: dựng query, khớp tiêu đề/năm, chấm điểm, dedupe, indexer native với governor) trên dữ liệu thật, không cần mở UI. **Chỉ đọc**: DB mở ở chế độ read-only, qBittorrent chỉ được gọi `search/*`, không add torrent nào.

Dùng để đo trước/sau mỗi thay đổi search, và làm **cổng A/B trước khi chuyển hẳn sang native** (`docs/planning/native-search/06-rollout-and-migration.md` §4).

## Yêu cầu

- .NET SDK theo `global.json`. qBittorrent WebUI tại `localhost:8080` với localhost bypass (không cần mật khẩu).
- Cloudflare WARP phải bật (ở Việt Nam tracker bị chặn nếu không có WARP).
- Không chạy hai lệnh bench cùng lúc: mỗi process có governor riêng, nên chạy song song sẽ gọi site nhiều gấp đôi.

## Lệnh

```bash
cd "media management app/media management app/tools/SearchBench"
dotnet build -c Release

# Movie thật trong DB: luật trước P1 so với luật hiện tại, search qua qBittorrent
dotnet bin/Release/net8.0/SearchBench.dll movies --max-seconds 45 --plugins "limetorrents|thepiratebay|torrentdownloads|yts|nyaasi|nekobt|sktorrent"

# Như trên nhưng search bằng indexer native
dotnet bin/Release/net8.0/SearchBench.dll movies --backend native

# Chẩn đoán: liệt kê bản luật cũ nhận mà luật mới loại (kèm lý do)
dotnet bin/Release/net8.0/SearchBench.dll movies --diag 1 --only "Coherence,Predestination"

# Native so với qBittorrent trên bộ tiêu đề đặc thù (../SearchProbe/special_titles.json)
dotnet bin/Release/net8.0/SearchBench.dll compare --plugins enabled --max-seconds 180
```

Tham số chung: `--state D:\MediaManagerState` (mặc định), `--only "a,b"` lọc theo tên movie.

## Đọc kết quả

- **movies**: số ứng viên đạt theo luật cũ và luật mới (sau dedupe theo infohash), cùng bản đứng đầu. Movie có số giảm thì chạy `--diag`: nếu "cũ nhận mà mới loại" bằng 0 thì phần giảm chỉ là bản trùng.
- **compare**: với mỗi tiêu đề, số bản khác nhau (theo infohash) của native và của qBittorrent, phần giao, phần riêng của mỗi bên, thời gian, và trạng thái từng indexer native (`ok`, `timeout`, `blocked`, `skipped`, `parse-error`).
