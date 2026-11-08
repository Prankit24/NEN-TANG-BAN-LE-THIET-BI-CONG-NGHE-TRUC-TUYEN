# -*- coding: utf-8 -*-
"""EMUA scraper: export candidates for manual source verification; never writes to DB."""
import argparse
import csv
import datetime
import json
import logging
import re
import sys
import time
import unicodedata
from pathlib import Path
from urllib.parse import urlparse

import requests
from bs4 import BeautifulSoup
from config import (BRAND_MAP, CATEGORY_MAP, OUTPUT_DIR, CSV_VERIFIED, CSV_NEEDS_REVIEW,
                    MISSING_LOOKUP_FILE, ERROR_LOG_FILE, REPORT_FILE,
                    REQUEST_DELAY_SECONDS, REQUEST_TIMEOUT_SECONDS, TARGET_TOTAL_PRODUCTS, TARGET_BY_GROUP, GROUP_CATEGORY_IDS)
from discovery import discover_product_urls, HEADERS, _is_product_url
from extractors import extract, _extract_json_ld_product

logging.basicConfig(level=logging.INFO, format='%(asctime)s [%(levelname)s] %(message)s', stream=sys.stdout)
log = logging.getLogger('emua')
FIELDS = ['TenSanPham','ThuongHieu','MaThuongHieu','DanhMuc','MaDanhMuc','ModelSKU',
          'MoTa','GiaBan','GiaGoc','ThongSoKyThuat','BaoHanh','MauSac','PhienBan',
          'AnhURL','NhomMucTieu','NguonURL','NgayThuThap','NguonWebsite','TrangThaiXacMinh','LyDoCanKiemTra']
SOURCES = {'fptshop.com.vn':'FPT Shop','thegioididong.com':'Thế Giới Di Động',
           'cellphones.com.vn':'CellphoneS','phongvu.vn':'Phong Vũ','gearvn.com':'GearVN'}

def norm(s):
    s = unicodedata.normalize('NFKD', str(s or '').casefold())
    return re.sub(r'\s+', ' ', ''.join(c for c in s if c.isalnum() or c.isspace())).strip()

def resolve_brand(value):
    value = str(value or '').strip().casefold()
    aliases = {'apple inc.':'apple', 'hp inc.':'hp', 'asustek':'asus'}
    value = aliases.get(value, value)
    return BRAND_MAP.get(value)

def price_number(value):
    if value is None or isinstance(value, bool):
        return None
    text = str(value).strip()
    if re.fullmatch(r'\d+(?:\.\d{1,2})?', text):
        n = float(text)
    elif re.fullmatch(r'\d{1,3}(?:[.,]\d{3})+(?:\s*(?:₫|đ|vnd))?', text, re.I):
        n = float(re.sub(r'[^0-9]', '', text))
    else:
        return None
    return str(int(n)) if 1000 <= n <= 999999999999 else None

def product_schema(soup):
    """Only structured Product JSON-LD is sufficient evidence for a product page."""
    return _extract_json_ld_product(soup)

def group_for(name, url, category):
    """Conservative grouping based on title and URL; ambiguous items need review."""
    text = norm(name)
    url_path = urlparse(url).path.casefold()
    if re.search(r"\b(laptop|notebook|macbook)\b", text) or '/laptop/' in url_path or '/may-tinh-xach-tay/' in url_path:
        return 'laptop'
    if re.search(r"\b(iphone|smartphone|dien thoai)\b", text) or re.search(r"\b(galaxy s|galaxy a|galaxy z|redmi note|oppo reno|vivo y)\b", text) or '/dtdd/' in url_path or '/dien-thoai/' in url_path:
        return 'điện thoại'
    if re.search(r"\b(monitor|man hinh|desktop|may tinh de ban|mini pc|all in one|pc gaming)\b", text):
        return 'pc và màn hình'
    if re.search(r"\b(ban phim|keyboard|chuot|mouse)\b", text):
        return 'bàn phím, chuột'
    if re.search(r"\b(tai nghe|headphone|headset|earbuds|loa bluetooth|sac du phong|cu sac|cap sac|adapter|hub usb)\b", text):
        return 'tai nghe, phụ kiện'
    return ''


def product_image(schema):
    images = schema.get('image') or []
    if isinstance(images, (str, dict)):
        images = [images]
    for image in images:
        if isinstance(image, dict):
            image = image.get('url') or image.get('contentUrl')
        if isinstance(image, str) and urlparse(image).scheme == 'https':
            return image[:1000]
    return ''

