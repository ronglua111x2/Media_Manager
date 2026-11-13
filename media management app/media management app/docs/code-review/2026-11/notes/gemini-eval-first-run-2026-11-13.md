# Đo lần đầu: Gemini map special/OVA, prompt v2 và v3 (13/11/2026)

Báo cáo đầy đủ do tool sinh (tiếng Anh): `reports/20261113-030143-replay-3-runs.md` (gộp 3 run, chấm lại bằng replay, 0 request). Từng run có `runs/<id>/report.md`. Tool: nhánh `claude/gemini-special-eval-096063`, commit `f5297e7`, `7fc4d85`, `ba4ddce`.

## Đã gửi bao nhiêu request (ngày Pacific 2026-11-12, reset 15:00 giờ VN)

| Model | Request | Ghi chú |
|---|---|---|
| gemini-3.5-flash-lite | 38 | gồm 4 request `@medium` |
| gemini-3.1-flash-lite | 38 | gồm 3 lần thử lại sau 503 |
| gemini-3.8-flash | 5 (hết cap 5/ngày) | 0 câu trả lời: 2 timeout 90 s, 3 × 503 |
| Tổng | 81 | Flash-Lite 76/100 cap của eval; key đọc chỉ-đọc từ settings.json của app, không có trong file nào (đã quét 207 file) |

## Bảng chính (chỉ các file Gemini được hỏi; 9 case gọi Gemini, 4 case rule giải hết)

| Model | Prompt | Mẫu | Đúng | **Sai im lặng** | Validator bỏ | Bỏ trống | Tỉ lệ sai (95% ≤) |
|---|---|---|---|---|---|---|---|
| 3.1 Flash-Lite | v2 | 17 | 62 | **20** | 0 | 0 | 24,4% (34,7%) |
| 3.1 Flash-Lite | v3 | 17 | 67 | **15** | 0 | 0 | 18,3% (28,0%) |
| 3.5 Flash-Lite | v2 | 17 | 66 | **19** | 0 | 0 | 22,4% (32,3%) |
| 3.5 Flash-Lite | v3 | 17 | 65 | **17** | 0 | 0 | 20,7% (30,7%) |
| 3.5 Flash-Lite @medium | v3 | 4 (4 case khó) | 9 | **8** | 0 | 0 | không suy nghĩ thật (xem dưới) |
| 3.8 Flash | v3 | 0 | – | – | – | – | không có câu trả lời |

Bộ case nghiêng về case khó: 4 case viết riêng cho eval (OVA ở thư mục gốc, đánh số quanh cặp cố định, file không có trong TMDB) và 2 mẫu thêm chỉ trên 4 case mà v2/v3 lệch nhau. Trên 5 fixture "hình dạng thật" (brass, rail ×2, cafe, recap), Flash-Lite v3 sai 1–3 trên 48 link (3.1: 1, 3.5: 3); v2 sai 4 trên 48 ở cả hai model.

## v3 so với v2

- So cặp trên cùng request (run × case × model × mẫu): 34 cặp; v3 ít sai hơn ở 11, v2 ít sai hơn ở 2, bằng nhau 21; tổng sai 39 → 32; sign test p = 0,022.
- Chỉ tính mẫu mới (run stability, mẫu lấy sau khi chọn case): v3 tốt hơn 6, v2 tốt hơn 1, p = 0,125. Cùng hướng, nhưng riêng mẫu mới thì chưa đủ chắc.
- v3 giúp ở: file không có trong TMDB (3.5 FL v2 gán blooper reel vào "Director Interview" cả 3 lần, v3 bỏ trống cả 3 lần); khoảng recap trong dãy mini (rail-untitled); S1S04 ở `season-specials-fixed-middle`.
- v3 không giúp ở đúng thứ nó nhắm tới (thứ tự trong thư mục gốc, thứ tự quanh `ctx.fx`): model vốn đã giữ thứ tự; cái sai là chọn **mục nào** (xem dưới). Ở `root-ova-fixed-middle` trên 3.5 FL, v3 còn kém hơn (0/9 so với 4/9).
- Kết luận: giữ v3. Lợi ích có thật nhưng nhỏ (ít hơn khoảng 18% link sai), và không đến từ quy tắc thứ tự.

## Kiểu lỗi chính: "trượt theo số thứ tự"

Gần như mọi link sai có cùng một dạng: file thứ N được gán vào số tập còn trống thứ N, bỏ qua tiêu đề TMDB cho thấy mục đó là recap, drama CD, promo, phỏng vấn.
- `OVA - 01..04` ở thư mục gốc → E1..E4 (E1 là "Recap Special", E4 là "Picture Drama Collection"); đúng là E2, E3, E5, E6. Sai cả 4 file, ở mọi model, mọi prompt, mọi mẫu.
- `S01S01` → E1 "Recap Before the Premiere" thay vì E2 (case kịch bản I của audit): sai ở mọi ô.
- `SP09` → E9 recap thay vì E10 "Mini Anime 09".

