# Synode Inventor Exporter — Autodesk Inventor Professional 2027

Add-in **Synode** cho Autodesk Inventor 2027: xuất Part (`.ipt`) hoặc Assembly (`.iam`) đang mở thành file **GLB (glTF 2.0 binary)** bằng một nút bấm.

> Dự án phát triển/kiểm thử nội bộ, chạy trên bản Inventor Professional 2027 Trial (non-commercial). Add-in không can thiệp license.

---

## A. Tổng quan

| Thành phần | Mô tả |
|---|---|
| Tên add-in | `Synode Inventor Exporter` (Tools › Add-Ins) |
| Giao diện | Ribbon tab **Synode** › panel **Synode** › nút **Export to GLB** (môi trường Part và Assembly) |
| Export | Dùng **translator glTF gốc của Inventor 2027** (`{856E3C5D-A1F4-4B86-A6C0-8B849C0C8BE0}`, `TranslatorAddIn.SaveCopyAs`) — hình học, transform của component, vật liệu/màu và đơn vị do chính Inventor chuyển đổi |
| Kiểm tra đầu ra | Mỗi file được kiểm tra bằng `GlbValidator`: magic `glTF`, version 2, độ dài khai báo, chunk JSON/BIN, alignment 4 byte, buffer/bufferView/accessor, mesh, node, scene. File không hợp lệ thì không được ghi vào đích |
| Cơ chế nạp | File manifest `.addin` (Inventor tự quét thư mục Addins). **Không dùng RegAsm, không ghi registry** |
| Công nghệ | C# / .NET (net10.0-windows nếu có .NET 10 SDK, nếu không thì net8.0-windows), x64, Inventor Interop 31.x, không dùng NuGet trong add-in |
| GUID add-in | `{7001FEF0-B792-4C1D-9FB4-0515C8F2F90B}` |
| Log | `%LOCALAPPDATA%\Synode\InventorExporter\logs\SynodeInventor.log` |

Cấu trúc thư mục:

```text
autodeskinventor-export-plugin\
├── src\SynodeInventor.Core\      # Không phụ thuộc Inventor: GlbValidator, ExportPathHelper, logger, mã lỗi, icon
├── src\SynodeInventor.AddIn\     # Add-in: StandardAddInServer, RibbonUi, ExportGlbCommand, các service
├── tests\SynodeInventor.Tests\   # Unit test (không cần Inventor)
├── tests\SynodeInventor.IntegrationTests\  # Test thật trên Inventor qua COM
├── scripts\                      # Find-Inventor.ps1, Test-ScriptSyntax.ps1
├── docs\                         # ANALYSIS.md, MANUAL_TEST.md
├── build.ps1 / install.ps1 / uninstall.ps1
└── SynodeInventor.sln
```

## B. Yêu cầu hệ thống

- Windows 10/11 x64.
- Autodesk Inventor Professional 2027 (bản 31.x) đã cài và kích hoạt (trial cũng được).
- .NET SDK: **khuyến nghị .NET 10 SDK x64** (khớp runtime .NET 10 mà Inventor 2027 dùng cho add-in). Nếu chỉ có SDK 8/9, `build.ps1` sẽ build `net8.0-windows` và in cảnh báo.
- Không cần Inventor SDK riêng: dùng `Autodesk.Inventor.Interop.dll` có sẵn trong `Inventor 2027\Bin\Public Assemblies`.
- Script tương thích Windows PowerShell 5.1 và PowerShell 7 (toàn bộ `.ps1` chỉ chứa ký tự ASCII).

## C. Build

