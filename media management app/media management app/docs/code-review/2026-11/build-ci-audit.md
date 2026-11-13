# Audit: dependencies, build, CI/release và test (2026-11)

- Ngày: 2026-11-12
- Nhánh: `claude/build-ci-audit-b8a0b0` (worktree), baseline `ff2caa317bbd8936ee827e264fd92589523e0358` (= `main`, tag `v1.0.6`)
- Phạm vi: dependency, license, build Release x64 single-file, ba workflow + composite action, script cài đặt, test suite, lịch sử git của file build/CI.
- Thang mức độ: [00-method-and-scope.md](../00-method-and-scope.md). Audit chỉ đọc: không sửa code, cấu hình, workflow hay cài đặt repo trên GitHub. Mọi build, test và publish đều xuất ra `bin/`, `obj/` của worktree hoặc thư mục scratch.

## Tóm tắt

| ID | Mức độ | Finding | Độ tin cậy |
|---|---|---|---|
| BLD-01 | Medium | Runtime .NET 8 đi kèm bản phát hành đã hết hỗ trợ từ 2026-11-10 | High |
| BLD-02 | Medium | SQLite đi kèm dính CVE-2025-6965 và không có bản vá trong nhánh SQLitePCLRaw 2.1.x | High |
| BLD-03 | Medium | Binary công khai chứa code GPL-3.0 nhưng repo không có LICENSE cho toàn bộ tác phẩm | High (về sự kiện; không phải tư vấn pháp lý) |
| BLD-04 | Medium | `main` không được bảo vệ trong khi docs nói có, và mỗi push lên `main` tự phát hành bản public | High |
| BLD-05 | Low | Auto-release mặc định phát hành khi gặp lỗi, và nếu release lỗi sau khi tag thì tag bị bỏ lại | High |
| BLD-06 | Low | Bản phát hành không ký số và không có provenance; checksum chỉ chống hỏng khi truyền tải | High |
| BLD-07 | Low | Action chỉ pin theo tag có thể đổi, repo cho phép mọi action, `ci.yml` không khai báo `permissions` | High |
| BLD-08 | Low | Restore/CI không hiện lỗ hổng transitive (NuGetAudit chỉ xét direct trên net8) | High |
| BLD-09 | Low | `.gitignore` vẫn che source thật: `scripts/`, `docs/debug/`, `tools/PackResolverSmoke/` | High |
| BLD-10 | Low | THIRD_PARTY_NOTICES chỉ có Sonarr; thiếu notice MIT/BSD/Apache của package và runtime đi kèm | High |
| BLD-11 | Low | Dependency không dùng hoặc nặng không cần thiết; exe 245 MB | High |
| BLD-12 | Low | Bộ công cụ test cũ hoặc deprecated (xunit 2.5.3, Test SDK 17.8.0); FluentAssertions 8 dùng license Xceed | High |
| BLD-13 | Low | CI trên `main` tự huỷ khi có hai lần merge liên tiếp | High |
| BLD-14 | Low | Docs về build/CI lệch thực tế; IL3000 cố ý nhưng không được ghi chú | High |
| AUD-007 | Medium (cập nhật) | Vẫn tái hiện. **Đã tìm ra nguyên nhân gốc là TFM `10.0.17763` < `10.0.19041`** | High |

Không có Critical. Không có finding High.

Những điểm ổn, ghi lại để không phải kiểm lại:

- Không file rác nào bị track (`nul`, `debug-*.log`, `*.csproj.user`, `TestResults`, `bin/`, `obj/`, `*.trx`).
- Workflow không dùng `pull_request_target` và không dùng secret ngoài `github.token`. Quyền mặc định của token trong repo là `read`.
- Giá trị do người dùng nhập (`inputs.tag`) đi vào script qua biến môi trường `TAG`, không nội suy thẳng vào lệnh.
- Release kiểm tra tag phải nằm trên `main` và version trong exe phải khớp tag.
- Code first-party không có warning nullable. Toàn bộ 13 warning CS86xx nằm trong `ThirdParty/Sonarr.Parser`.
- Test được cô lập tốt: dùng fake, ghi vào thư mục temp riêng và dọn sau khi chạy. Không test nào gọi mạng thật, đụng state folder thật hay registry.

---

## 1. Kết quả lệnh

Môi trường: Windows 11, SDK `10.0.401` (thoả `global.json` 10.0.400 + `latestFeature`), runtime 8.0.31 / 10.0.12.

| Lệnh | Kết quả | Thời gian |
|---|---|---|
| `dotnet restore "media management app.slnx"` | OK; 6× `NU1701` (3 package × app + App.Tests) | ~3 s (cache ấm) |
| `dotnet build slnx -c Release -p:Platform=x64 --no-restore` | 0 lỗi, 24 warning (18 NU1701, 13 CS86xx trong Sonarr.Parser, IL3000 ×2 do wpftmp) | 13 s |
| `dotnet test slnx -c Release -p:Platform=x64 --no-build --logger trx` | **386 pass / 0 fail / 0 skip** (Core 248, App 138) | 13 s wall; tổng thời lượng các test 20,5 s |
| `dotnet publish app.csproj -c Release -p:Platform=x64` | OK; một exe **245 MB** + `Assets/notifications/*.png` | 12 s |
| `dotnet list slnx package --vulnerable --include-transitive` | xem 1.1 | |
| `dotnet list slnx package --deprecated --include-transitive` | `SQLitePCLRaw.lib.e_sqlite3 2.1.11` (CriticalBugs); `xunit 2.5.3` + 4 package xunit.* (Legacy) | |
| `dotnet list slnx package --outdated --include-transitive` | xem 1.3 | |

