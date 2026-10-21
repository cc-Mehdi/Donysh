(() => {
    "use strict";

    const script = document.currentScript;
    const statusUrl = script?.dataset.sessionStatusUrl;
    const signedOutUrl = script?.dataset.signedOutUrl;
    if (!statusUrl || !signedOutUrl) return;

    let checking = false;
    let redirected = false;
    let logoutInProgress = false;
    let logoutRecoveryTimer = null;
    const logoutRecoveryDelay = 5000;

    const checkSession = async () => {
        if (checking || redirected || logoutInProgress || document.visibilityState === "hidden") return;
        checking = true;
        try {
            const response = await fetch(statusUrl, {
                method: "GET",
                credentials: "same-origin",
                cache: "no-store",
                headers: { "X-Donysh-Session-Check": "1" }
            });
            if (!logoutInProgress && response.status === 401) {
                redirected = true;
                window.location.replace(signedOutUrl);
            }
        } catch {
            // A transient network failure must not interrupt the current page.
        } finally {
            checking = false;
        }
    };

    document.querySelectorAll("form[data-logout-form]").forEach(form => {
        form.addEventListener("submit", () => {
            // Keep the initiating navigation alive until the central and peer
            // product logout endpoints have both completed.
            logoutInProgress = true;
            if (logoutRecoveryTimer !== null) window.clearTimeout(logoutRecoveryTimer);
            logoutRecoveryTimer = window.setTimeout(() => {
                logoutRecoveryTimer = null;
                logoutInProgress = false;
                void checkSession();
            }, logoutRecoveryDelay);
        }, { once: true });
    });
    document.addEventListener("visibilitychange", () => {
        if (document.visibilityState === "visible") void checkSession();
    });
    window.addEventListener("focus", checkSession);
    window.addEventListener("pageshow", event => {
        // Recover when the original page returns from the back/forward cache.
        if (event.persisted) logoutInProgress = false;
        void checkSession();
    });
    window.setInterval(checkSession, 15000);
})();
