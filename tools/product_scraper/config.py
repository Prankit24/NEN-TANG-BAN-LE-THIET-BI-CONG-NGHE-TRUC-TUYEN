
# -*- coding: utf-8 -*-
"""
config.py - Cau hinh EMUA Product Scraper

Mapping duoc doi chieu voi Supabase:
- 18 thuong hieu
- 3 danh muc

Chi thu thap san pham co that.
Khong tu tao thong so, gia ban hoac hinh anh.
"""

# ==========================================================
# 1. THUONG HIEU - ID THUC TE TU SUPABASE
# ==========================================================

BRAND_MAP: dict[str, int] = {
    "acer": 1,
    "anker": 2,
    "apple": 3,
    "asus": 4,
    "baseus": 5,
    "dell": 6,
    "hp": 7,
    "jbl": 8,
    "lenovo": 9,
    "logitech": 10,
    "msi": 11,
    "no brand": 12,
    "oppo": 13,
    "rk": 14,
    "samsung": 15,
    "sony": 16,
    "vivo": 17,
    "xiaomi": 18,
}

# ==========================================================
# 2. DANH MUC - ID THUC TE TU SUPABASE
# ==========================================================

CATEGORY_MAP: dict[str, int] = {
    "điện thoại": 1,
    "laptop": 2,
    "phụ kiện": 3,
}

# ==========================================================
# 3. NGUON THU THAP SAN PHAM
# ==========================================================
# Chi bat 3 danh muc hien co.
# Cac CSS selector la cau hinh ban dau, can kiem thu.

SOURCES = [
    {
        "name": "FPT Shop",
        "base_url": "https://fptshop.com.vn",
        "category_urls": {
            "laptop": "https://fptshop.com.vn/may-tinh-xach-tay",
            "điện thoại": "https://fptshop.com.vn/dien-thoai",
            "phụ kiện": "https://fptshop.com.vn/phu-kien",
        },
        "product_link_selector": "a.Card_cardTitle__HlwIo",
        "max_pages": 3,
    },
    {
        "name": "Thế Giới Di Động",
        "base_url": "https://www.thegioididong.com",
        "category_urls": {
            "laptop": "https://www.thegioididong.com/laptop",
            "điện thoại": "https://www.thegioididong.com/dtdd",
            "phụ kiện": "https://www.thegioididong.com/phu-kien",
        },
        "product_link_selector": "a.main-contain",
        "max_pages": 3,
    },
    {
        "name": "CellphoneS",
        "base_url": "https://cellphones.com.vn",
        "category_urls": {
            "laptop": "https://cellphones.com.vn/laptop.html",
            "điện thoại": "https://cellphones.com.vn/mobile.html",
            "phụ kiện": "https://cellphones.com.vn/phu-kien.html",
        },
        "product_link_selector": "div.product-info a",
        "max_pages": 3,
    },
    {
        "name": "Phong Vũ",
        "base_url": "https://phongvu.vn",
        "category_urls": {
            "laptop": "https://phongvu.vn/c/laptop",
            "phụ kiện": "https://phongvu.vn/c/phu-kien-may-tinh",
        },
        "product_link_selector": "a.css-pxdb0j",
        "max_pages": 3,
    },
    {
        "name": "GearVN",
        "base_url": "https://gearvn.com",
        "category_urls": {
            "laptop": "https://gearvn.com/collections/laptop",
            "phụ kiện": "https://gearvn.com/collections/phu-kien",
        },
        "product_link_selector": "a.product-name",
        "max_pages": 3,
    },
]

# ==========================================================
# 4. GIOI HAN THU THAP
# ==========================================================

TARGET_TOTAL_PRODUCTS = 180

MAX_PRODUCTS_PER_CATEGORY = 250

REQUEST_DELAY_SECONDS = 2.0

REQUEST_TIMEOUT_SECONDS = 15

# ==========================================================
# 5. FILE DAU RA
# ==========================================================

OUTPUT_DIR = "output"

CSV_VERIFIED = "emua_verified_products.csv"

CSV_NEEDS_REVIEW = "emua_needs_review.csv"

MISSING_LOOKUP_FILE = "emua_missing_lookups.txt"

ERROR_LOG_FILE = "emua_scraper_errors.log"

REPORT_FILE = "emua_scraper_report.md"

# ==========================================================
# 6. SCHEMA EMUA
# ==========================================================
#
# SanPham:
# - MaSanPham: PK
# - TenSanPham: varchar(100)
# - MoTa: varchar(255)
# - TrangThaiSanPham: varchar(20)
# - BaoHanh: varchar(50)
# - ThongSoKyThuat: text
# - GiaGoc: decimal(12,2)
# - MaThuongHieu: FK NOT NULL
# - MaDanhMuc: FK nullable
#
# BienTheSanPham:
# - MaBienThe: PK
# - MaSanPham: FK NOT NULL
# - MauSac: varchar(50)
# - PhienBan: varchar(100)
# - Gia: decimal(18,2) NOT NULL
# - SoLuong: int NOT NULL
# - HinhAnh: varchar(255)
# - TrangThai: varchar(50)
#
# QUY TAC:
# - Khong tu tao gia san pham.
# - Khong tu tao bien the.
# - Khong lay ton kho cua nha ban le lam ton kho EMUA.
# - Khong tao URL anh gia.
# - Khong ghi du lieu truc tiep vao Supabase.

# Chỉ tiêu theo nhóm sản phẩm, không phải số lượng được đảm bảo.
TARGET_BY_GROUP = {
    "laptop": 50,
    "điện thoại": 40,
    "pc và màn hình": 30,
    "bàn phím, chuột": 30,
    "tai nghe, phụ kiện": 30,
}
# ID danh mục phải lấy từ database thật; không tự tạo ID.
# Những nhóm chưa có ID hợp lệ sẽ ở needs_review, không import.
GROUP_CATEGORY_IDS = {
    "laptop": 2,
    "điện thoại": 1,
    "pc và màn hình": None,
    "bàn phím, chuột": 3,
    "tai nghe, phụ kiện": 3,
}
