# Synode NX GLB Exporter

Plugin cho **Siemens NX** dùng để export part hoặc assembly đang hiển thị ra file **GLB (binary glTF 2.0)**. Plugin được viết lại từ plugin SOLIDWORKS `PluginSynode` (lệnh *Export to GLB*), nằm ở `..\solidworks-export-plugin` (project đó không bị chỉnh sửa).

> [!IMPORTANT]
> Plugin **chưa được chạy trong một phiên NX thật**. Trên máy phát triển (Designcenter NX 2606 Student Edition), `run_journal.exe` báo lỗi license: *"SPLM_LICENSE_SERVER needs to point to a license server"*. Toàn bộ logic đã được kiểm thử bằng test double và đối chiếu với writer gốc của SOLIDWORKS, nhưng phần gọi NX Open (UF_FACET, GetPosition, file box, menu/ribbon) vẫn cần bạn kiểm tra trực tiếp trong NX. Xem mục [Kiểm thử trong NX](#kiểm-thử-trong-nx).

## 1. Tính năng

| Tính năng | Mô tả |
|---|---|
| Lệnh *Export to GLB* | Menu **Synode › Export to GLB**, tab ribbon **Synode**, hoặc chạy journal bằng File › Execute › NX Open (Ctrl+U) |
| Phạm vi export | Display Part (part hoặc assembly). Plugin SW dùng tài liệu đang active |
| Hierarchy | Root = tên part; mỗi component occurrence là một node glTF, giữ quan hệ cha–con, kể cả nested assembly |
| Transform | Ma trận local = `inverse(world cha) × world` (giống SW). Có kiểm tra quy ước ma trận `GetPosition` bằng hình học thật lúc chạy |
| Đơn vị | Chuyển đơn vị part (mm, inch, m, µm) sang mét. Hỗ trợ part con có đơn vị khác assembly |
| Instance dùng chung part | Mỗi occurrence có node và transform riêng; mỗi body chỉ tessellate một lần (cache theo prototype) |
| Multi-body | Mọi solid body nhìn thấy được; mỗi body chia thành các mesh theo vật liệu (`<node>_Material_<i>`, giống SW) |
| Màu và độ trong suốt | Màu hiển thị của NX (kể cả màu override trong assembly) và translucency → `baseColorFactor` (sRGB → linear), `alphaMode BLEND` |
| Lọc body | Bỏ qua sheet body, body bị blank, body nằm trên layer ẩn, body ngoài reference set, component bị suppress |
| An toàn dữ liệu | Không sửa và không lưu model. Facet model tạm luôn được xóa. GLB được ghi atomic (không để lại file hỏng) |
| Thông báo | Hộp thoại thành công/lỗi giống SW; có log `%TEMP%\SynodeNX\synode_nx_export.log` |
| Batch | `run_journal.exe synode_export_glb.py -args <model.prt> <out.glb>` (cần license NX hợp lệ) |

## 2. Yêu cầu

- Windows 10/11 x64, Siemens NX có **NX Open for Python** (`NXBIN\python\NXOpen.pyd`). Plugin được viết và kiểm tra API theo **NX 2606** (Designcenter NX 2606.3002). Các bản NX khác dùng Python 3 có thể chạy được, nhưng **chưa được kiểm chứng**.
- License:
  - Chạy journal Python (không compile, không ký) **không** cần license NX Open Author.
  - `UF_FACET_facet_solid` cần license **ASSEMBLIES MODULE** (theo docstring của NXOpen_UF NX 2606).
  - Chạy batch (`run_journal.exe`) cần license server / license batch hợp lệ.
- Không cần cài thêm thư viện Python nào: writer GLB viết bằng thư viện chuẩn của Python.

## 3. Công nghệ

- Python 3.12 (interpreter có sẵn trong NX), NX Open (`NXOpen`, `NXOpen.UF`).
- Lý do chọn Python thay vì C#/C++: không cần compile, không cần ký DLL hay license Author, và chạy trực tiếp bằng MenuScript hoặc Ctrl+U. Tất cả API cần dùng (Session, Part, ComponentAssembly, Component, Body/Face, UF_FACET, UF_OBJ, UF_UI file box, NXMessageBox) đều có trong Python.
- Writer GLB: viết lại bằng Python thuần, mô phỏng đúng output của SharpGLTF 1.0.6 mà plugin SW dùng. Đã đối chiếu byte-by-byte.
- Test: `unittest`. `PyHost` (C#) dùng để chạy Python của NX ngoài NX. `ReferenceSw` (C#) biên dịch **nguyên bản** writer của plugin SW (`Export\*.cs` + SharpGLTF) để so sánh.

## 4. Cấu trúc source

```
siesmen-export-plugin/
├─ install.ps1 / uninstall.ps1          # cài đặt và gỡ cài đặt (Windows, theo user)
├─ nx_plugin/                           # thư mục NX user dir (UGII_USER_DIR)
│  ├─ startup/
│  │  ├─ synode_glb_exporter.men        # MenuScript: menu "Synode > Export to GLB"
│  │  └─ synode_glb_exporter.rtb        # tab ribbon "Synode"
│  └─ application/
│     ├─ synode_export_glb.py           # journal entry point (ACTIONS của menu/ribbon)
│     ├─ synode_nx_exporter.json.example
│     └─ synode_nx_exporter/
│        ├─ config.py                   # ExportOptions + file JSON
│        ├─ errors.py, logger.py
│        ├─ scene.py                    # ExtractedScene/Node/Mesh/Material (giống SW)
│        ├─ matrix.py                   # ma trận 4x4 (bố cục System.Numerics)
│        ├─ materials.py                # sRGB→linear, alpha, so sánh vật liệu
│        ├─ mesh_builder.py             # gộp đỉnh, normal, kiểm tra mesh
│        ├─ coordinate_converter.py     # port CoordinateConverter (normalize float32)
│        ├─ gltf_writer.py              # port GltfSceneWriter (SharpGLTF layout)
│        ├─ output_path.py              # kiểm tra đường dẫn, quyền ghi, ghi atomic
│        ├─ exporter.py                 # scene → converter → GLB
│        └─ nx/                         # phần phụ thuộc NX Open
│           ├─ __init__.py              # NxContext (Session, UFSession, UI)
│           ├─ command.py               # lệnh Export (port ExportGlbCommand)
│           ├─ extractor.py             # duyệt assembly, transform, body (port SolidWorksGeometryExtractor)
│           ├─ tessellator.py           # UF_FACET
│           ├─ appearance.py            # màu/translucency
│           ├─ units.py                 # đơn vị part
│           └─ ui.py                    # file box, message box, chế độ batch
└─ tests/
   ├─ run_tests.ps1 / run_tests.py      # build công cụ test và chạy toàn bộ test
   ├─ unit/                             # test_core.py, test_nx_adapter.py
   ├─ compat/test_reference_sw.py       # so sánh với writer SOLIDWORKS gốc
   ├─ fakes/fake_nx.py                  # TEST DOUBLE của NX Open (không phải NX thật)
   ├─ reference_sw/                     # ReferenceSw.csproj (link read-only tới source SW)
   └─ tools/                            # pyhost, glb_reader, scene_json, synthetic_scenes
```

## 5. Cài đặt

```powershell
cd C:\plugin_export_3d\siesmen-export-plugin
powershell -ExecutionPolicy Bypass -File .\install.ps1 -WhatIf   # chỉ kiểm tra, không thay đổi gì
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

Script sẽ **kiểm tra trước** rồi mới thay đổi:

1. Tìm NX (`-NxRoot`, `UGII_BASE_DIR` hoặc `Program Files\Siemens\*\NXBIN\ugraf.exe`), in phiên bản từ `ugraf.exe`.
2. Kiểm tra `NXBIN\python\NXOpen*.pyd` và các file của plugin.
3. Chọn thư mục đích:
   - Nếu đã có `UGII_USER_DIR`: thêm file vào đó, không động tới file khác.
   - Nếu chưa có: copy vào `%LOCALAPPDATA%\SynodeNX\nx_plugin` và đặt biến môi trường **User** `UGII_USER_DIR`.
   - `-Target <thư mục>`: cài vào thư mục chỉ định, không đổi biến môi trường.
4. Ghi `synode_install_manifest.json`. Nếu plugin đã được cài thì dừng lại, trừ khi dùng `-Force`.

Sau khi cài, **khởi động lại NX**. Gỡ cài đặt: `powershell -ExecutionPolicy Bypass -File .\uninstall.ps1` (chỉ xóa đúng những gì có trong manifest; chỉ xóa `UGII_USER_DIR` nếu script đã tạo biến đó).

Cài thủ công: copy `nx_plugin\startup\*` và `nx_plugin\application\*` vào `<UGII_USER_DIR>\startup` và `<UGII_USER_DIR>\application`.

## 6. Sử dụng

1. Mở part hoặc assembly trong NX (đây sẽ là Display Part).
2. Chọn **Menu › Synode › Export to GLB**, tab ribbon **Synode › Export to GLB**, hoặc **File › Execute › NX Open…** (Ctrl+U) rồi chọn `application\synode_export_glb.py`.
3. Chọn đường dẫn `.glb`. Mặc định là thư mục của part và tên `nx-export.glb`. Nếu file đã tồn tại, plugin sẽ hỏi trước khi ghi đè.
4. Sau khi export, plugin hiện thông báo *"GLB exported successfully: <path>"* (kèm số cảnh báo nếu có), hoặc hộp thoại lỗi.

Batch, không có UI (cần license batch):

```bat
"%UGII_BASE_DIR%\NXBIN\run_journal.exe" "%UGII_USER_DIR%\application\synode_export_glb.py" -args "D:\models\asm.prt" "D:\out\asm.glb"
```

Tùy chọn: copy `synode_nx_exporter.json.example` thành `synode_nx_exporter.json` cạnh journal, hoặc trỏ biến `SYNODE_NX_EXPORTER_CONFIG` tới file JSON. Các khóa chính: `chord_tolerance_mm`, `angular_tolerance_deg`, `component_name_style` (`path`/`leaf`), `include_root_part_bodies`, `share_instance_meshes`, `skip_hidden_layer_bodies`, `unit_scale`, `log_directory`. Khóa không hợp lệ sẽ bị báo lỗi.

### Tích hợp menu/ribbon

- `startup\synode_glb_exporter.men` (VERSION 120, `EDIT UG_GATEWAY_MAIN_MENUBAR`, `BEFORE UG_HELP`) tạo menu **Synode** có nút `SYNODE_EXPORT_GLB`, với `ACTIONS synode_export_glb.py` (NX tìm file trong `application` của `UGII_USER_DIR`).
- `startup\synode_glb_exporter.rtb` tạo tab **Synode**, nhóm *Synode GLB Exporter*, dùng lại nút `SYNODE_EXPORT_GLB`. Nếu tab không tự hiện, nhấn chuột phải vào ribbon và bật tab **Synode**. Nút ribbon và menu cùng chạy một lệnh export thật.
- Cú pháp được đối chiếu với các file menu/ribbon của NX 2606 (`UGII\menus\*.men`, `*.rtb`). Không có file NX gốc nào dùng `.py` làm ACTIONS; việc này dựa trên tài liệu MenuScript của Siemens và **cần kiểm tra trong NX**. Nếu menu không xuất hiện, hãy dùng Ctrl+U (luôn chạy được) và xem syslog của NX (Help › Log File) để tìm lỗi khi nạp file menu.

## 7. Định dạng output (giống plugin SOLIDWORKS)

- GLB 2.0, có một scene. Node gốc = tên part (`Part.Leaf`), node con là component. Node nhận `matrix` khi transform khác identity.
- Tên node: `"<tên part>-<n>"` (n đánh số theo từng cha). Kiểu `path` nối theo cha (`"sub_a-1/bolt-2"`), giống chuỗi `Component2.Name2` của SW.
- Mỗi `ExtractedMesh` là một glTF mesh có một primitive tam giác. Mesh đầu tiên gắn vào node, các mesh tiếp theo gắn vào node con không tên (giống SharpGLTF).
- `POSITION`/`NORMAL` float32 interleaved (stride 24), đơn vị mét, **không đổi trục** (giống SW). Index dùng uint16, hoặc uint32 cho toàn file khi có mesh vượt 65535 đỉnh.
- Material: `pbrMetallicRoughness` (`baseColorFactor` linear, metallic 0, roughness 0), `doubleSided: true`, `alphaMode: BLEND` khi trong suốt. Tên là `<body>_Material` hoặc `<body>_Face_Material`.
- `asset.generator` = `"Synode NX GLB Exporter 1.0.0"` (SW: chuỗi của SharpGLTF).

## 8. Ví dụ

Assembly `main_assembly` gồm `sub_a(bolt ×2)`, `sub_b(plate)`, `bracket`:

```
main_assembly
├─ sub_a-1            (matrix: rotZ 90°, t=0.2 m)
│  ├─ sub_a-1/bolt-1  mesh sub_a-1/bolt-1_Material_0
│  └─ sub_a-1/bolt-2  mesh ... (rotX 30°)
├─ sub_b-1
│  └─ sub_b-1/plate-1 mesh _Material_0 + node con không tên (mesh _Material_1, BLEND)
└─ bracket-1          2 body → 2 mesh
```

## 9. Kiểm thử

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\run_tests.ps1            # build PyHost + ReferenceSw, chạy tất cả
powershell -ExecutionPolicy Bypass -File .\tests\run_tests.ps1 -Python C:\Python312\python.exe -SkipReference
```

Kết quả gần nhất: static checks (29 file Python, import 19 module không cần NXOpen) + **59 test PASS**. Trong đó 9 test so sánh với writer SW gốc, kiểm tra BIN giống từng byte và JSON giống hệt (trừ `generator`). Các tình huống 1–10 (không có part, part, assembly, nested, instance dùng chung, transform, multi-body, đường dẫn sai, không có quyền ghi, geometry không hỗ trợ) được kiểm tra bằng **test double** (`tests\fakes\fake_nx.py`). Test double không thay thế được NX thật.

### Kiểm thử trong NX

1. Chạy `install.ps1`, khởi động NX, kiểm tra có menu **Synode** và tab **Synode** hay không.
2. Mở một part đơn rồi export. Mở file trong viewer glTF (ví dụ https://gltf-viewer.donmccurdy.com hoặc Windows 3D Viewer), so kích thước (mét) với NX (Analysis › Measure).
3. Mở assembly có nested sub-assembly, component lặp lại, component bị xoay. Kiểm tra cây node và vị trí. Trong log, dòng *"Component orientation convention confirmed: rows/columns"* cho biết kết quả kiểm tra `GetPosition`.
4. Kiểm tra màu override trong assembly, body trong suốt, layer ẩn, body bị blank, part dùng inch.
5. Thử đường dẫn không hợp lệ và thư mục chỉ đọc (cần hiện hộp thoại lỗi, không tạo file).
6. Đóng mọi part rồi chạy lệnh: phải hiện *"No active NX part…"*.
7. Gửi lại `%TEMP%\SynodeNX\synode_nx_export.log` nếu có lỗi.

## 10. Lỗi thường gặp

| Thông báo | Nguyên nhân / cách xử lý |
|---|---|
| `No active NX part. Open a part or an assembly first.` | Chưa mở part (Display Part rỗng) |
| `Only parts and assemblies are supported.` | Display Part không phải `NXOpen.Part` (ví dụ drawing-only hoặc loại khác) |
| `The output folder does not exist` / `No permission to write…` / `…read-only` | Chọn thư mục khác; file đang mở trong chương trình khác |
| `The output file must have the .glb extension` | Đổi đuôi file thành `.glb` |
| `The model contains no exportable solid geometry.` | Không có solid body nhìn thấy được (sheet, blank, layer ẩn, ngoài reference set) |
| Body bị bỏ qua, có cảnh báo `could not be tessellated` | UF_FACET lỗi hoặc thiếu license ASSEMBLIES; xem log |
| Menu không xuất hiện | `UGII_USER_DIR` chưa được NX đọc (khởi động lại NX), hoặc ACTIONS `.py` không được bản NX hỗ trợ → dùng Ctrl+U |
| `License Error, SPLM_LICENSE_SERVER…` (batch) | Batch cần license server; hãy chạy trong phiên NX tương tác |

## 11. Khác biệt so với plugin SOLIDWORKS

| Mục | SOLIDWORKS | NX plugin |
|---|---|---|
| Tích hợp | COM add-in C#, CommandGroup | Journal Python + MenuScript/ribbon |
| Phạm vi | Active document | Display Part |
| Tên node | `Component2.Name2` | `<part leaf>-<n>` theo từng cha, nối path (cùng dạng chuỗi) |
| Material | DSPBR render material (face → feature → body) | Màu hiển thị NX + translucency (face → body). NX Open không cung cấp DSPBR tương đương; metallic/roughness = 0 |
| Body trong assembly part | Không có khái niệm tương ứng | Được export ở node gốc (`include_root_part_bodies`) |
| Normal bằng 0 | Ném lỗi | Thay bằng normal của facet, ghi log |
| Màn hình chọn file | SaveFileDialog | UF_UI file box (`CreateFilebox`) |
| `asset.generator` | SharpGLTF | Synode NX GLB Exporter 1.0.0 |
| Translucency của face | – | Face không có translucency riêng thì dùng của body (giả định, chưa kiểm chứng trong NX) |

## 12. Giới hạn API và license

- NX có `ExtendedRealityFileCreator` (glTF) gốc nhưng **không dùng**, vì cấu trúc output (tên, node, material) khác contract của plugin SW.
- UF_FACET cần license ASSEMBLIES. Nếu tessellate prototype thất bại, plugin thử tessellate occurrence rồi đưa về hệ tọa độ component.
- Quy ước ma trận của `Component.GetPosition()` (hàng hay cột là trục) được xác minh bằng hình học lúc chạy, vì không có cách kiểm chứng offline.
- Part có thể hiện trạng thái "modified" vì undo mark hoặc facet model tạm. Plugin không lưu part.
- Student Edition và batch: cần license phù hợp. Plugin chưa được chạy trong NX thật trên máy phát triển.
