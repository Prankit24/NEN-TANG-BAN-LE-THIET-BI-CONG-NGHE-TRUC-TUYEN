# -*- coding: utf-8 -*-
"""
extractors.py – Bộ trích xuất dữ liệu sản phẩm cho từng nhà bán lẻ.

Mỗi extractor nhận BeautifulSoup của trang sản phẩm cụ thể và trả về
dict với các key: TenSanPham, ModelSKU, MoTa, GiaBan, ThongSo (dict CPU/RAM/...),
MauSac, PhienBan, ThuongHieu (tên chuẩn hóa), DanhMuc (tên chuẩn hóa).

Trả về None cho field không tìm được. KHÔNG bịa dữ liệu.
"""

import json
import re
import logging
from typing import Any, Dict, Optional

from bs4 import BeautifulSoup

logger = logging.getLogger(__name__)

# -------------------------------------------------------------------
# Hàm tiện ích dùng chung
# -------------------------------------------------------------------


def _clean_price(text: str) -> Optional[str]:
    """Trích xuất giá từ chuỗi chứa VNĐ. Trả về chuỗi số (đơn vị đồng)."""
    if not text:
        return None
    digits = re.sub(r"[^\d]", "", text)
    if digits and int(digits) > 0:
        return digits
    return None


def _extract_json_ld_product(soup: BeautifulSoup) -> Dict[str, Any]:
    """Read Product JSON-LD from nested arrays/@graph, without guessing."""
    def walk(node):
        if isinstance(node, list):
            for child in node:
                found = walk(child)
                if found:
                    return found
        elif isinstance(node, dict):
            kind = node.get("@type", "")
            kinds = kind if isinstance(kind, list) else [kind]
            if any(str(k).lower().endswith("product") for k in kinds):
                return node
            for key in ("@graph", "mainEntity", "itemListElement"):
                found = walk(node.get(key))
                if found:
                    return found
        return {}
    for script in soup.select("script[type='application/ld+json']"):
        try:
            data = json.loads(script.string or script.get_text() or "")
            product = walk(data)
            if product:
                return product
        except (json.JSONDecodeError, TypeError, ValueError):
            continue
    return {}


def _guess_brand_from_name(name: str) -> Optional[str]:
    """Đoán thương hiệu từ tên sản phẩm dựa trên danh sách brand phổ biến."""
    known = [
        "Apple", "Samsung", "Dell", "HP", "Lenovo", "Asus", "Acer",
        "MSI", "Xiaomi", "Oppo", "Vivo", "Realme", "Huawei", "LG",
        "Sony", "Microsoft", "Gigabyte", "Intel", "AMD", "NVIDIA",
        "Logitech", "Razer", "Corsair", "Kingston", "Western Digital",
        "Seagate", "TP-Link", "Anker", "JBL", "Baseus", "Ugreen",
    ]
    name_lower = name.lower()
    for brand in known:
        if re.search(r"(?<![a-z0-9])" + re.escape(brand.lower()) + r"(?![a-z0-9])", name_lower):
            return brand.lower()
    return None


def _guess_category_from_url_and_name(url: str, name: str) -> Optional[str]:
    """Đoán danh mục từ URL và tên sản phẩm."""
    combined = name.lower()
    patterns = [
        (r"laptop|notebook|macbook", "laptop"),
        (r"điện thoại|dien thoai|smartphone|iphone|galaxy\s*s|galaxy\s*a|galaxy\s*z", "điện thoại"),
        (r"may-tinh-bang|tablet|ipad", "máy tính bảng"),
        (r"man-hinh|monitor", "màn hình"),
        (r"may-tinh-de-ban|desktop|pc-gvn|pc\b", "pc"),
        (r"linh-kien|cpu|ram|ssd|vga|card|mainboard|psu|case", "linh kiện máy tính"),
        (r"phu-kien|accessory|tai-nghe|chuot|ban-phim|sac|cap", "phụ kiện"),
    ]
    for pattern, category in patterns:
        if re.search(pattern, combined):
            return category
    return None


