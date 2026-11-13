# Synode GLB Exporter cho Autodesk Fusion

Add-in Python cho Autodesk Fusion (Fusion 360). Add-in xuất thiết kế đang mở ra file **GLB (glTF 2.0 binary)**, tương đương với plugin SOLIDWORKS "Synode Plugin / Synode GLB Exporter" (`solidworks-export-plugin`).

> [!IMPORTANT]
> Logic CAD-independent và lớp trích xuất Fusion đã được kiểm thử bằng unit test (dùng đối tượng Fusion giả lập). **Add-in chưa được nạp và chạy thử bên trong Fusion thật.** Xem mục 8 và 10 để biết những gì cần kiểm tra thủ công.

---

## 1. Mục đích và chức năng

Giống plugin SOLIDWORKS gốc:

| Chức năng | Mô tả |
|---|---|
| Nút lệnh **Export to GLB** | Nằm trong workspace DESIGN → tab UTILITIES → panel ADD-INS (được promote ra thanh công cụ). |
| Chọn file đích | Hộp thoại Save, filter `*.glb`, tên mặc định `fusion-export.glb`. Hủy hộp thoại thì không làm gì (giống SW). |
| Xuất toàn bộ model | Part (root component chỉ có body) hoặc assembly (occurrence lồng nhau). |
| Cấu trúc node | 1 node gốc (tên = tên document) → 1 node cho mỗi occurrence (tên = `Occurrence.name`, ví dụ `Bolt:1`), giữ nguyên phân cấp. |
| Transform | Mỗi node lưu **transform cục bộ** so với node cha (như SW: `world * inverse(parentWorld)`). |
| Hình học | Chỉ B-Rep solid body đang hiển thị; tessellation có pháp tuyến đỉnh; bỏ tam giác suy biến; sửa chiều quấn tam giác theo pháp tuyến. |
| Vật liệu | Gom tam giác theo appearance tương đương (dung sai 1e-4 như SW); PBR metallic-roughness, double-sided, sRGB→linear, trong suốt → `BLEND`; không có appearance → vật liệu xám mặc định `Material`. |
| Tên mesh / vật liệu | Mesh `{tênNode}_Material_{i}`; vật liệu `{tênBody}_Material` hoặc `{tênBody}_Face_Material` (appearance gán ở mức face). |
| Đơn vị | Mét (Fusion API dùng cm → nhân `unit_scale = 0.01`), giống output SW (SW API dùng mét). |
| Upload (tùy chọn) | POST multipart (`file` = GLB `model/gltf-binary`, `api_key`) – giống `GlbUploader.cs`. Chỉ hiện khi `upload_url` được cấu hình (trong plugin SW nhánh upload bị vô hiệu vì URL rỗng). |
| Thông báo | `GLB exported successfully:\n<path>` / `GLB uploaded successfully.` / thông báo lỗi cụ thể; thêm thống kê và cảnh báo. |

Bổ sung do khác biệt nền tảng (không làm thay đổi output contract): chọn chất lượng mesh, tùy chọn xoay Z-up → Y-up, hộp thoại tiến trình có nút Cancel, cảnh báo về body bị bỏ qua.

## 2. Cấu trúc dự án

```
autodeskfusion-export-plugin/
├── README.md
├── install.ps1                      # Cài / gỡ add-in trên Windows
├── .gitignore
├── SynodeGlbExporter/               # Thư mục add-in (copy nguyên thư mục này vào Fusion)
│   ├── SynodeGlbExporter.manifest   # Manifest add-in Fusion
│   ├── SynodeGlbExporter.py         # Entry point: run() / stop()
│   ├── config.json                  # Cấu hình (thay ExportOptions hard-code của SW)
│   ├── resources/export_glb/        # Icon nút lệnh 16/32 px (+ @2x)
│   └── synode_glb/
│       ├── core/                    # KHÔNG phụ thuộc Fusion (test được ngoài Fusion)
│       │   ├── scene.py             # ExtractedScene/Node/Mesh/Material (như SW)
│       │   ├── materials.py         # sRGB→linear, so sánh vật liệu tương đương
│       │   ├── matrix.py            # Ma trận 4x4, tách TRS
│       │   ├── coordinate_converter.py  # cm→m, tùy chọn Z-up→Y-up
│       │   ├── gltf_writer.py       # Ghi GLB (thay SharpGLTF), ghi file nguyên tử
│       │   ├── uploader.py          # Upload multipart (port GlbUploader.cs)
│       │   ├── config.py            # Đọc/kiểm tra config.json
│       │   ├── output_path.py       # Kiểm tra đường dẫn đích
│       │   └── errors.py
│       └── fusion/                  # Dùng adsk.core / adsk.fusion
│           ├── export_command.py    # Nút lệnh, dialog, quy trình export, thông báo
│           ├── geometry_extractor.py  # Duyệt occurrence/body, tessellation, gom vật liệu
│           └── appearance_reader.py # Đọc màu/roughness/độ trong suốt từ Appearance
└── tests/                           # unittest, chỉ dùng thư viện chuẩn
    ├── support.py, glb_reader.py    # module adsk giả lập tối thiểu, bộ đọc GLB độc lập
    └── test_*.py
```