Warning first-party duy nhất: `Services/BundleExtractionGuard.cs(19,35) IL3000` (xem BLD-14).

### 1.1 Lỗ hổng (`--vulnerable`)

| Package (transitive) | Bản | Advisory | Project | Có ảnh hưởng app đã ship không |
|---|---|---|---|---|
| `SQLitePCLRaw.lib.e_sqlite3` | 2.1.11 | GHSA-2m69-gcr7-jv3q / **CVE-2025-6965** (SQLite < 3.50.2, memory corruption khi số aggregate term vượt số cột) | cả 4 | **Có.** `e_sqlite3.dll` nằm trong bundle. Xem BLD-02 |
| `System.Drawing.Common` | 4.7.0 | GHSA-rxg9-xrhp-64gj / CVE-2021-24112 (Critical) | app, App.Tests | **Không.** Do `Microsoft.Toolkit.Uwp.Notifications 7.1.3` kéo vào, nhưng `deps.json` của bản build win-x64 resolve `System.Drawing.Common.dll` từ runtime pack WindowsDesktop **8.0.0.0 / 8.0.3126**. Package 4.7.0 không được ship. Cảnh báo này là dương tính giả |
| `System.Net.Http` | 4.3.0 | CVE-2018-8292 | Core.Tests, App.Tests | Không. Chỉ có trong test (Test SDK 17.8.0); trên net8 assembly của framework được ưu tiên |
| `System.Text.RegularExpressions` | 4.3.0 | CVE-2019-0820 | Core.Tests, App.Tests | Không. Lý do như trên |

### 1.2 Kiểm tra lại AUD-007

Vẫn tái hiện y hệt: `NU1701` cho `OpenTK 3.3.1`, `OpenTK.GLWpfControl 3.3.0`, `SkiaSharp.Views.WPF 3.119.0`. **Nguyên nhân gốc** (mới):

- `SkiaSharp.Views.WPF 3.119.0` có `lib/net462`, `lib/net6.0-windows10.0.19041`, `lib/net8.0-windows10.0.19041`.
- App target `net8.0-windows10.0.17763.0`. Vì 17763 < 19041 nên asset net8-windows **không áp dụng được**, và NuGet lùi về `net462`.
- Nhóm dependency `.NETFramework4.6.2` trong nuspec kéo `OpenTK 3.3.1` (`lib/net20`) và `OpenTK.GLWpfControl 3.3.0` (`lib/net452`). Nhóm `net8.0-windows10.0.19041` thì kéo `OpenTK 4.3.0` / `GLWpfControl 4.2.3` (bản .NET).
- `LiveChartsCore.SkiaSharpView.WPF 2.0.5` có cả `net8.0-windows7.0` lẫn `net8.0-windows10.0.19041`, nên chính nó không bị lệch.

Hướng sửa: đổi `TargetFramework` của app (và App.Tests) thành `net8.0-windows10.0.19041.0` (hoặc `net10.0-windows10.0.19041.0`, xem BLD-01), **giữ nguyên** `SupportedOSPlatformVersion` 10.0.17763.0 để không nâng yêu cầu hệ điều hành. Sau đó kiểm tra lại trang Stats/chart và toast notification. Đây là thay đổi một dòng và có thể làm cùng BLD-01. Nên cập nhật AUD-007 trong `03-findings-register.md` theo phân tích này.

### 1.3 Package cũ (`--outdated`, rút gọn)

| Package | Đang dùng | Mới nhất | Ghi chú |
|---|---|---|---|
| Microsoft.Data.Sqlite / Microsoft.Extensions.* | 10.0.8 | 11.0.0 | Bản 11.0.0 **chỉ có net10.0** và kéo SQLitePCLRaw 3.0.5. Không lên được khi còn ở net8 |
| SQLitePCLRaw.* | 2.1.11 | 3.0.5 (lib 3.53.3) | Liên quan BLD-02 |
| SkiaSharp.* / HarfBuzzSharp | 3.119.0 / 8.3.1.1 | 4.156.0 / 14.4.0.100 | Phải đi theo LiveCharts, không nâng riêng |
| Microsoft.Web.WebView2 | 1.0.3967.48 | 1.0.4314.35 | SDK loader; runtime là Evergreen trên máy |
| Google.Apis.* | 1.75.0 | 1.78.0 | |
| MaterialDesignThemes/Colors | 5.3.2 | 5.4.0 | |
| MinVer | 6.0.0 | 8.0.0 | Chỉ ảnh hưởng lúc build |
| xunit / runner / Test SDK / coverlet | 2.5.3 / 2.5.3 / 17.8.0 / 6.0.0 | 2.9.3 / 4.0.1 / 18.12.0 / 10.1.0 | BLD-12 |

### 1.4 Lệch TFM

- App: `net8.0-windows10.0.17763.0`. Core, Core.Tests, Sonarr.Parser: `net8.0`. App.Tests: `net8.0-windows10.0.17763.0`. SDK: 10.0.4xx.
- `Microsoft.Extensions.* 10.0.8` và `Microsoft.Data.Sqlite 10.0.8` đều có `lib/net8.0`, nên chạy trên net8 là **được hỗ trợ**. Đi kèm là các assembly 10.x app-local (`System.Diagnostics.DiagnosticSource`…). Self-contained nên không xung đột.
- SDK 10 build net8 cũng không có vấn đề gì. Vấn đề thật là vòng đời runtime (BLD-01) và việc bản sửa SQLite chỉ có trên net10 (BLD-02).

