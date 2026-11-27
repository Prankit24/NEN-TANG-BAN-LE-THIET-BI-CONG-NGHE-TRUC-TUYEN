/**
 * EMUA UNIFIED NOTIFICATION & CONFIRMATION SYSTEM
 * - Toasts: Success (5s), Info (5s), Warning (7s), Error (10s)
 * - Interactive: Hover pauses timeout, leave resumes countdown
 * - Custom Modal Confirm: Replaces browser confirm() & alert()
 */

(function (window, document) {
    'use strict';

    // Durations in milliseconds per requirements:
    const DURATIONS = {
        success: 5000,
        info: 5000,
        warning: 7000,
        error: 10000
    };

    const TITLES = {
        success: 'Thành công',
        info: 'Thông tin',
        warning: 'Cảnh báo',
        error: 'Lỗi'
    };

    const ICONS = {
        success: 'bi-check-circle-fill',
        info: 'bi-info-circle-fill',
        warning: 'bi-exclamation-triangle-fill',
        error: 'bi-x-circle-fill'
    };

    function getContainer() {
        let container = document.getElementById('emuaToastContainer');
        if (!container) {
            container = document.createElement('div');
            container.id = 'emuaToastContainer';
            container.className = 'emua-toast-container';
            container.setAttribute('aria-live', 'polite');
            container.setAttribute('aria-atomic', 'true');
            document.body.appendChild(container);
        }
        return container;
    }

    function createToastElement(type, title, message, duration, undoOptions) {
        const toast = document.createElement('div');
        toast.className = `emua-toast emua-toast-${type}`;
        toast.setAttribute('role', 'alert');

        const iconClass = ICONS[type] || ICONS.info;
        const safeTitle = title || TITLES[type] || 'Thông báo';

        let undoHtml = '';
        if (undoOptions && typeof undoOptions.onUndo === 'function') {
            const countdownSec = Math.round(duration / 1000);
            undoHtml = `
                <div class="emua-toast-undo-wrapper mt-2 d-flex align-items-center justify-content-between gap-2 pt-2 border-top border-light border-opacity-25">
                    <span class="small text-white-50"><i class="bi bi-stopwatch me-1"></i><span class="emua-undo-timer">${countdownSec}s</span></span>
                    <button type="button" class="btn btn-sm btn-light text-primary fw-bold emua-btn-undo py-0 px-2" style="font-size: 12px; height: 26px;">
                        <i class="bi bi-arrow-counterclockwise"></i> Hoàn tác
                    </button>
                </div>
            `;
        }

        toast.innerHTML = `
            <i class="bi ${iconClass} emua-toast-icon"></i>
            <div class="emua-toast-body w-100">
                <div class="emua-toast-title">${safeTitle}</div>
                <div class="emua-toast-message">${message}</div>
                ${undoHtml}
            </div>
            <button type="button" class="emua-toast-close" aria-label="Đóng">
                <i class="bi bi-x"></i>
            </button>
            <div class="emua-toast-progress"></div>
        `;

        const closeBtn = toast.querySelector('.emua-toast-close');
        const progressBar = toast.querySelector('.emua-toast-progress');
        const undoBtn = toast.querySelector('.emua-btn-undo');
        const timerText = toast.querySelector('.emua-undo-timer');

        let remainingTime = duration;
        let startTime = Date.now();
        let timeoutId = null;
        let isPaused = false;
        let intervalId = null;

        if (timerText) {
            intervalId = setInterval(() => {
                if (!isPaused) {
                    const sec = Math.max(0, Math.ceil(remainingTime / 1000));
                    timerText.textContent = `${sec}s`;
                    if (sec <= 0) clearInterval(intervalId);
                }
            }, 1000);
        }

        if (undoBtn && undoOptions && typeof undoOptions.onUndo === 'function') {
            undoBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                undoBtn.disabled = true;
                undoBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span> Đang hoàn tác...';
                clearInterval(intervalId);
                clearTimeout(timeoutId);
                Promise.resolve(undoOptions.onUndo()).then(() => {
                    dismiss();
                }).catch(() => {
                    undoBtn.disabled = false;
                    undoBtn.innerHTML = '<i class="bi bi-arrow-counterclockwise"></i> Thử lại';
                });
            });
        }

        function startTimer() {
            startTime = Date.now();
            if (progressBar) {
                progressBar.style.transition = `width ${remainingTime}ms linear`;
                progressBar.style.width = '0%';
            }
            timeoutId = setTimeout(dismiss, remainingTime);
        }

        function pauseTimer() {
            if (isPaused) return;
            isPaused = true;
            clearTimeout(timeoutId);
            const elapsed = Date.now() - startTime;
            remainingTime = Math.max(0, remainingTime - elapsed);
            if (progressBar) {
                const currentWidth = (remainingTime / duration) * 100;
                progressBar.style.transition = 'none';
                progressBar.style.width = `${currentWidth}%`;
            }
        }

        function resumeTimer() {
            if (!isPaused || remainingTime <= 0) return;
            isPaused = false;
            startTimer();
        }

        function dismiss() {
            clearTimeout(timeoutId);
            toast.classList.remove('show');
            toast.classList.add('hide');
            setTimeout(() => {
                if (toast.parentNode) {
                    toast.parentNode.removeChild(toast);
                }
            }, 350);
        }

        closeBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            dismiss();
        });

        // Hover pauses, leave resumes
        toast.addEventListener('mouseenter', pauseTimer);
        toast.addEventListener('mouseleave', resumeTimer);

        // Auto trigger display
        setTimeout(() => {
            toast.classList.add('show');
            startTimer();
        }, 20);

        return toast;
    }

    const EmuaNotify = {
        show: function (options) {
            const type = (options.type || 'info').toLowerCase();
            const message = options.message || '';
            const title = options.title || TITLES[type] || 'Thông báo';
            const duration = options.duration || DURATIONS[type] || 5000;
            const undoOptions = options.undo;

            if (!message) return;

            const container = getContainer();
            const toast = createToastElement(type, title, message, duration, undoOptions);
            container.appendChild(toast);
        },

        success: function (message, title) {
            this.show({ type: 'success', message, title });
        },

        toastWithUndo: function (message, durationSeconds = 60, onUndo) {
            this.show({
                type: 'warning',
                title: 'Đã xóa (Có thể hoàn tác)',
                message: message,
                duration: durationSeconds * 1000,
                undo: { onUndo }
            });
        },

        info: function (message, title) {
            this.show({ type: 'info', message, title });
        },

        warning: function (message, title) {
            this.show({ type: 'warning', message, title });
        },

        error: function (message, title) {
            this.show({ type: 'error', message, title });
        },

        /**
         * Custom Modal Confirm Dialog
         * Replaces window.confirm
         */
        confirm: function (message, onConfirm, options = {}) {
            const modalEl = document.getElementById('emuaConfirmModal');
            if (!modalEl || !window.bootstrap?.Modal) {
                // Fallback nếu không có modal
                if (window.confirm(message)) {
                    if (typeof onConfirm === 'function') onConfirm();
                }
                return;
            }

            const title = options.title || 'Xác nhận thao tác';
            const confirmText = options.confirmText || 'Đồng ý';
            const cancelText = options.cancelText || 'Hủy bỏ';
            const isDanger = options.isDanger !== false; // mặc định là thao tác quan trọng

            document.getElementById('emuaConfirmTitle').textContent = title;
            document.getElementById('emuaConfirmMessage').textContent = message;

            const confirmBtn = document.getElementById('emuaConfirmSubmitBtn');
            const cancelBtn = document.getElementById('emuaConfirmCancelBtn');
            const iconWrapper = document.getElementById('emuaConfirmIconWrapper');

            confirmBtn.textContent = confirmText;
            confirmBtn.className = `btn ${isDanger ? 'btn-danger' : 'btn-primary'} px-3`;
            cancelBtn.textContent = cancelText;

            if (iconWrapper) {
                iconWrapper.className = `emua-confirm-icon-wrapper ${isDanger ? 'emua-confirm-icon-danger' : 'emua-confirm-icon-warning'}`;
                iconWrapper.innerHTML = `<i class="bi ${isDanger ? 'bi-exclamation-triangle-fill' : 'bi-question-lg'}"></i>`;
            }

            const modalInstance = bootstrap.Modal.getOrCreateInstance(modalEl);

            // Xóa listener cũ
            const newConfirmBtn = confirmBtn.cloneNode(true);
            confirmBtn.parentNode.replaceChild(newConfirmBtn, confirmBtn);

            newConfirmBtn.addEventListener('click', function () {
                modalInstance.hide();
                if (typeof onConfirm === 'function') {
                    onConfirm();
                }
            });

            modalInstance.show();
        }
    };

    // Override browser alert để không dùng popup mặc định
    window.alert = function (message) {
        EmuaNotify.info(message, 'Thông báo');
    };

    // Form confirmation helper
    window.EmuaConfirmForm = function (form, message, title = 'Xác nhận thao tác') {
        EmuaNotify.confirm(message, function () {
            form.submit();
        }, { title });
        return false;
    };

    // Expose global
    window.EmuaNotify = EmuaNotify;
    window.EmuaConfirm = EmuaNotify.confirm.bind(EmuaNotify);

    // Tự động gắn listener cho các form / nút có attribute data-emua-confirm
    document.addEventListener('DOMContentLoaded', function () {
        // Cấu hình thông báo tiếng Việt mặc định cho jQuery Validation nếu có
        if (window.jQuery && window.jQuery.validator) {
            window.jQuery.extend(window.jQuery.validator.messages, {
                required: "Vui lòng nhập trường này.",
                remote: "Vui lòng sửa trường này.",
                email: "Vui lòng nhập địa chỉ email hợp lệ.",
                url: "Vui lòng nhập đường dẫn URL hợp lệ.",
                date: "Vui lòng nhập ngày hợp lệ.",
                dateISO: "Vui lòng nhập ngày hợp lệ (ISO).",
                number: "Vui lòng nhập số hợp lệ.",
                digits: "Chỉ được phép nhập chữ số.",
                creditcard: "Vui lòng nhập số thẻ tín dụng hợp lệ.",
                equalTo: "Vui lòng nhập lại giá trị tương tự.",
                accept: "Vui lòng nhập phần mở rộng hợp lệ.",
                maxlength: window.jQuery.validator.format("Vui lòng không nhập quá {0} ký tự."),
                minlength: window.jQuery.validator.format("Vui lòng nhập ít nhất {0} ký tự."),
                rangelength: window.jQuery.validator.format("Vui lòng nhập từ {0} đến {1} ký tự."),
                range: window.jQuery.validator.format("Vui lòng nhập giá trị từ {0} đến {1}."),
                max: window.jQuery.validator.format("Vui lòng nhập giá trị nhỏ hơn hoặc bằng {0}."),
                min: window.jQuery.validator.format("Vui lòng nhập giá trị lớn hơn hoặc bằng {0}.")
            });
        }

        document.body.addEventListener('click', function (e) {
            const trigger = e.target.closest('[data-emua-confirm]');
            if (!trigger) return;

            e.preventDefault();
            const message = trigger.getAttribute('data-emua-confirm') || 'Bạn có chắc chắn muốn thực hiện thao tác này?';
            const title = trigger.getAttribute('data-emua-confirm-title') || 'Xác nhận thao tác';

            if (trigger.tagName === 'A') {
                const href = trigger.getAttribute('href');
                if (href && href !== '#') {
                    EmuaNotify.confirm(message, () => { window.location.href = href; }, { title });
                }
            } else if (trigger.tagName === 'BUTTON' || trigger.tagName === 'INPUT') {
                const form = trigger.closest('form');
                if (form) {
                    EmuaNotify.confirm(message, () => { form.submit(); }, { title });
                }
            }
        });
    });

})(window, document);