```powershell
cd C:\plugin_export_3d\autodeskinventor-export-plugin
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Script sẽ: kiểm tra SDK → tìm Inventor 2027 qua registry (`RegistryVersion31.0\InventorLocation`) → build Release x64 → kiểm tra DLL (PE32+/AMD64, tên assembly) → chép ra `dist\` → chạy unit test.

Kết quả trong `dist\`: `SynodeInventor.AddIn.dll`, `SynodeInventor.AddIn.pdb`, `Synode.InventorExporter.addin`, `build-info.txt`.

Tuỳ chọn: `-SkipTests`, `-TargetFramework net10.0-windows`, `-InventorBinDir "<...>\Inventor 2027\Bin"`, `-OutputDir`, `-WorkDir`.

## D. Cài đặt và sử dụng

**Cài đặt (theo người dùng, không cần quyền admin):**

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1 -DryRun   # xem trước, không thay đổi gì
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

File được chép vào `%APPDATA%\Autodesk\Inventor 2027\Addins\SynodeInventorExporter\`. Có thể chạy lại nhiều lần (ghi đè bản cũ). Nếu Inventor đang mở, script chỉ cảnh báo, **không tự đóng Inventor**. Cài cho mọi người dùng: `-AllUsers` (cần PowerShell Administrator, chép vào `%ProgramData%`).

**Sử dụng:**

1. Khởi động lại Inventor 2027.
2. Mở một file `.ipt` hoặc `.iam`.
3. Chọn tab **Synode** › **Export to GLB**.
4. Chọn nơi lưu (mặc định: cùng thư mục và cùng tên với tài liệu, đuôi `.glb`).
5. Khi xong sẽ hiện thông báo gồm đường dẫn, dung lượng, số mesh/node/vật liệu/tam giác. Nếu lỗi sẽ hiện thông báo lỗi rõ ràng kèm đường dẫn log.

Kiểm tra add-in đã nạp: **Tools › Add-Ins** › `Synode Inventor Exporter` › trạng thái *Loaded*, có thể tick *Load Automatically*.

## E. Kiểm thử

```powershell
# Unit test (không cần Inventor) - 39 test
powershell -ExecutionPolicy Bypass -File .\tests\run_tests.ps1

# Kiểm tra cú pháp + encoding mọi script PowerShell
powershell -ExecutionPolicy Bypass -File .\scripts\Test-ScriptSyntax.ps1

# Integration test trên Inventor thật (mở một Inventor mới qua COM, tự đóng khi xong)
powershell -ExecutionPolicy Bypass -File .\tests\run_integration.ps1 [-Model C:\path\part.ipt] [-Visible]
```

Integration test sao chép file mẫu vào `test-output\integration\input` (không sửa file gốc), export Part, tạo Assembly gồm 3 occurrence (1 cái xoay 90°) rồi export, kiểm tra GLB bằng `GlbValidator` và bằng **SharpGLTF** (validator độc lập), kiểm tra tỉ lệ đơn vị (cm → m), và các trường hợp lỗi (file đích bị khoá, thư mục không tồn tại, tài liệu Drawing). Báo cáo nằm ở `test-output\integration\integration-report.txt`.

Kiểm thử thủ công trong giao diện: xem [docs/MANUAL_TEST.md](docs/MANUAL_TEST.md).

## F. Gỡ cài đặt và xử lý sự cố

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1 -DryRun
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1            # thêm -AllUsers nếu đã cài cho mọi người, -RemoveLogs để xoá log
```

Chỉ xoá các file của Synode (DLL, PDB, manifest có đúng ClassId). Không đụng registry hay add-in khác.

| Hiện tượng | Nguyên nhân / Cách xử lý |
|---|---|
| `ParserError`, ký tự lạ khi chạy script | Script chỉ chứa ASCII; nếu bạn sửa và thêm tiếng Việt, hãy lưu **UTF-8 with BOM**. Kiểm tra bằng `scripts\Test-ScriptSyntax.ps1` |
| Không thấy tab **Synode** | Kiểm tra Tools › Add-Ins; xem log; chắc chắn đã khởi động lại Inventor sau khi cài. Tab chỉ có ở môi trường Part/Assembly (không có ở màn hình Home) |
| Add-in báo lỗi khi nạp | Mở log `%LOCALAPPDATA%\Synode\InventorExporter\logs\SynodeInventor.log`. Nếu build `net8.0-windows` mà Inventor từ chối nạp, cài .NET 10 SDK và build lại (`build.ps1` tự chọn net10.0) |
| `install.ps1` báo file đang bị dùng | Inventor đang nạp add-in → đóng Inventor rồi chạy lại |
| "No document is open" / "not supported" | Phải mở Part hoặc Assembly (không hỗ trợ Drawing `.idw/.dwg`, Presentation `.ipn`) |
| "has no solid, surface or mesh bodies" | Part rỗng hoặc assembly không có component |
| "glTF translator ... not available" | Bật **Translator: glTF** trong Tools › Add-Ins (translator gốc của Inventor) |
| "output file is in use" | Đóng chương trình đang mở file `.glb` (viewer, trình duyệt) hoặc chọn tên khác |
| Hộp thoại license / trial hết hạn | Vấn đề môi trường của Inventor, không phải lỗi plugin; add-in không can thiệp license |
| `build.ps1` cảnh báo thiếu .NET 10 SDK | Vẫn build được net8.0; khuyến nghị cài .NET 10 SDK x64 |
