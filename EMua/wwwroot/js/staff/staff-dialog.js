/**
 * EMUA Staff Dialog System
 * Standardizes all feedback, errors, alerts, and confirmations into professional modals.
 */
(function (window) {
    'use strict';

    let alertModalInstance = null;
    let confirmModalInstance = null;
    let confirmResolveCallback = null;

    function getAlertModal() {
        const el = document.getElementById('staffAlertModal');
        if (!el) return null;
        if (!alertModalInstance && window.bootstrap && window.bootstrap.Modal) {
            alertModalInstance = new window.bootstrap.Modal(el, {
                backdrop: 'static',
                keyboard: true
            });
        }
        return { el, instance: alertModalInstance };
    }

    function getConfirmModal() {
        const el = document.getElementById('staffConfirmModal');
        if (!el) return null;
        if (!confirmModalInstance && window.bootstrap && window.bootstrap.Modal) {
            confirmModalInstance = new window.bootstrap.Modal(el, {
                backdrop: 'static',
                keyboard: false
            });
        }
        return { el, instance: confirmModalInstance };
    }

    const StaffDialog = {
        /**
         * Show an alert dialog (Success, Error, Warning, Info)
         * Returns a Promise that resolves when the user closes the modal.
         */
        show: function (options) {
            return new Promise(function (resolve) {
                const target = getAlertModal();
                if (!target || !target.instance) {
                    // Fallback if bootstrap is not ready
                    window.alert((options.title ? options.title + ': ' : '') + (options.message || ''));
                    resolve();
                    return;
                }

                const el = target.el;
                const type = options.type || 'info'; // 'success', 'error', 'warning', 'info'
                const title = options.title || (type === 'success' ? 'Thành công' : (type === 'error' ? 'Đã có lỗi xảy ra' : 'Thông báo'));
                const message = options.message || '';
                const btnText = options.buttonText || (type === 'success' ? 'Đã hiểu' : 'Đóng');

                // Reset types
                el.classList.remove('staff-dialog-success', 'staff-dialog-error', 'staff-dialog-warning', 'staff-dialog-info');
                el.classList.add('staff-dialog-' + type);

                // Set icons
                const iconEl = el.querySelector('#staffAlertIcon');
                if (iconEl) {
                    if (type === 'success') iconEl.className = 'bi bi-check-lg';
                    else if (type === 'error') iconEl.className = 'bi bi-x-lg';
                    else if (type === 'warning') iconEl.className = 'bi bi-exclamation-triangle';
                    else iconEl.className = 'bi bi-info-lg';
                }

                const titleEl = el.querySelector('#staffAlertTitle');
                if (titleEl) titleEl.textContent = title;

                const msgEl = el.querySelector('#staffAlertMessage');
                if (msgEl) msgEl.textContent = message;

                const btn = el.querySelector('#staffAlertBtn');
                if (btn) {
                    btn.textContent = btnText;
                    if (type === 'error') {
                        btn.className = 'btn btn-outline-danger';
                    } else if (type === 'success') {
                        btn.className = 'btn btn-success';
                    } else {
                        btn.className = 'btn btn-primary';
                    }
                }

                function onHidden() {
                    el.removeEventListener('hidden.bs.modal', onHidden);
                    resolve();
                }
                el.addEventListener('hidden.bs.modal', onHidden);

                target.instance.show();
            });
        },

        success: function (title, message, buttonText) {
            if (arguments.length === 1) {
                message = title;
                title = 'Thành công';
            }
            return this.show({ type: 'success', title: title, message: message, buttonText: buttonText || 'Đã hiểu' });
        },

        error: function (title, message, buttonText) {
            if (arguments.length === 1) {
                message = title;
                title = 'Đã có lỗi xảy ra';
            }
            return this.show({ type: 'error', title: title, message: message, buttonText: buttonText || 'Đóng' });
        },

        warning: function (title, message, buttonText) {
            if (arguments.length === 1) {
                message = title;
                title = 'Cảnh báo';
            }
            return this.show({ type: 'warning', title: title, message: message, buttonText: buttonText || 'Đồng ý' });
        },

        info: function (title, message, buttonText) {
            if (arguments.length === 1) {
                message = title;
                title = 'Thông tin';
            }
            return this.show({ type: 'info', title: title, message: message, buttonText: buttonText || 'Đóng' });
        },

        /**
         * Confirmation dialog
         * StaffDialog.confirm({ title, message, confirmText, cancelText, isDanger: true })
         * Returns a Promise resolving to true or false.
         */
        confirm: function (options) {
            return new Promise(function (resolve) {
                const target = getConfirmModal();
                if (!target || !target.instance) {
                    const ok = window.confirm((options.title ? options.title + '\n' : '') + (options.message || ''));
                    resolve(ok);
                    return;
                }

                const el = target.el;
                const title = options.title || 'Xác nhận thao tác';
                const message = options.message || 'Bạn có chắc chắn muốn thực hiện thao tác này?';
                const confirmText = options.confirmText || 'Xác nhận';
                const cancelText = options.cancelText || 'Hủy';
                const isDanger = options.isDanger !== false; // default true for safe prompt

                el.classList.remove('staff-dialog-warning', 'staff-dialog-confirm', 'staff-dialog-error');
                el.classList.add(isDanger ? 'staff-dialog-error' : 'staff-dialog-warning');

                const iconEl = el.querySelector('#staffConfirmIcon');
                if (iconEl) {
                    iconEl.className = isDanger ? 'bi bi-exclamation-octagon' : 'bi bi-question-lg';
                }

                const titleEl = el.querySelector('#staffConfirmTitle');
                if (titleEl) titleEl.textContent = title;

                const msgEl = el.querySelector('#staffConfirmMessage');
                if (msgEl) msgEl.textContent = message;

                const cancelBtn = el.querySelector('#staffConfirmCancelBtn');
                if (cancelBtn) cancelBtn.textContent = cancelText;

                const submitBtn = el.querySelector('#staffConfirmSubmitBtn');
                if (submitBtn) {
                    submitBtn.textContent = confirmText;
                    submitBtn.className = isDanger ? 'btn btn-confirm-danger' : 'btn btn-primary';
                    submitBtn.disabled = false;
                }

                let userChoice = false;

                function onConfirmClick() {
                    userChoice = true;
                    target.instance.hide();
                }

                function onHidden() {
                    submitBtn.removeEventListener('click', onConfirmClick);
                    el.removeEventListener('hidden.bs.modal', onHidden);
                    resolve(userChoice);
                }

                submitBtn.addEventListener('click', onConfirmClick);
                el.addEventListener('hidden.bs.modal', onHidden);

                target.instance.show();
            });
        }
    };

    window.StaffDialog = StaffDialog;

    // Automatic check for TempData when DOM is ready
    document.addEventListener('DOMContentLoaded', function () {
        const payloadEl = document.getElementById('staffTempDataPayload');
        if (payloadEl) {
            const successMsg = payloadEl.getAttribute('data-success');
            const errorMsg = payloadEl.getAttribute('data-error');
            const warningMsg = payloadEl.getAttribute('data-warning');

            if (successMsg && successMsg.trim()) {
                StaffDialog.success('Thành công', successMsg.trim());
            } else if (errorMsg && errorMsg.trim()) {
                StaffDialog.error('Đã có lỗi xảy ra', errorMsg.trim());
            } else if (warningMsg && warningMsg.trim()) {
                StaffDialog.warning('Cảnh báo', warningMsg.trim());
            }
        }
    });

})(window);
