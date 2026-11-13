# Nhánh C (special/OVA, phần không gọi model): bàn giao

Nhánh `claude/fix-special-mapping`, tách từ `main` (`ff2caa3`). Chưa push, chưa merge.

| Commit | Nội dung |
|---|---|
| `9ee4ede` | P0-C: `MediaManager.App.Tests/Special/` (fixture show giả, 8 ca mục 10.4, response Gemini giả, test hành vi mong muốn gắn Skip) |
| `4e571e9` | PR-12: PARSE-10, 20, 23 + R5, R10 |
| `98ee6c2` | PR-13: PARSE-09, 18, 19 + R1, R2, R8 |

Kiểm chứng: `dotnet test MediaManager.Core.Tests -c Release` 248/248; `dotnet test MediaManager.App.Tests -c Release -p:Platform=x64` 207 pass, 2 skip (`PARSE-16` chờ PR-14, `PARSE-17` chờ PR-7 của nhánh B).

## Điểm nối khi merge/rebase

- `ISpecialMappingOrchestrator.ResolveAsync`: tham số thứ 3 giờ là **mọi tập của show** (`episodes`), không còn chỉ S00. Orchestrator tự tách S00 và dùng ngày phát sóng các mùa thường để chia khung. Fake bằng `DispatchProxy` (như `TestFake<T>` của nhánh A) không bị ảnh hưởng.
- `IAutoTorrentLinkService`: đã xóa `LinkShowAsync`, `LinkSeasonPackAsync`, `LinkSeasonPackFromInventoryAsync` (và `LinkSeasonPackCoreAsync` private). Test của nhánh A (`d73284d`..`79f7107`) không dùng chúng.
- `AutoTorrentLinkService.cs`: chỉ sửa `PrepareSeasonPackLinkCoreAsync` (bước special được bọc trong `ResolveSpecialMappingsAsync`, `RequiresReview` theo `SpecialMappingReviewPolicy`, thêm `UsedGeminiForSpecials`) và xóa 3 API trên. Không đụng link/reset/refresh.
- `LibraryViewModel.RunPackLinkAsync` viết lại (token hủy, `ApplyPackLinkAsync`, `MarkPackLinkCancelled`). Nhánh A chỉ sửa `ResetEpisode`/`ResetMovie` nên không chồng.
- `SeasonPackLinkPreview.UsedGeminiForSpecials` mới (init, mặc định false).
- `SpecialMappingProposalReason` thêm `Window`, `Inferred`, `OvaNumber` ở **cuối** enum (cache lưu enum dạng số).
- `GeminiJsonTextResult` thêm tham số tùy chọn `FinishReason`.

## Cho nhánh B (PR-7, PARSE-17)

Ca `rooftop-complete-minis` (32 mini `Specials/... - Mini Anime 01..32`) hiện **không** được tự áp vì `LocalIndex` chỉ đọc chữ số cuối (trùng số → window mapper từ chối, an toàn). Sau khi `PackEpisodePatternInferrer.Infer` không còn trả pattern trượt validate, bỏ `Skip = "PARSE-17"` ở `SpecialRuleMappingTests.CompleteMiniSet_RulesOnly_AllThirtyTwoResolve`; kỳ vọng 32/32.

Ca `cafe-later-shorts` (release chỉ gồm short đặt tên `- 01..06`) không thành ứng viên special, thuộc PR-8 (báo cáo 10.4 ý 5).

## Kết quả rules-only trên 8 ca (show giả, cùng cấu trúc S00 thật)

| Ca | File | Đúng | Sai | Để Gemini/review |
|---|---|---|---|---|
| brass-two-season-pack | 15 | 8 (S1, gồm OVA "inferred") | 0 | 7 (S2, có gợi ý E9–E15 không áp) |
| rail-untitled / rail-titled | 9 / 9 | 0 | 0 | 9 / 9 |
| cafe-bd-shorts | 4 | 0 | 0 | 4 |
| cafe-later-shorts | 6 | 0 | 0 | không phải ứng viên special |
| rooftop-complete-minis | 32 | 0 | 0 | 32 (chờ PARSE-17) |
| archive-s1-ovas | 2 | 2 | 0 | 0 |
| club-ova-per-season | 3 | 3 | 0 | 0 |
| recap-before-season-one (kịch bản I) | 2 | 1 | 0 | 1 |
