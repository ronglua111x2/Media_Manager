# Gemini special-mapping eval — tiến độ

Phiên: `build-ci-audit-b8a0b0` (worktree `.claude/worktrees/build-ci-audit-b8a0b0`), nhánh `claude/gemini-special-eval-096063`, tách từ `afdbfea` (prompt v3). Không push.

## Đã xong
- `f5297e7` tool `media management app/media management app/tools/GeminiSpecialEval/` (console, ngoài slnx/CI/`dotnet test`). README trong thư mục đó.
- Selftest (0 request): oracle 44/44 đúng, shifted (+1) 37 sai im lặng + 7 bị validator chặn, silent 44 bỏ trống. Prompt v2 đóng băng khớp hash commit `92e6b58` (`dcc995eb…`).
- Rehearsal `--offline-responder` (0 request thật, data dir trong scratchpad): ledger, cap, retry 503, replay, stale đều chạy đúng.

## Dữ liệu
- Data dir: `gemini-eval/` (cùng thư mục này). `quota.json` = sổ request của eval theo ngày Pacific (reset 15:00 giờ VN).
- Key: không có biến môi trường; dùng `--key-from-settings <StateFolder>\settings.json` (chỉ đọc; app lưu key dạng text thường, không DPAPI).

## Kế hoạch lần chạy đầu
- 9 case gọi Gemini (46 file/ô) × v2,v3 × 3.5 Flash-Lite, 3.1 Flash-Lite = 36 request (cap 100/ngày).
- Sau đó ≤5 request trên 3.8 Flash (model đầu tiên của router app, 20/ngày).

## Kết quả (ngày Pacific 2026-11-12)
- `7fc4d85` thêm `model@thinking` và ghi `thinkingConfig` đã gửi.
- Run `20261113-023724-flashlite-v2v3`: 37 request (1 lần retry 503). Sai im lặng: 3.1 FL v2 10, v3 7; 3.5 FL v2 9, v3 9 (trên 44 file thật mỗi ô). Validator không chặn cặp nào. Lỗi chủ yếu: đánh số theo thứ tự (OVA 01..04 → E1..E4), rơi vào recap/promo.
- Run `20261113-024300-hard-cases-stronger`: 3.8 Flash hết 5/5 request (2 timeout 90 s, 3 × 503), không có câu trả lời. 3.5 FL@medium: gửi `thinkingLevel: medium` nhưng `thoughtsTokenCount` trống → không suy nghĩ; vẫn sai giống minimal.
- Run `20261113-025223-flashlite-stability`: 35 request (3 lần retry 503), 2 mẫu thêm trên 4 case v2/v3 lệch nhau.
- `ba4ddce` báo cáo có phần so cặp prompt trên cùng request (sign test). v2 → v3: 34 cặp, v3 tốt hơn 11, v2 tốt hơn 2, p = 0,022; riêng mẫu mới 6:1, p = 0,125.
- Tổng ngày Pacific 2026-11-12: 3.5 FL 38, 3.1 FL 38, 3.8 Flash 5 (hết cap). Quét 207 file: không file nào chứa key.

## Xong (13/11/2026)
- Phân tích tiếng Việt: `gemini-eval/first-run-analysis-2026-11-13.md`. Báo cáo gộp: `gemini-eval/reports/20261113-030143-replay-3-runs.md`.
- Test thường vẫn xanh: App.Tests 436/436, Core.Tests 405 pass + 4 skip (skip có sẵn).
- Chưa làm: case thật (cần user), 3.6/3.8 Flash ngày sau, kiểm `thinkingLevel` không có tác dụng trên Flash-Lite.

## Sửa ở tầng rule (13/11/2026, theo yêu cầu user)
- Nhánh `claude/special-ova-ordinal-rule` (tách từ `ba4ddce`), commit `ab5bd15`. Chưa push.
- `SpecialSeasonWindowMapper.MapByKind`: lượt 2 sau title rule, "file thứ k → mục thứ k, đúng số lượng" trên cửa sổ đã lọc: file OVA chỉ đếm mục có tiêu đề OVA/OAD (`OvaOrder`); còn lại bỏ mục tiêu đề recap/digest/preview/promo/commentary/interview/trailer/teaser/PV/CM (`WindowWithoutAsides`). Số phải đúng 1..m; file đã được title rule đặt phải trùng ô của nó, không thì bỏ cả nhóm. Enum thêm ở cuối. Review: "OVA order" / "Window, recaps skipped", vẫn tô đỏ; chính sách review không đổi.
- Fixture brass: E16–E19 đổi tiêu đề giống list thật (phim và promo không có chữ recap/promo), nên S2 vẫn đi Gemini như pack thật.
- Test: App 445/445 (+9), Core 405 + 4 skip. Eval `list`: root-ova ×2, rail ×2, recap-before-season-one, SP01–04 của blooper do rule đặt, 0 sai. Trên đúng các câu trả lời đã ghi, 29/32 link sai của v3 rơi vào file nay do rule đặt.
- Replay 3 run cũ: 58 mẫu bị đánh dấu stale (payload đổi), đúng như mong đợi; báo cáo `reports/20261113-031735-replay-3-runs-after-ova-rule.md`.