def process_url(url, hint=None):
    parsed = urlparse(url)
    host = (parsed.hostname or '').lower().removeprefix('www.')
    if not any(host == d or host.endswith('.' + d) for d in SOURCES):
        log.warning('Bỏ qua domain ngoài danh sách: %s', url)
        return None
    if not _is_product_url(url, host):
        log.warning('Bỏ qua URL không phải chi tiết sản phẩm: %s', url)
        return None
    try:
        response = requests.get(url, headers=HEADERS, timeout=REQUEST_TIMEOUT_SECONDS)
        response.raise_for_status()
    except requests.RequestException as exc:
        log.warning('Không tải được %s: %s', url, exc)
        return None
    soup = BeautifulSoup(response.text, 'lxml')
    schema = product_schema(soup)
    if not schema:
        log.info('Không có Product JSON-LD, đưa qua danh sách lỗi: %s', url)
        return None
    data = extract(soup, url)
    name = str(schema.get('name') or '').strip()
    if not name or not data.get('TenSanPham'):
        return None
    reasons = []
    if len(name) > 100:
        reasons.append('Tên dài hơn giới hạn 100 ký tự; không tự cắt')
    if len(name) < 8:
        reasons.append('Tên sản phẩm quá ngắn')
    if norm(name) != norm(data.get('TenSanPham')):
        reasons.append('Tên trong dữ liệu trích xuất khác Product JSON-LD')
    brand = str(data.get('ThuongHieu') or '').strip().lower()
    brand_id = resolve_brand(brand)
    if not brand_id:
        reasons.append('Thiếu hoặc chưa ánh xạ thương hiệu')
    category = str(data.get('DanhMuc') or '').strip()
    if category not in CATEGORY_MAP:
        reasons.append('Danh mục chưa ánh xạ trực tiếp trong Supabase')
    # Listing categories can be broad (e.g. accessories); do not reject on hint alone.
    group = group_for(name, url, category)
    if not group:
        reasons.append('Không xác định chắc chắn nhóm mục tiêu')
    group_id = GROUP_CATEGORY_IDS.get(group)
    if group and group_id is None:
        reasons.append('Nhóm chưa có mã danh mục Supabase; cần cấu hình')
    # No category hint alone is sufficient proof of the product category.
    offers = schema.get('offers') or {}
    if isinstance(offers, list):
        offers = next((x for x in offers if isinstance(x, dict) and x.get('price')), {})
    if not isinstance(offers, dict):
        offers = {}
    structured_price = price_number(offers.get('price'))
    extracted_price = price_number(data.get('GiaBan'))
    if not structured_price:
        reasons.append('Thiếu giá hợp lệ trong Product JSON-LD')
    elif extracted_price != structured_price:
        reasons.append('Giá trích xuất không khớp giá Product JSON-LD')
    sku = str(data.get('ModelSKU') or '').strip()
    if not sku:
        reasons.append('Không có SKU/model để kiểm tra')
    if len(sku) > 100:
        reasons.append('SKU quá dài')
    specs = data.get('ThongSo') or {}
    desc = str(data.get('MoTa') or '').strip()
    if len(desc) > 255:
        reasons.append('Mô tả vượt quá 255 ký tự')
    color = str(data.get('MauSac') or '').strip()
    version = str(data.get('PhienBan') or '').strip()
    if len(color) > 50 or len(version) > 100:
        reasons.append('Màu sắc hoặc phiên bản vượt giới hạn CSDL')
    # "verified" here means structured-page checks passed, NOT manual independent verification.
    status = 'verified' if not reasons else 'needs_review'
    row = {
        'TenSanPham':name, 'ThuongHieu':brand, 'MaThuongHieu':brand_id or '',
        'DanhMuc':category, 'MaDanhMuc':group_id or '',
        'ModelSKU':sku, 'MoTa':desc, 'GiaBan':structured_price or '',
        'GiaGoc':'', 'ThongSoKyThuat':json.dumps(specs,ensure_ascii=False) if specs else '',
        'BaoHanh':'', 'MauSac':color, 'PhienBan':version,
        'AnhURL':product_image(schema), 'NhomMucTieu':group, 'NguonURL':url, 'NgayThuThap':datetime.date.today().isoformat(),
        'NguonWebsite':next((n for d,n in SOURCES.items() if host == d or host.endswith('.'+d)),host),
        'TrangThaiXacMinh':status, 'LyDoCanKiemTra':'; '.join(reasons)
    }
    return row