### 1.5 Package trùng hoặc không dùng

| Package | Hiện trạng |
|---|---|
| `MahApps.Metro.IconPacks.Material` 6.2.1 | **Không dùng.** Chỉ xuất hiện trong csproj, không có `PackIconMaterial*` trong `.cs` hay `.xaml` nào. Tốn 3,6 MB trong bundle |
| `WPF-UI` 4.3.0 | Chỉ dùng một API: `Wpf.Ui.Appearance.ApplicationThemeManager.Apply` (`Services/ThemeService.cs:10-65`). DLL 6,3 MB, kéo theo `Microsoft.Xaml.Behaviors.Wpf` cũng không dùng |
| `Microsoft.Data.Sqlite` | Khai báo ở cả app lẫn Core (app đã có qua ProjectReference). Thừa nhưng vô hại |
| `Google.Apis.Auth` | Khai báo trực tiếp, trùng với dependency transitive của `Google.Apis.Drive.v3`, cùng version. Vô hại |
| `Microsoft.Toolkit.Uwp.Notifications` 7.1.3 | Thuộc Windows Community Toolkit 7 (đã ngừng phát triển). Là nguồn của cảnh báo System.Drawing.Common 4.7.0 |
| `coverlet.collector` 6.0.0 | Có trong Core.Tests nhưng CI không chạy `--collect`, nên hiện không dùng |
| `UseWindowsForms` | Có dùng (tray `NotifyIcon`, folder dialog, `SystemInformation.PowerStatus`) |

### 1.6 Native DLL trong single-file

