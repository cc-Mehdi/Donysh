(() => {
    "use strict";

    const interactionForm = document.getElementById("schedule-interaction-form");
    const scopeDialog = document.getElementById("schedule-scope-dialog");
    const createDialog = document.getElementById("schedule-create-dialog");
    const createForm = createDialog?.querySelector(".schedule-form");
    if (!interactionForm || !scopeDialog || !createDialog || !createForm) return;

    let draggedBlock = null;
    let pendingChange = null;
    let resizeState = null;
    const fields = Object.fromEntries(
        ["PatternId", "OriginalDate", "TargetDate", "StartMinute", "DurationMinutes", "Scope"]
            .map(name => [name, interactionForm.querySelector(`[name="Interaction.${name}"]`)]));
    const dayNames = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    const dayForDate = value => dayNames[new Date(`${value}T12:00:00Z`).getUTCDay()];
    const clamp = (value, minimum, maximum) => Math.min(maximum, Math.max(minimum, value));
    const durationSlots = block => Math.max(1, Number(block.dataset.duration) / 30);

    const openCreate = () => {
        if (!createDialog.open) createDialog.showModal();
    };
    const propose = change => {
        pendingChange = change;
        if (!scopeDialog.open) scopeDialog.showModal();
    };
    const applyEventHeight = block => block.style.setProperty("--event-slots", String(durationSlots(block)));

    document.querySelectorAll(".calendar-event").forEach(block => {
        applyEventHeight(block);
        block.addEventListener("dragstart", event => {
            if (resizeState || event.target.closest("button, input, select, details")) {
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

        const handle = block.querySelector(".schedule-resize-handle");
        handle?.addEventListener("pointerdown", event => {
            if (event.button !== 0) return;
            event.preventDefault();
            const slot = block.closest(".calendar-slot");
            const slotHeight = slot?.getBoundingClientRect().height || 28;
            resizeState = { block, handle, startY: event.clientY, slotHeight, original: Number(block.dataset.duration), duration: Number(block.dataset.duration) };
            handle.setPointerCapture(event.pointerId);
            block.classList.add("resizing");
        });
        handle?.addEventListener("pointermove", event => {
            if (!resizeState || resizeState.handle !== handle) return;
            const deltaSlots = Math.round((event.clientY - resizeState.startY) / resizeState.slotHeight);
            const maximum = 1440 - Number(block.dataset.startMinute);
            resizeState.duration = clamp(resizeState.original + deltaSlots * 30, 30, maximum);
            block.style.setProperty("--event-slots", String(resizeState.duration / 30));
            block.setAttribute("aria-label", `${block.querySelector("strong")?.textContent ?? "برنامه"}، ${resizeState.duration} دقیقه`);
        });
        const finishResize = event => {
            if (!resizeState || resizeState.handle !== handle) return;
            if (handle.hasPointerCapture(event.pointerId)) handle.releasePointerCapture(event.pointerId);
            block.classList.remove("resizing");
            const duration = resizeState.duration;
            const changed = duration !== resizeState.original;
            resizeState = null;
            if (changed) propose({ block, targetDate: block.dataset.scheduledDate, startMinute: Number(block.dataset.startMinute), duration });
            else applyEventHeight(block);
        };
        handle?.addEventListener("pointerup", finishResize);
        handle?.addEventListener("pointercancel", finishResize);
        handle?.addEventListener("keydown", event => {
            if (event.key !== "ArrowUp" && event.key !== "ArrowDown") return;
            event.preventDefault();
            const delta = event.key === "ArrowDown" ? 30 : -30;
            const current = Number(block.dataset.duration);
            const duration = clamp(current + delta, 30, 1440 - Number(block.dataset.startMinute));
            if (duration !== current) propose({ block, targetDate: block.dataset.scheduledDate, startMinute: Number(block.dataset.startMinute), duration });
        });
    });

    document.querySelectorAll(".calendar-slot").forEach(slot => {
        const dayColumn = slot.closest(".calendar-day-column");
        slot.addEventListener("dragover", event => {
            if (!draggedBlock) return;
            event.preventDefault();
            event.dataTransfer.dropEffect = "move";
            slot.classList.add("drop-target");
        });
        slot.addEventListener("dragleave", event => {
            if (!slot.contains(event.relatedTarget)) slot.classList.remove("drop-target");
        });
        slot.addEventListener("drop", event => {
            event.preventDefault();
            slot.classList.remove("drop-target");
            if (!draggedBlock) return;
            propose({ block: draggedBlock, targetDate: dayColumn.dataset.date, startMinute: Number(slot.dataset.minute), duration: Number(draggedBlock.dataset.duration) });
        });
        slot.querySelector(":scope > .time-slot")?.addEventListener("click", () => {
            createForm.querySelector('[name="Input.DayOfWeek"]').value = dayForDate(dayColumn.dataset.date);
            const minute = Number(slot.dataset.minute);
            createForm.querySelector('[name="Input.StartTime"]').value = `${String(Math.floor(minute / 60)).padStart(2, "0")}:${minute % 60 ? "30" : "00"}`;
            document.querySelectorAll(".calendar-slot.selected").forEach(item => item.classList.remove("selected"));
            slot.classList.add("selected");
            openCreate();
            createForm.querySelector('[name="Input.ActivityId"]').focus({ preventScroll: true });
        });
    });

    document.querySelectorAll("[data-open-create]").forEach(button => button.addEventListener("click", openCreate));
    document.querySelectorAll("[data-close-create]").forEach(button => button.addEventListener("click", () => createDialog.close()));
    createDialog.addEventListener("click", event => { if (event.target === createDialog) createDialog.close(); });
    document.querySelectorAll("[data-open-edit]").forEach(button => button.addEventListener("click", () => {
        const dialog = document.getElementById(`schedule-edit-${button.dataset.openEdit}`);
        if (dialog && !dialog.open) dialog.showModal();
    }));
    document.querySelectorAll("[data-close-edit]").forEach(button => button.addEventListener("click", () => button.closest("dialog")?.close()));
    document.querySelectorAll(".schedule-edit-dialog").forEach(dialog =>
        dialog.addEventListener("click", event => { if (event.target === dialog) dialog.close(); }));
    scopeDialog.addEventListener("cancel", () => {
        pendingChange = null;
        document.querySelectorAll(".calendar-event").forEach(applyEventHeight);
    });
    scopeDialog.addEventListener("close", () => {
        if (!pendingChange) return;
        pendingChange = null;
        document.querySelectorAll(".calendar-event").forEach(applyEventHeight);
    });

    scopeDialog.querySelectorAll("[data-scope]").forEach(button => button.addEventListener("click", () => {
        if (!pendingChange) return;
        fields.PatternId.value = pendingChange.block.dataset.patternId;
        fields.OriginalDate.value = pendingChange.block.dataset.originalDate;
        fields.TargetDate.value = pendingChange.targetDate;
        fields.StartMinute.value = String(pendingChange.startMinute);
        fields.DurationMinutes.value = String(pendingChange.duration);
        fields.Scope.value = button.dataset.scope;
        pendingChange = null;
        scopeDialog.close();
        interactionForm.requestSubmit();
    }));

    const todayHeader = document.querySelector(".calendar-days-header .today");
    todayHeader?.scrollIntoView({ inline: "center", block: "nearest" });
    const board = document.querySelector("[data-calendar-board]");
    const starts = [...document.querySelectorAll(".calendar-event")].map(block => Number(block.dataset.startMinute));
    const initialMinute = starts.length ? Math.max(0, Math.min(...starts) - 60) : 7 * 60;
    if (board) board.scrollTop = initialMinute / 30 * (board.querySelector(".calendar-slot")?.getBoundingClientRect().height || 36);

    const now = new Date();
    const localDate = [now.getFullYear(), String(now.getMonth() + 1).padStart(2, "0"), String(now.getDate()).padStart(2, "0")].join("-");
    const todayColumn = document.querySelector(`.calendar-day-column[data-date="${localDate}"]`);
    if (todayColumn) {
        const line = document.createElement("span");
        line.className = "calendar-now-line";
        line.setAttribute("aria-hidden", "true");
        line.style.setProperty("--current-slot", String((now.getHours() * 60 + now.getMinutes()) / 30));
        todayColumn.append(line);
    }
    if (createDialog.querySelector(".validation-summary")) openCreate();
})();
