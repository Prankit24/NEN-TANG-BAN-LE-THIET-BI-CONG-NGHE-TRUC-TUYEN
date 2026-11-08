# -*- coding: utf-8 -*-
"""Safe append-only EMUA importer. Default: preview, no writes."""
import argparse
import csv
import os
import re
import sys
from pathlib import Path


def clean(value):
    return (value or '').strip()


def parse_rows(path):
    rows, problems = [], []
    with path.open(encoding='utf-8-sig', newline='') as file:
        for line, r in enumerate(csv.DictReader(file), 2):
            name = clean(r.get('TenSanPham'))
            price = clean(r.get('GiaBan'))
            brand = clean(r.get('MaThuongHieu'))
            category = clean(r.get('MaDanhMuc'))
            reasons = []
            if clean(r.get('TrangThaiXacMinh')) != 'verified':
                reasons.append('chưa verified')
            if not name or len(name) > 100:
                reasons.append('tên rỗng hoặc quá 100 ký tự')
            if not re.fullmatch(r'[0-9]+', price) or not (1000 <= int(price or '0') <= 999999999999):
                reasons.append('giá không hợp lệ')
            if not brand.isdigit() or not category.isdigit() or int(brand or '0') < 1 or int(category or '0') < 1:
                reasons.append('brand/category ID không hợp lệ')
            if not clean(r.get('NguonURL')).startswith('https://'):
                reasons.append('thiếu URL nguồn HTTPS')
            if len(clean(r.get('MoTa'))) > 255:
                reasons.append('mô tả vượt 255 ký tự')
            if len(clean(r.get('BaoHanh'))) > 50:
                reasons.append('bảo hành vượt 50 ký tự')
            if len(clean(r.get('MauSac'))) > 50 or len(clean(r.get('PhienBan'))) > 100:
                reasons.append('màu/phiên bản quá dài')
            if reasons:
                problems.append((line, name, ', '.join(reasons)))
            else:
                rows.append(r)
    return rows, problems


def main():
    parser = argparse.ArgumentParser(description='EMUA import: mặc định chỉ xem trước, không ghi DB')
    parser.add_argument('--csv', type=Path, default=Path('output/emua_verified_products.csv'))
    parser.add_argument('--apply', action='store_true', help='Cho phép ghi dữ liệu sau khi đã duyệt')
    args = parser.parse_args()
    if not args.csv.is_file():
        parser.error(f'Không thấy file {args.csv}')
    rows, problems = parse_rows(args.csv)
    print(f'CSV hợp lệ: {len(rows)}; cần sửa: {len(problems)}')
    for line, name, reason in problems[:30]:
        print(f'  Dòng {line}: {name[:55]} — {reason}')
    if problems:
        print('Có dòng không hợp lệ: KHÔNG ghi DB. Sửa CSV trước.')
        return 2
    seen = set()
    for r in rows:
        key = clean(r['TenSanPham']).casefold()
        if key in seen:
            print('Trùng tên trong CSV:', r['TenSanPham'])
            return 2
        seen.add(key)
    if not args.apply:
        print('DRY RUN: chưa kết nối và chưa ghi Supabase.')
        print('Đã kiểm tra cấu trúc CSV. Cần đối chiếu ID, tên trùng và CHECK constraint trên DB trước khi --apply.')
        return 0
    dsn = os.environ.get('EMUA_DATABASE_URL')
    if not dsn:
        print('Thiếu EMUA_DATABASE_URL. Không đưa mật khẩu vào source code.')
        return 2
    try:
        import psycopg
    except ImportError:
        print('Cài psycopg: python -m pip install "psycopg[binary]>=3.2,<4"')
        return 2
    with psycopg.connect(dsn, autocommit=False) as conn:
        with conn.cursor() as cur:
            try:
                # Serialize ID allocation; no update/delete of existing records.
                cur.execute('LOCK TABLE public.sanpham, public.bienthesanpham IN SHARE ROW EXCLUSIVE MODE')
                cur.execute('SELECT COALESCE(MAX(masanpham),0), COALESCE((SELECT MAX(mabienthe) FROM public.bienthesanpham),0) FROM public.sanpham')
                next_p, next_v = cur.fetchone()
                cur.execute('SELECT lower(trim(tensanpham)) FROM public.sanpham WHERE tensanpham IS NOT NULL')
                existing = {x[0] for x in cur.fetchall()}
                cur.execute('SELECT mathuonghieu FROM public.thuonghieu')
                brands = {x[0] for x in cur.fetchall()}
                cur.execute('SELECT madanhmuc FROM public.danhmucsanpham')
                cats = {x[0] for x in cur.fetchall()}
                added = skipped = 0
                for r in rows:
                    name = clean(r['TenSanPham'])
                    if name.casefold() in existing:
                        print('SKIP trùng tên:', name)
                        skipped += 1
                        continue
                    brand, cat = int(r['MaThuongHieu']), int(r['MaDanhMuc'])
                    if brand not in brands or cat not in cats:
                        raise ValueError(f'ID thương hiệu/danh mục không tồn tại: {brand}/{cat}: {name}')
                    next_p += 1
                    next_v += 1
                    cur.execute('''INSERT INTO public.sanpham
                        (masanpham,madanhmuc,tensanpham,mota,trangthaisanpham,baohanh,thongsokythuat,mathuonghieu)
                        VALUES (%s,%s,%s,%s,%s,%s,%s,%s)''',
                        (next_p,cat,name,clean(r.get('MoTa')) or None,'Hết hàng',
                         clean(r.get('BaoHanh')) or None,clean(r.get('ThongSoKyThuat')) or None,brand))
                    cur.execute('''INSERT INTO public.bienthesanpham
                        (mabienthe,masanpham,mausac,phienban,gia,soluong,trangthai)
                        VALUES (%s,%s,%s,%s,%s,%s,%s)''',
                        (next_v,next_p,clean(r.get('MauSac')) or None,
                         clean(r.get('PhienBan')) or None,int(r['GiaBan']),0,'Hết hàng'))
                    # Deliberately not importing retailer-owned image URLs without rights review.
                    existing.add(name.casefold())
                    added += 1
                conn.commit()
                print(f'COMMIT thành công: thêm {added}, bỏ qua trùng {skipped}.')
            except Exception:
                conn.rollback()
                print('ROLLBACK: không có thay đổi nào từ lần chạy này được lưu.')
                raise
    return 0


if __name__ == '__main__':
    sys.exit(main())