`IncludeNativeLibrariesForSelfExtract=true` gom cả native vào exe, và khi chạy sẽ giải nén ra `%TEMP%\.net\media management app\<hash>\`. Bundle chứa: `e_sqlite3.dll`, `libSkiaSharp.dll` (10,9 MB), `libHarfBuzzSharp.dll`, `WebView2Loader.dll`, cùng các DLL native của runtime WPF (`PresentationNative_cor3`, `wpfgfx_cor3`, `PenImc_cor3`, `D3DCompiler_47_cor3`, `vcruntime140_cor3`). `SkiaSharp.NativeAssets.macOS` có trong graph restore nhưng không được copy vì RID là win-x64. Runtime pack đi kèm: `Microsoft.NETCore.App.Runtime.win-x64` và `WindowsDesktop.App.Runtime` **8.0.31**.

Lịch sử PR #5 và #6 cho thấy việc giải nén vào `%TEMP%` từng gây crash (Storage Sense xoá `PenImc_cor3.dll`). PR #6 xử lý bằng cách giữ handle mở cho mọi DLL đã giải nén (`BundleExtractionGuard`). Cách này hợp lý, nhưng phụ thuộc vào việc phát hiện "đang chạy từ bundle" qua `Assembly.Location == ""` (IL3000) và vào biến runtime `NATIVE_DLL_SEARCH_DIRECTORIES`, vốn không phải API công khai. Không có test nào cho guard này (xem bản đồ độ phủ).

`PublishTrimmed` và `PublishReadyToRun` không bật nên không có warning trimming. `EnableCompressionInSingleFile` cũng không bật, vì vậy exe nặng 245 MB, còn zip phát hành là 94 MB.

---

## 2. Findings

### BLD-01 — Runtime .NET 8 đi kèm bản phát hành đã hết hỗ trợ (Medium)

Vị trí: `media management app.csproj:5,14-15`, `MediaManager.Core/MediaManager.Core.csproj`, các csproj test, `.github/actions/build-test/action.yml:11`.

Bằng chứng: app publish self-contained và `deps.json` ghi `runtimepack.Microsoft.NETCore.App.Runtime.win-x64/8.0.31` cùng `WindowsDesktop.App.Runtime.win-x64/8.0.31`. .NET 8 LTS hết hỗ trợ ngày **2026-11-10**, tức hai ngày trước audit. Từ nay runtime nằm trong exe sẽ không còn nhận bản vá bảo mật, mà app lại gọi HTTPS ra tracker, TMDB, Jellyfin, Gemini, Google Drive và parse dữ liệu torrent từ bên ngoài.

Tác động: mọi CVE runtime phát hiện sau ngày này (TLS, `System.Text.Json`, `System.Xml`, Regex…) sẽ nằm vĩnh viễn trong các bản phát hành. Đây cũng là rào cản của BLD-02, vì `Microsoft.Data.Sqlite 11` chỉ có net10. Hiện chưa có CVE cụ thể nào chưa được vá, nên mức Medium; mức này sẽ tăng lên khi xuất hiện advisory đầu tiên cho .NET 8 sau EOL.

Hướng sửa (làm trong phiên bảo trì riêng): retarget sang `net10.0-windows10.0.19041.0` cho app và App.Tests, `net10.0` cho Core, Core.Tests và Sonarr.Parser. Giữ `SupportedOSPlatformVersion` 10.0.17763.0. Bỏ `dotnet-version: 8.0.x` trong action. Việc này đồng thời đóng AUD-007. SDK đã là 10, và các package `Microsoft.Extensions.*` 10.x đã khớp sẵn.

### BLD-02 — SQLite đi kèm dính CVE-2025-6965 (Medium)

Vị trí: transitive `Microsoft.Data.Sqlite 10.0.8 → SQLitePCLRaw.bundle_e_sqlite3 2.1.11 → lib.e_sqlite3 2.1.11`, ship trong bundle dưới dạng `e_sqlite3.dll`.

Bằng chứng: `--vulnerable` báo GHSA-2m69-gcr7-jv3q (High), với dải ảnh hưởng `<= 2.1.11` và **không có bản vá trong 2.x**. `--deprecated` đánh dấu `CriticalBugs`. Bản sửa nằm ở SQLitePCLRaw 3.0.x (lib theo version SQLite, đến 3.53.3). `Microsoft.Data.Sqlite 11.0.0` kéo 3.0.5 nhưng chỉ có `net10.0`.

Tác động và điều kiện: muốn khai thác, kẻ tấn công phải chạy được SQL tuỳ ý. SQL của app là SQL nội bộ, có tham số. Đường thực tế nhất là **file DB do kẻ tấn công tạo** được mở qua restore backup (zip hoặc Google Drive, xem AUD-003 và phiên backup-audit), vì view/trigger trong file DB sẽ chạy khi app truy vấn. Cần quyền ghi vào backup của chính người dùng. Chưa tái hiện được.

Hướng sửa: đi cùng BLD-01 (net10 + Microsoft.Data.Sqlite 10.0.x/11 cùng SQLitePCLRaw 3.0.x). Nếu chưa chuyển net10 được thì thử thêm PackageReference trực tiếp `SQLitePCLRaw.bundle_e_sqlite3` 3.0.x vào Core. Cần build và chạy MigrationRunnerTests để xác nhận API 2.x→3.x tương thích, vì audit này chưa kiểm.

### BLD-03 — Code GPL-3.0 trong binary công khai nhưng không có LICENSE cho toàn bộ tác phẩm (Medium)

Vị trí: `ThirdParty/Sonarr.Parser/` (`PackageLicenseExpression GPL-3.0-only`, `LICENSE.md` là GPLv3 đầy đủ). App liên kết qua `ProjectReference` (`media management app.csproj:62`) và dùng trong `Services/PackEpisodeResolver.cs`. `gh repo view` trả về `visibility: PUBLIC, licenseInfo: null`, và gốc repo không có file LICENSE. Có 8 GitHub Release công khai (v1.0.0-rc.1 → v1.0.6).

Phân tích: Sonarr.Parser được biên dịch thành `MediaManager.Sonarr.Parser.dll` và nạp chung process trong cùng một exe, nên tạo thành một "work based on the Program". GPLv3 §5(c) yêu cầu khi phân phối thì **toàn bộ** tác phẩm phải được license theo GPLv3. Hiện code first-party không có license, nghĩa là mặc định "all rights reserved", và điều này mâu thuẫn với điều khoản trên. Về §6 (corresponding source): repo public và có tag cho từng bản nên gần như đáp ứng, nhưng zip không ghi link hay lời đề nghị cung cấp source. `ATTRIBUTION.md` liệt kê thay đổi nhưng thiếu ngày (§5(a)), và bỏ sót `ParserConstants.cs`, `Extensions/RegexExtensions.cs`, `Extensions/StringExtensions.cs`. Các dependency khác (MIT, BSD-3, Apache-2.0) đều tương thích GPLv3. FluentAssertions chỉ dùng trong test nên không được phân phối.

Hướng sửa (không phải tư vấn pháp lý): (a) thêm `LICENSE` GPL-3.0 ở gốc repo, ghi trong README và zip rằng app dùng GPL-3.0, kèm link tới source đúng tag; hoặc (b) viết lại hay thay thế phần parser để bỏ GPL; hoặc (c) chuyển repo và release sang private, tức là ngừng phân phối. Chủ repo cần quyết định trước khi phát hành bản tiếp theo.

### BLD-04 — `main` không được bảo vệ, merge trước khi CI xong, và mỗi push đều phát hành (Medium)

Bằng chứng:

- `gh api repos/…/branches/main` trả về `protected: false`, `required_status_checks.enforcement_level: off`. `rulesets` rỗng.
- Trong khi đó `docs/BUILD.md:114` ghi "required check `build-test` (branch protection on `main`)", và `docs/AI_CONTEXT.md:12` ghi "protected; … CI must pass". Commit `e8daa78` cũng tuyên bố như vậy.
- PR #8: check `build-test` trên commit cuối `c35f0e1` bắt đầu 01:59:08, kết thúc 02:02:00, còn PR **được merge lúc 01:59:22**, tức trước khi CI chạy xong.
- `auto-release.yml` chạy trên mọi `push` vào `main`. Vì `main` không có bảo vệ, một lần push trực tiếp (từ người hay từ một phiên AI) sẽ tự tạo tag và GitHub Release **public** ngay lập tức. Lớp kiểm soát duy nhất là bước build/test bên trong `release.yml`.

Tác động: tính đúng đắn của `main` và của bản phát hành phụ thuộc vào kỷ luật thủ công, trong khi docs mô tả một rào chắn không có thật.

Hướng sửa: tạo ruleset hoặc branch protection cho `main` với yêu cầu PR, check `build-test` phải pass và branch phải up-to-date, đồng thời chặn force-push và xoá nhánh. Sửa docs cho khớp thực tế nếu chủ repo không muốn bật.

### BLD-05 — Auto-release mặc định phát hành khi gặp lỗi và có thể để lại tag mồ côi (Low)

Vị trí: `.github/workflows/auto-release.yml:50-62,64-83,85-92`.

- `labels=$(gh api … || true)`: nếu gọi API thất bại hoặc commit không gắn với PR nào (push trực tiếp), `labels` rỗng và job **mặc định `patch`**, tức là vẫn phát hành. Kể cả khi PR có nhãn `release:skip` mà API lỗi tạm thời thì vẫn phát hành.
- Bước nhận diện thay đổi chỉ gồm docs/CI chỉ so `$GITHUB_SHA^1..$GITHUB_SHA`. Với rebase-merge hay push nhiều commit (repo cho phép cả merge, squash và rebase), các commit trước đó bị bỏ qua. Bước này cũng chỉ áp dụng khi bump là `patch`.
- Tag được push **trước** khi build/test. Nếu `release.yml` thất bại thì tag vẫn còn mà không có Release, và lần chạy lại `auto-release` sẽ bỏ qua vì "HEAD already tagged". `release.yml` không có `workflow_dispatch`, nên muốn khôi phục phải "Re-run failed jobs" hoặc xoá rồi push lại tag bằng tay. `docs/RELEASING.md` không mô tả cách này.

Hướng sửa: khi gọi API lỗi thì cho job fail thay vì mặc định `patch`; so thay đổi với tag release gần nhất (`git diff <last-tag>..HEAD`); chạy build-test trước, chỉ tạo tag hoặc release khi pass (hoặc thêm `workflow_dispatch` cho `release.yml`); ghi quy trình khôi phục vào RELEASING.md.

### BLD-06 — Bản phát hành không ký số và không có provenance (Low)

Vị trí: `release.yml:57-79`, `scripts/install-release.ps1:106-143`.

Bằng chứng: không có bước Authenticode hay `actions/attest-build-provenance`. `SHA256SUMS.txt` được tạo trong cùng job và đăng vào cùng Release, nên nó chỉ chứng minh zip không bị hỏng khi tải, không chứng minh nguồn gốc. Ai sửa được Release (token có `contents: write` hoặc tài khoản owner) thì sửa được cả zip lẫn checksum. `install-release.ps1` tải từ `api.github.com` qua TLS 1.2, kiểm SHA256, giải nén rồi **chạy exe** (`Start-Process`) mà không kiểm `Get-AuthenticodeSignature`, không kiểm attestation, và không so ProductVersion của exe với tag. Script không tải hay chạy thêm script nào khác, nên không có đường "download-and-execute" ngoài exe của chính release. Backup trước khi ghi đè chỉ áp dụng cho exe, còn các file khác trong zip (PNG, notices) bị ghi đè không có bản sao lưu.

Hướng sửa: thêm `actions/attest-build-provenance` (miễn phí với repo public) và cho script cài đặt chạy `gh attestation verify` nếu có `gh`; về lâu dài cân nhắc Authenticode (Azure Trusted Signing) để tránh SmartScreen. Trong script, so `ProductVersion` của exe với `$Tag` trước khi swap.

### BLD-07 — Pin action và quyền token (Low)

- Mọi action đều pin theo tag lớn có thể đổi: `actions/checkout@v4`, `upload-artifact@v4`, `setup-dotnet@v4`, `cache@v4`. Tất cả đều do GitHub sở hữu nên rủi ro thấp. Repo có `allowed_actions: all` và `sha_pinning_required: false`.
- `ci.yml` không có khối `permissions`, nên dựa vào mặc định của repo (`read`, đã kiểm). Nếu mặc định đó bị đổi thì CI trên PR sẽ có token ghi. `auto-release.yml` khai `contents: write` ở cấp workflow, và job `tag` thực sự cần quyền này để push tag.
- `actions/cache` dùng `restore-keys: nuget-Windows-` (khớp prefix). Cache từ PR bị cô lập theo ref nên không có đường đầu độc cache vào `main`.

Hướng sửa: pin SHA kèm comment version và bật Dependabot cho `github-actions`; giới hạn allowed actions ở "GitHub-owned"; thêm `permissions: contents: read` vào `ci.yml`.

### BLD-08 — Lỗ hổng transitive không hiện trong restore/CI (Low)

Bằng chứng: `dotnet restore` trên SDK 10 không in ra NU1902-NU1904 cho `SQLitePCLRaw.lib.e_sqlite3`, vì với project net8 thì `NuGetAuditMode` mặc định chỉ xét package direct. CI không có bước `dotnet list package --vulnerable`, repo không có Dependabot, và không có `packages.lock.json`. BLD-02 chỉ được phát hiện nhờ chạy tay.

Hướng sửa: đặt `<NuGetAuditMode>all</NuGetAuditMode>` trong `Directory.Build.props`. Thêm `<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-rxg9-xrhp-64gj" />` kèm lý do (dương tính giả, xem 1.1). Trong release có thể bật `WarningsAsErrors` cho NU1903/NU1904. Thêm Dependabot cho `nuget`.

