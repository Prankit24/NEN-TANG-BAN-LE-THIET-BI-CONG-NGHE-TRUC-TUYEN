document.addEventListener("DOMContentLoaded", function () {
    const wrapper = document.getElementById("draggableAiWrapper");
    const aiButton = document.getElementById("aiToggleButton");
    const helpButton = document.getElementById("aiHelpButton");
    const chatBox = document.getElementById("aiChatBox");
    const closeButton = document.getElementById("aiCloseButton");
    const input = document.getElementById("aiMessageInput");
    const sendButton = document.getElementById("aiSendButton");
    const messages = document.getElementById("aiMessages");

    if (!wrapper || !aiButton || !chatBox || !input || !sendButton || !messages) {
        console.error("Không tìm thấy đầy đủ phần tử của EMUA AI.");
        return;
    }

    const POSITION_KEY = "emuaAiPosition";
    const DRAG_THRESHOLD = 5;

    let isDragging = false;
    let hasMoved = false;
    let startMouseX = 0;
    let startMouseY = 0;
    let startLeft = 0;
    let startTop = 0;

    function saveAiPosition() {
        const rect = wrapper.getBoundingClientRect();

        try {
            localStorage.setItem(
                POSITION_KEY,
                JSON.stringify({
                    left: rect.left,
                    top: rect.top
                })
            );
        } catch (error) {
            console.error("Không thể lưu vị trí EMUA AI:", error);
        }
    }

    function restoreAiPosition() {
        try {
            const saved = localStorage.getItem(POSITION_KEY);

            if (!saved) {
                return;
            }

            const position = JSON.parse(saved);

            if (
                typeof position.left !== "number" ||
                typeof position.top !== "number"
            ) {
                return;
            }

            const maxLeft = Math.max(
                10,
                window.innerWidth - wrapper.offsetWidth - 10
            );

            const maxTop = Math.max(
                10,
                window.innerHeight - wrapper.offsetHeight - 10
            );

            const left = Math.max(
                10,
                Math.min(maxLeft, position.left)
            );

            const top = Math.max(
                10,
                Math.min(maxTop, position.top)
            );

            wrapper.style.left = left + "px";
            wrapper.style.top = top + "px";
            wrapper.style.right = "auto";
            wrapper.style.bottom = "auto";
        } catch (error) {
            console.error("Vị trí AI không hợp lệ:", error);
        }
    }

    function positionChatNearMascot() {
        if (!chatBox.classList.contains("open")) {
            return;
        }

        const rect = wrapper.getBoundingClientRect();
        const gap = 15;

        const chatWidth = chatBox.offsetWidth;
        const chatHeight = chatBox.offsetHeight;

        let left = rect.right + gap;
        let top = rect.top;
        if (left + chatWidth > window.innerWidth - 10) {
            left = rect.left - chatWidth - gap;
        }

        if (left < 10) {
            left = 10;
        }

        if (top + chatHeight > window.innerHeight - 10) {
            top = window.innerHeight - chatHeight - 10;
        }

        if (top < 10) {
            top = 10;
        }

        chatBox.style.left = left + "px";
        chatBox.style.top = top + "px";
        chatBox.style.right = "auto";
        chatBox.style.bottom = "auto";
    }

    function moveMascot(clientX, clientY) {
        const deltaX = clientX - startMouseX;
        const deltaY = clientY - startMouseY;

        if (
            !hasMoved &&
            (
                Math.abs(deltaX) > DRAG_THRESHOLD ||
                Math.abs(deltaY) > DRAG_THRESHOLD
            )
        ) {
            hasMoved = true;
        }

        if (!hasMoved) {
            return;
        }

        let newLeft = startLeft + deltaX;
        let newTop = startTop + deltaY;

        const maxLeft = Math.max(
            10,
            window.innerWidth - wrapper.offsetWidth - 10
        );

        const maxTop = Math.max(
            10,
            window.innerHeight - wrapper.offsetHeight - 10
        );

        newLeft = Math.max(10, Math.min(maxLeft, newLeft));
        newTop = Math.max(10, Math.min(maxTop, newTop));

        wrapper.style.left = newLeft + "px";
        wrapper.style.top = newTop + "px";
        wrapper.style.right = "auto";
        wrapper.style.bottom = "auto";
        positionChatNearMascot();
    }

    function startDragging(clientX, clientY) {
        const rect = wrapper.getBoundingClientRect();

        isDragging = true;
        hasMoved = false;

        startMouseX = clientX;
        startMouseY = clientY;
        startLeft = rect.left;
        startTop = rect.top;

        aiButton.classList.add("dragging");
    }

    function stopDragging() {
        if (!isDragging) {
            return;
        }

        isDragging = false;
        aiButton.classList.remove("dragging");

        if (hasMoved) {
            saveAiPosition();
        }
    }

    restoreAiPosition();
    aiButton.addEventListener("mousedown", function (event) {
        if (event.button !== 0) {
            return;
        }

        startDragging(event.clientX, event.clientY);
        event.preventDefault();
    });

    document.addEventListener("mousemove", function (event) {
        if (!isDragging) {
            return;
        }

        if (event.buttons !== 1) {
            stopDragging();
            return;
        }

        moveMascot(event.clientX, event.clientY);
    });

    document.addEventListener("mouseup", stopDragging);
    window.addEventListener("blur", stopDragging);
    aiButton.addEventListener("dragstart", function (event) {
        event.preventDefault();
    });
    aiButton.addEventListener(
        "touchstart",
        function (event) {
            if (event.touches.length !== 1) {
                return;
            }

            const touch = event.touches[0];
            startDragging(touch.clientX, touch.clientY);
        },
        { passive: true }
    );

    document.addEventListener(
        "touchmove",
        function (event) {
            if (!isDragging || event.touches.length !== 1) {
                return;
            }

            const touch = event.touches[0];
            moveMascot(touch.clientX, touch.clientY);

            if (hasMoved) {
                event.preventDefault();
            }
        },
        { passive: false }
    );

    document.addEventListener("touchend", stopDragging);
    document.addEventListener("touchcancel", stopDragging);

    function toggleChat() {
        const isOpen = chatBox.classList.contains("open");

        if (isOpen) {
            chatBox.classList.remove("open");
            return;
        }

        chatBox.classList.add("open");
        requestAnimationFrame(function () {
            positionChatNearMascot();
            input.focus();
        });
    }

    aiButton.addEventListener("click", function () {
        if (hasMoved) {
            hasMoved = false;
            return;
        }

        toggleChat();
    });

    if (helpButton) {
        helpButton.addEventListener("click", function (event) {
            event.stopPropagation();
            toggleChat();
        });
    }

    if (closeButton) {
        closeButton.addEventListener("click", function (event) {
            event.stopPropagation();
            chatBox.classList.remove("open");
        });
    }

    function isUserScrolledNearBottom() {
        const threshold = 100;
        return (messages.scrollHeight - messages.scrollTop - messages.clientHeight) <= threshold;
    }

    function scrollMessagesToBottom(force) {
        if (force || isUserScrolledNearBottom()) {
            messages.scrollTop = messages.scrollHeight;
        }
    }

    function renderProductCards(products) {
        if (!Array.isArray(products) || products.length === 0) {
            return null;
        }

        const container = document.createElement("div");
        container.classList.add("ai-products-container");

        products.slice(0, 3).forEach(function (product) {
            const card = document.createElement("div");
            card.classList.add("ai-product-card");

            const top = document.createElement("div");
            top.classList.add("ai-product-top");

            const img = document.createElement("img");
            img.classList.add("ai-product-img");
            img.src = product.image || "/images/sanpham/default.png";
            img.alt = product.name || "Sản phẩm";
            img.onerror = function () {
                this.src = "/images/AI/CarrtoonAI.png";
            };

            const info = document.createElement("div");
            info.classList.add("ai-product-info");

            const title = document.createElement("a");
            title.classList.add("ai-product-name");
            title.href = product.url;
            title.textContent = product.name;
            title.title = product.name;

            const meta = document.createElement("div");
            meta.classList.add("ai-product-meta");

            const price = document.createElement("span");
            price.classList.add("ai-product-price");
            price.textContent = product.price;

            const badge = document.createElement("span");
            badge.classList.add("ai-stock-badge", product.inStock ? "ai-stock-in" : "ai-stock-out");
            badge.textContent = product.inStock ? "Còn hàng" : "Hết hàng";

            meta.appendChild(price);
            meta.appendChild(badge);

            info.appendChild(title);
            info.appendChild(meta);

            top.appendChild(img);
            top.appendChild(info);

            const actions = document.createElement("div");
            actions.classList.add("ai-product-actions");

            const viewBtn = document.createElement("a");
            viewBtn.classList.add("ai-action-btn", "ai-action-view");
            viewBtn.href = product.url;
            viewBtn.textContent = "Xem sản phẩm";

            actions.appendChild(viewBtn);

            if (product.inStock) {
                const buyBtn = document.createElement("a");
                buyBtn.classList.add("ai-action-btn", "ai-action-buy");
                buyBtn.href = product.url;
                buyBtn.textContent = "Mua ngay";
                actions.appendChild(buyBtn);
            } else {
                const contactBtn = document.createElement("a");
                contactBtn.classList.add("ai-action-btn", "ai-action-contact");
                contactBtn.href = "/Home/Contact";
                contactBtn.textContent = "Liên hệ";
                actions.appendChild(contactBtn);
            }

            card.appendChild(top);
            card.appendChild(actions);
            container.appendChild(card);
        });

        return container;
    }

    function addMessage(text, sender, products) {
        const wasNearBottom = isUserScrolledNearBottom();
        const message = document.createElement("div");
        message.classList.add("ai-message");

        if (sender === "user") {
            message.classList.add("ai-message-user");

            const bubble = document.createElement("div");
            bubble.classList.add("ai-bubble", "ai-bubble-user");
            bubble.textContent = text;

            message.appendChild(bubble);
        } else {
            message.classList.add("ai-message-bot");

            const avatar = document.createElement("div");
            avatar.classList.add("ai-msg-avatar");

            const image = document.createElement("img");
            image.src = "/images/AI/CarrtoonAI.png";
            image.alt = "EMUA AI";
            image.draggable = false;

            avatar.appendChild(image);

            const content = document.createElement("div");
            content.classList.add("ai-msg-content");

            const bubble = document.createElement("div");
            bubble.classList.add("ai-bubble", "ai-bubble-bot");
            bubble.textContent = text;

            content.appendChild(bubble);

            if (products && products.length > 0) {
                const productCards = renderProductCards(products);
                if (productCards) {
                    content.appendChild(productCards);
                }
            }

            message.appendChild(avatar);
            message.appendChild(content);
        }

        messages.appendChild(message);

        // Tin nhắn của user luôn tự cuộn xuống. Với bot, chỉ cuộn xuống nếu user đang ở gần đáy để tránh ngắt quãng đọc lịch sử.
        if (sender === "user" || wasNearBottom) {
            scrollMessagesToBottom(true);
        }
    }

    async function sendMessage() {
        const text = input.value.trim();

        if (!text) {
            return;
        }

        addMessage(text, "user");
        input.value = "";
        sendButton.disabled = true;

        try {
            const response = await fetch("/AI/Chat", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    message: text
                })
            });

            if (!response.ok) {
                let errorMessage = "Mình đang gặp một chút sự cố khi xử lý câu hỏi. Bạn thử lại nhé.";
                try {
                    const errData = await response.json();
                    if (errData && errData.message) {
                        errorMessage = errData.message;
                    }
                } catch (e) {
                    // response không có JSON body
                }

                // Giữ lại nội dung câu hỏi trong ô nhập để người dùng không phải gõ lại
                input.value = text;
                addMessage(errorMessage, "bot");
                return;
            }

            const data = await response.json();

            if (!data || !data.message) {
                input.value = text;
                addMessage(
                    "Mình chưa hiểu rõ câu hỏi. Bạn có thể nói cụ thể hơn một chút nhé.",
                    "bot"
                );
                return;
            }

            addMessage(data.message, "bot", data.products);
        } catch (error) {
            console.error("EMUA AI Error:", error);

            // Giữ lại nội dung câu hỏi trong ô nhập khi gặp lỗi kết nối
            input.value = text;
            addMessage(
                "Mình chưa thể kết nối tới hệ thống lúc này. Bạn thử lại sau nhé.",
                "bot"
            );
        } finally {
            sendButton.disabled = false;
            input.focus();
        }
    }

    sendButton.addEventListener("click", sendMessage);

    input.addEventListener("keydown", function (event) {
        if (event.key === "Enter" && !event.shiftKey) {
            event.preventDefault();
            sendMessage();
        }
    });

    const quickButtons = document.querySelectorAll(".ai-quick-btn");

    quickButtons.forEach(function (button) {
        button.addEventListener("click", function () {
            const quickMessage = button.dataset.message;

            if (!quickMessage) {
                return;
            }

            input.value = quickMessage;
            sendMessage();
        });
    });

    window.addEventListener("resize", function () {
        if (wrapper.style.left) {
            const rect = wrapper.getBoundingClientRect();

            const maxLeft = Math.max(
                10,
                window.innerWidth - wrapper.offsetWidth - 10
            );

            const maxTop = Math.max(
                10,
                window.innerHeight - wrapper.offsetHeight - 10
            );

            const left = Math.max(10, Math.min(maxLeft, rect.left));
            const top = Math.max(10, Math.min(maxTop, rect.top));

            wrapper.style.left = left + "px";
            wrapper.style.top = top + "px";
            wrapper.style.right = "auto";
            wrapper.style.bottom = "auto";

            saveAiPosition();
        }
        positionChatNearMascot();
    });
});