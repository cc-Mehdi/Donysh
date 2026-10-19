(() => {
    "use strict";

    const interactionForm = document.getElementById("schedule-interaction-form");
    const scopeDialog = document.getElementById("schedule-scope-dialog");
    const createForm = document.querySelector(".schedule-form-panel .schedule-form");
    if (!interactionForm || !scopeDialog || !createForm) return;

    let draggedBlock = null;
    let pendingChange = null;
    const fields = Object.fromEntries(
        ["PatternId", "OriginalDate", "TargetDate", "StartMinute", "DurationMinutes", "Scope"]
            .map(name => [name, interactionForm.querySelector(`[name="Interaction.${name}"]`)]));

    const dayNames = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    const dayForDate = value => dayNames[new Date(`${value}T12:00:00Z`).getUTCDay()];

    const propose = change => {
        pendingChange = change;
        if (typeof scopeDialog.showModal === "function") scopeDialog.showModal();
    };

    document.querySelectorAll(".schedule-block").forEach(block => {
        block.addEventListener("dragstart", event => {
            if (event.target.closest("button, input, select, details")) {
                event.preventDefault();
                return;
            }
            draggedBlock = block;
            block.classList.add("dragging");
            event.dataTransfer.effectAllowed = "move";
            event.dataTransfer.setData("text/plain", block.dataset.patternId);
        });
        block.addEventListener("dragend", () => {
            block.classList.remove("dragging");
            draggedBlock = null;
            document.querySelectorAll(".drop-target").forEach(slot => slot.classList.remove("drop-target"));
        });
        block.querySelectorAll(".resize-block").forEach(button => button.addEventListener("click", () => {
            const duration = Number(block.dataset.duration) + Number(button.dataset.resize);
            propose({ block, targetDate: block.dataset.scheduledDate, startMinute: Number(block.dataset.startMinute), duration });
        }));
    });

    document.querySelectorAll(".time-slot").forEach(slot => {
        const grid = slot.closest(".drop-grid");
        slot.addEventListener("dragover", event => {
            if (!draggedBlock) return;
            event.preventDefault();
            event.dataTransfer.dropEffect = "move";
            slot.classList.add("drop-target");
        });
        slot.addEventListener("dragleave", () => slot.classList.remove("drop-target"));
        slot.addEventListener("drop", event => {
            event.preventDefault();
            slot.classList.remove("drop-target");
            if (!draggedBlock) return;
            propose({ block: draggedBlock, targetDate: grid.dataset.date, startMinute: Number(slot.dataset.minute), duration: Number(draggedBlock.dataset.duration) });
        });
        slot.addEventListener("click", () => {
            createForm.querySelector('[name="Input.DayOfWeek"]').value = dayForDate(grid.dataset.date);
            const minute = Number(slot.dataset.minute);
            createForm.querySelector('[name="Input.StartTime"]').value = `${String(Math.floor(minute / 60)).padStart(2, "0")}:${minute % 60 ? "30" : "00"}`;
            document.querySelectorAll(".time-slot.selected").forEach(item => item.classList.remove("selected"));
            slot.classList.add("selected");
            createForm.querySelector('[name="Input.ActivityId"]').focus({ preventScroll: true });
            document.querySelector(".schedule-form-panel").scrollIntoView({ behavior: "smooth", block: "start" });
        });
    });

    scopeDialog.querySelectorAll("[data-scope]").forEach(button => button.addEventListener("click", () => {
        if (!pendingChange) return;
        fields.PatternId.value = pendingChange.block.dataset.patternId;
        fields.OriginalDate.value = pendingChange.block.dataset.originalDate;
        fields.TargetDate.value = pendingChange.targetDate;
        fields.StartMinute.value = String(pendingChange.startMinute);
        fields.DurationMinutes.value = String(pendingChange.duration);
        fields.Scope.value = button.dataset.scope;
        scopeDialog.close();
        interactionForm.requestSubmit();
    }));
})();