### BLD-09 — `.gitignore` vẫn che source thật (Low)

Sự cố cùng loại với `Services/Backup` (đã sửa trong `290da3b` bằng `!**/Services/Backup/`). Kiểm bằng `git ls-files -ci --exclude-standard` và `git ls-files -o -i --exclude-standard` trên checkout chính:

| Pattern | Hệ quả |
|---|---|
| `.gitignore:440 /media management app/media management app/scripts` | Cả thư mục `scripts/` bị ignore. Bốn file đang track (`build.sh`, `install-release.ps1`, `enable/disable-localdumps.ps1`) chỉ có vì từng được force-add. `run-test-state.cmd`, `run-test-state.sh` và `make-test-restore-zip.cmd` **không có trong repo**, dù `docs/first-run/01-phase-test-harness.md:55-118` hướng dẫn dùng chúng. Script mới thêm vào sẽ bị bỏ qua mà không có cảnh báo |
| `.gitignore:24 [Dd]ebug/` | Che `docs/debug/` (README và các bản ghi điều tra auto-track, pack-cleanup, torrent-hunt, unclean-exit), trong khi `docs/FEATURES.md:75` có link tới `docs/debug/unclean-exit`. Riêng `CrashDumps_archived/`, `logs_archived/` thì nên tiếp tục ignore |
| `tools/PackResolverSmoke/` | Chỉ có `bin/` và `obj/`, không có source. Hoặc là đã mất, hoặc chưa bao giờ được commit |
| Pattern chung không neo vào đường dẫn: `[Ll]og/`, `[Ll]ogs/`, `[Rr]elease/`, `[Oo]ut/`, `Backup*/`, `artifacts/`, `publish/`, `*.env`, `*.log` | Thư mục nguồn nào trùng tên sẽ bị nuốt mất mà không báo |

