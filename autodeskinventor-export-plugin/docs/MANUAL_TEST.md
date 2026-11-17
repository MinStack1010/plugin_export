# Hướng dẫn kiểm thử thủ công (Test 2–6)

Điền kết quả: PASS / FAIL / NOT TESTED. Log: `%LOCALAPPDATA%\Synode\InventorExporter\logs\SynodeInventor.log`.

Chuẩn bị: `build.ps1` → `install.ps1` → khởi động lại Inventor 2027.

## Test 2 — Nạp add-in

| # | Bước | Kỳ vọng | Kết quả |
|---|---|---|---|
| 2.1 | Tools › Add-Ins | Có `Synode Inventor Exporter`, trạng thái Loaded | |
| 2.2 | Mở hoặc tạo một Part | Có tab **Synode** › panel **Synode** › nút **Export to GLB** có icon | |
| 2.3 | Mở hoặc tạo một Assembly | Có cùng tab/nút | |
| 2.4 | Bỏ tick Loaded trong Add-Ins rồi tick lại | Tab biến mất rồi xuất hiện lại, không bị nhân đôi | |
| 2.5 | Log | Có dòng `Activate completed.` | |

## Test 3 — Part → GLB

| # | Bước | Kỳ vọng | Kết quả |
|---|---|---|---|
| 3.1 | Mở `.ipt` có solid → Export to GLB | Hộp thoại lưu gợi ý `<tên part>.glb` trong thư mục của part | |
| 3.2 | Lưu | Thông báo thành công có đường dẫn, dung lượng, số tam giác | |
| 3.3 | Mở file trong https://gltf-viewer.donmccurdy.com hoặc Windows 3D Viewer | Hiển thị đúng hình, màu, kích thước | |
| 3.4 | `tests\SynodeInventor.Tests.exe --validate <file>` | `VALID` | |
| 3.5 | Part chưa lưu (File › New) có body | Gợi ý tên `Part1.glb`, export thành công | |

## Test 4 — Assembly → GLB

| # | Bước | Kỳ vọng | Kết quả |
|---|---|---|---|
| 4.1 | Mở `.iam` có nhiều component (có sub-assembly) → Export | Thành công | |
| 4.2 | Xem trong viewer | Vị trí/hướng các component đúng như trong Inventor, có hierarchy node | |
| 4.3 | Component bị ẩn / suppressed | Ghi nhận hành vi (do translator gốc quyết định) | |

## Test 5 — Trường hợp lỗi

| # | Bước | Kỳ vọng | Kết quả |
|---|---|---|---|
| 5.1 | Bấm Export khi đang mở Drawing | Cảnh báo "not supported", không crash | |
| 5.2 | Part rỗng (không có body) | Cảnh báo "has no solid, surface or mesh bodies" | |
| 5.3 | Assembly rỗng | Cảnh báo "has no components" | |
| 5.4 | Cancel trong hộp thoại lưu | Không có thông báo lỗi, status bar "export cancelled" | |
| 5.5 | Ghi đè file `.glb` đang mở bằng chương trình khác (khoá file) | Lỗi "in use by another program" | |
| 5.6 | Lưu vào thư mục không có quyền ghi (vd `C:\Windows`) | Lỗi "Cannot write to the output folder" | |
| 5.7 | Gõ tên có đuôi `.gltf` | File được lưu thành `.glb` | |
| 5.8 | Sau mỗi lỗi | Không còn file `*.synode-tmp-*.glb` trong thư mục đích | |

## Test 6 — Ổn định

| # | Bước | Kỳ vọng | Kết quả |
|---|---|---|---|
| 6.1 | Export 5 lần liên tiếp cùng một model | Đều thành công, Inventor không chậm/treo | |
| 6.2 | Export một assembly lớn | Hoàn tất, ghi nhận thời gian trong log | |
| 6.3 | Đóng Inventor | Không lỗi; log có `Deactivate completed.` | |
| 6.4 | `uninstall.ps1` rồi mở Inventor | Không còn tab Synode | |
