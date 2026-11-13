# Nhánh C2: phần gọi Gemini (PR-12b, PR-14, PR-15) — plan

Người làm: phiên AI Studio billing (`local_026e5375…`), theo yêu cầu của user ngày 12/11/2026.
Nhánh `claude/fix-special-gemini`, tách từ `claude/fix-special-mapping` (`98ee6c2`, nhánh C: P0-C, PR-12, PR-13). Worktree riêng `.claude/worktrees/gemini-router`. Không đụng worktree hay nhánh của phiên khác. Chưa push.

Tài liệu nền (cùng thư mục ghi chú của phiên gốc `local_ff4ed399…`):
- `gemini-model-router-docs.md`: docs Gemini, repo tham khảo, kết quả probe (mục 6), mô hình router (mục 7), theo kịp model mới (mục 8).
- `gemini-probe-2026-11-12/`: response thật (400 key sai, 429 ngày, 429 limit 0, 503, 200 trên 3.x) dùng làm fixture.
- `aistudio-billing.md`: billing, 402, free tier.

Thứ tự: PR-12b → PR-14 → PR-15, mỗi PR một commit, test xanh trước khi sang PR sau.

**Trạng thái 12/11/2026: đã xong cả ba** (`1973159`, `4e477eb`, `cbb753e`). Xem `track-c2-gemini-handover.md`.

## PR-12b — router chọn model (PARSE-22, R6, R7)

Thay `GetModelChain()` + `GeminiQuotaTracker` (trần 1.500/ngày theo giờ UTC, sai) bằng:

| File mới (`Services/Gemini/Routing/`) | Việc |
|---|---|
| `GeminiErrorClassifier` | response lỗi → `Outcome`. Port logic `classifyGoogleError` của gemini-cli (Apache-2.0, ghi nguồn), thêm `quotaValue`, 400 `API_KEY_INVALID`, 402 |
| `GeminiModelAvailabilityStore` | trạng thái theo model: `RequestsToday` (ngày Pacific), `LearnedRpd`, `LockedUntil` + lý do, mốc request gần nhất; lưu `gemini-router-state.json` trong state folder; `IClock` để test |
| `GeminiModelRouter` | `Select()` theo thứ tự catalog, chờ RPM ≤ 12 giây; `Report(model, outcome)` |

Catalog: `GeminiModelEntry` thêm `Rank`, `FreeRpm`, `FreeRpd`, `Thinking` (Level/Budget/None + giá trị), `RetireAfter`. Seed mới (thứ tự): 3.8 Flash `low` (5/20) → 3.6 Flash `minimal` (5/20) → 3.5 Flash-Lite `minimal` (15/500) → 3.1 Flash-Lite `minimal` (15/500, ngừng sớm nhất 7/5/2027) → 2.5 Flash-Lite không thinking (10/20). Đánh dấu tắt: 2.0 Flash, 2.0 Flash-Lite, 2.5 Flash (chuyển hướng 1/12/2026), 2.5 Pro, 3.1 Pro preview, Gemini 4. Thêm `SeedVersion`: file catalog cũ trên máy user được merge với seed mới (giữ phần user sửa).

`GeminiApiClient`:
- Vòng `Select → request theo model (thinkingConfig đúng loại) → gửi → Classify → Report`; tối đa 3 model mỗi lần gọi.
- Key qua header `x-goog-api-key` (cả ListModels), không còn trong URL.
- Xử lý: 200 trả kết quả; 429 ngày → học `quotaValue`, khóa `retryDelay`, sang model sau; 429 "limit: 0" → `NoFree`; 429 phút → khóa `retryDelay`; 503/timeout → khóa 60 giây, sang model sau; 404 → `Gone`; 400 khác key → `Incompatible`; 400 key sai / 402 / 403 → dừng hẳn, message gốc; 500/502/504 → thử lại 1 lần có jitter.
- Timeout mặc định 90 giây (settings cũ 45 được nâng lên khi load).
- Không còn ngoại lệ "1.500 request/ngày".

Settings: thêm `GeminiSettings.AutoSelectModel` (mặc định true). Bật thì bỏ qua `Model`/`FallbackModels`. Màn Settings: một checkbox "Tự chọn model" và dòng trạng thái mỗi model (còn bao nhiêu request hôm nay, đang khóa vì gì, tới mấy giờ). Màn chọn tay giữ nguyên, chỉ dùng khi tắt Tự động. Model lạ không bị thay ngầm bằng mặc định (R7): giữ và log cảnh báo.

Test (không gọi mạng): fixture JSON từ probe, HTTP giả, đồng hồ giả. Kiểm: chọn đúng model đầu; 429 ngày → sang model sau và khóa tới đúng giờ; học trần 20; 429 limit 0 → bỏ model; 503 → sang model sau, mở lại sau 60 giây; key sai → dừng, chỉ 1 request; 402/403 → dừng; đếm cả request lỗi; không quá 3 model mỗi lần; request 3.x có `thinkingLevel`, 2.5 không có; key nằm trong header, không nằm trong URL.

## PR-14 — Gemini chỉ cho phần còn lại (PARSE-16, PARSE-21, SPEC-10)

Như `parser-fix-plan.md` (payload v2, kiểm tra cứng sau Gemini, mỗi pack một request, đề xuất tầng 1 chỉ dùng để so, `SpecialPromptVersion`), cộng các điều chỉnh từ research:
- Schema giới hạn `s00e` bằng `enum` số nguyên: chỉ JSON Schema (`responseJsonSchema`) ghi rõ hỗ trợ `enum` cho integer. Đổi field này cho riêng request special, kiểm bằng 1 request thật trên 3.5 Flash-Lite trước khi chốt.
- Header key đã làm ở PR-12b.
- Kiểm Gemini thật (tối đa 6 request) chạy trên Flash-Lite 500/ngày, không trên 2.5 Flash-Lite. **Hỏi user trước khi chạy.**

## PR-15 — cache (PARSE-11, SPEC-09)

Như `parser-fix-plan.md`: key cache gồm `SpecialPromptVersion` và model; chỉ lưu sau Accept; cache trúng thì chạy lại kiểm tra cứng; entry v1 được kiểm rồi giữ; lock `_entries`; bỏ `Overview`/`Thought`. Cộng: ListModels đọc hết các trang (`pageToken`), ghi `modelVersion` trả về vào cache và màn review.

## Điểm nối khi merge
- Dựa trên nhánh C chưa merge: merge nhánh C trước, rồi nhánh này.
- Phiên chính đang chuyển file orphan sang `extras/` (nhánh `claude/fix-link-state`): file mà review không map được sẽ đi vào đó. Không dự kiến chồng file; kiểm lại `AutoTorrentLinkService.cs` khi merge.
- Settings: `GeminiSettings` thêm field; `NormalizeGeminiSettings` trong `App.xaml.cs` đổi.
