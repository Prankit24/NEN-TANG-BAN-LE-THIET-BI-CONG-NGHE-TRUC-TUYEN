(() => {
    "use strict";
    const form = document.getElementById("emuaProductFilter");
    const results = document.getElementById("emuaProductResults");
    const count = document.getElementById("emuaProductCount");
    const status = document.getElementById("emuaFilterStatus");
    if (!form || !results || !count || !status) return;
    const keyword = document.getElementById("productKeyword");
    const category = document.getElementById("productCategory");
    const brand = document.getElementById("productBrand");
    const price = document.getElementById("productPrice");
    const sort = document.getElementById("productSort");
    const pageInput = document.getElementById("productPage");

    let controller = null;
    let timer = null;
    let serial = 0;
    let currentPage = parseInt(pageInput?.value || "1", 10) || 1;

    function urlFromFilters() {
        const url = new URL(form.action, location.origin);
        const params = {
            keyword: keyword?.value?.trim(),
            categoryId: category?.value,
            brandId: brand?.value,
            priceRange: price?.value,
            sort: sort?.value
        };
        for (const [key, value] of Object.entries(params)) {
            if (value) url.searchParams.set(key, value);
        }
        if (currentPage > 1) {
            url.searchParams.set("page", String(currentPage));
        }
        return url;
    }

    function setLoading(loading) {
        results.classList.toggle("emua-results-loading", loading);
        results.setAttribute("aria-busy", String(loading));
        status.textContent = loading ? "Đang cập nhật sản phẩm..." : "";
    }

    async function load(push = true, shouldScroll = false) {
        clearTimeout(timer);
        if (controller) controller.abort();
        controller = new AbortController();
        const current = ++serial;
        const url = urlFromFilters();
        setLoading(true);
        try {
            const response = await fetch(url, {
                headers: { "X-Requested-With": "XMLHttpRequest", "Accept": "text/html" },
                credentials: "same-origin", signal: controller.signal
            });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            const html = await response.text();
            if (current !== serial) return;
            const fragment = document.createElement("template");
            fragment.innerHTML = html;

            const totalInput = fragment.content.querySelector("#emuaTotalCount");
            const newCount = totalInput ? totalInput.value : fragment.content.querySelectorAll(".emua-product-card").length;

            results.replaceChildren(fragment.content.cloneNode(true));
            count.textContent = String(newCount);

            if (pageInput) {
                pageInput.value = String(currentPage);
            }

            if (push) history.pushState({ emuaFilter: true, page: currentPage }, "", url.pathname + url.search);

            if (shouldScroll) {
                const targetY = form.getBoundingClientRect().top + window.scrollY - 80;
                window.scrollTo({ top: Math.max(0, targetY), behavior: "smooth" });
            }
        } catch (error) {
            if (error.name !== "AbortError" && current === serial) {
                console.error("EMUA AJAX filter:", error);
                status.textContent = "Không tải được sản phẩm. Vui lòng thử lại.";
            }
        } finally {
            if (current === serial) {
                results.classList.remove("emua-results-loading");
                results.setAttribute("aria-busy", "false");
                if (!status.textContent.startsWith("Không tải")) status.textContent = "";
                controller = null;
            }
        }
    }

    // Reset về trang 1 khi thay đổi bộ lọc tìm kiếm
    function resetAndLoad() {
        currentPage = 1;
        if (pageInput) pageInput.value = "1";
        load(true, false);
    }

    keyword?.addEventListener("input", () => {
        clearTimeout(timer);
        if (controller) controller.abort();
        ++serial;
        setLoading(false);
        timer = setTimeout(() => resetAndLoad(), 350);
    });

    [category, brand, price, sort].filter(Boolean).forEach(el => el.addEventListener("change", () => resetAndLoad()));
    form.addEventListener("submit", event => { event.preventDefault(); resetAndLoad(); });

    // Bắt sự kiện chuyển trang phân trang mượt mà qua delegation
    results.addEventListener("click", event => {
        const pageBtn = event.target.closest(".emua-page-btn[data-page]");
        if (!pageBtn || pageBtn.classList.contains("disabled") || pageBtn.classList.contains("active")) return;
        event.preventDefault();
        const targetPage = parseInt(pageBtn.dataset.page, 10);
        if (targetPage && targetPage > 0 && targetPage !== currentPage) {
            currentPage = targetPage;
            load(true, true);
        }
    });

    window.addEventListener("popstate", () => {
        const params = new URLSearchParams(location.search);
        if (keyword) keyword.value = params.get("keyword") || "";
        if (category) category.value = params.get("categoryId") || "";
        if (brand) brand.value = params.get("brandId") || "";
        if (price) price.value = params.get("priceRange") || "";
        if (sort) sort.value = params.get("sort") || "";
        currentPage = parseInt(params.get("page") || "1", 10) || 1;
        if (pageInput) pageInput.value = String(currentPage);
        load(false, false);
    });
})();
