-- ============================================================================
-- DỰ ÁN EMUA - NÂNG CẤP CƠ SỞ DỮ LIỆU NHẬP HÀNG & BÁO GIÁ NHÀ CUNG CẤP
-- HỆ THỐNG: ASP.NET CORE MVC 8 + POSTGRESQL (SUPABASE)
-- TẬP LỆNH: supabase_staff_import_upgrade.sql
-- TÍNH CHẤT: AN TOÀN, KHÔNG PHÁ HỦY DỮ LIỆU HIỆN CÓ (NON-DESTRUCTIVE)
-- ============================================================================

-- ============================================================================
-- PHẦN A: CÁC TRUY VẤN KIỂM TRA HIỆN TRẠNG (DIAGNOSTIC QUERIES)
-- Chạy phần này để kiểm tra cấu trúc bảng hiện tại trên Supabase trước khi nâng cấp
-- ============================================================================

-- A.1. Kiểm tra sự tồn tại của các bảng liên quan đến Nhà cung cấp và Nhập hàng
SELECT 
    table_schema, 
    table_name,
    table_type
FROM information_schema.tables 
WHERE table_schema = 'public' 
  AND table_name IN ('NhaCungCap', 'BaoGiaNhapHang', 'PhieuNhapHang', 'ChiTietPhieuNhapHang', 'BienTheSanPham', 'SanPham')
ORDER BY table_name;

-- A.2. Kiểm tra các cột hiện có của bảng PhieuNhapHang
SELECT 
    column_name, 
    data_type, 
    is_nullable, 
    character_maximum_length,
    numeric_precision,
    numeric_scale
FROM information_schema.columns 
WHERE table_schema = 'public' 
  AND table_name = 'PhieuNhapHang'
ORDER BY ordinal_position;

-- A.3. Kiểm tra các cột hiện có của bảng ChiTietPhieuNhapHang
SELECT 
    column_name, 
    data_type, 
    is_nullable, 
    character_maximum_length,
    numeric_precision,
    numeric_scale
FROM information_schema.columns 
WHERE table_schema = 'public' 
  AND table_name = 'ChiTietPhieuNhapHang'
ORDER BY ordinal_position;


-- ============================================================================
-- PHẦN B: TẬP LỆNH DDL NÂNG CẤP HỆ THỐNG (CHẠY CHÍNH THỨC)
-- Gồm: Tạo bảng độc lập Nhà cung cấp, Báo giá nhập hàng, bổ sung cột thuế & chi phí
-- ============================================================================

BEGIN;

-- B.1. TẠO BẢNG ĐỘC LẬP QUẢN LÝ NHÀ CUNG CẤP (NhaCungCap)
CREATE TABLE IF NOT EXISTS public."NhaCungCap" (
    "MaNhaCungCap" SERIAL PRIMARY KEY,
    "TenNhaCungCap" VARCHAR(200) NOT NULL,
    "MaSoThue" VARCHAR(50),
    "SoDienThoai" VARCHAR(20),
    "Email" VARCHAR(100),
    "DiaChi" VARCHAR(255),
    "NguoiLienHe" VARCHAR(100),
    "GhiChu" VARCHAR(500),
    "TrangThai" VARCHAR(50) NOT NULL DEFAULT 'Đang hợp tác',
    "NgayTao" TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC')
);

-- B.2. DI CHUYỂN DỮ LIỆU TÊN NHÀ CUNG CẤP CŨ VÀO BẢNG MỚI (NẾU CHƯA CÓ)
-- Đảm bảo dữ liệu lịch sử từ PhieuNhapHang.NhaCungCap không bị mất
INSERT INTO public."NhaCungCap" ("TenNhaCungCap", "TrangThai", "GhiChu")
SELECT DISTINCT 
    TRIM("NhaCungCap"), 
    'Đang hợp tác', 
    'Tự động đồng bộ từ lịch sử phiếu nhập hàng EMUA'
FROM public."PhieuNhapHang"
WHERE "NhaCungCap" IS NOT NULL 
  AND TRIM("NhaCungCap") <> ''
  AND NOT EXISTS (
      SELECT 1 FROM public."NhaCungCap" ncc 
      WHERE LOWER(TRIM(ncc."TenNhaCungCap")) = LOWER(TRIM("PhieuNhapHang"."NhaCungCap"))
  );

