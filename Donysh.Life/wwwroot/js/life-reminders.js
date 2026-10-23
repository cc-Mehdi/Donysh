(() => {
    "use strict";

    const button = document.querySelector("[data-enable-reminders]");
    const permission = () => "Notification" in window ? Notification.permission : "unsupported";
    const updateButton = () => {
        if (!button) return;
        const label = button.querySelector("span");
        const enabled = permission() === "granted";
        if (label) label.textContent = enabled ? "اعلان‌ها فعال‌اند" : "فعال‌سازی اعلان‌ها";
        button.setAttribute("aria-label", enabled ? "اعلان‌های برنامه‌ها فعال‌اند" : "فعال‌سازی اعلان‌های برنامه‌ها");
        button.title = button.getAttribute("aria-label");
    };
    let workerPromise;
    const worker = () => {
        if (!("serviceWorker" in navigator)) return Promise.resolve(null);
        workerPromise ??= navigator.serviceWorker.register("/life-reminder-worker.js", { scope: "/" })
            .then(() => navigator.serviceWorker.ready).catch(() => null);
        return workerPromise;
    };
    const toast = (title, message) => {
        const notice = document.createElement("div");
        notice.className = "life-reminder-toast";
        notice.setAttribute("role", "status");
        const heading = document.createElement("strong");
        heading.textContent = title;
        const copy = document.createElement("span");
        copy.textContent = message;
        notice.append(heading, copy);
        document.body.append(notice);
        window.setTimeout(() => notice.remove(), 12000);
    };

    button?.addEventListener("click", async () => {
        if (permission() === "unsupported") {
            toast("اعلان مرورگر در دسترس نیست", "یادآورها را هنگام باز بودن این صفحه در خود دنیش می‌بینی.");
            return;
        }
        if (permission() === "default") await Notification.requestPermission();
        updateButton();
        if (permission() === "granted") await worker();
        if (permission() === "denied") toast("اعلان‌ها بسته‌اند", "دسترسی اعلان را در تنظیمات این سایت در مرورگر فعال کن.");
    });
    updateButton();
    if (permission() === "granted") worker();

    let reminders = [];
    const deliveredKey = "donysh-life-delivered-reminders";
    const delivered = (() => {
        try {
            const value = JSON.parse(localStorage.getItem(deliveredKey) || "{}");
            return value && typeof value === "object" && !Array.isArray(value) ? value : {};
        }
        catch { return {}; }
    })();
    const persist = () => {
        const cutoff = Date.now() - 8 * 86400000;
        for (const [id, time] of Object.entries(delivered)) if (time < cutoff) delete delivered[id];
        try { localStorage.setItem(deliveredKey, JSON.stringify(delivered)); } catch { /* Private browsing may block storage. */ }
    };
    const deliver = async (item) => {
        const message = `${item.title} · ساعت ${item.startLabel}`;
        if (permission() === "granted") {
            try {
                const registration = await worker();
                if (registration) {
                    await registration.showNotification("یادآور دنیش زندگی", { body: message, tag: item.id, icon: "/images/donysh-life-icon.png" });
                    return true;
                }
                const notification = new Notification("یادآور دنیش زندگی", { body: message, tag: item.id, icon: "/images/donysh-life-icon.png" });
                notification.onclick = () => { window.focus(); window.location.href = "/Week"; };
                return true;
            } catch { /* Some mobile browsers only support service-worker notifications. */ }
        }
        if (document.hidden) return false;
        toast("یادآور دنیش زندگی", message);
        return true;
    };
    let checking = false;
    const tick = async () => {
        if (checking) return;
        checking = true;
        try {
        const now = Date.now();
        for (const item of reminders) {
            const due = Date.parse(item.remindAtUtc);
            if (!item.id || !Number.isFinite(due) || due > now || due < now - 300000 || delivered[item.id]) continue;
            if (await deliver(item)) delivered[item.id] = now;
        }
        persist();
        } finally { checking = false; }
    };
    const refresh = async () => {
        try {
            const response = await fetch("/Week?handler=DueReminders", { credentials: "same-origin", cache: "no-store" });
            if (!response.ok) return;
            const payload = await response.json();
            reminders = Array.isArray(payload) ? payload : [];
            tick();
        } catch { /* The next refresh retries after a temporary network failure. */ }
    };

    refresh();
    window.setInterval(tick, 20000);
    window.setInterval(refresh, 300000);
    document.addEventListener("visibilitychange", () => { if (!document.hidden) refresh(); });
})();
