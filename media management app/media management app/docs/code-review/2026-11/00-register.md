# Findings Register tổng hợp — đợt audit 2026-11

Baseline: `main` @ `ff2caa3`. Mỗi vùng có báo cáo riêng trên nhánh `claude/<vùng>-audit-*`, chưa push và chưa merge.
Thang mức độ theo `docs/code-review/00-method-and-scope.md`. Register này chỉ là chỉ mục: bằng chứng, kịch bản và hướng sửa nằm trong từng báo cáo.

> Bản trên nhánh `audit/2026-11-reports` đã được lọc: đường dẫn máy thay bằng `<StateFolder>`, `<LibraryRoot1>`..., tên show/phim/torrent thật thay bằng `Show E`, `Movie One`, `GroupK`... Bản gốc vẫn nằm trên các nhánh `claude/*-audit-*` ở máy local, **không push các nhánh đó** vì repo đang public.

## 1. Tổng quan

| Vùng | Báo cáo (nhánh @ commit) | Critical | High | Medium | Low | Tổng |
|---|---|---|---|---|---|---|
| Parser → special → link | `parser-audit-5f9a30` @ `cd1556d` (kèm `parser-fix-plan.md`) | 0 | 10 | 14 | 8 | 32 |
| Settings | `settings-audit-4d6a3b` @ `7cd2e91` | 0 | 5 | 4 | 4 | 13 |
| Lifecycle / threading | `lifecycle-audit-ed145a` @ `8e5fcfc` | 0 | 3 | 5 | 6 | 14 |
| Backup / restore | `backup-audit-e0f13c` @ `5bd4d53` | 1 | 2 | 4 | 6 | 13 |
| Xóa / dọn dẹp | `deletion-audit-82f156` @ `69b8e4e` | 0 | 2 | 5 | 4 | 11 |
| Library workspace | `library-audit-a86664` @ `d73d24b` | 0 | 2 | 9 | 7 | 18 |
| Auto-Track | `autotrack-audit-6484b8` @ `a7cf2b5` | 0 | 1 | 8 | 4 | 13 |
| Database | `database-audit-b34d45` @ `1fac932` | 0 | 1 | 7 | 7 | 15 |
| TMDB / Jellyfin / WARP | `integrations-audit-e9519e` @ `eff38c1` | 0 | 1 | 5 | 10 | 16 |
| UI / XAML | `ui-audit-b26564` @ `651fea9` | 0 | 1 | 5 | 12 | 18 |
| Deps / build / CI / test | `build-ci-audit-b8a0b0` @ `699dcf4` | 0 | 0 | 4 | 10 | 14 |
| **Tổng** | | **1** | **28** | **70** | **78** | **177** |

Search của hunt không nằm trong register này. Phần đó đang được viết lại trên `feat/native-search`. Phần cart và add gate (ngoài DEL-02) sẽ audit sau khi nhánh đó merge.

Test hiện có: App 138/138 và Core 248/248 pass (theo phiên deletion-audit).

## 2. Cụm nguyên nhân chung

Nhiều finding ở các vùng khác nhau có cùng một gốc. Sửa theo cụm thì giảm được khoảng 1/3 số việc và tránh hai PR đụng nhau.

| # | Cụm | Finding | Ghi chú |
|---|---|---|---|
| C1 | **Restore thay DB đang chạy** | BAK-01 (Critical), BAK-02, DB-02, AUD-003 | Đã tái hiện thành DB hỏng thật. Sửa một lần: chặn writer, validate, giữ bản cũ, rollback. |
| C2 | **Ổ chưa sẵn sàng bị coi là file đã mất** | LC-02, PARSE-27, DEL-03 | Autostart khi ổ chưa mount có thể xóa symlink Jellyfin và `SourceItems`. Cần một guard "volume available" dùng chung. |
| C3 | **Gỡ link lỗi nhưng vẫn xóa bản ghi** | AUD-005, DEL-04, DEL-07, PARSE-28, PARSE-29 | Để lại link mồ côi, show "ma" trong Jellyfin, trạng thái Available sai. |
| C4 | **Đổi Episode Organization** | AT-10, DB-06, PARSE-26 | Xóa season/tập trước khi gọi TMDB, không transaction, không gỡ link, mất rating. |
| C5 | **TMDB đổi hoặc xóa tập → tập ma** | INT-01, INT-03, INT-04, PARSE-31, AT-02 | Refresh chỉ cộng dồn, lỗi season bị nuốt, Auto-Track săn tập không tồn tại mãi. |
| C6 | **App không khởi động được** | DB-01, LC-01, SET-01, LC-07 | DB cũ (trước 15/06) bị migration 007 chặn; `settings.json` rỗng hoặc hỏng thì crash trước khi crash hook gắn. |
| C7 | **Exception UI làm chết app** | LC-06, UI-02, UI-06, UI-08, UI-09, LIB-10, SET-06 | `DispatcherUnhandledException` không bao giờ `Handled`. Một sửa chung cộng với try/catch tại chỗ. |
| C8 | **`Settings.Current` bị sửa và ghi tùy tiện** | SET-02, SET-03, SET-04, SET-13, DB-03, BAK-03, BAK-04 | Mở trang Settings làm mất cập nhật nền; sửa chưa lưu bị ghi xuống đĩa; mỗi lần `Save()` làm scheduler backup tự kích hoạt (~22 phút một lần). |
| C9 | **Thao tác xóa dữ liệu thật của user** | DEL-01, DEL-02, DEL-05, DEL-10 = INT-09, DEL-11 | Reset xóa bản duy nhất; add gate xóa torrent có sẵn kèm file; "xóa symlink" xóa cả file thật; dọn NFO xóa file Jellyfin ghi. |
| C10 | **Vòng lặp nền không backoff** | AT-01, AT-08, BAK-03, BAK-04, INT-04, INT-06, INT-11 | Retry vô hạn, làm đói show khác, spam backup và TMDB. |
| C11 | **Việc nặng trên UI thread** | LIB-01, LIB-02, UI-04, UI-05, LC-04, LC-05 (AUD-004) | Đơ UI và tắt app chậm. |
| C12 | **Secret** | SET-05, SET-10, BAK-08, BAK-11 | SET-05: TMDB bearer token bị gửi kèm mọi request Gemini (High). |
| C13 | **Phát hành và chuỗi cung ứng** | BLD-01, BLD-02, BLD-03, BLD-04, BLD-05 | .NET 8 hết hỗ trợ từ 10/11/2026; SQLite dính CVE-2025-6965; thiếu LICENSE GPL; `main` không được bảo vệ và **mỗi push lên `main` tự phát hành bản public**. |
| C14 | **Parser và link sai tập** | PARSE-01..16, PARSE-17..25, PARSE-30, PARSE-32 | Đã có kế hoạch riêng: `parser-fix-plan.md` (19 PR, ba nhánh song song, chờ chốt Q1–Q7). |

