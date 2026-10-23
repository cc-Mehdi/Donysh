(() => {
    "use strict";

    const interactionForm = document.getElementById("schedule-interaction-form");
    const scopeDialog = document.getElementById("schedule-scope-dialog");
    const createDialog = document.getElementById("schedule-create-dialog");
    const createForm = createDialog?.querySelector(".schedule-form");
    if (!interactionForm || !scopeDialog || !createDialog || !createForm) return;

    let draggedBlock = null;
    let dragPreview = null;
    let pendingChange = null;
    let resizeState = null;
    const fields = Object.fromEntries(
        ["PatternId", "OriginalDate", "TargetDate", "StartMinute", "DurationMinutes", "Scope"]
            .map(name => [name, interactionForm.querySelector(`[name="Interaction.${name}"]`)]));
    const dayNames = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    const dayForDate = value => dayNames[new Date(`${value}T12:00:00Z`).getUTCDay()];
    const clamp = (value, minimum, maximum) => Math.min(maximum, Math.max(minimum, value));
    const formatMinute = minute => `${String(Math.floor((minute % 1440) / 60)).padStart(2, "0")}:${String(minute % 60).padStart(2, "0")}`;
    const durationSlots = block => Math.max(1, Number(block.dataset.displayDuration || block.dataset.duration) / 30);

    const layoutOverlaps = () => {
        document.querySelectorAll(".calendar-day-column").forEach(day => {
            const entries = [...day.querySelectorAll(".calendar-event")].map(element => ({
                element, start: Number(element.dataset.displayStartMinute),
                end: Number(element.dataset.displayStartMinute) + Number(element.dataset.displayDuration)
            })).sort((a, b) => a.start - b.start || b.end - a.end);
            let group = [], groupEnd = -1, lanes = [];
            const finish = () => {
                const count = Math.max(1, lanes.length);
                group.forEach(item => {
                    item.element.style.setProperty("--overlap-index", String(item.lane));
                    item.element.style.setProperty("--overlap-count", String(count));
                    item.element.style.setProperty("--overlap-width", `${100 / count}%`);
                    item.element.style.setProperty("--overlap-offset", `${100 * item.lane / count}%`);
                    item.element.classList.toggle("is-overlapping", count > 1);
                });
            };
            for (const entry of entries) {
                if (entry.start >= groupEnd) { finish(); group = []; lanes = []; }
                const free = lanes.findIndex(end => end <= entry.start);
                entry.lane = free < 0 ? lanes.length : free;
                lanes[entry.lane] = entry.end;
                group.push(entry);
                groupEnd = Math.max(groupEnd, entry.end);
            }
            finish();
        });
    };

    const updateTimeLabel = (block, duration) => {
        const start = Number(block.dataset.startMinute);
        const label = block.querySelector(".block-time");
        if (label) label.textContent = `${formatMinute(start)} تا ${formatMinute(start + duration)}${start + duration >= 1440 ? " فردا" : ""}`;
    };
    const resetBlock = block => {
        block.style.setProperty("--event-slots", String(durationSlots(block)));
        updateTimeLabel(block, Number(block.dataset.duration));
    };

    const timeToMinute = value => /^\d{2}:\d{2}$/.test(value) ? Number(value.slice(0, 2)) * 60 + Number(value.slice(3, 5)) : null;
    document.querySelectorAll(".schedule-form").forEach(form => {
        const start = form.querySelector('[name="Input.StartTime"]');
        const end = form.querySelector('[name="Input.EndTime"]');
        const duration = form.querySelector('[name="Input.DurationMinutes"]');
        const hint = form.querySelector("[data-end-day-hint]");
        if (!start || !end || !duration || !hint) return;
        const update = () => {
            const startMinute = timeToMinute(start.value), endMinute = timeToMinute(end.value);
            if (startMinute === null || endMinute === null) return;
            const overnight = endMinute <= startMinute;
            duration.value = String(endMinute + (overnight ? 1440 : 0) - startMinute);
            hint.textContent = overnight ? "پایان در روز بعد · یک برنامهٔ پیوسته" : "شروع و پایان در همین روز";
            hint.classList.toggle("overnight", overnight);
        };
        start.addEventListener("input", update);
        end.addEventListener("input", update);
        update();
    });

    const openCreate = () => {
        if (!createDialog.open) createDialog.showModal();
    };
    const propose = change => {
        pendingChange = change;
        if (!scopeDialog.open) scopeDialog.showModal();
    };
    const applyEventHeight = block => block.style.setProperty("--event-slots", String(durationSlots(block)));

    layoutOverlaps();
    document.querySelectorAll(".schedule-continuation").forEach(applyEventHeight);

    document.querySelectorAll('.calendar-event[draggable="true"]').forEach(block => {
        applyEventHeight(block);
        block.addEventListener("dragstart", event => {
            if (resizeState || event.target.closest("button, input, select, details")) {
                event.preventDefault();
                return;
            }
            draggedBlock = block;
            block.classList.add("dragging");
            dragPreview = document.createElement("div");
            dragPreview.className = "calendar-drag-preview";
            dragPreview.setAttribute("aria-hidden", "true");
            event.dataTransfer.effectAllowed = "move";
            event.dataTransfer.setData("text/plain", block.dataset.patternId);
        });
        block.addEventListener("dragend", () => {
            block.classList.remove("dragging");
            draggedBlock = null;
            dragPreview?.remove();
            dragPreview = null;
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
            const maximum = 1440;
            resizeState.duration = clamp(resizeState.original + deltaSlots * 30, 30, maximum);
            block.style.setProperty("--event-slots", String(Math.min(resizeState.duration, 1440 - Number(block.dataset.startMinute)) / 30));
            updateTimeLabel(block, resizeState.duration);
            block.setAttribute("aria-label", `${block.querySelector("strong")?.textContent ?? "برنامه"}، ${resizeState.duration} دقیقه`);
        });
        const finishResize = event => {
            if (!resizeState || resizeState.handle !== handle) return;
            if (handle.hasPointerCapture(event.pointerId)) handle.releasePointerCapture(event.pointerId);
            block.classList.remove("resizing");
            const duration = resizeState.duration;
            const changed = duration !== resizeState.original;
            resizeState = null;
            if (changed && event.type !== "pointercancel") propose({ block, targetDate: block.dataset.scheduledDate, startMinute: Number(block.dataset.startMinute), duration });
            else resetBlock(block);
        };
        handle?.addEventListener("pointerup", finishResize);
        handle?.addEventListener("pointercancel", finishResize);
        handle?.addEventListener("keydown", event => {
            if (event.key !== "ArrowUp" && event.key !== "ArrowDown") return;
            event.preventDefault();
            const delta = event.key === "ArrowDown" ? 30 : -30;
            const current = Number(block.dataset.duration);
            const duration = clamp(current + delta, 30, 1440);
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
            if (dragPreview) {
                const minute = Number(slot.dataset.minute), duration = Number(draggedBlock.dataset.duration);
                dragPreview.textContent = `${draggedBlock.querySelector("strong")?.textContent ?? "برنامه"} · ${formatMinute(minute)} تا ${formatMinute(minute + duration)}${minute + duration >= 1440 ? " فردا" : ""}`;
                dragPreview.style.setProperty("--preview-slots", String(Math.min(duration, 1440 - minute) / 30));
                slot.append(dragPreview);
            }
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
            createForm.querySelector('[name="Input.EndTime"]').value = formatMinute(minute + 60);
            createForm.querySelector('[name="Input.StartTime"]').dispatchEvent(new Event("input", { bubbles: true }));
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
        document.querySelectorAll('.calendar-event[draggable="true"]').forEach(resetBlock);
    });
    scopeDialog.addEventListener("close", () => {
        if (!pendingChange) return;
        pendingChange = null;
        document.querySelectorAll('.calendar-event[draggable="true"]').forEach(resetBlock);
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
    const starts = [...document.querySelectorAll(".calendar-event")].map(block => Number(block.dataset.displayStartMinute));
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