**Validator không chặn được cặp sai nào** (0 cặp bị bỏ trong 81 request): các cặp sai vẫn đúng thứ tự, đúng cửa sổ mùa, không trùng tập. Selftest cho thấy cùng điều đó: câu trả lời lệch +1 lọt qua 37/44 lần. Kiểm tra cứng hiện tại không phải lưới an toàn cho kiểu lỗi này.

Tầng rule: 0 link sai trên cả 13 case (6 file `cafe-later-shorts` không được nhận là special; giới hạn đã biết của builder).

## Hai điều phát hiện thêm

1. **3.8 Flash (model đầu tiên của router app) không trả lời lần nào**: 2 timeout đúng 90 s, 3 × 503 trong khoảng 10 phút. Hôm qua probe cũng 503 hai lần. Trong app, mỗi lần AI Link có thể phải chờ tới 90 s rồi mới rơi xuống model sau.
2. **`thinkingLevel: medium` trên 3.5 Flash-Lite không làm model suy nghĩ**: request ghi rõ `{"thinkingLevel":"medium"}` nhưng response không có `thoughtsTokenCount`, độ trễ vẫn ~1,2 s. Dòng `@medium` vì vậy chỉ đo nhiễu giữa các mẫu, không đo tác dụng của suy nghĩ. Chưa rõ do Flash-Lite bỏ qua hay do cách gửi (chữ thường); cần 1–2 request để kiểm.

## Auto-Track "đặt một lần rồi quên"

Chưa thể tự áp dụng mà bỏ màn review: tỉ lệ link sai im lặng của Flash-Lite là khoảng 18–24% trên bộ case này, 2–8% trên các fixture giống pack thật, và validator không bắt được kiểu sai chính. Một ngưỡng hợp lý trước khi bật: model + prompt có **0 link sai trên ít nhất 150 link** của một bộ có pack thật (cận trên 95% khoảng 2,5%), hoặc 300 link để xuống khoảng 1,3%.

## Đề xuất bước tiếp (đều đo được bằng tool này)

1. **Sửa ở tầng rule trước** (an toàn hơn Gemini): file `OVA - N` cùng một thư mục ↔ mục thứ N có tiêu đề chứa OVA/OAD theo ngày phát, khi số lượng khớp; dãy SP/Mini bỏ qua mục có tiêu đề Recap/Digest/Preview/Promo/Commentary khi số mục còn lại khớp số file. Hai case `root-ova-*` sẽ không cần Gemini.
2. **Prompt v4** nhắm đúng lỗi trượt: nói rõ S00 có recap, preview, promo, drama CD; "OVA N" là mục OVA thứ N chứ không phải số trống thứ N; tiêu đề cho biết loại mục; nghi ngờ thì bỏ trống. Đo v4 so với v3.
3. **Cờ review mềm**: cặp Gemini rơi vào tiêu đề kiểu Recap/Preview/Promo/Interview trong khi file là OVA/SP có số → tô đỏ trong màn review (không bỏ).
4. **Đo theo "đồng ý với gợi ý"**: tách tỉ lệ sai của các cặp Gemini trùng gợi ý cửa sổ mùa (app đã tính `MatchesSuggestion`). Nếu nhóm này gần 0 sai, chỉ nhóm đó mới là ứng viên tự áp dụng.
5. Ngày Pacific sau (sau 15:00 VN): 3.6 Flash và thử lại 3.8 Flash, mỗi model ≤5 request trên 4 case khó; 1–2 request kiểm `thinkingLevel` trên Flash-Lite.
6. Case thật: cần user chọn vài pack thật và xác nhận đáp án từng file; case chỉ nằm trong `gemini-eval/cases/` (định dạng trong README của tool), không vào repo.

## Cập nhật: đã sửa ở tầng rule (đề xuất 1)

Nhánh `claude/special-ova-ordinal-rule`, commit `ab5bd15` (chưa push). Rule mới đếm file OVA/SP theo các mục cùng loại trong cửa sổ mùa (mục có tiêu đề OVA; hoặc bỏ mục recap/preview/promo…), chỉ khi số lượng khớp đúng. Trên 13 case: root-ova ×2, rail ×2, recap trước mùa 1 và SP01–04 của case blooper nay do rule đặt, 0 sai. Trong 32 link sai của v3 ở lần đo này, 29 rơi vào các file đó (3.1 FL: 13/15, 3.5 FL: 16/17). Đây là phép chiếu trên bộ case tổng hợp (có 4 case viết quanh đúng lỗi này), chưa phải số trên pack thật. Pack brass vẫn đi Gemini vì tiêu đề thật của phim/promo không có chữ recap/promo.
