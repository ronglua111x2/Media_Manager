# Nhánh C2 (phần gọi Gemini: PR-12b, PR-14, PR-15): bàn giao

Nhánh `claude/fix-special-gemini`, tách từ `claude/fix-special-mapping` (`98ee6c2`, nhánh C). Worktree `.claude/worktrees/gemini-router`. Chưa push, chưa merge, chưa gọi Gemini thật bằng code mới, chưa mở app để thử giao diện. Plan: `track-c2-gemini-plan.md` (cùng thư mục).

| Commit | Nội dung |
|---|---|
| `1973159` | PR-12b: router chọn model tự động (catalog seed v2, bộ đếm theo ngày Pacific, học `quotaValue`, khóa theo `retryDelay`, bộ phân loại lỗi từ response thật, key qua header, ListModels đọc hết trang, timeout 90 giây, R7) |
| `4e477eb` | PR-14: payload v2 chỉ gồm file rule chưa giải; pack rule giải hết thì không gọi Gemini; kiểm tra cứng sau Gemini (S00Exx trong tên, cửa sổ mùa, thứ tự trong mùa); so với gợi ý cửa sổ mùa; prompt v2; câu xác nhận mới |
| `cbb753e` | PR-15: cache khóa v2, chỉ lưu sau Accept, kiểm lại khi đọc, entry cũ chỉ dùng khi khớp rule, lưu bản gọn, ghi model |

Kiểm chứng: `dotnet test MediaManager.App.Tests -c Release -p:Platform=x64` 256 pass, 1 skip (`PARSE-17` chờ PR-7 của nhánh B); `dotnet test MediaManager.Core.Tests -c Release` 248/248. Test PARSE-16 đã bật và pass.

## Điểm nối khi merge
- Merge nhánh C (`claude/fix-special-mapping`) trước, rồi nhánh này.
- `AutoTorrentLinkService.ApplySeasonPackLinkAsync`: từ expression-bodied thành async, gọi thêm `RememberAccepted` sau khi link xong. `ISpecialMappingOrchestrator` có thêm `RememberAccepted` (fake kiểu `DispatchProxy` không bị ảnh hưởng).
- `App.xaml.cs`: DI bỏ `GeminiQuotaTracker`, thêm `TimeProvider.System`, `GeminiModelAvailabilityStore`, `GeminiModelRouter`; `NormalizeGeminiSettings` nâng timeout 45 → 90 và bỏ fallback trỏ vào model deprecated.
- `SettingsViewModel`/`SystemSettingsViewModel`: tham số `GeminiQuotaTracker` → `GeminiModelRouter`. `SettingsView.xaml`: checkbox "Choose the model automatically", phần chọn tay bị khóa khi bật.
- `AppConstants` (vùng Gemini): bỏ `GeminiDailyRequestLimit`, `GeminiDailyWarningThreshold`, `GeminiQuotaStateFileName`, `GeminiMinRequestSpacingMs`, `GeminiMaxRetriesPerModel`, `GeminiRetryBaseDelayMs`; `DefaultGeminiModel` = `gemini-3.5-flash-lite`.
- Phiên chính đang chuyển orphan sang `extras/` (`claude/fix-link-state`): chưa thấy chồng file ngoài `AutoTorrentLinkService.cs`; kiểm khi merge.
- Test đã sửa vì Gemini không còn thấy mùa 1: `FakeGeminiResponses` (DuplicateCandidateIndex, DuplicateS00E dùng S2; thêm SeasonTwoBreaksOrder, SeasonTwoLastDiffersFromSuggestion), `SpecialMappingFailureTests`, `PackLinkReviewTests`, `GeminiClientCancellationTests`.

## Thay đổi trên máy user khi chạy bản mới
- `gemini-models.json` được merge với seed v2 (giữ model user thêm tay hoặc từ ListModels).
- `gemini-quota.json` cũ không còn được đọc (để nguyên trên đĩa); trạng thái mới ở `gemini-router-state.json`.
- Settings: `Gemini.AutoSelectModel` mặc định true; `TimeoutSeconds` 45 → 90; `FallbackModels` = `gemini-2.0-flash` bị thay bằng danh sách mới.
- `gemini-mapping-cache.json`: entry cũ giữ nguyên, chỉ dùng khi khớp rule; entry mới có `KeyVersion` 2.

## Chưa làm / khác plan
- **Kiểm Gemini thật (PR-14, tối đa 6 request)**: chưa chạy, cần user đồng ý. Gợi ý: Show E (tắt tầng 1), Show N, recap, Show T không tiêu đề; router sẽ dùng 3.8 Flash trước (20/ngày).
- **`enum` cho `s00e` trong schema**: chưa làm. `responseSchema` (OpenAPI) app đang dùng không ghi hỗ trợ `enum` cho số nguyên; đổi sang `responseJsonSchema` + `prefixItems` cần 1 request thật để xác nhận, nếu không model trả 400 và router khóa model đó. Hiện validator đã bỏ episode ngoài danh sách và prompt có `ctx.free`.
- Cache: model được lưu trong entry và hiện ở màn review, **không** nằm trong khóa (khi tra cache chưa biết model nào sẽ trả lời). Retry vẫn bỏ qua cache.
- Chưa thử trên giao diện thật: checkbox Settings, dòng trạng thái quota từng model, câu tóm tắt "Answered by …" ở màn review.