-- B.3. TẠO BẢNG BÁO GIÁ NHẬP HÀNG (BaoGiaNhapHang)
-- Quản lý giá nhập trước VAT, VAT đầu vào, chiết khấu, chi phí vận chuyển & ngày hiệu lực
CREATE TABLE IF NOT EXISTS public."BaoGiaNhapHang" (
    "MaBaoGia" SERIAL PRIMARY KEY,
    "MaNhaCungCap" INT NOT NULL REFERENCES public."NhaCungCap"("MaNhaCungCap") ON DELETE RESTRICT,
    "MaBienThe" INT NOT NULL REFERENCES public."BienTheSanPham"("MaBienThe") ON DELETE CASCADE,
    "SoLuongToiThieu" INT NOT NULL DEFAULT 1,
    "DonGiaChuaThue" NUMERIC(18, 2) NOT NULL,
    "ThueSuatVat" NUMERIC(5, 2) NOT NULL DEFAULT 10.00,
    "TienThueVat" NUMERIC(18, 2) NOT NULL DEFAULT 0.00,
    "DonGiaSauThue" NUMERIC(18, 2) NOT NULL,
    "ChietKhau" NUMERIC(18, 2) NOT NULL DEFAULT 0.00,
    "ChiPhiVanChuyenUocTinh" NUMERIC(18, 2) NOT NULL DEFAULT 0.00,
    "NgayHieuLuc" TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
    "NgayHetHan" TIMESTAMP WITHOUT TIME ZONE,
    "TrangThai" VARCHAR(50) NOT NULL DEFAULT 'Hiệu lực',
    "GhiChu" VARCHAR(500),
    "NgayCapNhat" TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC')
);

-- B.4. BỔ SUNG CỘT CHO BẢNG PHIẾU NHẬP HÀNG (PhieuNhapHang)
-- Giữ nguyên cột "NhaCungCap" cũ để backward-compatibility, bổ sung FK "MaNhaCungCap" và thuế phí
ALTER TABLE public."PhieuNhapHang"
    ADD COLUMN IF NOT EXISTS "MaNhaCungCap" INT REFERENCES public."NhaCungCap"("MaNhaCungCap") ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS "TienHangChuaThue" NUMERIC(18, 2) NOT NULL DEFAULT 0.00,
    ADD COLUMN IF NOT EXISTS "TienThueVat" NUMERIC(18, 2) NOT NULL DEFAULT 0.00,
    ADD COLUMN IF NOT EXISTS "ChiPhiVanChuyen" NUMERIC(18, 2) NOT NULL DEFAULT 0.00,
    ADD COLUMN IF NOT EXISTS "ChiPhiKhac" NUMERIC(18, 2) NOT NULL DEFAULT 0.00,
    ADD COLUMN IF NOT EXISTS "ChietKhau" NUMERIC(18, 2) NOT NULL DEFAULT 0.00;

-- Cập nhật FK MaNhaCungCap cho các phiếu nhập cũ dựa theo TenNhaCungCap
UPDATE public."PhieuNhapHang" pnh
SET "MaNhaCungCap" = ncc."MaNhaCungCap"
FROM public."NhaCungCap" ncc
WHERE pnh."MaNhaCungCap" IS NULL 
  AND LOWER(TRIM(pnh."NhaCungCap")) = LOWER(TRIM(ncc."TenNhaCungCap"));

