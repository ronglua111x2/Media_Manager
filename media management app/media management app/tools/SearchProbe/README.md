# SearchProbe — bộ công cụ đo search của hunt

Bộ script Python dùng để đo và chẩn đoán giai đoạn search bằng engine của hunt. Được tạo trong đợt audit [07b — hunt search](../../docs/code-review/2026-11/07b-hunt-search.md).

**Mọi script đều chỉ đọc.** Với qBittorrent, chúng chỉ gọi `search/start`, `search/results`, `search/stop`, `search/delete` và `search/plugins`, **không bao giờ add torrent**. Với state folder, chúng chỉ đọc (DB mở ở chế độ `mode=ro`) và không in ra credentials.

## Yêu cầu

- Python 3.10+ (máy đã có sẵn vì qBittorrent cần Python để chạy plugin search).
- qBittorrent WebUI tại `http://localhost:8080` và bật **"Bypass authentication for clients on localhost"**. Script không dùng mật khẩu; phiên đăng nhập được lấy bằng cookie mà localhost bypass cấp.
- **Cloudflare WARP phải đang Connected.** Ở Việt Nam, tracker bị chặn nếu không có WARP, nên script từ chối chạy khi WARP tắt. Dùng `--no-warp-check` nếu chạy ở mạng khác.
- Chạy bằng `python -I -X utf8 …` (`-I` để cô lập môi trường, `-X utf8` để in được tiếng Việt và tên có dấu).

## Các script

| Script | Làm gì |
|---|---|
| `qbt_search.py` | Thư viện dùng chung: client search chỉ đọc (giữ cookie phiên), vòng poll đọc buffer bằng con trỏ, kiểm WARP. |
| `engine_probe.py` | **Sức khoẻ từng engine với tiêu đề đặc thù.** Mỗi cặp (plugin đang bật × tiêu đề) chạy một job riêng. Ghi lại trạng thái (xong, treo, lỗi), thời điểm có dòng đầu và dòng cuối, số dòng. Danh sách tiêu đề nằm trong `special_titles.json`. |
| `plugin_direct.py` | Chạy **thẳng** plugin trong `%LOCALAPPDATA%\qBittorrent\nova3` (encode query giống `nova2.py`) để thấy **lỗi thật**: traceback, lỗi SSL, JSON hỏng, timeout. qBittorrent nuốt các lỗi này nên engine chỉ trông như "rỗng". Phải chạy **không có `-I`**, vì plugin import `helpers` và `novaprinter` từ thư mục nova3. |
| `site_probe.py` + `site_candidates.json` | Khảo sát các **site torrent public ứng viên** cho indexer native: HTTP status, thời gian, có bị challenge không, đếm magnet, infohash và item RSS. Chỉ gửi GET, không vượt challenge. Python có thể báo lỗi chứng chỉ ở một số site; khi đó kiểm lại bằng curl, vì .NET dùng kho chứng chỉ Windows giống curl. |
| `analyze_engine_probe.py` | Tóm tắt kết quả của `engine_probe.py` thành bảng Markdown: sức khoẻ engine, bảng engine × tiêu đề, và năm xuất hiện trong tên bản phát hành. |
| `ab_timeline.py` | **A/B "app dừng sớm" so với "chờ tới khi xong".** Mỗi case chạy một job, ghi timeline từng giây, rồi mô phỏng quy tắc dừng của app (timeout và idle) để biết app sẽ giữ được bao nhiêu dòng. Các case nằm trong `ab_cases.json`. |
| `recipe_effective.py` | Bảng **tham số search hiệu lực** của từng recipe (lấy theo thứ tự recipe → settings → default code), kèm đường search thật sự được dùng, recipe đang gán cho bao nhiêu show/movie, và recipe có bị snapshot bỏ template hay không. |
| `hunt_log_summary.py` | Tóm tắt các lần search trong `systemlog`: trạng thái lúc dừng, lý do dừng, số dòng, và engine nào rỗng ở mọi lần search. |

## Cách dùng nhanh

```bash
cd "media management app/media management app/tools/SearchProbe"
OUT="$TEMP/searchprobe-$(date +%Y%m%d-%H%M)"
python -I -X utf8 engine_probe.py --out "$OUT/engines" --workers 4 --max-seconds 120
python -I -X utf8 analyze_engine_probe.py "$OUT/engines" --report "$OUT/engines.md"
python -I -X utf8 ab_timeline.py --out "$OUT/ab"
python -I -X utf8 recipe_effective.py --markdown
python -I -X utf8 hunt_log_summary.py --since 20261101
```

Kết quả (JSON và bảng Markdown) nên để ngoài repo, ví dụ trong `%TEMP%`, vì chúng chứa tên và link torrent.

## Thêm tiêu đề hoặc case

- **Tiêu đề đặc thù** (`special_titles.json`): mỗi mục gồm `id`, `query`, `note`. Nên có đủ các loại: dấu nháy, dấu thanh, năm lệch, tên romaji, số trong tên, season pack, tập anime, movie nhiều bản encode.
- **Case A/B** (`ab_cases.json`): mỗi case gồm `id`, `query`, `plugins` (`enabled` hoặc `a|b|c`), `timeout`, `idle`, `note`. Lấy `timeout` và `idle` từ `recipe_effective.py` để mô phỏng đúng recipe đang dùng.

## Lưu ý

- qBittorrent giới hạn số job search chạy đồng thời. Script tự thử lại khi gặp 409, nhưng nên để `--workers` ≤ 4 và tránh mở tab search trên UI trong lúc chạy.
- `engine_probe.py` với 23 engine × 10 tiêu đề tạo ra 230 job. Với `--max-seconds 120`, trường hợp xấu nhất mất khoảng 1–2 giờ.
- Khi code dispatch trong `RecipeRuntimeSettings`, `FetchJobService`, `ShowSearchSnapshotService` hoặc `AutomationFlowService` thay đổi, phải cập nhật `recipe_effective.py` theo.
