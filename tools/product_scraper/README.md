# EMUA Scraper nâng cấp (bản thử nghiệm)

## Những thay đổi
- Đặt chỉ tiêu 50 laptop, 40 điện thoại, 30 PC/màn hình, 30 bàn phím/chuột, 30 tai nghe/phụ kiện.
- Có nhóm mục tiêu `NhomMucTieu` và `AnhURL` lấy từ JSON-LD (không bịa ảnh).
- `verified` chỉ là kiểm tra cấu trúc, **không phải xác minh thủ công**.
- Import mặc định **dry-run**; `--apply` chỉ thêm mới, không sửa/xóa sản phẩm cũ, rollback nếu lỗi.

## Giới hạn cần lưu ý
1. Database hiện mới xác nhận có 3 danh mục (điện thoại ID 1, laptop ID 2, phụ kiện ID 3). **PC/màn hình chưa có ID**: phải tạo danh mục trong Supabase và sửa `GROUP_CATEGORY_IDS` trong `config.py` trước. Chưa có ID thì nhóm này chuyển sang `needs_review`.
2. Các URL danh mục hiện tại chưa được xác minh lại theo thời gian thực; một số nguồn có thể chặn bot hoặc đổi giao diện. Chưa có đảm bảo thu đủ 180 sản phẩm.
3. Chưa bổ sung trang danh mục mới cho PC/màn hình, chuột/bàn phím, tai nghe; cần cập nhật `SOURCES` bằng URL danh mục thực tế được kiểm chứng, tránh tự bịa URL. Scraper vẫn lọc được các nhóm này nếu chúng xuất hiện trong trang danh mục đang có hoặc file URL thủ công.
4. Dữ liệu nguồn chỉ để tham khảo, không phải giá bán/tồn kho của EMUA. Import mặc định tồn kho 0, trạng thái Hết hàng. Không import ảnh khi chưa kiểm tra quyền sử dụng.
5. Scraper không tự động kiểm tra robots.txt. Hãy kiểm tra robots.txt/điều khoản từng website, giảm tốc độ và dừng khi gặp 403/429; không vượt cơ chế chặn.
6. Mã SKU chưa có cột riêng trong `sanpham`; hiện dùng để kiểm tra CSV, import chống trùng theo tên. Để chống trùng theo SKU/URL ở cấp DB cần bổ sung bảng nguồn với unique constraint.
7. Import giả định trạng thái `Hết hàng` được phép như đợt nhập 5 sản phẩm trước. Nếu CHECK constraint không cho phép, toàn bộ transaction rollback.

## Chạy trên Windows PowerShell
```powershell
cd .\tools\product_scraper
py -m pip install -r requirements.txt
py scraper.py --target 180 --max-urls 2000
py import_supabase.py --csv .\output\emua_verified_products.csv
```

Xem `output/emua_scraper_report.md`, `output/emua_needs_review.csv`, kiểm tra từng nguồn. Nếu muốn nhập thật sau khi đã duyệt CSV, cấu hình `EMUA_DATABASE_URL` trong **biến môi trường của phiên PowerShell** (không commit hoặc chia sẻ giá trị) rồi chạy:
```powershell
py import_supabase.py --csv .\output\emua_verified_products.csv --apply
```

**Sao lưu Supabase trước khi import.** Trước khi chạy `--apply`, cần kiểm tra `MaThuongHieu`, `MaDanhMuc`, các CHECK constraint và quyền sử dụng dữ liệu/ảnh. Không chạy `--apply` khi chưa duyệt CSV.
