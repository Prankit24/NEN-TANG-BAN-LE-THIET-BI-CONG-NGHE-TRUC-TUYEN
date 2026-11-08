# -*- coding: utf-8 -*-
"""Discover candidate product detail URLs conservatively."""
import logging
import re
import time
from collections import defaultdict
from urllib.parse import urljoin, urlparse, urlunparse

import requests
from bs4 import BeautifulSoup
from config import SOURCES, CATEGORY_MAP, REQUEST_DELAY_SECONDS, REQUEST_TIMEOUT_SECONDS, MAX_PRODUCTS_PER_CATEGORY, TARGET_TOTAL_PRODUCTS

logger = logging.getLogger(__name__)
HEADERS = {
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
    "Accept-Language": "vi-VN,vi;q=0.9,en;q=0.8",
    "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
}
BLOCKED = re.compile(r"(?:^|/)(?:c|p|collections|collection|pages|page|blogs?|news|tin-tuc|danh-muc|category|categories|search|tim-kiem|cart|gio-hang|account|login|register|support|ho-tro|he-thong|showroom|khach-hang|apple-education|doi-diem|policy|chinh-sach|bao-hanh|tra-gop)(?:/|$)", re.I)

def _host_matches(host, base):
    host, base = host.lower().removeprefix('www.'), base.lower().removeprefix('www.')
    return host == base or host.endswith('.' + base)

def _is_product_url(url, base_domain):
    """Only accept plausible retailer-specific detail paths, never category/info pages."""
    p = urlparse(url)
    if p.scheme not in ('http', 'https') or not _host_matches(p.hostname or '', base_domain):
        return False
    path = p.path.rstrip('/')
    if not path or BLOCKED.search(path):
        return False
    host = (p.hostname or '').lower()
    if 'phongvu.vn' in host:
        return bool(re.search(r'--p\d+/?$', path, re.I))
    if 'gearvn.com' in host:
        return bool(re.match(r'^/products/[a-z0-9][a-z0-9-]+$', path, re.I))
    if 'cellphones.com.vn' in host:
        return bool(re.match(r'^/[a-z0-9][a-z0-9-]+\.html$', path, re.I)) and len(path) > 18
    if 'thegioididong.com' in host:
        return bool(re.match(r'^/(?:dtdd|laptop|may-tinh-bang|phu-kien|tai-nghe|chuot|ban-phim)/[a-z0-9][a-z0-9-]+$', path, re.I))
    if 'fptshop.com.vn' in host:
        return bool(re.match(r'^/(?:dien-thoai|may-tinh-xach-tay|may-tinh-bang|phu-kien|laptop)/[a-z0-9][a-z0-9-]+$', path, re.I))
    return False

def _fetch_page(url):
    try:
        r = requests.get(url, headers=HEADERS, timeout=REQUEST_TIMEOUT_SECONDS)
        r.raise_for_status()
        return r.text
    except requests.RequestException as exc:
        logger.warning('[DISCOVERY] Lỗi tải %s: %s', url, exc)
        return None

def _extract_product_links(html, base_url, selector, base_domain):
    soup = BeautifulSoup(html, 'lxml')
    anchors = soup.select(selector)
    if not anchors:
        logger.info('[DISCOVERY] Selector không có kết quả, dùng liên kết hợp lệ trong trang: %s', selector)
        anchors = soup.select('a[href]')
    links, seen = [], set()
    for a in anchors:
        href = a.get('href')
        if not href:
            continue
        full = urljoin(base_url, href)
        p = urlparse(full)
        clean = urlunparse((p.scheme, p.netloc, p.path.rstrip('/'), '', '', ''))
        if clean not in seen and _is_product_url(clean, base_domain):
            seen.add(clean)
            links.append(clean)
    return links

def discover_product_urls(target=None):
    """Return category -> candidates, with a larger candidate pool than requested verified count."""
    target = target or TARGET_TOTAL_PRODUCTS
    limit = max(40, target * 8)
    result = defaultdict(list)
    seen = set()
    for source in SOURCES:
        base_domain = urlparse(source['base_url']).hostname
        for cat, listing in source['category_urls'].items():
            if len(seen) >= limit:
                continue
            logger.info('[DISCOVERY] %s / %s: %s', source['name'], cat, listing)
            html = _fetch_page(listing)
            if html:
                links = _extract_product_links(html, listing, source['product_link_selector'], base_domain)
                count = 0
                for link in links:
                    if link in seen:
                        continue
                    seen.add(link)
                    result[cat].append(link)
                    count += 1
                    if count >= MAX_PRODUCTS_PER_CATEGORY or len(seen) >= limit:
                        break
                logger.info('[DISCOVERY] %s URL ứng viên mới', count)
            time.sleep(REQUEST_DELAY_SECONDS)
        if len(seen) >= limit:
            break
    logger.info('[DISCOVERY] Tổng URL ứng viên: %s', len(seen))
    return dict(result)
