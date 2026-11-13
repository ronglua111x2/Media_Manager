# 02 — Danh mục indexer

Số liệu đo ngày 12/11/2026, qua WARP, bằng `tools/SearchProbe` (`engine_probe.py`, `site_probe.py`, kiểm lại bằng curl). Chi tiết ở [07b mục 5c và 9](../../code-review/2026-11/07b-hunt-search.md). **Không indexer nào dưới đây cần đăng ký hay API key.**

## 1. Danh sách theo giai đoạn

| Ưu tiên | Indexer | Loại media | Giao diện | Số đo | Ghi chú triển khai |
|---|---|---|---|---|---|
| **P2** | **Knaben** | Mọi loại (meta) | API JSON `https://api.knaben.org/v1` | 150 kết quả / 1.7s, tổng ≥ 10000. Nguồn: TPB, TokyoTosho, Nyaa, 1337x | Ứng viên số 1. Có sẵn `hash`, `magnetUrl`, `seeders`, `bytes`, `date`, `tracker`, `virusDetection`. Là meta, nên phải gộp theo infohash. Đọc kỹ điều khoản và giới hạn của API trước khi bật mặc định |
| **P2** | **TPB (apibay)** | Movie, TV | API JSON `https://apibay.org/q.php?q=` | 77 kết quả / 0.2s | Thay cho 2 plugin `piratebay` và `thepiratebay` trùng nhau. Dựng magnet từ `info_hash` |
| **P2** | **nyaa** | Anime | RSS `https://nyaa.si/?page=rss&q=…&c=…&f=…` | 75 item / 0.9s | RSS có `nyaa:infoHash`, `nyaa:seeders`, `nyaa:size`. Phân trang bằng `&p=` |
| **P2** | **limetorrents** | Mọi loại | HTML | 10/10 tiêu đề có kết quả, ~5.5s | Engine tốt nhất của đường cũ. Cào HTML, nên phải có fixture |
| **P3** | **EZTV** | TV | API JSON `https://eztvx.to/api/get-torrents?imdb_id=` | 50 kết quả / 0.5s | **Tra theo IMDb id**, cần lấy IMDb id từ TMDB `external_ids` và lưu DB (xem [06](./06-rollout-and-migration.md)). Trang HTML search bị Cloudflare chặn, **không dùng** |
| **P3** | **BT4G** | Mọi loại (DHT) | RSS `https://bt4gprx.com/search?q=…&page=rss` | 15 item / 0.4s | Phủ rộng nội dung **cũ**, tốt cho movie cũ. Nhiều rác: độ tin cậy thấp, phải qua JunkFilter |
| **P3** | YTS | Movie | API JSON `…/api/v2/list_movies.json?query_term=` | 2 kết quả | Domain đã chuyển sang `yts.gg` (theo redirect). Ít bản nhưng sạch |
| **P3** | torrents-csv | Mọi loại | API JSON `https://torrents-csv.com/service/search?q=` | 10 kết quả | Python lỗi chứng chỉ, .NET thì chạy |
| **P3** | TsukiHime | Anime | API JSON `https://api.tsukihime.org/v1/search/torrents?q=` | 50 hash | Plugin cũ bị chặn chống bot; API trực tiếp thì chạy |
| **P3** | SubsPlease | Anime | API JSON `https://subsplease.org/api/?f=search&s=` | 21 magnet | Chỉ nhận **tên show**, không nhận số tập |
| **P3** | nekoBT | Anime | API JSON | có kết quả | Plugin cũ chạy được |
| **P3** | AnimeTosho | Anime | HTML search `https://animetosho.org/search?q=` | 75 magnet / 1.3s | JSON feed trả 403, `feed.` không phân giải được; dùng HTML |
| **P3** | bitsearch | Mọi loại (DHT) | HTML `https://bitsearch.eu/search?q=` | 20 hash | `solidtorrents.to` và `bitsearch.to` đều redirect về đây: một indexer |
| **P3** | torrentdownloads | Mọi loại | HTML | 9/10 tiêu đề, ~30s, tối đa 100 | Chậm vì phải mở trang chi tiết để lấy magnet. Dùng **resolve lười**: chỉ lấy magnet khi bản đó được chọn |
| P3, tùy chọn | rutor | Mọi loại (Nga) | HTML | 22 magnet / 0.8s | Nhiều movie quốc tế |
| P3, tùy chọn | kickasstorrents | Mọi loại | HTML | 5/10 tiêu đề, trung vị 60s, có lần treo | Chậm và không ổn định; timeout ngắn |
| Thấp | bangumi.moe, Mikan, dmhy, TokyoTosho | Anime (raw, phụ đề Trung) | API / RSS | 23 / 23 / 321 / 149 (TokyoTosho mất 13s) | Knaben đã gom TokyoTosho. Chỉ thêm nếu cần raw |

## 2. Không làm

| Site | Lý do |
|---|---|
| 1337x (cả mirror), ext.to, uindex, therarbg, yourbittorrent | Chỉ có trang bị Cloudflare hoặc chống bot (403). Nội dung 1337x lấy gián tiếp qua Knaben |
| eztvx (trang HTML) | Bị challenge. Dùng API chính thức thay thế |
| TorrentGalaxy, torrentz2, yts.mx | Domain không phân giải được |
| magnetdl (522), btdig, glodls (timeout), anidex (từ chối kết nối), cloudtorrents (526) | Chết hoặc không ổn định |
| erai-raws RSS | 403, cần token thành viên |
| animetosho JSON feed | 403 hoặc DNS lỗi; dùng HTML |
| onlinefix, redetorrent | Ngoài chuyên môn (game, site Brazil) |

## 3. Hợp đồng chung mỗi indexer phải đáp ứng

1. **Fixture test:** lưu ít nhất 2 phản hồi thật (có kết quả và rỗng) vào `MediaManager.Core.Tests/Fixtures/Indexers/<id>/`. Parser phải ra đúng `IndexerResult` (tiêu đề, dung lượng, seed, infohash hoặc magnet, ngày).
2. **Không tự gọi mạng:** chỉ dùng `IIndexerHttp` do governor cấp.
3. **Khai báo chính sách tần suất mặc định** (xem [03](./03-request-governor.md)): số request mỗi phút, số request đồng thời, ngân sách theo ngày.
4. **Phát hiện challenge:** nhận ra 403, 503 hay trang "Just a moment" và báo `IndexerBlocked`, không parse rác.
5. **Hiểu biến đổi query riêng** (xem [04](./04-query-and-matching.md)): ví dụ SubsPlease chỉ nhận tên show; nyaa dùng `- 05`; EZTV dùng IMDb id thay cho text.
6. **Dòng trong `SearchProbe`:** thêm indexer vào `site_candidates.json` để kiểm định kỳ.

## 4. Cấu hình (`settings.json` → `Search.Indexers`)

```json
{
  "knaben":  { "enabled": true,  "baseUrl": "https://api.knaben.org", "timeoutSeconds": 10, "maxPages": 2 },
  "apibay":  { "enabled": true,  "baseUrl": "https://apibay.org" },
  "nyaa":    { "enabled": true,  "baseUrl": "https://nyaa.si", "maxPages": 3 },
  "bt4g":    { "enabled": false, "trust": "low" }
}
```

Chính sách tần suất mặc định nằm trong code (an toàn), user chỉ được **giảm**, không được tăng vượt mức an toàn.