# ===================================================================
# EXTRACTOR: FPT Shop
# ===================================================================


def extract_fptshop(soup: BeautifulSoup, url: str) -> Dict[str, Any]:
    """Trích xuất từ trang chi tiết sản phẩm fptshop.com.vn"""
    result: Dict[str, Any] = {}

    # --- JSON-LD (nguồn chính xác nhất) ---
    jld = _extract_json_ld_product(soup)
    if jld:
        result["TenSanPham"] = jld.get("name")
        result["ModelSKU"] = jld.get("sku") or jld.get("mpn")
        result["MoTa"] = jld.get("description")
        offers = jld.get("offers", {})
        if isinstance(offers, list):
            offers = offers[0] if offers else {}
        price = offers.get("price")
        if price:
            result["GiaBan"] = str(int(float(price)))
        brand_info = jld.get("brand", {})
        if isinstance(brand_info, dict):
            result["ThuongHieu"] = (brand_info.get("name") or "").lower()

    # --- Fallback: HTML elements ---
    if not result.get("TenSanPham"):
        h1 = soup.select_one("h1")
        if h1:
            result["TenSanPham"] = h1.get_text(strip=True)

    if not result.get("GiaBan"):
        for sel in ["p.Price_currentPrice__PBYSr", "div.product-price span", "span.st-price-main"]:
            el = soup.select_one(sel)
            if el:
                cleaned = _clean_price(el.get_text())
                if cleaned:
                    result["GiaBan"] = cleaned
                    break

    # Thông số kỹ thuật
    specs = {}
    spec_section = soup.select("div.SpecificationsDetail_content__bG1rS li, table.specificationsTable tr, div.ts-list div.ts-item")
    for item in spec_section:
        label_el = item.select_one("span.SpecificationsDetail_label__YsSH0, td:first-child, div.ts-label, span:first-child")
        value_el = item.select_one("span.SpecificationsDetail_value__FZSVK, td:last-child, div.ts-value, span:last-child")
        if label_el and value_el:
            label = label_el.get_text(strip=True).lower()
            value = value_el.get_text(strip=True)
            if any(k in label for k in ["cpu", "vi xử lý", "chip"]):
                specs["CPU"] = value
            elif any(k in label for k in ["ram", "bộ nhớ trong"]):
                specs["RAM"] = value
            elif any(k in label for k in ["ssd", "ổ cứng", "lưu trữ", "dung lượng"]):
                specs["SSD"] = value
            elif any(k in label for k in ["gpu", "card đồ họa", "vga"]):
                specs["GPU"] = value
            elif any(k in label for k in ["màu", "color"]):
                result["MauSac"] = value

    if specs:
        result["ThongSo"] = specs

    # Thương hiệu fallback
    if not result.get("ThuongHieu") and result.get("TenSanPham"):
        result["ThuongHieu"] = _guess_brand_from_name(result["TenSanPham"])

    # Danh mục
    result["DanhMuc"] = _guess_category_from_url_and_name(url, result.get("TenSanPham", ""))

    return result


# ===================================================================
# EXTRACTOR: Thế Giới Di Động
# ===================================================================