-- B.5. BỔ SUNG CỘT CHO BẢNG CHI TIẾT PHIẾU NHẬP HÀNG (ChiTietPhieuNhapHang)
-- Bổ sung đơn giá trước thuế, thuế suất VAT, tiền thuế, đơn giá sau thuế, chiết khấu và thành tiền
ALTER TABLE public."ChiTietPhieuNhapHang"
    ADD COLUMN IF NOT EXISTS "DonGiaChuaThue" NUMERIC(18, 2) NOT NULL DEFAULT 0.00,
    ADD COLUMN IF NOT EXISTS "ThueSuatVat" NUMERIC(5, 2) NOT NULL DEFAULT 10.00,
    ADD COLUMN IF NOT EXISTS "TienThueVat" NUMERIC(18, 2) NOT NULL DEFAULT 0.00,
    ADD COLUMN IF NOT EXISTS "DonGiaSauThue" NUMERIC(18, 2) NOT NULL DEFAULT 0.00,
    ADD COLUMN IF NOT EXISTS "ChietKhau" NUMERIC(18, 2) NOT NULL DEFAULT 0.00,
    ADD COLUMN IF NOT EXISTS "ThanhTien" NUMERIC(18, 2) NOT NULL DEFAULT 0.00;

-- Tính lại giá trị cho các bản ghi cũ của ChiTietPhieuNhapHang nếu DonGiaChuaThue đang = 0
UPDATE public."ChiTietPhieuNhapHang"
SET 
    "DonGiaChuaThue" = CASE WHEN "DonGiaNhapDuKien" > 0 THEN ROUND("DonGiaNhapDuKien" / 1.10, 2) ELSE 0 END,
    "ThueSuatVat" = 10.00,
    "TienThueVat" = CASE WHEN "DonGiaNhapDuKien" > 0 THEN ("DonGiaNhapDuKien" - ROUND("DonGiaNhapDuKien" / 1.10, 2)) * "SoLuongNhap" ELSE 0 END,
    "DonGiaSauThue" = "DonGiaNhapDuKien",
    "ThanhTien" = "DonGiaNhapDuKien" * "SoLuongNhap"
WHERE "DonGiaSauThue" = 0 AND "DonGiaNhapDuKien" > 0;

-- B.6. TẠO INDEXES TỐI ƯU HIỆU NĂNG CHO CÁC TRUY VẤN
CREATE INDEX IF NOT EXISTS "idx_baogianhaphang_nhacungcap" ON public."BaoGiaNhapHang" ("MaNhaCungCap");
CREATE INDEX IF NOT EXISTS "idx_baogianhaphang_bienthe" ON public."BaoGiaNhapHang" ("MaBienThe");
CREATE INDEX IF NOT EXISTS "idx_baogianhaphang_trangthai" ON public."BaoGiaNhapHang" ("TrangThai");
CREATE INDEX IF NOT EXISTS "idx_phieunhaphang_manhacungcap" ON public."PhieuNhapHang" ("MaNhaCungCap");

COMMIT;


-- ============================================================================
-- PHẦN C: TRUY VẤN XÁC MINH SAU KHI NÂNG CẤP (VERIFICATION QUERIES)
-- ============================================================================

-- C.1. Kiểm tra 2 bảng mới đã tồn tại
SELECT table_name 
FROM information_schema.tables 
WHERE table_schema = 'public' 
  AND table_name IN ('NhaCungCap', 'BaoGiaNhapHang');

-- C.2. Kiểm tra các cột mới của PhieuNhapHang
SELECT column_name, data_type 
FROM information_schema.columns 
WHERE table_schema = 'public' 
  AND table_name = 'PhieuNhapHang' 
  AND column_name IN ('MaNhaCungCap', 'TienHangChuaThue', 'TienThueVat', 'ChiPhiVanChuyen', 'ChiPhiKhac', 'ChietKhau');

-- C.3. Kiểm tra các cột mới của ChiTietPhieuNhapHang
SELECT column_name, data_type 
FROM information_schema.columns 
WHERE table_schema = 'public' 
  AND table_name = 'ChiTietPhieuNhapHang' 
  AND column_name IN ('DonGiaChuaThue', 'ThueSuatVat', 'TienThueVat', 'DonGiaSauThue', 'ChietKhau', 'ThanhTien');

-- C.4. Xem danh sách nhà cung cấp vừa được đồng bộ
SELECT "MaNhaCungCap", "TenNhaCungCap", "TrangThai", "NgayTao" 
FROM public."NhaCungCap" 
LIMIT 10;


-- ============================================================================
-- PHẦN D: DỮ LIỆU MẪU BÁO GIÁ NHÀ CUNG CẤP (OPTIONAL / SEED DATA)
-- Chạy nếu muốn có sẵn báo giá mẫu để kiểm thử tính năng so sánh nhà cung cấp
-- ============================================================================

