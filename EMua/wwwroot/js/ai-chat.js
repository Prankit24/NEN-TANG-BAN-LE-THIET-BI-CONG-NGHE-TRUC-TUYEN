document.addEventListener("DOMContentLoaded", function () {

    // =====================================================
    // ELEMENTS
    // =====================================================

    const wrapper =
        document.getElementById("draggableAiWrapper");

    const aiButton =
        document.getElementById("aiToggleButton");

    const helpButton =
        document.getElementById("aiHelpButton");

    const chatBox =
        document.getElementById("aiChatBox");

    const closeButton =
        document.getElementById("aiCloseButton");

    const input =
        document.getElementById("aiMessageInput");

    const sendButton =
        document.getElementById("aiSendButton");

    const messages =
        document.getElementById("aiMessages");


    // =====================================================
    // CHECK ELEMENTS
    // =====================================================

    if (
        !wrapper ||
        !aiButton ||
        !chatBox ||
        !input ||
        !sendButton ||
        !messages
    ) {
        console.error(
            "Không tìm thấy đầy đủ phần tử của EMUA AI."
        );

        return;
    }


    // =====================================================
    // SAVE POSITION
    // =====================================================

    const POSITION_KEY =
        "emuaAiPosition";


    function saveAiPosition() {

        const rect =
            wrapper.getBoundingClientRect();


        const position = {
            left: rect.left,
            top: rect.top
        };


        try {

            localStorage.setItem(
                POSITION_KEY,
                JSON.stringify(position)
            );

        }
        catch (error) {

            console.error(
                "Không thể lưu vị trí EMUA AI:",
                error
            );
        }
    }


    // =====================================================
    // RESTORE POSITION
    // =====================================================

    function restoreAiPosition() {

        let saved;


        try {

            saved =
                localStorage.getItem(
                    POSITION_KEY
                );

        }
        catch (error) {

            console.error(
                "Không thể đọc vị trí EMUA AI:",
                error
            );

            return;
        }


        if (!saved) {
            return;
        }


        try {

            const position =
                JSON.parse(saved);


            if (
                typeof position.left !== "number" ||
                typeof position.top !== "number"
            ) {
                return;
            }


            const maxLeft =
                Math.max(
                    10,
                    window.innerWidth -
                    wrapper.offsetWidth -
                    10
                );


            const maxTop =
                Math.max(
                    10,
                    window.innerHeight -
                    wrapper.offsetHeight -
                    10
                );


            const left =
                Math.max(
                    10,
                    Math.min(
                        maxLeft,
                        position.left
                    )
                );


            const top =
                Math.max(
                    10,
                    Math.min(
                        maxTop,
                        position.top
                    )
                );


            wrapper.style.left =
                left + "px";

            wrapper.style.top =
                top + "px";

            wrapper.style.right =
                "auto";

            wrapper.style.bottom =
                "auto";

        }
        catch (error) {

            console.error(
                "Vị trí AI không hợp lệ:",
                error
            );
        }
    }


    restoreAiPosition();


    // =====================================================
    // DRAG VARIABLES
    // =====================================================

    let isDragging = false;
    let hasMoved = false;

    let startMouseX = 0;
    let startMouseY = 0;

    let startLeft = 0;
    let startTop = 0;

    const DRAG_THRESHOLD = 5;


    // =====================================================
    // PC - MOUSE DOWN
    // =====================================================

    aiButton.addEventListener(
        "mousedown",
        function (event) {

            // Chỉ chuột trái
            if (event.button !== 0) {
                return;
            }


            isDragging = true;
            hasMoved = false;


            startMouseX =
                event.clientX;

            startMouseY =
                event.clientY;


            const rect =
                wrapper.getBoundingClientRect();


            startLeft =
                rect.left;

            startTop =
                rect.top;


            aiButton.classList.add(
                "dragging"
            );


            event.preventDefault();
        }
    );


    // =====================================================
    // PC - MOUSE MOVE
    // =====================================================

    document.addEventListener(
        "mousemove",
        function (event) {

            if (!isDragging) {
                return;
            }


            // Chuột trái không còn được giữ
            if (event.buttons !== 1) {

                stopDragging();

                return;
            }


            const deltaX =
                event.clientX -
                startMouseX;


            const deltaY =
                event.clientY -
                startMouseY;


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


            let newLeft =
                startLeft + deltaX;

            let newTop =
                startTop + deltaY;


            const maxLeft =
                Math.max(
                    10,
                    window.innerWidth -
                    wrapper.offsetWidth -
                    10
                );


            const maxTop =
                Math.max(
                    10,
                    window.innerHeight -
                    wrapper.offsetHeight -
                    10
                );


            newLeft =
                Math.max(
                    10,
                    Math.min(
                        maxLeft,
                        newLeft
                    )
                );


            newTop =
                Math.max(
                    10,
                    Math.min(
                        maxTop,
                        newTop
                    )
                );


            wrapper.style.left =
                newLeft + "px";

            wrapper.style.top =
                newTop + "px";

            wrapper.style.right =
                "auto";

            wrapper.style.bottom =
                "auto";
        }
    );


    // =====================================================
    // STOP DRAG
    // =====================================================

    function stopDragging() {

        if (!isDragging) {
            return;
        }


        isDragging = false;


        aiButton.classList.remove(
            "dragging"
        );


        if (hasMoved) {
            saveAiPosition();
        }
    }


    document.addEventListener(
        "mouseup",
        stopDragging
    );


    window.addEventListener(
        "blur",
        stopDragging
    );


    // Không cho browser kéo ảnh
    aiButton.addEventListener(
        "dragstart",
        function (event) {

            event.preventDefault();

        }
    );


    // =====================================================
    // MOBILE - TOUCH START
    // =====================================================

    aiButton.addEventListener(
        "touchstart",
        function (event) {

            if (
                event.touches.length !== 1
            ) {
                return;
            }


            const touch =
                event.touches[0];


            const rect =
                wrapper.getBoundingClientRect();


            isDragging = true;
            hasMoved = false;


            startMouseX =
                touch.clientX;

            startMouseY =
                touch.clientY;


            startLeft =
                rect.left;

            startTop =
                rect.top;


            aiButton.classList.add(
                "dragging"
            );

        },
        {
            passive: true
        }
    );


    // =====================================================
    // MOBILE - TOUCH MOVE
    // =====================================================

    document.addEventListener(
        "touchmove",
        function (event) {

            if (!isDragging) {
                return;
            }


            if (
                event.touches.length !== 1
            ) {
                return;
            }


            const touch =
                event.touches[0];


            const deltaX =
                touch.clientX -
                startMouseX;


            const deltaY =
                touch.clientY -
                startMouseY;


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


            let newLeft =
                startLeft + deltaX;

            let newTop =
                startTop + deltaY;


            const maxLeft =
                Math.max(
                    10,
                    window.innerWidth -
                    wrapper.offsetWidth -
                    10
                );


            const maxTop =
                Math.max(
                    10,
                    window.innerHeight -
                    wrapper.offsetHeight -
                    10
                );


            newLeft =
                Math.max(
                    10,
                    Math.min(
                        maxLeft,
                        newLeft
                    )
                );


            newTop =
                Math.max(
                    10,
                    Math.min(
                        maxTop,
                        newTop
                    )
                );


            wrapper.style.left =
                newLeft + "px";

            wrapper.style.top =
                newTop + "px";

            wrapper.style.right =
                "auto";

            wrapper.style.bottom =
                "auto";


            event.preventDefault();

        },
        {
            passive: false
        }
    );


    document.addEventListener(
        "touchend",
        stopDragging
    );


    document.addEventListener(
        "touchcancel",
        stopDragging
    );

    function positionChatNearMascot() {

        const rect = wrapper.getBoundingClientRect();

        const gap = 15;

        const chatWidth = chatBox.offsetWidth;
        const chatHeight = chatBox.offsetHeight;

        let left = rect.right + gap;
        let top = rect.top;

        // Nếu bên phải không đủ chỗ
        // thì mở sang bên trái
        if (left + chatWidth > window.innerWidth - 10) {
            left = rect.left - chatWidth - gap;
        }

        // Không cho vượt mép trái
        if (left < 10) {
            left = 10;
        }

        // Không cho vượt phía dưới
        if (top + chatHeight > window.innerHeight - 10) {
            top = window.innerHeight - chatHeight - 10;
        }

        // Không cho vượt phía trên
        if (top < 10) {
            top = 10;
        }

        chatBox.style.left = left + "px";
        chatBox.style.top = top + "px";
        chatBox.style.right = "auto";
        chatBox.style.bottom = "auto";
    }
    // =====================================================
    // TOGGLE CHAT
    // =====================================================

    function toggleChat() {

        const isOpen =
            chatBox.classList.contains(
                "open"
            );


        if (isOpen) {

            chatBox.classList.remove(
                "open"
            );

            return;
        }


        chatBox.classList.add(
            "open"
        );
        positionChatNearMascot();

        setTimeout(
            function () {

                input.focus();

            },
            80
        );
    }


    // =====================================================
    // CLICK MASCOT
    // =====================================================

    aiButton.addEventListener(
        "click",
        function () {

            // Vừa kéo mascot
            // thì không toggle chat
            if (hasMoved) {

                hasMoved = false;

                return;
            }


            toggleChat();
        }
    );


    // =====================================================
    // HELP BUTTON
    // =====================================================

    if (helpButton) {

        helpButton.addEventListener(
            "click",
            function (event) {

                event.stopPropagation();

                toggleChat();
            }
        );
    }


    // =====================================================
    // CLOSE BUTTON
    // =====================================================

    if (closeButton) {

        closeButton.addEventListener(
            "click",
            function (event) {

                event.stopPropagation();

                chatBox.classList.remove(
                    "open"
                );
            }
        );
    }


    // =====================================================
    // ADD MESSAGE
    // =====================================================

    function addMessage(
        text,
        sender
    ) {

        const message =
            document.createElement(
                "div"
            );


        message.classList.add(
            "ai-message"
        );


        // =================================================
        // USER MESSAGE
        // =================================================

        if (sender === "user") {

            message.classList.add(
                "ai-message-user"
            );


            const bubble =
                document.createElement(
                    "div"
                );


            bubble.classList.add(
                "ai-bubble",
                "ai-bubble-user"
            );


            bubble.textContent =
                text;


            message.appendChild(
                bubble
            );
        }


        // =================================================
        // BOT MESSAGE
        // =================================================

        else {

            message.classList.add(
                "ai-message-bot"
            );


            // Avatar
            const avatar =
                document.createElement(
                    "div"
                );


            avatar.classList.add(
                "ai-msg-avatar"
            );


            const image =
                document.createElement(
                    "img"
                );


            image.src =
                "/images/AI/CarrtoonAI.png";

            image.alt =
                "EMUA AI";

            image.draggable =
                false;


            avatar.appendChild(
                image
            );


            // Message body
            const content =
                document.createElement(
                    "div"
                );


            content.classList.add(
                "ai-msg-content"
            );


            const bubble =
                document.createElement(
                    "div"
                );


            bubble.classList.add(
                "ai-bubble",
                "ai-bubble-bot"
            );


            bubble.textContent =
                text;


            content.appendChild(
                bubble
            );


            message.appendChild(
                avatar
            );


            message.appendChild(
                content
            );
        }


        messages.appendChild(
            message
        );


        messages.scrollTop =
            messages.scrollHeight;
    }


    // =====================================================
    // SEND MESSAGE
    // =====================================================

    async function sendMessage() {

        const text =
            input.value.trim();


        if (!text) {
            return;
        }


        addMessage(
            text,
            "user"
        );


        input.value =
            "";


        sendButton.disabled =
            true;


        try {

            const response =
                await fetch(
                    "/AI/Chat",
                    {
                        method:
                            "POST",

                        headers: {

                            "Content-Type":
                                "application/json"

                        },

                        body:
                            JSON.stringify(
                                {
                                    message:
                                        text
                                }
                            )
                    }
                );


            if (!response.ok) {

                addMessage(
                    "Mình đang gặp một chút sự cố khi xử lý câu hỏi. Bạn thử lại nhé.",
                    "bot"
                );

                return;
            }


            const data =
                await response.json();


            if (
                !data ||
                !data.message
            ) {

                addMessage(
                    "Mình chưa hiểu rõ câu hỏi. Bạn có thể nói cụ thể hơn một chút nhé.",
                    "bot"
                );

                return;
            }


            addMessage(
                data.message,
                "bot"
            );

        }
        catch (error) {

            console.error(
                "EMUA AI Error:",
                error
            );


            addMessage(
                "Mình chưa thể kết nối tới hệ thống lúc này. Bạn thử lại sau nhé.",
                "bot"
            );
        }
        finally {

            sendButton.disabled =
                false;


            input.focus();
        }
    }


    // =====================================================
    // SEND BUTTON
    // =====================================================

    sendButton.addEventListener(
        "click",
        sendMessage
    );


    // =====================================================
    // ENTER TO SEND
    // =====================================================

    input.addEventListener(
        "keydown",
        function (event) {

            if (
                event.key === "Enter" &&
                !event.shiftKey
            ) {

                event.preventDefault();

                sendMessage();
            }
        }
    );


    // =====================================================
    // QUICK ACTIONS
    // =====================================================

    const quickButtons =
        document.querySelectorAll(
            ".ai-quick-btn"
        );


    quickButtons.forEach(
        function (button) {

            button.addEventListener(
                "click",
                function () {

                    const quickMessage =
                        button.dataset.message;


                    if (!quickMessage) {
                        return;
                    }


                    input.value =
                        quickMessage;


                    sendMessage();
                }
            );
        }
    );


    // =====================================================
    // WINDOW RESIZE
    // =====================================================

    window.addEventListener(
        "resize",
        function () {

            // Chưa từng kéo mascot
            if (!wrapper.style.left) {
                return;
            }


            const rect =
                wrapper.getBoundingClientRect();


            const maxLeft =
                Math.max(
                    10,
                    window.innerWidth -
                    wrapper.offsetWidth -
                    10
                );


            const maxTop =
                Math.max(
                    10,
                    window.innerHeight -
                    wrapper.offsetHeight -
                    10
                );


            const left =
                Math.max(
                    10,
                    Math.min(
                        maxLeft,
                        rect.left
                    )
                );


            const top =
                Math.max(
                    10,
                    Math.min(
                        maxTop,
                        rect.top
                    )
                );


            wrapper.style.left =
                left + "px";

            wrapper.style.top =
                top + "px";

            wrapper.style.right =
                "auto";

            wrapper.style.bottom =
                "auto";


            saveAiPosition();
        }
    );

});