def extract_thegioididong(soup: BeautifulSoup, url: str) -> Dict[str, Any]:
    """Trích xuất từ trang chi tiết sản phẩm thegioididong.com"""
    result: Dict[str, Any] = {}

    jld = _extract_json_ld_product(soup)
    if jld:
        result["TenSanPham"] = jld.get("name")
        result["ModelSKU"] = jld.get("sku") or jld.get("mpn")
        result["MoTa"] = jld.get("description")
        offers = jld.get("offers", {})
        if isinstance(offers, list):
            offers = offers[0] if offers else {}
        price = offers.get("price")
        if price:
            result["GiaBan"] = str(int(float(price)))
        brand_info = jld.get("brand", {})
        if isinstance(brand_info, dict):
            result["ThuongHieu"] = (brand_info.get("name") or "").lower()

    if not result.get("TenSanPham"):
        h1 = soup.select_one("h1")
        if h1:
            result["TenSanPham"] = h1.get_text(strip=True)

    if not result.get("GiaBan"):
        for sel in ["div.box-price-present", "p.box-price-present", "span.price"]:
            el = soup.select_one(sel)
            if el:
                cleaned = _clean_price(el.get_text())
                if cleaned:
                    result["GiaBan"] = cleaned
                    break

    # Thông số kỹ thuật
    specs = {}
    for row in soup.select("ul.parameter__list li, div.parameter__item, ul.text-specifi li"):
        label_el = row.select_one("p:first-child, span:first-child, strong")
        value_el = row.select_one("p:last-child, span:last-child, div")
        if label_el and value_el and label_el != value_el:
            label = label_el.get_text(strip=True).lower()
            value = value_el.get_text(strip=True)
            if any(k in label for k in ["cpu", "vi xử lý", "chip xử lý"]):
                specs["CPU"] = value
            elif any(k in label for k in ["ram"]):
                specs["RAM"] = value
            elif any(k in label for k in ["ssd", "ổ cứng", "bộ nhớ trong", "dung lượng"]):
                specs["SSD"] = value
            elif any(k in label for k in ["gpu", "card đồ họa", "card màn hình"]):
                specs["GPU"] = value
            elif any(k in label for k in ["màu", "color"]):
                result["MauSac"] = value

    # Biến thể màu từ nút chọn
    colors = []
    for color_btn in soup.select("a.box-color, a.color__item"):
        c = color_btn.get("title") or color_btn.get_text(strip=True)
        if c:
            colors.append(c)
    if colors and not result.get("MauSac"):
        result["MauSac"] = ", ".join(colors)

    if specs:
        result["ThongSo"] = specs

    if not result.get("ThuongHieu") and result.get("TenSanPham"):
        result["ThuongHieu"] = _guess_brand_from_name(result["TenSanPham"])

    result["DanhMuc"] = _guess_category_from_url_and_name(url, result.get("TenSanPham", ""))

    return result


# ===================================================================
# EXTRACTOR: CellphoneS
# ===================================================================


def extract_cellphones(soup: BeautifulSoup, url: str) -> Dict[str, Any]:
    """Trích xuất từ trang chi tiết sản phẩm cellphones.com.vn"""
    result: Dict[str, Any] = {}

    jld = _extract_json_ld_product(soup)
    if jld:
        result["TenSanPham"] = jld.get("name")
        result["ModelSKU"] = jld.get("sku") or jld.get("mpn")
        result["MoTa"] = jld.get("description")
        offers = jld.get("offers", {})
        if isinstance(offers, list):
            offers = offers[0] if offers else {}
        price = offers.get("price")
        if price:
            result["GiaBan"] = str(int(float(price)))
        brand_info = jld.get("brand", {})
        if isinstance(brand_info, dict):
            result["ThuongHieu"] = (brand_info.get("name") or "").lower()

    if not result.get("TenSanPham"):
        h1 = soup.select_one("h1")
        if h1:
            result["TenSanPham"] = h1.get_text(strip=True)

    if not result.get("GiaBan"):
        for sel in ["p.product__price--show", "span.product__price--show", "p.tpt-price"]:
            el = soup.select_one(sel)
            if el:
                cleaned = _clean_price(el.get_text())
                if cleaned:
                    result["GiaBan"] = cleaned
                    break

    specs = {}
    for row in soup.select("div.technical-content ul li, ul.technical__content li"):
        label_el = row.select_one("span:first-child, p:first-child")
        value_el = row.select_one("span:last-child, p:last-child, div")
        if label_el and value_el and label_el != value_el:
            label = label_el.get_text(strip=True).lower()
            value = value_el.get_text(strip=True)
            if any(k in label for k in ["cpu", "vi xử lý", "chip"]):
                specs["CPU"] = value
            elif "ram" in label:
                specs["RAM"] = value
            elif any(k in label for k in ["ssd", "ổ cứng", "lưu trữ", "dung lượng"]):
                specs["SSD"] = value
            elif any(k in label for k in ["gpu", "card đồ họa"]):
                specs["GPU"] = value
            elif any(k in label for k in ["màu", "color"]):
                result["MauSac"] = value

    if specs:
        result["ThongSo"] = specs

    if not result.get("ThuongHieu") and result.get("TenSanPham"):
        result["ThuongHieu"] = _guess_brand_from_name(result["TenSanPham"])

    result["DanhMuc"] = _guess_category_from_url_and_name(url, result.get("TenSanPham", ""))

    return result


