(() => {
    "use strict";

    const form = document.querySelector("[data-setup-form]");
    if (!(form instanceof HTMLFormElement)) return;

    const cards = [...form.querySelectorAll("[data-template-card]")];
    const submitButtons = [...form.querySelectorAll('button[type="submit"]')];
    const countOutput = form.querySelector("[data-selected-count]");
    const hoursOutput = form.querySelector("[data-selected-hours]");
    const numberFormat = new Intl.NumberFormat("fa-IR", { maximumFractionDigits: 1 });

    const update = () => {
        let selectedCount = 0;
        let selectedHours = 0;
        cards.forEach(card => {
            const toggle = card.querySelector("[data-template-toggle]");
            const hours = card.querySelector("[data-weekly-hours]");
            const goal = card.querySelector(".goal-input");
            const selected = toggle instanceof HTMLInputElement && toggle.checked;
            card.classList.toggle("selected", selected);
            if (goal instanceof HTMLElement) goal.hidden = !selected;
            if (!selected) return;
            selectedCount += 1;
            if (hours instanceof HTMLInputElement) selectedHours += Number.parseFloat(hours.value) || 0;
        });
        if (countOutput) countOutput.textContent = numberFormat.format(selectedCount);
        if (hoursOutput) hoursOutput.textContent = numberFormat.format(selectedHours);
        submitButtons.forEach(button => {
            button.disabled = selectedCount === 0;
            button.setAttribute("aria-disabled", String(selectedCount === 0));
        });
    };

    form.addEventListener("change", event => {
        if (event.target instanceof HTMLInputElement) update();
    });
    form.querySelector("[data-select-all]")?.addEventListener("click", () => {
        cards.forEach(card => {
            const toggle = card.querySelector("[data-template-toggle]");
            if (toggle instanceof HTMLInputElement) toggle.checked = true;
        });
        update();
    });
    form.querySelector("[data-clear-all]")?.addEventListener("click", () => {
        cards.forEach(card => {
            const toggle = card.querySelector("[data-template-toggle]");
            if (toggle instanceof HTMLInputElement) toggle.checked = false;
        });
        update();
    });
    update();
})();
