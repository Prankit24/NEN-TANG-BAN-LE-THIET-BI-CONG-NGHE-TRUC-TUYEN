// ============================================================
// EMUA - GLOBAL SITE JAVASCRIPT
// ============================================================

(function () {
    'use strict';

    // Cấu hình thông báo tiếng Việt mặc định cho jQuery Validation
    function setupJqueryValidationVietnamese() {
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
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', setupJqueryValidationVietnamese);
    } else {
        setupJqueryValidationVietnamese();
    }
})();
