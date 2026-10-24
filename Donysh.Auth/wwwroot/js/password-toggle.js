document.querySelectorAll("[data-password-toggle]").forEach((button) => {
    const inputId = button.getAttribute("aria-controls");
    const input = inputId ? document.getElementById(inputId) : null;
    if (!(input instanceof HTMLInputElement)) return;

    button.addEventListener("click", () => {
        const shouldShow = input.type === "password";
        input.type = shouldShow ? "text" : "password";
        button.setAttribute("aria-pressed", shouldShow ? "true" : "false");
        button.setAttribute("aria-label", shouldShow ? "پنهان کردن رمز عبور" : "نمایش رمز عبور");
        if (!button.querySelector("svg")) button.textContent = shouldShow ? "پنهان" : "نمایش";

        const openEye = button.querySelector("[data-eye-open]");
        const closedEye = button.querySelector("[data-eye-closed]");
        if (openEye) openEye.hidden = shouldShow;
        if (closedEye) closedEye.hidden = !shouldShow;
    });
});

document.querySelectorAll("[data-new-password]").forEach((input) => {
    const rules = input.closest("form")?.querySelector("[data-password-rules]");
    if (!rules) return;
    const checks = {
        length: (value) => value.length >= 8,
        letter: (value) => /[a-z]/.test(value),
        digit: (value) => /[0-9]/.test(value)
    };
    const update = () => {
        for (const [name, check] of Object.entries(checks)) {
            const item = rules.querySelector(`[data-password-rule="${name}"]`);
            if (item) item.classList.toggle("is-met", check(input.value));
        }
    };
    input.addEventListener("input", update);
    update();
});
