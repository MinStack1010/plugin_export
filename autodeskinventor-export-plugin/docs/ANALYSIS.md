# Phân tích plugin SolidWorks và ánh xạ sang Inventor 2027

Nguồn đã đọc (chỉ đọc, không chỉnh sửa): `C:\plugin_export_3d\solidworks-export-plugin`.

## 1. Plugin SolidWorks (dựa trên mã nguồn thực tế)

| Hạng mục | Thực tế trong mã nguồn |
|---|---|
| Công nghệ | C# .NET Framework 4.8, COM add-in, SharpGLTF 1.0.6 |
| Điểm vào | `SwAddin.cs` implement `ISwAddin`, GUID `d2717311-4a25-4677-877a-fa8d5ec18c71`, ProgId `PluginSolidWorks.SwAddin` |
| Đăng ký | RegAsm → `[ComRegisterFunction]` ghi `HKLM\SOFTWARE\SolidWorks\AddIns\{guid}` (Title "Synode Plugin") và `HKCU ...\AddInsStartup` |
| Giao diện | CommandManager group 1001 "Synode GLB Exporter", lệnh "Export to GLB" (menu + toolbar), bật khi có ActiveDoc |
| Luồng export | `Commands\ExportGlbCommand.cs`: SaveFileDialog (mặc định `solidworks-export.glb`) → `SolidWorksGeometryExtractor` (duyệt part/assembly, tessellation, normal, vật liệu theo face/body, transform component) → `CoordinateConverter` (Z-up→Y-up, UnitScale = 1) → `GltfSceneWriter` (SharpGLTF) → `SendMsgToUser2` báo thành công/lỗi |
| Upload | `GlbUploader` (HTTP multipart, api_key `API_KEY_DEV`, URL rỗng) — **mã chết**: luôn gọi `AskOutputPath(true)` nên không bao giờ upload |
| Log | `Desktop\SynodeSolidWorks.log` |
| Phụ thuộc | AssemblyResolve từ thư mục add-in |

Định dạng đầu ra duy nhất: **GLB** (glTF 2.0 binary).

## 2. Ánh xạ SolidWorks → Inventor 2027

| SolidWorks | Inventor 2027 | Phương án |
|---|---|---|
| `ISwAddin.ConnectToSW / DisconnectFromSW` | `ApplicationAddInServer.Activate / Deactivate` | Viết lại (`StandardAddInServer.cs`) |
| RegAsm + registry `SolidWorks\AddIns` | Manifest `.addin` trong `%APPDATA%\Autodesk\Inventor 2027\Addins` | Viết lại; **không dùng RegAsm/registry** |
| CommandManager / CommandGroup | `ControlDefinitions.AddButtonDefinition` + Ribbon tab/panel (`RibbonUi.cs`) | Viết lại |
| Biểu tượng toolbar (bitmap file) | `stdole.IPictureDisp` vẽ bằng code (`IconRenderer` + `IconFactory`) | Viết lại |
| `ActiveDoc`, `swDocPART/ASSEMBLY` | `Application.ActiveDocument`, `kPartDocumentObject/kAssemblyDocumentObject` | Viết lại (`InventorDocumentService`) |
| SaveFileDialog (WinForms) | `Application.CreateFileDialog` (`ShowSave`) | Viết lại, giữ hành vi (đuôi .glb, tên gợi ý) |
| `SendMsgToUser2` | `MessageBoxW` gắn cửa sổ chính Inventor | Viết lại, giữ nội dung thông báo |
| GeometryExtractor + CoordinateConverter + SharpGLTF writer | **Translator glTF gốc** `{856E3C5D-…}` `SaveCopyAs` | Thay thế: Inventor tự tessellate, chuyển trục/đơn vị, giữ hierarchy và vật liệu |
| (không có) | `GlbValidator` kiểm tra mọi file trước khi ghi vào đích | Thêm mới |
| Logger ra Desktop | `FileLogger` ra `%LOCALAPPDATA%\Synode\InventorExporter\logs` (inject, không static) | Viết lại |
| GlbUploader (mã chết) | — | Không chuyển (không có URL/API thật, không hardcode token) |

**Tái sử dụng (về khái niệm/hành vi):** luồng lệnh Export → hộp thoại lưu → export → thông báo; quy tắc nút chỉ hoạt động với Part/Assembly; xử lý lỗi có thông báo rõ ràng; đầu ra GLB.

**Viết lại hoàn toàn:** vòng đời add-in, đăng ký, UI, truy cập tài liệu, export (translator gốc), logging, cài đặt. Không dùng bất kỳ API SolidWorks nào.

## 3. Lý do chọn translator gốc

- Manifest `Bin\Addins\Autodesk.TranslatorglTFExport.Inventor.addin` khai báo `Type="Translator"`, `SupportsSaveCopyAs=1`, `SupportsSaveCopyAsFrom=.ipt;.iam`, `FileExtensions=.glb;*.gltf`.
- Chất lượng hình học, vật liệu, transform và đơn vị do chính Autodesk đảm bảo.
- Add-in chỉ dùng API đã xác minh bằng reflection trên `Autodesk.Inventor.Interop.dll` 31.20: `ApplicationAddIns.ItemById`, `TranslatorAddIn.HasSaveCopyAsOptions/SaveCopyAs`, `TransientObjects.CreateTranslationContext/CreateNameValueMap/CreateDataMedium`.
- Không đặt tuỳ chọn translator chưa được công bố; chỉ ghi log các tuỳ chọn mặc định.
- Chỉ xây pipeline tessellation riêng nếu translator gốc được xác minh là không hoạt động.

## 4. Target framework

Inventor 2027 nạp add-in managed trên .NET 10 (`Bin\ClrAddinLoader.runtimeconfig.json`: `net10.0`). Máy build hiện chỉ có SDK 9.0.305 nên build `net8.0-windows` (tương thích tiến với runtime 10). Cài .NET 10 SDK thì `build.ps1` tự chuyển sang `net10.0-windows`.
