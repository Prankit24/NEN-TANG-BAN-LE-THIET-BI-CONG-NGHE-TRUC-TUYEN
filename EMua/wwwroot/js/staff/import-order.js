(function () {
    'use strict';
    // Làm tròn tiền theo chuẩn VNĐ không số lẻ
    const money = value => Math.round(Number(value) || 0);

    function formatNumberVn(val) {
        if (val === null || val === undefined || val === '') return '';
        const num = Math.round(Number(val));
        if (isNaN(num)) return '';
        return num.toString().replace(/\B(?=(\d{3})+(?!\d))/g, '.');
    }

    function parseRawNumber(str) {
        if (typeof str === 'number') return str;
        if (!str) return 0;
        const cleaned = String(str).replace(/\./g, '').replace(/,/g, '.').replace(/[^\d.-]/g, '');
        const val = parseFloat(cleaned);
        return isNaN(val) ? 0 : val;
    }

    function attachLiveCurrency(input, onValChange) {
        if (!input || input.dataset.hasCurrencyFormat) return;
        input.dataset.hasCurrencyFormat = 'true';
        input.type = 'text';
        input.inputMode = 'numeric';
        input.classList.add('text-end');

        const updateDisplay = () => {
            const rawVal = parseRawNumber(input.value);
            input.value = rawVal > 0 ? formatNumberVn(rawVal) : (input.value.trim() === '' ? '' : '0');
        };

        // Format giá trị khởi tạo
        if (input.value !== '') {
            updateDisplay();
        }

        input.addEventListener('input', () => {
            const cursor = input.selectionStart;
            const prevLen = input.value.length;
            const raw = parseRawNumber(input.value);
            input.value = raw > 0 ? formatNumberVn(raw) : (input.value.trim() === '' ? '' : '0');
            const newLen = input.value.length;
            const newCursor = Math.max(0, cursor + (newLen - prevLen));
            input.setSelectionRange(newCursor, newCursor);

            if (typeof onValChange === 'function') {
                onValChange(raw);
            }
        });
    }

    function lineAmounts(qty, net, vatRate, discount) {
        if (!Number.isInteger(qty) || qty < 1 || qty > 100000) throw new Error('Số lượng phải là số nguyên từ 1 đến 100.000.');
        if (!Number.isFinite(net) || net < 0 || net > 1000000000) throw new Error('Giá trước VAT phải từ 0 đến 1 tỷ đồng.');
        if (![0, 5, 8, 10].includes(vatRate)) throw new Error('Thuế suất không hợp lệ.');
        net = money(net);
        // Làm tròn giá sau VAT bằng Math.round
        const gross = Math.round(net * (1 + vatRate / 100));
        const vat = gross - net;
        if (!Number.isFinite(discount) || discount < 0 || discount > gross) throw new Error('Chiết khấu / SP không được vượt giá sau VAT.');
        discount = money(discount);
        return { 
            net: money(net * qty), 
            vat: money(vat * qty), 
            gross, 
            discount: money(discount * qty), 
            total: money((gross - discount) * qty) 
        };
    }

    function receiptTotal(lines, shipping, other, discount) {
        if (![shipping, other, discount].every(Number.isFinite) || shipping < 0 || other < 0 || discount < 0 ||
            shipping > 1000000000 || other > 1000000000 || discount > 1000000000000) throw new Error('Phí và chiết khấu không hợp lệ.');
        const base = money(lines.reduce((s, x) => s + x.total, 0) + money(shipping) + money(other));
        if (money(discount) > base) throw new Error('Chiết khấu toàn phiếu vượt tổng tiền hàng và phụ phí.');
        const total = money(base - money(discount));
        if (total > 1000000000000) throw new Error('Tổng giá trị một phiếu tối đa 1.000 tỷ đồng.');
        return total;
    }

    if (typeof module !== 'undefined' && module.exports) module.exports = { money, lineAmounts, receiptTotal };
    if (typeof document === 'undefined') return;

    document.addEventListener('DOMContentLoaded', () => {
        const form = document.getElementById('createImportOrderForm');
        if (!form) return;
        const body = document.getElementById('importItemsBody');
        const errorBox = document.getElementById('importClientError');
        const supplierSelect = document.getElementById('supplierSelect');
        const supplierName = document.getElementById('NhaCungCapInput');
        const supplierId = document.getElementById('MaNhaCungCapHidden');
        const variants = JSON.parse(document.getElementById('importVariantsData').textContent || '[]');
        const initial = JSON.parse(document.getElementById('importInitialItems').textContent || '[]');
        const fmt = n => Math.round(Number(n) || 0).toLocaleString('vi-VN') + ' đ';

        function error(message) { errorBox.textContent = message || ''; errorBox.classList.toggle('d-none', !message); }
        function showModal(id) {
            if (!window.bootstrap?.Modal) throw new Error('Không mở được hộp thoại. Vui lòng tải lại trang.');
            const modal = window.bootstrap.Modal.getOrCreateInstance(document.getElementById(id));
            modal.show(); return modal;
        }

        function clearQuote(row) {
            row.querySelector('.quote-id').value = '';
            row.querySelector('.quote-label').textContent = 'Giá nhập dự kiến / thủ công';
            delete row.dataset.minimum;
        }

        function selectSupplier(id, name) {
            let option = Array.from(supplierSelect.options).find(x => x.value === String(id));
            if (!option) { option = new Option(name, String(id)); option.dataset.name = name; supplierSelect.add(option); }
            supplierSelect.value = String(id); supplierId.value = String(id); supplierName.value = name;
        }

        supplierSelect.addEventListener('change', () => {
            supplierId.value = supplierSelect.value;
            supplierName.value = supplierSelect.selectedOptions[0]?.dataset.name || '';
            body.querySelectorAll('tr').forEach(clearQuote);
        });

        supplierName.addEventListener('input', () => {
            supplierId.value = ''; supplierSelect.value = ''; body.querySelectorAll('tr').forEach(clearQuote);
        });

        function reindex() {
            body.querySelectorAll('tr').forEach((row, i) => {
                row.querySelectorAll('[data-field]').forEach(input => { input.name = `Items[${i}].${input.dataset.field}`; });
            });
        }

        function readLine(row) {
            const val = field => {
                const el = row.querySelector(`[data-field="${field}"]`);
                if (!el) return '';
                if (el.dataset.hasCurrencyFormat) {
                    return parseRawNumber(el.value);
                }
                return el.value;
            };

            if (!val('MaBienThe')) throw new Error('Vui lòng chọn biến thể cho tất cả các dòng.');
            const qtyStr = String(row.querySelector('[data-field="SoLuongNhap"]').value).trim();
            const netStr = String(row.querySelector('[data-field="DonGiaChuaThue"]').value).trim();
            const discountStr = String(row.querySelector('[data-field="ChietKhau"]').value).trim();
            if (qtyStr === '' || netStr === '' || discountStr === '') throw new Error('Vui lòng nhập đủ số lượng, giá và chiết khấu.');

            const qty = Number(qtyStr);
            if (row.dataset.minimum && qty < Number(row.dataset.minimum)) throw new Error('Số lượng thấp hơn mức tối thiểu của báo giá. Vui lòng chọn lại báo giá.');
            return lineAmounts(qty, parseRawNumber(netStr), Number(val('ThueSuatVat')), parseRawNumber(discountStr));
        }

        function refresh(quiet = false) {
            const lines = []; let invalid = '';
            body.querySelectorAll('tr').forEach(row => {
                try {
                    const a = readLine(row); lines.push(a);
                    const grossInput = row.querySelector('.gross');
                    grossInput.value = formatNumberVn(a.gross);
                    row.querySelector('.line-total').textContent = fmt(a.total);
                    const retail = Number(variants.find(v => String(v.id) === row.querySelector('[data-field="MaBienThe"]').value)?.price || 0);
                    row.querySelector('.retail').textContent = fmt(retail);
                    const margin = row.querySelector('.margin');
                    const qty = Number(row.querySelector('[data-field="SoLuongNhap"]').value);
                    const rate = retail > 0 ? (retail - a.total / qty) / retail * 100 : null;
                    margin.textContent = rate === null ? '—' : rate.toFixed(1) + '%';
                    margin.className = 'margin fw-bold ' + (rate !== null && rate < 0 ? 'text-danger' : 'text-success');
                    row.querySelector('.line-error').textContent = '';
                } catch (ex) { row.querySelector('.line-error').textContent = ex.message; invalid ||= ex.message; }
            });
            document.getElementById('totalNetAmount').textContent = fmt(lines.reduce((s, x) => s + x.net, 0));
            document.getElementById('totalVatAmount').textContent = fmt(lines.reduce((s, x) => s + x.vat, 0));
            document.getElementById('totalLineDiscount').textContent = '-' + fmt(lines.reduce((s, x) => s + x.discount, 0));
            try {
                const shipping = parseRawNumber(form.elements.ChiPhiVanChuyen.value);
                const other = parseRawNumber(form.elements.ChiPhiKhac.value);
                const discount = parseRawNumber(form.elements.ChietKhauTong.value);
                const total = receiptTotal(lines, shipping, other, discount);
                document.getElementById('totalPaymentAmount').textContent = invalid ? 'Kiểm tra dữ liệu' : fmt(total);
            } catch (ex) { invalid ||= ex.message; document.getElementById('totalPaymentAmount').textContent = 'Kiểm tra dữ liệu'; }
            if (!quiet) error(invalid);
            return !invalid;
        }

        function addRow(item = {}) {
            if (body.children.length >= 100) { error('Một phiếu tối đa 100 dòng.'); return; }
            const row = document.createElement('tr');
            row.innerHTML = `<td><select class="form-select form-select-sm" data-field="MaBienThe" required></select>
                <input type="hidden" class="quote-id" data-field="MaBaoGia"><small class="quote-label text-muted d-block"></small><small class="line-error text-danger d-block"></small><input class="form-control form-control-sm mt-1" data-field="GhiChu" maxlength="255" placeholder="Ghi chú dòng (nếu có)"></td>
                <td class="stock text-center"></td>
                <td><input type="number" class="form-control form-control-sm" data-field="SoLuongNhap" min="1" max="100000" step="1" required></td>
                <td><input type="text" class="form-control form-control-sm currency-input" data-field="DonGiaChuaThue" required></td>
                <td><select class="form-select form-select-sm" data-field="ThueSuatVat"><option value="0">0%</option><option value="5">5%</option><option value="8">8%</option><option value="10">10%</option></select></td>
                <td><input type="text" class="gross form-control form-control-sm text-end" data-field="DonGiaNhapDuKien" readonly></td>
                <td><input type="text" class="form-control form-control-sm currency-input" data-field="ChietKhau" required></td>
                <td class="retail text-nowrap"></td><td class="margin"></td><td class="line-total text-nowrap fw-bold"></td>
                <td><button type="button" class="compare btn btn-sm btn-outline-primary mb-1">Báo giá</button><button type="button" class="remove btn btn-sm btn-outline-danger" aria-label="Xóa dòng">×</button></td>`;
            const select = row.querySelector('select'); select.add(new Option('-- Chọn biến thể --', ''));
            variants.forEach(v => select.add(new Option(v.name, String(v.id))));
            
            const netInput = row.querySelector('[data-field="DonGiaChuaThue"]');
            const discountInput = row.querySelector('[data-field="ChietKhau"]');

            attachLiveCurrency(netInput, () => { clearQuote(row); refresh(); });
            attachLiveCurrency(discountInput, () => { clearQuote(row); refresh(); });

            const setVal = (field, value) => { 
                const el = row.querySelector(`[data-field="${field}"]`);
                if (el.dataset.hasCurrencyFormat) {
                    el.value = formatNumberVn(value);
                } else {
                    el.value = value;
                }
            };

            setVal('MaBienThe', item.MaBienThe > 0 ? String(item.MaBienThe) : '');
            setVal('GhiChu', item.GhiChu || ''); 
            setVal('MaBaoGia', item.MaBaoGia || ''); 
            setVal('SoLuongNhap', item.SoLuongNhap ?? 10);
            setVal('DonGiaChuaThue', item.DonGiaChuaThue ?? 0); 
            setVal('ThueSuatVat', item.ThueSuatVat ?? 10); 
            setVal('ChietKhau', item.ChietKhau ?? 0);

            row.querySelector('.quote-label').textContent = item.MaBaoGia ? `Báo giá #${item.MaBaoGia}` : 'Giá nhập dự kiến / thủ công';
            const updateStock = () => { row.querySelector('.stock').textContent = variants.find(v => String(v.id) === select.value)?.stock ?? '—'; };
            
            select.addEventListener('change', () => {
                clearQuote(row); const v = variants.find(v => String(v.id) === select.value);
                setVal('DonGiaChuaThue', v ? money(Number(v.price) * 0.7 / 1.1) : 0); 
                setVal('ThueSuatVat', 10); 
                setVal('ChietKhau', 0); 
                updateStock(); 
                refresh();
            });

            row.querySelector('[data-field="ThueSuatVat"]').addEventListener('change', () => { clearQuote(row); refresh(); });
            row.querySelector('[data-field="SoLuongNhap"]').addEventListener('input', () => refresh());
            row.querySelector('.remove').addEventListener('click', () => { if (body.children.length <= 1) { error('Cần ít nhất một dòng sản phẩm.'); return; } row.remove(); reindex(); refresh(); });
            row.querySelector('.compare').addEventListener('click', () => compare(row));
            
            updateStock(); 
            body.append(row); 
            reindex();
        }

        async function jsonRequest(url, options = {}) {
            const response = await fetch(url, { ...options, headers: { Accept: 'application/json', ...(options.headers || {}) } });
            if (response.status === 401 || response.status === 403 || response.redirected) throw new Error('Phiên đăng nhập hết hạn hoặc không có quyền. Vui lòng đăng nhập lại.');
            if (!(response.headers.get('content-type') || '').includes('application/json')) throw new Error('Máy chủ không trả về dữ liệu hợp lệ.');
            const data = await response.json();
            if (!response.ok || !data.success) throw new Error(data.message || 'Không xử lý được yêu cầu.');
            return data;
        }

        let comparisonSequence = 0;
        async function compare(row) {
            const id = Number(row.querySelector('[data-field="MaBienThe"]').value), qty = Number(row.querySelector('[data-field="SoLuongNhap"]').value);
            if (!id || !Number.isInteger(qty) || qty < 1 || qty > 100000) { error('Chọn biến thể và số lượng hợp lệ trước khi xem báo giá.'); return; }
            const container = document.getElementById('quotationCompareContent');
            const sequence = ++comparisonSequence;
            try {
                showModal('quotationCompareModal'); container.textContent = 'Đang tải báo giá...';
                const url = new URL(form.dataset.quotationUrl, window.location.origin); url.searchParams.set('variantId', id); url.searchParams.set('qty', qty);
                const data = await jsonRequest(url);
                if (sequence !== comparisonSequence) return;
                container.replaceChildren();
                if (!data.data?.length) { container.textContent = 'Chưa có báo giá còn hiệu lực đáp ứng số lượng này. Bạn có thể nhập giá dự kiến theo thỏa thuận với nhà cung cấp.'; return; }
                const table = document.createElement('table'); table.className = 'table table-sm table-bordered align-middle';
                const heading = table.createTHead().insertRow();
                ['Nhà cung cấp', 'SL tối thiểu', 'Giá trước VAT', 'VAT', 'Giá sau VAT', 'CK / SP', 'Giá vốn ước tính / SP', 'Chọn'].forEach(label => { const cell = document.createElement('th'); cell.textContent = label; heading.append(cell); });
                const tbody = table.createTBody();
                data.data.forEach(q => {
                    const tr = tbody.insertRow(); if (q.isBestPrice) tr.className = 'table-success';
                    [q.tenNhaCungCap, q.soLuongToiThieu, fmt(q.donGiaChuaThue), q.thueSuatVat + '%', fmt(q.donGiaSauThue), fmt(q.chietKhau), fmt(q.giaVonThucTeMotDonVi)].forEach(value => { tr.insertCell().textContent = value; });
                    const button = document.createElement('button'); button.type = 'button'; button.className = 'btn btn-sm btn-primary'; button.textContent = 'Chọn';
                    button.addEventListener('click', () => {
                        const currentName = supplierName.value.trim().toLocaleLowerCase('vi');
                        if ((supplierId.value && Number(supplierId.value) !== q.maNhaCungCap) ||
                            (currentName && currentName !== q.tenNhaCungCap.trim().toLocaleLowerCase('vi'))) {
                            error('Một phiếu chỉ có một nhà cung cấp. Hãy đổi hoặc bỏ NCC đang chọn trước khi áp dụng báo giá khác.');
                            window.bootstrap.Modal.getInstance(document.getElementById('quotationCompareModal')).hide(); return;
                        }
                        selectSupplier(q.maNhaCungCap, q.tenNhaCungCap);
                        const fields = { MaBaoGia: q.maBaoGia, DonGiaChuaThue: q.donGiaChuaThue, ThueSuatVat: q.thueSuatVat, ChietKhau: q.chietKhau };
                        for (const [field, value] of Object.entries(fields)) {
                            const el = row.querySelector(`[data-field="${field}"]`);
                            if (el.dataset.hasCurrencyFormat) {
                                el.value = formatNumberVn(value);
                            } else {
                                el.value = value;
                            }
                        }
                        row.dataset.minimum = q.soLuongToiThieu;
                        row.querySelector('.quote-label').textContent = `Báo giá #${q.maBaoGia} — ${q.tenNhaCungCap}`;
                        refresh(); window.bootstrap.Modal.getInstance(document.getElementById('quotationCompareModal')).hide();
                    });
                    tr.insertCell().append(button);
                });
                const wrapper = document.createElement('div'); wrapper.className = 'table-responsive'; wrapper.append(table); container.append(wrapper);
            } catch (ex) { if (sequence === comparisonSequence) container.textContent = ex.message; error(ex.message); }
        }

        document.getElementById('quickSupplierForm').addEventListener('submit', async event => {
            event.preventDefault(); const quick = event.currentTarget; if (!quick.reportValidity() || quick.dataset.submitting) return;
            quick.dataset.submitting = 'true'; const button = quick.querySelector('[type="submit"]'); button.disabled = true;
            const feedback = document.getElementById('supplierError'); feedback.textContent = '';
            try {
                const read = id => document.getElementById(id).value.trim();
                const data = await jsonRequest(form.dataset.supplierUrl, { method: 'POST', headers: { 'Content-Type': 'application/json', RequestVerificationToken: form.querySelector('[name="__RequestVerificationToken"]').value },
                    body: JSON.stringify({ TenNhaCungCap: read('modalSupplierName'), SoDienThoai: read('modalSupplierPhone'), MaSoThue: read('modalSupplierTax'), Email: read('modalSupplierEmail'), DiaChi: read('modalSupplierAddress'), GhiChu: read('modalSupplierNote') }) });
                body.querySelectorAll('tr').forEach(clearQuote); selectSupplier(data.id, data.name);
                window.bootstrap.Modal.getInstance(document.getElementById('addSupplierModal')).hide();
            } catch (ex) { feedback.textContent = ex.message; }
            finally { delete quick.dataset.submitting; button.disabled = false; }
        });

        document.getElementById('addImportLine').addEventListener('click', () => { addRow(); refresh(true); });

        // Gắn live currency cho các ô chi phí toàn phiếu
        form.querySelectorAll('.import-cost').forEach(input => {
            attachLiveCurrency(input, () => refresh());
        });

        // Hàm loại bỏ dấu chấm phân cách hàng nghìn trước khi submit
        function sanitizeFormInputs() {
            form.querySelectorAll('[data-has-currency-format="true"]').forEach(el => {
                el.value = parseRawNumber(el.value);
            });
            form.querySelectorAll('.gross').forEach(el => {
                el.value = parseRawNumber(el.value);
            });
        }

        form.addEventListener('submit', event => {
            event.preventDefault(); if (form.dataset.submitting || !form.reportValidity()) return;
            const ids = Array.from(body.querySelectorAll('[data-field="MaBienThe"]'), x => x.value);
            if (!ids.length || new Set(ids).size !== ids.length) { error('Không được bỏ trống hoặc chọn trùng biến thể.'); return; }
            if (!supplierName.value.trim()) { error('Vui lòng chọn hoặc nhập tên nhà cung cấp.'); supplierName.focus(); return; }
            if (!refresh()) return;
            try { showModal('importConfirmModal'); } catch (ex) { error(ex.message); }
        });

        document.getElementById('confirmImportSubmit').addEventListener('click', () => {
            if (form.dataset.submitting || !form.reportValidity() || !refresh()) return;
            form.dataset.submitting = 'true'; 
            reindex(); 
            sanitizeFormInputs();
            document.getElementById('submitImportOrder').disabled = true;
            document.getElementById('confirmImportSubmit').disabled = true;
            HTMLFormElement.prototype.submit.call(form);
        });

        (initial.length ? initial : [{}]).forEach(addRow); 
        refresh(true);
    });
})();