Ngoài ra (chỉ là ghi chú hygiene, không tính thành finding): trong checkout chính có `nul`, `debug-078d56.log`, `debug-431fbf.log`, `media management app.csproj.user` và `Properties/PublishProfiles/*.pubxml`. Tất cả **không bị track**, nhưng `nul` được xử lý bằng hai workaround: `.gitignore:439` và `<None Remove="nul" />` trong csproj (`:93-95`). Nên xoá file đó (`del \\?\…\nul`) rồi bỏ cả hai workaround.

Hướng sửa: thay dòng 440 bằng các pattern rác cụ thể, rồi `git add` các script đang được docs dùng. Thêm `!**/docs/debug/` và giữ ignore cho `**/docs/debug/**/*_archived/`. Thêm một bước CI kiểm `git ls-files -ci --exclude-standard` phải rỗng.

### BLD-10 — THIRD_PARTY_NOTICES thiếu (Low)

`THIRD_PARTY_NOTICES.md` (bản copy vào zip ở `release.yml:63`) chỉ có Sonarr. Exe self-contained thực tế còn phân phối: .NET runtime và WindowsDesktop (MIT, kèm THIRD-PARTY-NOTICES riêng), CommunityToolkit.Mvvm, LiveCharts, SkiaSharp, HarfBuzzSharp, MaterialDesign, MahApps IconPacks, WPF-UI, Microsoft.Toolkit.Uwp.Notifications, Microsoft.Xaml.Behaviors (MIT); Google.Apis.*, SQLitePCLRaw (Apache-2.0); WebView2 SDK (BSD-3, **yêu cầu tái hiện notice khi phân phối binary**); OpenTK và GLWpfControl (MIT); native Skia (BSD-3), HarfBuzz (MIT), SQLite (public domain). Các license MIT, BSD và Apache đều yêu cầu kèm notice khi phân phối.

Hướng sửa: sinh notices tự động (ví dụ `dotnet-project-licenses` hoặc `nuget-license`) trong bước Package, rồi gộp với notice của Sonarr.

### BLD-11 — Dependency không dùng hoặc nặng không cần thiết (Low)

Xem 1.5. Bỏ được ngay `MahApps.Metro.IconPacks.Material` (3,6 MB). WPF-UI (6,3 MB + Behaviors) chỉ phục vụ một lời gọi đổi theme. Exe 245 MB vì không nén; `EnableCompressionInSingleFile=true` sẽ giảm mạnh dung lượng, đổi lại thời gian khởi động tăng lên, nên cần đo trước khi bật.

### BLD-12 — Công cụ test cũ (Low)

`xunit 2.5.3` bị deprecated (Legacy); `Microsoft.NET.Test.Sdk 17.8.0` (tháng 11/2023) là nguồn của System.Net.Http và Regex 4.3.0 có lỗ hổng (chỉ trong test); `coverlet.collector 6.0.0` không dùng. `FluentAssertions 8.10.0` đi theo **Xceed Community License (Non-Commercial)**, đúng với thông báo mà verification log 2026-09 đã ghi nhận. Dùng được cho dự án mã nguồn mở và phi thương mại; nếu repo được dùng cho mục đích thương mại thì phải mua license hoặc chuyển sang FluentAssertions 7.x hoặc AwesomeAssertions (Apache-2.0).

Hướng sửa: nâng `xunit` lên 2.9.3 (hoặc v3), runner lên 3.x/4.x, Test SDK lên 18.x; bỏ `coverlet` hoặc dùng nó trong CI (`--collect "XPlat Code Coverage"`).

### BLD-13 — CI trên `main` tự huỷ (Low)

`ci.yml:10-12` có `group: ci-${{ github.ref }}` và `cancel-in-progress: true`. Khi merge hai PR liên tiếp, lần chạy CI của commit trước trên `main` bị huỷ: `2a09c78` (v1.0.2) và `d16a334` (v1.0.5) đều `cancelled`. Release vẫn có build/test riêng nên bản phát hành không mất kiểm tra, nhưng lịch sử `main` có lỗ hổng xác minh và artifact.

Hướng sửa: `cancel-in-progress: ${{ github.event_name == 'pull_request' }}`.

### BLD-14 — Docs lệch thực tế và IL3000 (Low)