Không có dependency ngoài thư viện chuẩn Python và Fusion API.

## 3. Yêu cầu môi trường

- Autodesk Fusion (bản hiện hành, có Python 3 tích hợp) trên Windows 10/11 (manifest cũng khai báo macOS nhưng chưa thử).
- Thiết kế mở trong workspace **DESIGN** (Fusion Design / parametric hoặc direct).
- Chạy test ngoài Fusion: Python ≥ 3.7 bất kỳ (có thể dùng `python.exe` đi kèm Fusion).

## 4. Cài đặt trên Windows

**Cách A – script:**

```powershell
cd <đường-dẫn>\autodeskfusion-export-plugin
powershell -ExecutionPolicy Bypass -File .\install.ps1
# gỡ cài đặt:
powershell -ExecutionPolicy Bypass -File .\install.ps1 -Uninstall
```

Script copy thư mục `SynodeGlbExporter` vào `%APPDATA%\Autodesk\Autodesk Fusion 360\API\AddIns\SynodeGlbExporter`.

**Cách B – thủ công:** copy thư mục `SynodeGlbExporter` vào `%APPDATA%\Autodesk\Autodesk Fusion 360\API\AddIns\` (tên thư mục phải trùng tên file `.py`/`.manifest`).

**Cách C – không copy:** trong Fusion, *Scripts and Add-Ins* → tab *Add-Ins* → nút **+** (bên cạnh "My Add-Ins") → chọn thư mục `SynodeGlbExporter`.

## 5. Khởi chạy từ Scripts and Add-Ins

1. Mở Fusion → workspace **DESIGN** → tab **UTILITIES** → **ADD-INS** → **Scripts and Add-Ins** (phím tắt `Shift+S`).
2. Tab **Add-Ins** → chọn **SynodeGlbExporter** → **Run**.
3. (Tùy chọn) tick **Run on Startup** để tự chạy khi mở Fusion.
4. Nút **Export to GLB** xuất hiện trong panel UTILITIES › ADD-INS (và được promote ra thanh công cụ).

Dừng add-in: chọn lại trong danh sách → **Stop** (nút lệnh và handler được gỡ trong `stop()`).

## 6. Cách sử dụng

1. Mở thiết kế cần xuất.
2. Bấm **Export to GLB**. Hộp thoại gồm:
   - **Design** – tên thiết kế hiện tại (hoặc cảnh báo nếu không có thiết kế → nút Export bị vô hiệu).
   - **Destination** – `Save to file` (mặc định); `Upload to server` chỉ hiện khi có `upload_url`.
   - **Mesh quality** – Low / Normal / **High** (mặc định, tương ứng ImprovedQuality của SW) / Very high.
   - **Design up axis** – `Y-up (no conversion)` hoặc `Z-up -> rotate to glTF Y-up`. Mặc định lấy từ preference *Default modeling orientation* của Fusion (`up_axis: auto`).
3. Bấm **Export** → chọn file `.glb` → chờ hộp thoại tiến trình (có thể Cancel).
4. Kết quả: thông báo thành công kèm số node/mesh/tam giác/kích thước và danh sách cảnh báo; hoặc thông báo lỗi. Nhật ký cũng được ghi vào cửa sổ **TEXT COMMANDS** với tiền tố `[Synode GLB]`.

### config.json

| Khóa | Mặc định | Ý nghĩa |
|---|---|---|
| `unit_scale` | `0.01` | Hệ số từ cm (Fusion) sang đơn vị output. `0.01` = mét (khớp output SW). |
| `upload_url` | `""` | URL upload; rỗng = chỉ lưu file (giống SW hiện tại). |
| `api_key` | `"API_KEY_DEV"` | Giá trị trường `api_key` khi upload (giá trị như SW). |
| `upload_timeout_seconds` | `120` | Timeout upload. |
| `default_file_name` | `"fusion-export.glb"` | Tên file gợi ý. |
| `mesh_quality` | `"high"` | `low` / `normal` / `high` / `very_high`. |
| `up_axis` | `"auto"` | `auto` / `y` / `z`. |

Cấu hình sai → dialog hiển thị "Config error" và không cho export.

## 7. Cấu trúc output

Ví dụ assembly `Gearbox v3` gồm `Housing:1` và `Shaft:1` (bên trong có `Bearing:1`, `Bearing:2` dùng chung một component), một body của Housing có face được gán appearance riêng:

```text
scene 0
└── node "Gearbox v3"                       (identity; mesh nếu root component có body)
    ├── node "Housing:1"   translation/rotation cục bộ (m)
    │   ├── node "Housing:1_Material_0"  → mesh "Housing:1_Material_0", material "Body1_Material"
    │   └── node "Housing:1_Material_1"  → mesh "Housing:1_Material_1", material "Body1_Face_Material"
    └── node "Shaft:1"     → mesh "Shaft:1_Material_0"
        ├── node "Bearing:1" → mesh "Bearing:1_Material_0"
        └── node "Bearing:2" → mesh "Bearing:2_Material_0"   (mesh riêng, giống SW)