# ===================================================================
# EXTRACTOR: Phong Vũ
# ===================================================================


def extract_phongvu(soup: BeautifulSoup, url: str) -> Dict[str, Any]:
    """Trích xuất từ trang chi tiết sản phẩm phongvu.vn"""
    result: Dict[str, Any] = {}

    jld = _extract_json_ld_product(soup)
    if jld:
        result["TenSanPham"] = jld.get("name")
        result["ModelSKU"] = jld.get("sku") or jld.get("mpn")
        result["MoTa"] = jld.get("description")
        offers = jld.get("offers", {})
        if isinstance(offers, list):
            offers = offers[0] if offers else {}
        price = offers.get("price")
        if price:
            result["GiaBan"] = str(int(float(price)))
        brand_info = jld.get("brand", {})
        if isinstance(brand_info, dict):
            result["ThuongHieu"] = (brand_info.get("name") or "").lower()

    if not result.get("TenSanPham"):
        h1 = soup.select_one("h1")
        if h1:
            result["TenSanPham"] = h1.get_text(strip=True)

    if not result.get("GiaBan"):
        for sel in ["div.product-price__current-price", "span.css-oqbaxb", "div.att-product-detail-latest-price"]:
            el = soup.select_one(sel)
            if el:
                cleaned = _clean_price(el.get_text())
                if cleaned:
                    result["GiaBan"] = cleaned
                    break

    specs = {}
    for row in soup.select("div.css-1uytbx3 div, table.specification-table tr, div.attr-content div.attr-item"):
        label_el = row.select_one("span:first-child, td:first-child")
        value_el = row.select_one("span:last-child, td:last-child")
        if label_el and value_el and label_el != value_el:
            label = label_el.get_text(strip=True).lower()
            value = value_el.get_text(strip=True)
            if any(k in label for k in ["cpu", "vi xử lý"]):
                specs["CPU"] = value
            elif "ram" in label:
                specs["RAM"] = value
            elif any(k in label for k in ["ssd", "ổ cứng", "lưu trữ"]):
                specs["SSD"] = value
            elif any(k in label for k in ["gpu", "vga", "card đồ họa"]):
                specs["GPU"] = value
            elif any(k in label for k in ["màu", "color"]):
                result["MauSac"] = value

    if specs:
        result["ThongSo"] = specs

    if not result.get("ThuongHieu") and result.get("TenSanPham"):
        result["ThuongHieu"] = _guess_brand_from_name(result["TenSanPham"])

    result["DanhMuc"] = _guess_category_from_url_and_name(url, result.get("TenSanPham", ""))

    return result


# ===================================================================
# EXTRACTOR: GearVN
# ===================================================================


