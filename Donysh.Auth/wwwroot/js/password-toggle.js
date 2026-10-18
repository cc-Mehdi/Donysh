document.querySelectorAll("[data-password-toggle]").forEach((button) => {
    const inputId = button.getAttribute("aria-controls");
    const input = inputId ? document.getElementById(inputId) : null;
    if (!(input instanceof HTMLInputElement)) return;

    button.addEventListener("click", () => {
        const shouldShow = input.type === "password";
        input.type = shouldShow ? "text" : "password";
        button.setAttribute("aria-pressed", shouldShow ? "true" : "false");
        button.setAttribute("aria-label", shouldShow ? "پنهان کردن رمز عبور" : "نمایش رمز عبور");

        const openEye = button.querySelector("[data-eye-open]");
        const closedEye = button.querySelector("[data-eye-closed]");
        if (openEye) openEye.hidden = shouldShow;
        if (closedEye) closedEye.hidden = !shouldShow;
    });
});
