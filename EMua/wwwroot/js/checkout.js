document.addEventListener("DOMContentLoaded", async function () {

    // =========================================================
    // GIỎ HÀNG TOÀN CỤC
    // =========================================================

    function setCartBadge(count) {
        const safeCount = Math.max(
            0,
            Number.parseInt(count, 10) || 0
        );

        document
            .querySelectorAll(".cart-count")
            .forEach(function (badge) {
                badge.textContent = safeCount.toString();

                badge.style.display =
                    safeCount > 0
                        ? "flex"
                        : "none";
            });
    }


    async function refreshCartCount() {
        try {
            const response = await fetch(
                "/Cart/Summary",
                {
                    method: "GET",
                    credentials: "same-origin",
                    cache: "no-store",
                    headers: {
                        "X-Requested-With": "XMLHttpRequest"
                    }
                }
            );


            if (response.ok) {
                const data = await response.json();

                const count = Number(
                    data.count
                    ?? data.totalQuantity
                    ?? 0
                );

                setCartBadge(count);

                localStorage.setItem(
                    "emuaCartCount",
                    String(count)
                );

                return;
            }


            if (
                response.status === 401
                ||
                response.status === 403
            ) {
                const guestCount = Number(
                    localStorage.getItem(
                        "emuaCartCount"
                    )
                    || 0
                );

                setCartBadge(guestCount);

                return;
            }

        } catch (error) {

            console.warn(
                "Không lấy được số lượng giỏ hàng:",
                error
            );
        }


        const localCount = Number(
            localStorage.getItem(
                "emuaCartCount"
            )
            || 0
        );

        setCartBadge(localCount);
    }


    window.refreshCartCount = refreshCartCount;
    window.setCartBadge = setCartBadge;


    await refreshCartCount();


    window.addEventListener(
        "pageshow",
        function () {
            refreshCartCount();
        }
    );


    window.addEventListener(
        "storage",
        function (event) {
            if (
                event.key === "emuaCartCount"
            ) {
                setCartBadge(
                    event.newValue || 0
                );
            }
        }
    );


    // =========================================================
    // CHECKOUT PAGE
    // =========================================================

    const checkoutPage =
        document.querySelector(".checkout-page");

    if (!checkoutPage) return;


    const placeOrderButton =
        document.getElementById("placeOrder");

    const applyCouponButton =
        document.getElementById("applyCoupon");

    const fullNameInput =
        document.getElementById("fullName");

    const phoneNumberInput =
        document.getElementById("phoneNumber");

    const addressInput =
        document.getElementById("address");

    const noteInput =
        document.getElementById("note");

    const couponCodeInput =
        document.getElementById("couponCode");

    const couponMessage =
        document.getElementById("couponMessage");

    const checkoutAlert =
        document.getElementById("checkoutAlert");

    const discountText =
        document.getElementById("discountText");

    const shippingText =
        document.getElementById("shippingText");

    const totalText =
        document.getElementById("totalText");


    let appliedCouponCode = null;

    let isPlacingOrder = false;

    let orderCreated = false;


    // =========================================================
    // TOKEN
    // =========================================================

    function getToken() {
        return document.querySelector(
            'input[name="__RequestVerificationToken"]'
        )?.value || "";
    }


    // =========================================================
    // FORMAT MONEY
    // =========================================================

    function formatMoney(value) {
        return new Intl.NumberFormat(
            "vi-VN"
        ).format(Number(value) || 0) + " ₫";
    }


    // =========================================================
    // ALERT
    // =========================================================

    function showMessage(
        message,
        type = "danger"
    ) {
        if (!checkoutAlert) {
            alert(message);
            return;
        }

        checkoutAlert.innerHTML = `
            <div class="alert alert-${type} alert-dismissible fade show"
                 role="alert">

                ${message}

                <button type="button"
                        class="btn-close"
                        data-bs-dismiss="alert">
                </button>

            </div>
        `;
    }


    // =========================================================
    // PAYMENT ACTIVE
    // =========================================================

    document
        .querySelectorAll(
            'input[name="paymentMethod"]'
        )
        .forEach(function (radio) {

            radio.addEventListener(
                "change",
                function () {

                    document
                        .querySelectorAll(
                            ".payment-option"
                        )
                        .forEach(
                            function (item) {
                                item.classList.remove(
                                    "active"
                                );
                            }
                        );

                    this
                        .closest(
                            ".payment-option"
                        )
                        ?.classList.add(
                            "active"
                        );
                }
            );
        });


    // =========================================================
    // COUPON
    // =========================================================

    applyCouponButton?.addEventListener(
        "click",
        async function () {

            const code =
                couponCodeInput
                    ?.value
                    ?.trim();


            if (!code) {
                if (couponMessage) {
                    couponMessage.textContent =
                        "Vui lòng nhập mã giảm giá.";
                }

                return;
            }


            const token =
                getToken();


            if (!token) {
                showMessage(
                    "Không tìm thấy mã bảo mật."
                );

                return;
            }


            const oldHtml =
                applyCouponButton.innerHTML;


            applyCouponButton.disabled =
                true;


            applyCouponButton.innerHTML =
                "Đang kiểm tra...";


            try {

                const response =
                    await fetch(
                        "/Checkout/ApplyCoupon",
                        {
                            method:
                                "POST",

                            headers: {
                                "Content-Type":
                                    "application/json",

                                "RequestVerificationToken":
                                    token
                            },

                            body:
                                JSON.stringify({
                                    couponCode:
                                        code
                                })
                        }
                    );


                const result =
                    await response.json();


                if (!response.ok) {

                    if (couponMessage) {
                        couponMessage.textContent =
                            result.message
                            ||
                            "Mã giảm giá không hợp lệ.";

                        couponMessage.style.color =
                            "#d83d55";
                    }

                    return;
                }


                appliedCouponCode =
                    result.couponCode;


                if (couponMessage) {
                    couponMessage.textContent =
                        result.message;

                    couponMessage.style.color =
                        "#25965f";
                }


                if (discountText) {
                    discountText.textContent =
                        "-"
                        +
                        formatMoney(
                            result.discount
                        );
                }


                if (shippingText) {
                    shippingText.textContent =
                        Number(
                            result.shippingFee
                        ) === 0
                            ? "Miễn phí"
                            : formatMoney(
                                result.shippingFee
                            );
                }


                if (totalText) {
                    totalText.textContent =
                        formatMoney(
                            result.total
                        );
                }

            }
            catch (error) {

                console.error(error);

                showMessage(
                    "Không thể áp dụng mã giảm giá."
                );

            }
            finally {

                applyCouponButton.disabled =
                    false;

                applyCouponButton.innerHTML =
                    oldHtml;
            }
        }
    );


    // =========================================================
    // PLACE ORDER
    // =========================================================

    placeOrderButton?.addEventListener(
        "click",
        async function () {

            if (
                isPlacingOrder
                ||
                orderCreated
            ) {
                return;
            }


            const fullName =
                fullNameInput
                    ?.value
                    ?.trim();


            const phoneNumber =
                phoneNumberInput
                    ?.value
                    ?.trim();


            const address =
                addressInput
                    ?.value
                    ?.trim();


            const note =
                noteInput
                    ?.value
                    ?.trim();


            const paymentMethod =
                document.querySelector(
                    'input[name="paymentMethod"]:checked'
                )?.value;


            if (!fullName) {
                showMessage(
                    "Vui lòng nhập họ và tên."
                );

                fullNameInput?.focus();

                return;
            }


            if (!phoneNumber) {
                showMessage(
                    "Vui lòng nhập số điện thoại."
                );

                phoneNumberInput?.focus();

                return;
            }


            if (!address) {
                showMessage(
                    "Vui lòng nhập địa chỉ nhận hàng."
                );

                addressInput?.focus();

                return;
            }


            if (!paymentMethod) {
                showMessage(
                    "Vui lòng chọn phương thức thanh toán."
                );

                return;
            }


            const token =
                getToken();


            if (!token) {
                showMessage(
                    "Không tìm thấy mã bảo mật."
                );

                return;
            }


            isPlacingOrder =
                true;


            placeOrderButton.disabled =
                true;


            placeOrderButton.innerHTML = `
                <span class="spinner-border spinner-border-sm me-2"></span>
                Đang tạo đơn hàng...
            `;


            const requestData = {
                fullName:
                    fullName,

                phoneNumber:
                    phoneNumber,

                address:
                    address,

                note:
                    note || null,

                couponCode:
                    appliedCouponCode
                    ||
                    couponCodeInput
                        ?.value
                        ?.trim()
                    ||
                    null,

                paymentMethod:
                    paymentMethod
            };


            try {

                const response =
                    await fetch(
                        "/Checkout/PlaceOrder",
                        {
                            method:
                                "POST",

                            headers: {
                                "Content-Type":
                                    "application/json",

                                "RequestVerificationToken":
                                    token
                            },

                            body:
                                JSON.stringify(
                                    requestData
                                )
                        }
                    );


                const result =
                    await response.json();


                if (!response.ok) {

                    showMessage(
                        result.message
                        ||
                        "Không thể tạo đơn hàng."
                    );

                    isPlacingOrder =
                        false;

                    placeOrderButton.disabled =
                        false;

                    placeOrderButton.innerHTML =
                        `Đặt hàng <i class="bi bi-arrow-right"></i>`;

                    return;
                }


                if (!result.success) {

                    isPlacingOrder =
                        false;

                    placeOrderButton.disabled =
                        false;

                    placeOrderButton.innerHTML =
                        `Đặt hàng <i class="bi bi-arrow-right"></i>`;

                    showMessage(
                        result.message
                        ||
                        "Không thể tạo đơn hàng."
                    );

                    return;
                }


                orderCreated =
                    true;


                isPlacingOrder =
                    false;


                placeOrderButton.disabled =
                    true;


                placeOrderButton.innerHTML = `
                    <i class="bi bi-check-circle me-2"></i>
                    Đơn hàng đã tạo
                `;


                localStorage.setItem(
                    "emuaCartCount",
                    "0"
                );

                setCartBadge(0);


                // =================================================
                // COD
                // =================================================

                if (
                    result.paymentMethod
                    === "COD"
                ) {

                    showMessage(
                        result.message,
                        "success"
                    );


                    setTimeout(
                        function () {

                            window.location.href =
                                "/Account/Profile";

                        },
                        1500
                    );


                    return;
                }


                // =================================================
                // ONLINE PAYMENT
                // =================================================

                showPaymentModal(
                    result
                );

            }
            catch (error) {

                console.error(
                    "PlaceOrder error:",
                    error
                );


                isPlacingOrder =
                    false;


                placeOrderButton.disabled =
                    false;


                placeOrderButton.innerHTML =
                    `Đặt hàng <i class="bi bi-arrow-right"></i>`;


                showMessage(
                    "Không thể kết nối đến hệ thống."
                );
            }
        }
    );


    // =========================================================
    // PAYMENT MODAL
    // =========================================================

    function showPaymentModal(result) {

        const qrImage =
            document.getElementById(
                "paymentQrImage"
            );


        const title =
            document.getElementById(
                "paymentGuideTitle"
            );


        const text =
            document.getElementById(
                "paymentGuideText"
            );


        const transferNote =
            document.getElementById(
                "paymentTransferNote"
            );


        if (
            result.paymentMethod
            === "MBBANK"
        ) {

            if (qrImage) {
                qrImage.style.display =
                    "inline-block";

                qrImage.src =
                    result.bankQrUrl;
            }


            if (title) {
                title.textContent =
                    "Thanh toán MB Bank";
            }


            if (text) {
                text.textContent =
                    "Quét mã QR để thanh toán "
                    +
                    formatMoney(
                        result.amount
                    );
            }


            if (transferNote) {
                transferNote.innerHTML = `
                    Nội dung chuyển khoản:
                    <strong>
                        ${result.transferContent}
                    </strong>
                `;
            }
        }


        else if (
            result.paymentMethod
            === "MOMO"
        ) {

            if (qrImage) {
                qrImage.style.display =
                    "inline-block";

                qrImage.src =
                    result.momoQrUrl;
            }


            if (title) {
                title.textContent =
                    "Thanh toán MoMo";
            }


            if (text) {
                text.textContent =
                    "Quét mã QR để thanh toán "
                    +
                    formatMoney(
                        result.amount
                    );
            }


            if (transferNote) {
                transferNote.innerHTML = `
                    Mã đơn hàng:
                    <strong>
                        ${result.transferContent}
                    </strong>
                `;
            }
        }


        else if (
            result.paymentMethod
            === "CARD"
        ) {

            if (qrImage) {
                qrImage.style.display =
                    "none";
            }


            if (title) {
                title.textContent =
                    "Thanh toán thẻ ngân hàng";
            }


            if (text) {
                text.textContent =
                    "Chức năng thanh toán thẻ hiện đang ở chế độ demo.";
            }


            if (transferNote) {
                transferNote.innerHTML = `
                    Số tiền:
                    <strong>
                        ${formatMoney(
                    result.amount
                )}
                    </strong>
                `;
            }
        }


        const modalElement =
            document.getElementById(
                "paymentGuideModal"
            );


        if (
            modalElement
            &&
            typeof bootstrap
            !== "undefined"
        ) {

            bootstrap.Modal
                .getOrCreateInstance(
                    modalElement
                )
                .show();
        }
    }

});