(() => {
    "use strict";

    const resetFeedback = () => {
        document.body.classList.remove("is-navigating");
        document.querySelector("main")?.removeAttribute("aria-busy");
        document.querySelectorAll("form[data-submitting='true']").forEach(form => {
            delete form.dataset.submitting;
            form.classList.remove("is-submitting");
            form.removeAttribute("aria-busy");
            form.querySelectorAll("button[data-original-text]").forEach(button => {
                button.textContent = button.dataset.originalText;
                delete button.dataset.originalText;
            });
        });
    };

    window.addEventListener("pageshow", resetFeedback);

    document.addEventListener("submit", event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || form.method.toLowerCase() === "dialog") return;
        if (form.hasAttribute("data-native-submit")) return;
        if (form.dataset.submitting === "true") {
            event.preventDefault();
            return;
        }

        form.dataset.submitting = "true";
        form.classList.add("is-submitting");
        form.setAttribute("aria-busy", "true");
        const submitter = event.submitter;
        if (submitter instanceof HTMLButtonElement) {
            submitter.dataset.originalText = submitter.textContent ?? "";
            submitter.textContent = submitter.dataset.busyText || "در حال ثبت…";
        }
    });

    document.addEventListener("click", event => {
        if (!(event.target instanceof Element)) return;
        const link = event.target.closest("a[href]");
        if (!link || event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
        const target = new URL(link.href, window.location.href);
        if (target.origin !== window.location.origin || link.hasAttribute("download") || link.target === "_blank" || target.hash) return;
        document.body.classList.add("is-navigating");
        document.querySelector("main")?.setAttribute("aria-busy", "true");
    });
})();
