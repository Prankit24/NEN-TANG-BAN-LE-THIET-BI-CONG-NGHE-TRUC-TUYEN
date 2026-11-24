#!/usr/bin/env python3
"""
Chuyển đổi toàn bộ ảnh gốc (jpg/jpeg/png) trong wwwroot/images/products/
sang định dạng .webp (max width 900px, quality 80%).

CÁCH CHẠY:
1. Cài Pillow nếu chưa có:
      pip install Pillow
2. Chạy script:
      python Tools/convert-images-to-webp.py
   hoặc:
      python Tools/convert-images-to-webp.py --source wwwroot/images/products --quality 80 --max-width 900
3. Script sẽ:
   - Quét tất cả subfolder trong --source
   - Với mỗi ảnh .jpg/.jpeg/.webp, tạo file .webp cùng thư mục
   - Giữ nguyên ảnh gốc (không xóa)
   - Bỏ qua nếu file .webp đã tồn tại VÀ mới hơn ảnh gốc
"""

import os
import sys
import argparse
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    print("❌ Chưa cài Pillow. Chạy: pip install Pillow")
    sys.exit(1)


def convert_to_webp(src_path: Path, max_width: int = 900, quality: int = 80) -> Path | None:
    """Convert a single image to .webp, return output path or None if skipped."""
    out_path = src_path.with_suffix('.webp')

    # Skip if webp already exists and is newer than source
    if out_path.exists() and out_path.stat().st_mtime >= src_path.stat().st_mtime:
        return None

    try:
        with Image.open(src_path) as img:
            # Convert RGBA to RGB for JPEG-sourced images (webp supports RGBA but smaller with RGB)
            if img.mode in ('RGBA', 'LA', 'P'):
                # Keep alpha if present
                img = img.convert('RGBA')
            elif img.mode != 'RGB':
                img = img.convert('RGB')

            # Resize if wider than max_width, keeping aspect ratio
            w, h = img.size
            if w > max_width:
                ratio = max_width / w
                new_size = (max_width, int(h * ratio))
                img = img.resize(new_size, Image.LANCZOS)

            img.save(out_path, 'WEBP', quality=quality, method=4)
            return out_path
    except Exception as e:
        print(f"  ⚠️  Lỗi chuyển đổi {src_path.name}: {e}")
        return None


def main():
    parser = argparse.ArgumentParser(description='Chuyển đổi ảnh sản phẩm sang WebP')
    parser.add_argument('--source', default=None,
                        help='Thư mục chứa ảnh gốc (mặc định: wwwroot/images/products)')
    parser.add_argument('--quality', type=int, default=80, help='Chất lượng WebP (1-100)')
    parser.add_argument('--max-width', type=int, default=900, help='Chiều rộng tối đa (px)')
    parser.add_argument('--dry-run', action='store_true', help='Chỉ liệt kê, không chuyển đổi')
    args = parser.parse_args()

    # Auto-detect project root
    script_dir = Path(__file__).resolve().parent
    project_root = script_dir.parent  # EMUA/EMua/
    if args.source:
        source_dir = Path(args.source)
    else:
        source_dir = project_root / 'wwwroot' / 'images' / 'products'

    if not source_dir.exists():
        print(f"❌ Thư mục không tồn tại: {source_dir}")
        sys.exit(1)

    extensions = {'.jpg', '.jpeg', '.webp'}
    total = 0
    converted = 0
    skipped = 0
    errors = 0

    print(f"🔍 Quét ảnh trong: {source_dir}")
    print(f"   Chất lượng: {args.quality}% | Max width: {args.max_width}px")
    print(f"   {'DRY RUN - không chuyển đổi' if args.dry_run else ''}")
    print()

    for root, dirs, files in os.walk(source_dir):
        for fname in sorted(files):
            fpath = Path(root) / fname
            if fpath.suffix.lower() not in extensions:
                continue
            total += 1
            rel = fpath.relative_to(source_dir)

            if args.dry_run:
                webp_path = fpath.with_suffix('.webp')
                status = "✅ có" if webp_path.exists() else "⏳ cần chuyển"
                print(f"  {status}: {rel}")
                continue

            result = convert_to_webp(fpath, args.max_width, args.quality)
            if result:
                orig_size = fpath.stat().st_size
                new_size = result.stat().st_size
                saving = (1 - new_size / orig_size) * 100 if orig_size > 0 else 0
                print(f"  ✅ {rel} → .webp ({orig_size // 1024}KB → {new_size // 1024}KB, giảm {saving:.0f}%)")
                converted += 1
            elif result is None:
                skipped += 1
            else:
                errors += 1

    print()
    print(f"📊 Kết quả: {total} ảnh gốc | {converted} đã chuyển | {skipped} bỏ qua | {errors} lỗi")


if __name__ == '__main__':
    main()
