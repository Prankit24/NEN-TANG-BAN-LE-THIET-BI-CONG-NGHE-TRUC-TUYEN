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
    const sort = document.getElementById("productSort");
    let controller = null;
    let timer = null;
    let serial = 0;

    function urlFromFilters() {
        const url = new URL(form.action, location.origin);
        const params = { keyword: keyword.value.trim(), categoryId: category.value,
                         brandId: brand.value, sort: sort.value };
        for (const [key, value] of Object.entries(params)) {
            if (value) url.searchParams.set(key, value);
        }
        return url;
    }
    function setLoading(loading) {
        results.classList.toggle("emua-results-loading", loading);
        results.setAttribute("aria-busy", String(loading));
        status.textContent = loading ? "Đang cập nhật sản phẩm..." : "";
    }
    async function load(push = true) {
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
            const newCount = fragment.content.querySelectorAll(".emua-product-card").length;
            results.replaceChildren(fragment.content.cloneNode(true));
            count.textContent = String(newCount);
            if (push) history.pushState({ emuaFilter: true }, "", url.pathname + url.search);
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
    keyword.addEventListener("input", () => {
        clearTimeout(timer);
        if (controller) controller.abort();
        ++serial;
        setLoading(false);
        timer = setTimeout(() => load(), 350);
    });
    [category, brand, sort].forEach(el => el.addEventListener("change", () => load()));
    form.addEventListener("submit", event => { event.preventDefault(); load(); });
    window.addEventListener("popstate", () => {
        const params = new URLSearchParams(location.search);
        keyword.value = params.get("keyword") || "";
        category.value = params.get("categoryId") || "";
        brand.value = params.get("brandId") || "";
        sort.value = params.get("sort") || "";
        load(false);
    });
})();