## 3. Danh sách High và Critical

| ID | Mức | Tóm tắt | Cụm |
|---|---|---|---|
| BAK-01 | Critical | Restore khi có writer làm hỏng DB vừa restore, rồi bản hỏng được backup lên Drive | C1 |
| BAK-02 | High | Restore không validate, không giữ bản cũ, không rollback | C1 |
| BAK-03 | High | Scheduler backup tự kích hoạt lại mình, khoảng 22 phút một lần | C8 |
| SET-01 | High | `settings.json` không durable; file hỏng thì app không khởi động | C6 |
| SET-02 | High | Mở trang Settings thay `Current`, làm mất cập nhật của service nền | C8 |
| SET-03 | High | Sửa chưa lưu bị áp ngay rồi bị `Save()` khác ghi xuống đĩa | C8 |
| SET-04 | High | Đổi State Folder tạo ngay DB rỗng và chạy tiếp trên đó | C8 |
| SET-05 | High | TMDB token gửi kèm mọi request Gemini | C12 |
| LC-01 | High | State folder hoặc settings lỗi làm crash trước khi crash hook gắn | C6 |
| LC-02 | High | Reconcile lúc autostart xóa symlink Jellyfin khi ổ nguồn chưa sẵn sàng | C2 |
| LC-03 | High | Exception trong subscriber `RunCompleted` làm chết vòng TMDB discovery tới hết phiên | C10 |
| DB-01 | High | DB tạo trước 15/06/2026 bị migration 007 chặn khởi động (ảnh hưởng cả khi restore backup cũ) | C6 |
| DEL-01 | High | "Reset Episode/Movie" xóa file trong Library kể cả khi là bản duy nhất | C9 |
| DEL-02 | High | Add gate xóa torrent đã có sẵn kèm file khi validate từ chối | C9 |
| AT-01 | High | Hunt thất bại retry vô hạn không backoff, làm đói show khác | C10 |
| INT-01 | High | Tập TMDB bị xóa hoặc đánh số lại thành tập ma, bị săn mãi | C5 |
| LIB-01 | High | Mỗi lần đổi rating, sort hoặc filter là xóa và dựng lại toàn bộ detail | C11 |
| LIB-02 | High | Dựng detail và quét toàn thư viện cùng ổ đĩa trên UI thread | C11 |
| UI-01 | High | Find/Add: "Add to Library" có thể thêm nhầm phim hoặc crash | — |
| PARSE-01..05, 16, 24..27 | High | 10 finding parser và link, xem `parser-audit.md` | C14, C2, C4 |

## 4. Đề xuất thứ tự sửa

**Đợt 0 — Chặn mất dữ liệu (nhỏ, làm ngay):** C9 (DEL-01, DEL-02, DEL-05), C2 (guard ổ chưa sẵn sàng), BAK-03 (dừng vòng backup), SET-05 (gỡ token khỏi `HttpClient` dùng chung), C1 (khóa restore). Trước khi merge bất kỳ PR nào, xử lý BLD-04 (bảo vệ `main` hoặc tắt auto-release), vì nếu không thì mỗi merge sẽ phát hành một bản public.

**Đợt 1 — Khởi động và crash:** C6, C7, LC-03.

**Đợt 2 — Trạng thái link và metadata:** nhánh A của `parser-fix-plan.md`, cùng C3, C4, C5.

**Đợt 3 — Nền và Settings:** C8, C10, các finding Medium của Auto-Track (AT-02..08).

**Đợt 4 — Parser còn lại (nhánh B, C, D), Library/UI (C11, các finding Medium), build/CI (C13), sau đó tới các finding Low.**

Mỗi cụm nên là một phiên riêng trong worktree riêng, mỗi PR viết test tái hiện trước rồi mới sửa. Các cụm chạm cùng file (C2, C3, C4 và nhánh A của parser đều đụng `SourceReconciliationService`, `HardlinkService` và `Symlink*`) phải chạy tuần tự, không song song.