- `docs/BUILD.md:81,261` ghi App.Tests "131 passed", trong khi đo được **138**. Core 248 khớp.
- `docs/BUILD.md:114` và `docs/AI_CONTEXT.md:12` nói `main` được bảo vệ (xem BLD-04).
- `Services/BundleExtractionGuard.cs:19` cố ý dùng `Assembly.Location` rỗng để nhận biết đang chạy trong bundle, nhưng không suppress IL3000 nên mỗi build và publish đều in warning. Warning thật sau này sẽ bị chìm giữa đống này.

Hướng sửa: cập nhật số test và trạng thái bảo vệ nhánh; thêm `#pragma warning disable IL3000` kèm lý do (hoặc `[UnconditionalSuppressMessage]`) tại dòng đó.

---

## 3. CI/release: tóm tắt đọc workflow

| Hạng mục | `ci.yml` | `release.yml` | `auto-release.yml` |
|---|---|---|---|
| Trigger | PR→main, push main, dispatch | push tag `v*.*.*`, `workflow_call` | push main, dispatch (bump) |
| `pull_request_target` | Không | Không | Không |
| `permissions` | Không khai báo (mặc định repo = read) | `contents: write` | `contents: write`, `pull-requests: read` |
| Secret | Không | `github.token` qua env `GH_TOKEN`, không echo | như trên |
| Nội suy `${{ }}` vào shell | Không | Không (dùng `$TAG`) | `inputs.bump` (choice) và `steps.bump.outputs.bump` (giá trị cố định): an toàn |
| Pin action | tag `@v4` | tag `@v4` | tag `@v4` |
| Checksum / ký | Không áp dụng | SHA256SUMS, không ký | Không áp dụng |
| Guard | Không áp dụng | tag phải nằm trên main; version exe phải khớp tag | bỏ qua nếu HEAD đã có tag, thay đổi chỉ gồm docs/CI, hoặc PR có nhãn `release:skip` |

Composite action `build-test`: setup-dotnet (global.json + 8.0.x), cache NuGet, restore, build, test (`--no-build`, trx), publish. Lưu ý: `dotnet publish` build lại app với RID, nên exe phát hành không phải đúng binary đã test (cùng source và cấu hình, nên rủi ro thấp).

Lịch sử chạy (40 run gần nhất): mọi run release và auto-release đều `success`. Hai run CI trên `main` bị `cancelled` (BLD-13). v1.0.0 được tạo bằng `workflow_dispatch` trên `a7bdc2c`, một commit chỉ thay đổi `.github`, nên đó là phát hành có chủ ý.

## 4. Lịch sử git các file build/CI

| Commit | PR | Nội dung | Nhận xét |
|---|---|---|---|
| `0e2a427` | | csproj sang single-file publish | |
| `e8daa78` | #1 (merge `4541fa4`) | CI, `global.json`, docs nói "main protected" | Không bật protection thật (BLD-04) |
| `60731dc` | #3 | MinVer, `DebugType=embedded`, composite action, `release.yml`, `install-release.ps1` | Có checksum, không ký (BLD-06) |
| `a7bdc2c` | #4 | `auto-release.yml`. Từng thêm auto-update theo lịch rồi **gỡ trong cùng PR** | Tốt: không có updater chạy nền tự tải exe |
| `f72a7a9` | #5 | Task tự khởi động bằng XML; thử ship `PenImc_cor3.dll` cạnh exe | Bị thay thế ở #6 |
| `2a09c78` | #6 | `BundleExtractionGuard` giữ DLL đã giải nén; bỏ exclude khỏi csproj | IL3000 (BLD-14); chưa có test |
| `290da3b` | | Mở lại `Services/Backup` bị `Backup*/` che | Cùng loại lỗi vẫn còn ở BLD-09 |

PR #2 bị đóng (trùng với #3). PR #12 đang mở và không liên quan tới build.

## 5. Test

### 5.1 Kết quả

386/386 pass, 0 skip, chạy hết 13 s. Test chậm nhất: `TorrentCleanupServiceTests.DeleteTorrentAsync_TransportErrors_RetryThenPauseAndThrow` 1,2 s, do `Task.Delay(400 ms)` thật trong `ITorrentCleanupService.cs:37,108`.

### 5.2 Flaky và phụ thuộc môi trường

Không thấy test nào phụ thuộc mạng, registry, state folder thật hay tên máy. Các rủi ro nhỏ:

| Test | Phụ thuộc | Rủi ro |
|---|---|---|
| `TmdbDnsProbeTests.WaitUntilUsable_*` | đồng hồ thật (timeout 50 ms / 5 s, chu kỳ 10 ms) | Thấp. Assert không nhạy với thời gian, chỉ chậm khi máy quá tải |
| `TorrentCleanupServiceTests.*` | `Task.Delay` thật (400 ms), timeout 20 s | Thấp, chỉ làm suite chậm. Nên inject `TimeProvider` |
| `TmdbWarpRecoveryTests.WarpConnects_RetrySucceedsAfterTunnelSettles` | delay thật (~0,8 s) | Thấp |
| `WarpModePolicyTests` (marker `DateTime.UtcNow`), `WindowsStartupServiceTests.BuildTaskXml_EscapesPath` (`DateTime.Now`) | giờ hệ thống | Rất thấp. Assert không so thời gian |
| `MigrationRunnerTests`, `SymlinkSubtitleCleanupServiceTests` | file trong `%TEMP%` | Không. Có dọn, `Pooling=false`; sau khi chạy không còn file `mm-mig-*` hay `test_*` |

