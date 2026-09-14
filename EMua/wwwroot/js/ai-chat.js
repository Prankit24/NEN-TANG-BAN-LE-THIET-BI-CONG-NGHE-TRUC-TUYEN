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

        // Không đủ chỗ bên phải thì mở chat về bên trái icon.
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

        // Đang mở chat thì chatbox luôn bám theo icon.
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

    // Kéo trên máy tính.
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

    // Không cho browser kéo ảnh icon.
    aiButton.addEventListener("dragstart", function (event) {
        event.preventDefault();
    });

    // Kéo trên điện thoại.
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

        // Đợi class open hiển thị xong để lấy đúng kích thước chatbox.
        requestAnimationFrame(function () {
            positionChatNearMascot();
            input.focus();
        });
    }

    aiButton.addEventListener("click", function () {
        // Kéo xong thì không được tự mở/đóng chat.
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

    function addMessage(text, sender) {
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

            message.appendChild(avatar);
            message.appendChild(content);
        }

        messages.appendChild(message);
        messages.scrollTop = messages.scrollHeight;
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
                addMessage(
                    "Mình đang gặp một chút sự cố khi xử lý câu hỏi. Bạn thử lại nhé.",
                    "bot"
                );
                return;
            }

            const data = await response.json();

            if (!data || !data.message) {
                addMessage(
                    "Mình chưa hiểu rõ câu hỏi. Bạn có thể nói cụ thể hơn một chút nhé.",
                    "bot"
                );
                return;
            }

            addMessage(data.message, "bot");
        } catch (error) {
            console.error("EMUA AI Error:", error);

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

        // Đổi kích thước màn hình vẫn giữ chat bám icon.
        positionChatNearMascot();
    });
});