```

Chi tiết glTF:

- `asset.generator = "Synode Fusion GLB Exporter 1.0.0"`, 1 scene, 1 node gốc.
- Transform cục bộ ghi dạng `translation`/`rotation`/`scale`; nếu ma trận có mirror/shear thì ghi `matrix`.
- Mỗi mesh có 1 primitive TRIANGLES với `POSITION` (có min/max), `NORMAL`, `indices` (`UNSIGNED_SHORT`, hoặc `UNSIGNED_INT` khi > 65535 đỉnh).
- Vật liệu: `pbrMetallicRoughness` (`baseColorFactor` linear, `metallicFactor`, `roughnessFactor`), `doubleSided: true`, `alphaMode: BLEND` nếu trong suốt. Vật liệu trùng nội dung được dùng chung.

## 8. Kiểm tra kết quả

**Tự động (ngoài Fusion):**

```powershell
cd <đường-dẫn>\autodeskfusion-export-plugin
# python bất kỳ ≥ 3.7, ví dụ python đi kèm Fusion:
# & "$env:LOCALAPPDATA\Autodesk\webdeploy\production\<build-id>\Python\python.exe" ...
python -B -m unittest discover -s tests -t tests -v
```

72 test bao phủ: ma trận, vật liệu, chuyển đổi đơn vị/trục, writer GLB (đọc lại bằng parser độc lập), config, đường dẫn, upload (server HTTP loopback), extractor với đối tượng Fusion giả lập, cấu trúc add-in (manifest, icon, cú pháp, import).

**Thủ công (trong Fusion – chưa được thực hiện):**

1. Add-in Run không lỗi; nút **Export to GLB** xuất hiện; Stop gỡ nút.
2. Không mở thiết kế (hoặc ở workspace khác) → nút Export bị khóa / thông báo "No active Fusion design".
3. Xuất 1 part, 1 assembly lồng nhau, 1 assembly có component dùng nhiều lần; mở file trong https://gltf-viewer.donmccurdy.com/ hoặc Blender: kiểm tra vị trí, hướng, kích thước (m), tên node, màu.
4. Kiểm tra bằng https://github.khronos.org/glTF-Validator/ (không có lỗi).
5. Appearance gán cho face / body / occurrence; appearance trong suốt (kính).
6. Thiết kế Z-up: chọn tùy chọn xoay → model đứng đúng trong viewer.
7. Cancel trong hộp thoại tiến trình; chọn thư mục không có quyền ghi → thông báo lỗi.
8. Nếu dùng upload: cấu hình `upload_url` và kiểm tra server nhận đúng `file` + `api_key`.
9. So sánh với file GLB do plugin SW xuất cùng một model (nếu có) bằng hệ thống downstream.

## 9. Lỗi thường gặp

| Hiện tượng | Nguyên nhân / Cách xử lý |
|---|---|
| Không thấy add-in trong danh sách | Thư mục phải tên `SynodeGlbExporter` và chứa `.py` + `.manifest` cùng tên; hoặc dùng nút **+** để trỏ tới thư mục. |
| "Synode GLB Exporter failed to start" | Xem traceback trong message; thường do thiếu file hoặc Fusion quá cũ. Panel `SolidScriptsAddinsPanel` không tồn tại → báo lỗi rõ. |
| Nút Export bị mờ | Không có thiết kế Fusion đang mở (ví dụ đang ở Drawing/CAM chưa có Design) hoặc `config.json` lỗi (dòng "Config error"). |
| "The active design contains no visible solid bodies to export." | Tất cả body bị ẩn, là surface body hoặc mesh body. Hiển thị body/solid hóa rồi xuất lại. |
| "The output folder does not exist / is not writable" | Chọn thư mục khác. |
| Model nằm ngang trong viewer | Thiết kế Z-up: chọn `Z-up -> rotate to glTF Y-up`. |
| Kích thước sai 100 lần | Kiểm tra `unit_scale` (0.01 = mét). |
| Upload: "HTTP 4xx/5xx …" | Nội dung phản hồi server được hiển thị nguyên văn (giống SW). |
| Lỗi không mong đợi | Chi tiết traceback trong cửa sổ TEXT COMMANDS (`[Synode GLB]`). |

## 10. Khác biệt và giới hạn so với plugin SOLIDWORKS

| Hạng mục | SOLIDWORKS | Fusion | Ghi chú |
|---|---|---|---|
| Nền tảng | C# .NET 4.8 COM add-in, `regasm` | Python add-in, copy thư mục | Không dùng SolidWorks API/DLL. |
| Ghi GLB | SharpGLTF `SceneBuilder` | Writer tự viết (stdlib) | Cùng nội dung; **layout node khi 1 component có nhiều mesh** (SW: `AddRigidMesh` nhiều lần trên cùng node) chưa đối chiếu được với file SharpGLTF thực tế – ở đây dùng node con mang tên mesh. Cần đối chiếu nếu downstream phụ thuộc vào số node. |
| Đơn vị | API mét, `UnitScale=1` | API cm, `unit_scale=0.01` | Output đều là mét. |
| Trục | SW Y-up, không xoay | Fusion có thể Y-up hoặc Z-up | Tùy chọn xoay; giá trị mặc định đoán theo preference toàn ứng dụng, không phải theo từng file. |
| Component bị suppress | Bỏ qua | Fusion API không có suppression cho occurrence | Không áp dụng. Occurrence bị ẩn vẫn được xuất (body ẩn – lightbulb tắt – bị bỏ và báo cảnh báo). |
| Nguồn vật liệu | Face → Feature → Body/Part (DSPBR) | Face/override → Occurrence → Body → Material (Appearance) | Fusion không có appearance cấp feature nên không có tên `_Feature_Material`. Metallic = 1 cho appearance kim loại Prism, 0 với loại khác (xấp xỉ). Texture không được xuất (SW cũng không) – có cảnh báo. |
| Độ trong suốt | DSPBR transparency | Alpha của màu hoặc `generic_transparency` | Appearance kính dạng Prism có thể không có alpha → có thể bị xuất đục. Cần kiểm tra thực tế. |
| Pháp tuyến bằng 0 | Ném lỗi | Tính lại từ tam giác, báo cảnh báo; chỉ lỗi nếu không thể tính | Tránh hỏng cả lần export. |
| Mesh/surface body | Không lấy (chỉ solid) | Bỏ qua + cảnh báo | Tương đương. |
| Model rỗng | Ghi GLB rỗng | Báo lỗi, không ghi file | Tránh file vô nghĩa. |
| Chất lượng mesh | ImprovedQuality cố định | Chọn được, mặc định High | Số tam giác sẽ khác SW. |
| Upload | Code có nhưng không dùng (URL rỗng) | Cùng contract, bật bằng `upload_url` | Chưa thử với server thật. |
| Logger | Desktop log (đã tắt) | TEXT COMMANDS | Không ghi file log. |
| Kiểm thử | – | Unit test ngoài Fusion | **Chưa chạy trong Fusion thật.** |