def extract_gearvn(soup: BeautifulSoup, url: str) -> Dict[str, Any]:
    """Trích xuất từ trang chi tiết sản phẩm gearvn.com"""
    result: Dict[str, Any] = {}

    jld = _extract_json_ld_product(soup)
    if jld:
        result["TenSanPham"] = jld.get("name")
        result["ModelSKU"] = jld.get("sku") or jld.get("mpn")
        result["MoTa"] = jld.get("description")
        offers = jld.get("offers", {})
        if isinstance(offers, list):
            offers = offers[0] if offers else {}
        price = offers.get("price")
        if price:
            result["GiaBan"] = str(int(float(price)))
        brand_info = jld.get("brand", {})
        if isinstance(brand_info, dict):
            result["ThuongHieu"] = (brand_info.get("name") or "").lower()

    if not result.get("TenSanPham"):
        h1 = soup.select_one("h1.product_name, h1")
        if h1:
            result["TenSanPham"] = h1.get_text(strip=True)

    if not result.get("GiaBan"):
        for sel in ["span.product_sale_price", "div.product-price span.pro-price"]:
            el = soup.select_one(sel)
            if el:
                cleaned = _clean_price(el.get_text())
                if cleaned:
                    result["GiaBan"] = cleaned
                    break

    specs = {}
    for row in soup.select("div.productSpecification_Content table tr, table.tblGeneralContent tr"):
        tds = row.select("td")
        if len(tds) >= 2:
            label = tds[0].get_text(strip=True).lower()
            value = tds[1].get_text(strip=True)
            if any(k in label for k in ["cpu", "vi xử lý"]):
                specs["CPU"] = value
            elif "ram" in label:
                specs["RAM"] = value
            elif any(k in label for k in ["ssd", "ổ cứng", "lưu trữ"]):
                specs["SSD"] = value
            elif any(k in label for k in ["gpu", "vga", "card đồ họa", "card màn hình"]):
                specs["GPU"] = value
            elif any(k in label for k in ["màu", "color"]):
                result["MauSac"] = value

    if specs:
        result["ThongSo"] = specs

    if not result.get("ThuongHieu") and result.get("TenSanPham"):
        result["ThuongHieu"] = _guess_brand_from_name(result["TenSanPham"])

    result["DanhMuc"] = _guess_category_from_url_and_name(url, result.get("TenSanPham", ""))

    return result


# ===================================================================
# EXTRACTOR: Generic (Fallback)
# ===================================================================


def extract_generic(soup: BeautifulSoup, url: str) -> Dict[str, Any]:
    """Fallback: dùng JSON-LD, Open Graph, meta tags."""
    result: Dict[str, Any] = {}

    jld = _extract_json_ld_product(soup)
    if jld:
        result["TenSanPham"] = jld.get("name")
        result["ModelSKU"] = jld.get("sku") or jld.get("mpn")
        result["MoTa"] = jld.get("description")
        offers = jld.get("offers", {})
        if isinstance(offers, list):
            offers = offers[0] if offers else {}
        price = offers.get("price")
        if price:
            result["GiaBan"] = str(int(float(price)))
        brand_info = jld.get("brand", {})
        if isinstance(brand_info, dict):
            result["ThuongHieu"] = (brand_info.get("name") or "").lower()

    if not result.get("TenSanPham"):
        og = soup.find("meta", property="og:title")
        if og and og.get("content"):
            result["TenSanPham"] = og["content"].strip()
        else:
            h1 = soup.select_one("h1")
            if h1:
                result["TenSanPham"] = h1.get_text(strip=True)

    if not result.get("MoTa"):
        og = soup.find("meta", property="og:description")
        if og and og.get("content"):
            result["MoTa"] = og["content"].strip()

    if not result.get("ThuongHieu") and result.get("TenSanPham"):
        result["ThuongHieu"] = _guess_brand_from_name(result["TenSanPham"])

    result["DanhMuc"] = _guess_category_from_url_and_name(url, result.get("TenSanPham", ""))

    return result


# ===================================================================
# DISPATCHER: Chọn extractor phù hợp theo domain
# ===================================================================


DOMAIN_EXTRACTORS = {
    "fptshop.com.vn": extract_fptshop,
    "thegioididong.com": extract_thegioididong,
    "cellphones.com.vn": extract_cellphones,
    "phongvu.vn": extract_phongvu,
    "gearvn.com": extract_gearvn,
}


def extract(soup: BeautifulSoup, url: str) -> Dict[str, Any]:
    """Chọn extractor phù hợp và chạy. Trả về dict dữ liệu sản phẩm."""
    url_lower = url.lower()
    for domain, func in DOMAIN_EXTRACTORS.items():
        if domain in url_lower:
            return func(soup, url)
    return extract_generic(soup, url)
