// Shared by home, product listing and product details.
(function () {
    'use strict';
    const pending = new Set();
    window.emuaAddCart = async function (variantId, quantity = 1, button = null) {
        variantId = Number(variantId);
        quantity = Number(quantity);
        if (!Number.isSafeInteger(variantId) || variantId <= 0 ||
            !Number.isSafeInteger(quantity) || quantity <= 0) {
            alert('Vui lòng chọn phiên bản và số lượng hợp lệ.');
            return false;
        }
        if (pending.has(variantId)) return false;
        pending.add(variantId);
        if (button) button.disabled = true;
        try {
            if (!window.EMUA_IS_AUTHENTICATED) {
                window.addToGuestCart(variantId, quantity);
            } else {
                await window.emuaCartReady;
                const token = document.querySelector('input[name="__RequestVerificationToken"]');
                if (!token) throw new Error('Vui lòng tải lại trang để cập nhật mã bảo mật.');
                const response = await fetch('/Cart/Add', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json',
                        'RequestVerificationToken': token.value },
                    body: JSON.stringify({ variantId, quantity })
                });
                if (response.status === 401 || response.status === 403 || response.redirected) {
                    window.location.href = window.getLoginUrl(window.location.pathname + window.location.search);
                    return false;
                }
                const isJson = (response.headers.get('content-type') || '').includes('application/json');
                const result = isJson ? await response.json() : null;
                if (!response.ok || !result || result.count === undefined) {
                    throw new Error(result?.message || (response.status === 400
                        ? 'Yêu cầu không hợp lệ hoặc mã bảo mật hết hạn. Vui lòng tải lại trang.'
                        : 'Không thể thêm vào giỏ hàng. Vui lòng thử lại.'));
                }
                window.setGlobalCartCount(result.count, true);
            }
            let notice = document.getElementById('cartActionStatus');
            if (!notice) {
                notice = document.createElement('div');
                notice.id = 'cartActionStatus';
                notice.setAttribute('role', 'status');
                notice.style.cssText = 'position:fixed;bottom:24px;left:50%;transform:translateX(-50%);z-index:10000;background:#172033;color:white;padding:12px 20px;border-radius:10px;max-width:90vw;box-shadow:0 8px 30px #0003';
                document.body.appendChild(notice);
            }
            notice.textContent = 'Đã thêm sản phẩm vào giỏ hàng';
            notice.hidden = false;
            clearTimeout(window.emuaCartNoticeTimer);
            window.emuaCartNoticeTimer = setTimeout(() => { notice.hidden = true; }, 3000);
            return true;
        } catch (error) {
            alert(error.message || 'Không thể kết nối đến giỏ hàng.');
            return false;
        } finally {
            pending.delete(variantId);
            if (button) button.disabled = false;
        }
    };
    window.homeAddCart = (variantId, button) => window.emuaAddCart(variantId, 1, button);
    window.addProductToCart = (button, variantId) => window.emuaAddCart(variantId, 1, button);

    document.addEventListener('DOMContentLoaded', function () {
        const form = document.getElementById('productBuyForm');
        if (!form) return;
        form.addEventListener('submit', async function (event) {
            const buyNow = event.submitter?.name === 'buyNow';
            // Authenticated "Mua ngay" keeps its independent server-side checkout.
            if (buyNow && window.EMUA_IS_AUTHENTICATED) return;
            event.preventDefault();
            const added = await window.emuaAddCart(form.elements.variantId.value,
                form.elements.quantity.value, event.submitter);
            if (added && buyNow) window.location.href = window.getLoginUrl('/Cart');
        });
    });
})();