def write_csv(path, rows):
    with path.open('w',encoding='utf-8-sig',newline='') as f:
        w = csv.DictWriter(f,fieldnames=FIELDS)
        w.writeheader()
        w.writerows(rows)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--target',type=int,default=TARGET_TOTAL_PRODUCTS,help='Số dòng qua kiểm tra cấu trúc cần đạt')
    ap.add_argument('--urls',type=Path)
    ap.add_argument('--skip-discovery',action='store_true')
    ap.add_argument('--max-urls',type=int,default=2000,help='Giới hạn số URL cần xử lý')
    args = ap.parse_args()
    if args.target < 1:
        ap.error('--target phải >= 1')
    if args.skip_discovery and not args.urls:
        ap.error('--skip-discovery yêu cầu --urls')
    out = Path(OUTPUT_DIR)
    out.mkdir(parents=True,exist_ok=True)
    fh = logging.FileHandler(out / ERROR_LOG_FILE,encoding='utf-8',mode='w')
    fh.setLevel(logging.WARNING)
    log.addHandler(fh)
    candidates = []
    if args.urls:
        if not args.urls.is_file():
            ap.error('Không tìm thấy file --urls')
        candidates.extend((line.strip(),None) for line in args.urls.read_text(encoding='utf-8-sig').splitlines()
                          if line.strip() and not line.lstrip().startswith('#'))
    if not args.skip_discovery:
        found = discover_product_urls(target=args.target)
        # Round-robin category ordering for more balanced results.
        lists = {k:list(v) for k,v in found.items()}
        while any(lists.values()):
            for cat in list(lists):
                if lists[cat]:
                    candidates.append((lists[cat].pop(0),cat))
    unique, seen = [], set()
    for url, cat in candidates:
        if url not in seen:
            unique.append((url,cat))
            seen.add(url)
    log.info('Có %d URL ứng viên',len(unique))
    verified, review, errors = [], [], []
    seen_products = set()
    attempts = 0
    for idx,(url,cat) in enumerate(unique,1):
        if len(verified) >= args.target or idx > args.max_urls:
            break
        if all(sum(1 for r in verified if r.get('NhomMucTieu') == g) >= n for g,n in TARGET_BY_GROUP.items()):
            break
        attempts += 1
        log.info('[%d/%d] %s',idx,len(unique),url)
        try:
            row = process_url(url,cat)
        except Exception as exc:
            log.exception('Lỗi xử lý %s',url)
            errors.append((url,str(exc)))
            continue
        if row is None:
            errors.append((url,'Không đủ bằng chứng là trang chi tiết Product'))
        else:
            key = (norm(row['TenSanPham']), norm(row['ModelSKU']))
            if key in seen_products:
                continue
            seen_products.add(key)
            group = row.get('NhomMucTieu')
            if row['TrangThaiXacMinh']=='verified' and sum(1 for r in verified if r.get('NhomMucTieu') == group) >= TARGET_BY_GROUP.get(group, 0):
                continue
            (verified if row['TrangThaiXacMinh']=='verified' else review).append(row)
            log.info('  -> %s: %s',row['TrangThaiXacMinh'],row['TenSanPham'])
        time.sleep(REQUEST_DELAY_SECONDS)
    write_csv(out / CSV_VERIFIED,verified)
    write_csv(out / CSV_NEEDS_REVIEW,review)
    missing_brands = sorted({r['ThuongHieu'] for r in review if not r['MaThuongHieu'] and r['ThuongHieu']})
    missing_cats = sorted({r['DanhMuc'] for r in review if not r['MaDanhMuc'] and r['DanhMuc']})
    (out / MISSING_LOOKUP_FILE).write_text('Thương hiệu thiếu:\n'+'\n'.join(missing_brands)+'\n\nDanh mục thiếu:\n'+'\n'.join(missing_cats),encoding='utf-8')
    report = [
        '# Báo cáo thu thập EMUA',
        f'Ngày chạy: {datetime.datetime.now().isoformat(timespec="seconds")}',
        f'URL ứng viên: {len(unique)}',f'URL đã thử: {attempts}',
        f'Qua kiểm tra cấu trúc (CHƯA kiểm chứng thủ công): {len(verified)}',
        *[f'{g}: {sum(1 for r in verified if r.get("NhomMucTieu") == g)}/{n} đạt' for g,n in TARGET_BY_GROUP.items()],
        f'Cần kiểm tra: {len(review)}',f'Lỗi/không đủ dữ liệu: {len(errors)}',
        '', '## Lưu ý',
        'Nhãn verified chỉ là kiểm tra tự động bằng Product JSON-LD và lookup, KHÔNG phải xác minh độc lập.',
        'Giá từ website bán lẻ không phải giá bán hoặc tồn kho chính thức của EMUA.',
        'Không nhập CSV vào Supabase khi chưa rà soát nguồn, tên, SKU, giá, phân loại và quyền sử dụng dữ liệu.',
        'Ảnh nguồn chỉ là URL tham khảo; cần kiểm tra quyền sử dụng trước khi hiển thị trên EMUA.',
        '', '## URL lỗi',
        *[f'- {u}: {err}' for u,err in errors[:100]],
    ]
    (out / REPORT_FILE).write_text('\n'.join(report)+'\n',encoding='utf-8')
    print('\n'.join(report[:7]))
    print(f'CSV: {out / CSV_VERIFIED} | {out / CSV_NEEDS_REVIEW}')

if __name__=='__main__':
    main()