Fixtures (`MediaManager.Core.Tests/Fixtures/*.json`, builders) nhỏ và không chứa đường dẫn cá nhân hay secret. `WindowsStartupServiceTests` có hằng chuỗi đường dẫn dev `d:\VScode\...`, nhưng chỉ dùng làm dữ liệu, không truy cập file. `TestResults/` và `*.trx` được ignore, và không có cái nào bị track.

### 5.3 Bản đồ độ phủ theo vùng

Một file được tính là có test nếu một type top-level trong file được nhắc tới trong code test. Đây là heuristic theo tên, không đo dòng code thực sự được chạy.

| Vùng | File | LOC | File có test | LOC có test |
|---|---|---|---|---|
| MediaManager.Core/Services | 42 | 5 854 | 32 | 4 665 |
| MediaManager.Core/Models | 24 | 1 618 | 18 | 1 499 |
| MediaManager.Core/Torrent | 3 | 883 | 1 | 509 |
| MediaManager.Core/Migrations | 3 | 303 | 1 | 193 |
| **Services (app)** | 135 | **32 463** | 15 | 3 694 |
| Services/Backup | 12 | 1 232 | **0** | 0 |
| Services/Gemini | 9 | 1 254 | **0** | 0 |
| Services/Symlink | 8 | 1 912 | 1 | 105 |
| Services/Events | 2 | 163 | 0 | 0 |
| ViewModels | 65 | 20 029 | 1 | 368 |
| Views, Views/Controls | 36 | 3 228 | 0 | 0 |
| Common (app) | 28 | 1 367 | 5 | 239 |
| ThirdParty/Sonarr.Parser | | | gián tiếp | không có test golden riêng |

File lớn không có test nào (> 500 dòng): `DatabaseService.cs` (3 306), `FetchJobService.cs` (1 501), `TmdbMetadataProvider.cs` (1 379), `AutoTorrentLinkService.cs` (1 329), `QbittorrentClient.cs` (1 324), `Symlink/SymlinkCoordinatorService.cs` (1 016), `WarpCliService.cs` (917), `JellyfinLibraryRefreshService.cs` (766), `PackTorrentInventoryAnalyzer.cs` (674), `HostScanService.cs` (651), `TorrentCartService.cs` (644), `AutomationFlowService.cs` (611), `TorrentReconciliationService.cs` (597), `Symlink/SymlinkSyncService.cs` (567), `TrackedShowService.cs` (539), `PackSpecialPayloadBuilder.cs` (535), `SettingsService.cs` (521), cùng mọi ViewModel lớn (`LibraryViewModel` 2 954, `TorrentWorkspaceViewModel` 2 156, `SettingsViewModel` 2 078…).

### 5.4 Test nên viết, theo thứ tự ưu tiên

Ưu tiên theo mức độ phá huỷ dữ liệu và theo finding đang mở:

1. **Backup restore** (`Services/Backup/BackupService.cs`, AUD-003): restore vào state folder tạm; zip hỏng, thiếu file, manifest sai version; DB bị thay khi connection còn mở. Hiện có 0 test.
2. **Hardlink, symlink và xoá media** (`HardlinkService.cs`, `Symlink/SymlinkCoordinatorService.cs`, `SymlinkSyncService.cs`, AUD-005): thư mục temp với cây media giả; lỗi gỡ hardlink thì không được xoá media; không được thao tác ngoài managed root.
3. **`DatabaseService`** với SQLite file tạm: blacklist khi DB lỗi phải fail-closed (AUD-006), các truy vấn cart, order và tracked show, transaction rollback.
4. **`QbittorrentClient`** với `HttpMessageHandler` giả: add, delete, pause, map lỗi HTTP 403/409/5xx, đăng nhập lại khi cookie hết hạn, parse JSON thiếu field.
5. **`TorrentReconciliationService` và `AutoTorrentLinkService`**: khớp torrent hoàn tất với episode và pack, tránh link trùng.
6. **`SettingsService`**: load/save, migrate JSON cũ, file hỏng, không ghi credential ra log.
7. **Sonarr.Parser golden tests** (`ParserService`, `PackEpisodeResolver`): bộ tên release thật và kết quả mong đợi, để khoá hành vi khi cập nhật hay thay parser (liên quan BLD-03).
8. **`TorrentCartService`, `FetchJobService`**: vòng đời order và job, huỷ giữa chừng.
9. **`GeminiApiClient`, `Backup/GoogleDriveClient`**: parse response, lỗi quota, token hết hạn (HTTP giả).
10. **`JellyfinClient`, `JellyfinLibraryRefreshService`, `WarpCliService`**: parse output và trạng thái retry (đã có test cho backoff và policy, chưa có cho client).
11. **`BundleExtractionGuard.FindExtractionDirectory`**: tách phần thuần (nhận chuỗi search dirs và base dir) để test được.
12. ViewModels: chỉ nên viết cho `SettingsViewModel` (đường lưu cài đặt) và `FirstRunViewModel` (chọn state folder). Phần còn lại ưu tiên thấp.

## 6. Việc tiếp theo đề xuất (ngoài phạm vi audit)

1. Quyết định license (BLD-03) trước bản phát hành tiếp theo.
2. Bật ruleset cho `main` (BLD-04): cấu hình GitHub, không cần sửa code.
3. Một phiên bảo trì: retarget net10 + `windows10.0.19041` (BLD-01, BLD-02, AUD-007), kèm kiểm thử thủ công chart, toast và WebView2.
4. Một phiên hygiene: `.gitignore` (BLD-09), notices (BLD-10), Dependabot, NuGetAudit, pin SHA (BLD-07, BLD-08), sửa auto-release (BLD-05, BLD-13).