DO $$
DECLARE
    ncc_apple_id INT;
    ncc_samsung_id INT;
    ncc_fpt_id INT;
    bt_id INT;
    gia_ban NUMERIC(18,2);
BEGIN
    -- Đảm bảo có ít nhất 3 nhà cung cấp mẫu
    INSERT INTO public."NhaCungCap" ("TenNhaCungCap", "MaSoThue", "SoDienThoai", "Email", "DiaChi", "TrangThai")
    VALUES 
        ('Công ty Cổ phần Synnex FPT', '0101778163', '02473006666', 'contact@synnexfpt.com.vn', 'Tòa nhà FPT, Cầu Giấy, Hà Nội', 'Đang hợp tác'),
        ('Công ty TNHH Apple Việt Nam', '0313506115', '18001127', 'contact@apple.com', 'Tầng 33, Tòa nhà Deutsches Haus, TP.HCM', 'Đang hợp tác'),
        ('Công ty TNHH Điện tử Samsung Vina', '0301111080', '1800588889', 'support@samsung.com', 'Tòa nhà Bitexco, Q.1, TP.HCM', 'Đang hợp tác')
    ON CONFLICT DO NOTHING;

    SELECT "MaNhaCungCap" INTO ncc_fpt_id FROM public."NhaCungCap" WHERE "TenNhaCungCap" = 'Công ty Cổ phần Synnex FPT' LIMIT 1;
    SELECT "MaNhaCungCap" INTO ncc_apple_id FROM public."NhaCungCap" WHERE "TenNhaCungCap" = 'Công ty TNHH Apple Việt Nam' LIMIT 1;

    -- Lấy 1 biến thể bất kỳ để gán báo giá demo
    SELECT "MaBienThe", "Gia" INTO bt_id, gia_ban FROM public."BienTheSanPham" ORDER BY "MaBienThe" ASC LIMIT 1;

    IF bt_id IS NOT NULL AND ncc_fpt_id IS NOT NULL AND ncc_apple_id IS NOT NULL THEN
        -- Báo giá từ Synnex FPT (giả sử giá nhập bằng 70% giá bán)
        INSERT INTO public."BaoGiaNhapHang" (
            "MaNhaCungCap", "MaBienThe", "SoLuongToiThieu", 
            "DonGiaChuaThue", "ThueSuatVat", "TienThueVat", "DonGiaSauThue", 
            "ChietKhau", "ChiPhiVanChuyenUocTinh", "NgayHieuLuc", "TrangThai", "GhiChu"
        ) VALUES (
            ncc_fpt_id, bt_id, 5,
            ROUND(gia_ban * 0.70 / 1.10, 2), 10.00, 
            ROUND((gia_ban * 0.70) - (gia_ban * 0.70 / 1.10), 2),
            ROUND(gia_ban * 0.70, 2),
            50000.00, 20000.00, (NOW() AT TIME ZONE 'UTC'), 'Hiệu lực', 'Báo giá đại lý cấp 1 ưu đãi'
        ) ON CONFLICT DO NOTHING;

        -- Báo giá từ Apple Việt Nam (giả sử giá nhập bằng 72% giá bán)
        INSERT INTO public."BaoGiaNhapHang" (
            "MaNhaCungCap", "MaBienThe", "SoLuongToiThieu", 
            "DonGiaChuaThue", "ThueSuatVat", "TienThueVat", "DonGiaSauThue", 
            "ChietKhau", "ChiPhiVanChuyenUocTinh", "NgayHieuLuc", "TrangThai", "GhiChu"
        ) VALUES (
            ncc_apple_id, bt_id, 10,
            ROUND(gia_ban * 0.72 / 1.10, 2), 10.00, 
            ROUND((gia_ban * 0.72) - (gia_ban * 0.72 / 1.10), 2),
            ROUND(gia_ban * 0.72, 2),
            0.00, 0.00, (NOW() AT TIME ZONE 'UTC'), 'Hiệu lực', 'Báo giá nhập trực tiếp chính hãng'
        ) ON CONFLICT DO NOTHING;
    END IF;
END $$;
