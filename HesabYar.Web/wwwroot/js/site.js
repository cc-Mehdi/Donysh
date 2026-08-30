(() => {
    const toLatinDigits = (value) => String(value ?? '')
        .replace(/[۰-۹]/g, (digit) => String('۰۱۲۳۴۵۶۷۸۹'.indexOf(digit)))
        .replace(/[٠-٩]/g, (digit) => String('٠١٢٣٤٥٦٧٨٩'.indexOf(digit)));

    const toPersianDigits = (value) => String(value ?? '').replace(/\d/g, (digit) => '۰۱۲۳۴۵۶۷۸۹'[Number(digit)]);

    const formatMoney = (value) => {
        const digits = toLatinDigits(value).replace(/[^0-9]/g, '').replace(/^0+(?=\d)/, '');
        if (!digits) return '';
        return digits.replace(/\B(?=(\d{3})+(?!\d))/g, ',');
    };

    document.querySelectorAll('[data-money]').forEach((input) => {
        input.value = formatMoney(input.value);
        input.addEventListener('input', () => {
            const cursorAtEnd = input.selectionStart === input.value.length;
            input.value = formatMoney(input.value);
            if (cursorAtEnd) input.setSelectionRange(input.value.length, input.value.length);
        });
        input.addEventListener('blur', () => {
            if (input.dataset.allowZero === 'true' && input.value === '') input.value = '0';
        });
    });

    const formatPersianDateInput = (value) => {
        const digits = toLatinDigits(value).replace(/[^0-9]/g, '').slice(0, 8);
        if (digits.length <= 4) return digits;
        if (digits.length <= 6) return `${digits.slice(0, 4)}/${digits.slice(4)}`;
        return `${digits.slice(0, 4)}/${digits.slice(4, 6)}/${digits.slice(6)}`;
    };

    /*
      Dependency-free Jalali conversion based on the arithmetic rules of the
      Persian calendar. Keeping it local makes the date picker work without a CDN.
    */
    const div = (a, b) => Math.trunc(a / b);
    const mod = (a, b) => a - Math.trunc(a / b) * b;

    const jalCal = (jy, withoutLeap = false) => {
        const breaks = [-61, 9, 38, 199, 426, 686, 756, 818, 1111, 1181, 1210, 1635, 2060, 2097, 2192, 2262, 2324, 2394, 2456, 3178];
        const bl = breaks.length;
        const gy = jy + 621;
        let leapJ = -14;
        let jp = breaks[0];
        let jm = 0;
        let jump = 0;

        if (jy < jp || jy >= breaks[bl - 1]) throw new RangeError(`Invalid Jalali year ${jy}`);

        for (let i = 1; i < bl; i += 1) {
            jm = breaks[i];
            jump = jm - jp;
            if (jy < jm) break;
            leapJ += div(jump, 33) * 8 + div(mod(jump, 33), 4);
            jp = jm;
        }

        let n = jy - jp;
        leapJ += div(n, 33) * 8 + div(mod(n, 33) + 3, 4);
        if (mod(jump, 33) === 4 && jump - n === 4) leapJ += 1;

        const leapG = div(gy, 4) - div((div(gy, 100) + 1) * 3, 4) - 150;
        const march = 20 + leapJ - leapG;

        if (withoutLeap) return { gy, march };

        if (jump - n < 6) n = n - jump + div(jump + 4, 33) * 33;
        let leap = mod(mod(n + 1, 33) - 1, 4);
        if (leap === -1) leap = 4;

        return { leap, gy, march };
    };

    const g2d = (gy, gm, gd) => {
        let d = div((gy + div(gm - 8, 6) + 100100) * 1461, 4)
            + div(153 * mod(gm + 9, 12) + 2, 5)
            + gd - 34840408;
        d = d - div(div(gy + 100100 + div(gm - 8, 6), 100) * 3, 4) + 752;
        return d;
    };

    const d2g = (jdn) => {
        let j = 4 * jdn + 139361631;
        j = j + div(div(4 * jdn + 183187720, 146097) * 3, 4) * 4 - 3908;
        const i = div(mod(j, 1461), 4) * 5 + 308;
        const gd = div(mod(i, 153), 5) + 1;
        const gm = mod(div(i, 153), 12) + 1;
        const gy = div(j, 1461) - 100100 + div(8 - gm, 6);
        return { gy, gm, gd };
    };

    const j2d = (jy, jm, jd) => {
        const result = jalCal(jy, true);
        return g2d(result.gy, 3, result.march)
            + (jm - 1) * 31
            - div(jm, 7) * (jm - 7)
            + jd - 1;
    };

    const d2j = (jdn) => {
        const gregorian = d2g(jdn);
        let jy = gregorian.gy - 621;
        const result = jalCal(jy);
        const firstFarvardin = g2d(gregorian.gy, 3, result.march);
        let k = jdn - firstFarvardin;
        let jm;
        let jd;

        if (k >= 0) {
            if (k <= 185) {
                jm = 1 + div(k, 31);
                jd = mod(k, 31) + 1;
                return { jy, jm, jd };
            }
            k -= 186;
        } else {
            jy -= 1;
            k += 179;
            if (result.leap === 1) k += 1;
        }

        jm = 7 + div(k, 30);
        jd = mod(k, 30) + 1;
        return { jy, jm, jd };
    };

    const toJalali = (gy, gm, gd) => d2j(g2d(gy, gm, gd));
    const toGregorian = (jy, jm, jd) => d2g(j2d(jy, jm, jd));
    const isLeapJalaliYear = (jy) => jalCal(jy).leap === 0;
    const getJalaliMonthLength = (jy, jm) => {
        if (jm <= 6) return 31;
        if (jm <= 11) return 30;
        return isLeapJalaliYear(jy) ? 30 : 29;
    };

    const parsePersianDate = (value) => {
        const normalized = formatPersianDateInput(value);
        const match = /^(\d{4})\/(\d{2})\/(\d{2})$/.exec(normalized);
        if (!match) return null;

        const jy = Number(match[1]);
        const jm = Number(match[2]);
        const jd = Number(match[3]);

        if (jy < minPickerYear || jy > maxPickerYear || jm < 1 || jm > 12) return null;
        if (jd < 1 || jd > getJalaliMonthLength(jy, jm)) return null;
        return { jy, jm, jd };
    };

    const formatJalaliDate = ({ jy, jm, jd }) => `${String(jy).padStart(4, '0')}/${String(jm).padStart(2, '0')}/${String(jd).padStart(2, '0')}`;
    const sameJalaliDate = (left, right) => Boolean(left && right && left.jy === right.jy && left.jm === right.jm && left.jd === right.jd);

    const getTodayJalali = () => {
        const today = new Date();
        return toJalali(today.getFullYear(), today.getMonth() + 1, today.getDate());
    };

    const jalaliWeekdayIndex = (jy, jm, jd) => {
        const gregorian = toGregorian(jy, jm, jd);
        const day = new Date(gregorian.gy, gregorian.gm - 1, gregorian.gd, 12).getDay();
        return (day + 1) % 7; // Saturday = 0, Friday = 6
    };

    const minPickerYear = 1200;
    const maxPickerYear = 1600;

    const persianMonthNames = [
        'فروردین', 'اردیبهشت', 'خرداد', 'تیر', 'مرداد', 'شهریور',
        'مهر', 'آبان', 'آذر', 'دی', 'بهمن', 'اسفند'
    ];
    const persianWeekdays = ['ش', 'ی', 'د', 'س', 'چ', 'پ', 'ج'];

    let activeDatePicker = null;

    const closeActiveDatePicker = () => {
        if (!activeDatePicker) return;
        activeDatePicker.close();
        activeDatePicker = null;
    };

    const createPersianDatePicker = (input) => {
        input.value = formatPersianDateInput(input.value);
        input.classList.add('persian-datepicker-input');
        input.setAttribute('aria-haspopup', 'dialog');
        input.setAttribute('aria-expanded', 'false');

        const wrapper = document.createElement('div');
        wrapper.className = 'persian-datepicker-control';
        input.parentNode.insertBefore(wrapper, input);
        wrapper.appendChild(input);

        const trigger = document.createElement('button');
        trigger.type = 'button';
        trigger.className = 'persian-datepicker-trigger';
        trigger.setAttribute('aria-label', 'انتخاب تاریخ شمسی');
        trigger.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 2v3M17 2v3M3.5 9h17M5.5 4h13a2 2 0 0 1 2 2v13a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2Z"/><path d="M8 13h.01M12 13h.01M16 13h.01M8 17h.01M12 17h.01M16 17h.01"/></svg>';
        wrapper.appendChild(trigger);

        const popover = document.createElement('div');
        popover.className = 'persian-datepicker-popover hidden';
        popover.setAttribute('role', 'dialog');
        popover.setAttribute('aria-modal', 'false');
        popover.setAttribute('aria-label', 'تقویم شمسی');
        popover.innerHTML = `
      <div class="persian-datepicker-header">
        <button type="button" class="persian-datepicker-nav" data-pdp-prev aria-label="ماه قبل">›</button>
        <div class="persian-datepicker-selects">
          <select class="persian-datepicker-select" data-pdp-month aria-label="ماه"></select>
          <select class="persian-datepicker-select" data-pdp-year aria-label="سال"></select>
        </div>
        <button type="button" class="persian-datepicker-nav" data-pdp-next aria-label="ماه بعد">‹</button>
      </div>
      <div class="persian-datepicker-weekdays" data-pdp-weekdays></div>
      <div class="persian-datepicker-days" data-pdp-days></div>
      <div class="persian-datepicker-footer">
        <button type="button" class="persian-datepicker-action" data-pdp-today>امروز</button>
        <button type="button" class="persian-datepicker-action persian-datepicker-clear" data-pdp-clear>پاک کردن</button>
      </div>`;
        document.body.appendChild(popover);

        const monthSelect = popover.querySelector('[data-pdp-month]');
        const yearSelect = popover.querySelector('[data-pdp-year]');
        const weekdaysElement = popover.querySelector('[data-pdp-weekdays]');
        const daysElement = popover.querySelector('[data-pdp-days]');
        const previousButton = popover.querySelector('[data-pdp-prev]');
        const nextButton = popover.querySelector('[data-pdp-next]');
        const clearButton = popover.querySelector('[data-pdp-clear]');

        persianMonthNames.forEach((name, index) => {
            const option = document.createElement('option');
            option.value = String(index + 1);
            option.textContent = name;
            monthSelect.appendChild(option);
        });

        for (let year = minPickerYear; year <= maxPickerYear; year += 1) {
            const option = document.createElement('option');
            option.value = String(year);
            option.textContent = toPersianDigits(year);
            yearSelect.appendChild(option);
        }

        persianWeekdays.forEach((weekday) => {
            const item = document.createElement('span');
            item.textContent = weekday;
            weekdaysElement.appendChild(item);
        });

        let viewDate = parsePersianDate(input.value) || getTodayJalali();

        const setInputValue = (date) => {
            input.value = date ? formatJalaliDate(date) : '';
            input.dispatchEvent(new Event('input', { bubbles: true }));
            input.dispatchEvent(new Event('change', { bubbles: true }));
        };

        const render = () => {
            monthSelect.value = String(viewDate.jm);
            yearSelect.value = String(viewDate.jy);
            clearButton.classList.toggle('hidden', input.required);
            previousButton.disabled = viewDate.jy === minPickerYear && viewDate.jm === 1;
            nextButton.disabled = viewDate.jy === maxPickerYear && viewDate.jm === 12;
            daysElement.replaceChildren();

            const selectedDate = parsePersianDate(input.value);
            const today = getTodayJalali();
            const firstWeekday = jalaliWeekdayIndex(viewDate.jy, viewDate.jm, 1);
            const monthLength = getJalaliMonthLength(viewDate.jy, viewDate.jm);

            for (let index = 0; index < firstWeekday; index += 1) {
                const placeholder = document.createElement('span');
                placeholder.className = 'persian-datepicker-empty-day';
                placeholder.setAttribute('aria-hidden', 'true');
                daysElement.appendChild(placeholder);
            }

            for (let day = 1; day <= monthLength; day += 1) {
                const date = { jy: viewDate.jy, jm: viewDate.jm, jd: day };
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'persian-datepicker-day';
                button.textContent = toPersianDigits(day);
                button.setAttribute('aria-label', `${toPersianDigits(day)} ${persianMonthNames[viewDate.jm - 1]} ${toPersianDigits(viewDate.jy)}`);

                if (sameJalaliDate(date, today)) button.classList.add('persian-datepicker-day-today');
                if (sameJalaliDate(date, selectedDate)) {
                    button.classList.add('persian-datepicker-day-selected');
                    button.setAttribute('aria-current', 'date');
                }

                button.addEventListener('click', () => {
                    setInputValue(date);
                    api.close();
                    input.focus();
                });
                daysElement.appendChild(button);
            }
        };

        const positionPopover = () => {
            const rect = wrapper.getBoundingClientRect();
            const viewportPadding = 8;
            const width = Math.min(320, window.innerWidth - viewportPadding * 2);
            popover.style.width = `${width}px`;
            popover.style.left = `${Math.min(Math.max(viewportPadding, rect.left), window.innerWidth - width - viewportPadding)}px`;

            const height = popover.offsetHeight;
            const belowTop = rect.bottom + 8;
            const aboveTop = rect.top - height - 8;
            const top = belowTop + height <= window.innerHeight - viewportPadding || aboveTop < viewportPadding
                ? belowTop
                : aboveTop;
            popover.style.top = `${Math.max(viewportPadding, top)}px`;
        };

        const api = {
            open() {
                if (activeDatePicker && activeDatePicker !== api) activeDatePicker.close();
                activeDatePicker = api;
                viewDate = parsePersianDate(input.value) || getTodayJalali();
                render();
                popover.classList.remove('hidden');
                input.setAttribute('aria-expanded', 'true');
                trigger.classList.add('persian-datepicker-trigger-active');
                requestAnimationFrame(positionPopover);
            },
            close() {
                popover.classList.add('hidden');
                input.setAttribute('aria-expanded', 'false');
                trigger.classList.remove('persian-datepicker-trigger-active');
                if (activeDatePicker === api) activeDatePicker = null;
            },
            contains(target) {
                return wrapper.contains(target) || popover.contains(target);
            },
            reposition: positionPopover
        };

        input.addEventListener('input', () => {
            input.value = formatPersianDateInput(input.value);
            input.setSelectionRange(input.value.length, input.value.length);
            if (!popover.classList.contains('hidden')) {
                const parsed = parsePersianDate(input.value);
                if (parsed) viewDate = parsed;
                render();
            }
        });

        input.addEventListener('keydown', (event) => {
            if (event.key === 'ArrowDown' && event.altKey) {
                event.preventDefault();
                api.open();
            }
            if (event.key === 'Escape') api.close();
        });

        trigger.addEventListener('click', () => {
            if (popover.classList.contains('hidden')) api.open();
            else api.close();
        });

        previousButton.addEventListener('click', () => {
            if (previousButton.disabled) return;
            if (viewDate.jm === 1) viewDate = { jy: viewDate.jy - 1, jm: 12, jd: 1 };
            else viewDate = { jy: viewDate.jy, jm: viewDate.jm - 1, jd: 1 };
            render();
        });

        nextButton.addEventListener('click', () => {
            if (nextButton.disabled) return;
            if (viewDate.jm === 12) viewDate = { jy: viewDate.jy + 1, jm: 1, jd: 1 };
            else viewDate = { jy: viewDate.jy, jm: viewDate.jm + 1, jd: 1 };
            render();
        });

        monthSelect.addEventListener('change', () => {
            viewDate = { jy: viewDate.jy, jm: Number(monthSelect.value), jd: 1 };
            render();
        });

        yearSelect.addEventListener('change', () => {
            viewDate = { jy: Number(yearSelect.value), jm: viewDate.jm, jd: 1 };
            render();
        });

        popover.querySelector('[data-pdp-today]').addEventListener('click', () => {
            const today = getTodayJalali();
            setInputValue(today);
            api.close();
            input.focus();
        });

        clearButton.addEventListener('click', () => {
            setInputValue(null);
            api.close();
            input.focus();
        });

        return api;
    };

    const datePickers = [...document.querySelectorAll('[data-persian-date]')].map(createPersianDatePicker);

    document.addEventListener('pointerdown', (event) => {
        if (activeDatePicker && !activeDatePicker.contains(event.target)) closeActiveDatePicker();
    });

    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') closeActiveDatePicker();
    });

    window.addEventListener('resize', () => activeDatePicker?.reposition());
    window.addEventListener('scroll', () => activeDatePicker?.reposition(), true);

    // Exposed for lightweight diagnostics and future UI integrations.
    window.HesabYarPersianDate = {
        toJalali,
        toGregorian,
        parse: parsePersianDate,
        format: formatJalaliDate,
        pickers: datePickers
    };

    const sidebar = document.getElementById('sidebar');
    document.querySelectorAll('[data-open-sidebar]').forEach((button) => {
        button.addEventListener('click', () => sidebar?.classList.remove('hidden'));
    });
    document.querySelectorAll('[data-close-sidebar]').forEach((button) => {
        button.addEventListener('click', () => sidebar?.classList.add('hidden'));
    });

    document.querySelectorAll('[data-confirm]').forEach((element) => {
        element.addEventListener('click', (event) => {
            const message = element.getAttribute('data-confirm') || 'آیا مطمئن هستید؟';
            if (!window.confirm(message)) event.preventDefault();
        });
    });

    document.querySelectorAll('[data-obligation-form]').forEach((form) => {
        const typeInput = form.querySelector('[data-obligation-type]');
        const durationGroup = form.querySelector('[data-obligation-duration]');
        const durationInput = form.querySelector('[data-obligation-duration-input]');
        if (!typeInput || !durationGroup || !durationInput) return;

        const syncDuration = () => {
            const isInstallment = typeInput.value === '1';
            durationGroup.hidden = !isInstallment;
            durationInput.disabled = !isInstallment;
            durationInput.required = isInstallment;
        };

        typeInput.addEventListener('change', syncDuration);
        syncDuration();
    });

    document.querySelectorAll('[data-icon-picker]').forEach((picker) => {
        const choices = [...picker.querySelectorAll('[data-icon-choice]')];
        const custom = picker.querySelector('[data-icon-custom]');
        const segmenter = 'Segmenter' in Intl
            ? new Intl.Segmenter(undefined, { granularity: 'grapheme' })
            : null;
        choices.forEach((choice) => choice.addEventListener('change', () => {
            if (choice.checked && custom) custom.value = '';
        }));
        custom?.addEventListener('input', () => {
            if (!custom.value.trim()) return;
            const characters = segmenter
                ? [...segmenter.segment(custom.value)].map((item) => item.segment)
                : Array.from(custom.value);
            if (characters.length > 1) custom.value = characters[0];
            choices.forEach((choice) => { choice.checked = false; });
        });
    });

    document.querySelectorAll('[data-ai-workspace-report]').forEach((row) => {
        const checkbox = row.querySelector('[data-ai-workspace-check]');
        const dates = row.querySelector('[data-ai-workspace-dates]');
        const sync = () => {
            const selected = checkbox?.checked === true;
            row.classList.toggle('border-indigo-300', selected);
            row.classList.toggle('bg-indigo-50', selected);
            row.classList.toggle('border-slate-200', !selected);
            row.classList.toggle('bg-slate-50', !selected);
            dates?.classList.toggle('opacity-50', !selected);
        };
        checkbox?.addEventListener('change', sync);
        sync();
    });

    const aiPreviewForm = document.querySelector('[data-ai-preview-form]');
    aiPreviewForm?.addEventListener('submit', () => {
        const submitButton = aiPreviewForm.querySelector('[data-ai-preview-submit]');
        if (!submitButton) return;
        submitButton.setAttribute('aria-busy', 'true');
        submitButton.textContent = 'در حال ساخت پیش‌نمایش…';

        // Do not disable the submit control. Some browsers omit a control as
        // soon as it is disabled during submit, and a failed navigation would
        // otherwise leave the page looking permanently stuck.
        window.setTimeout(() => {
            submitButton.removeAttribute('aria-busy');
            submitButton.textContent = 'ساخت پیش‌نمایش امن';
        }, 15000);
    });

    const aiCopyButton = document.querySelector('[data-copy-ai-prompt]');
    const aiPrompt = document.querySelector('#ai-user-prompt');
    aiCopyButton?.addEventListener('click', async () => {
        if (!aiPrompt) return;
        try {
            await navigator.clipboard.writeText(aiPrompt.value);
        } catch {
            aiPrompt.focus();
            aiPrompt.select();
            document.execCommand('copy');
        }
        const copyStatus = document.querySelector('[data-copy-ai-status]');
        if (copyStatus) copyStatus.hidden = false;
        aiCopyButton.textContent = 'کپی شد ✓';
    });

    const aiSelectAll = document.querySelector('[data-ai-select-all]');
    const aiChanges = [...document.querySelectorAll('[data-ai-change]:not(:disabled)')];
    if (aiSelectAll) {
        const syncAiSelection = () => {
            aiSelectAll.checked = aiChanges.length > 0 && aiChanges.every((item) => item.checked);
            aiSelectAll.indeterminate = aiChanges.some((item) => item.checked) && !aiSelectAll.checked;
        };
        aiSelectAll.addEventListener('change', () => aiChanges.forEach((item) => { item.checked = aiSelectAll.checked; }));
        aiChanges.forEach((item) => item.addEventListener('change', syncAiSelection));
        syncAiSelection();
    }

    if (document.querySelector('[data-ai-preview-submitted="true"]')) {
        const previewTarget = document.querySelector('#ai-preview-result')
            ?? document.querySelector('[data-valmsg-for="ChangesJson"]');
        if (previewTarget) {
            requestAnimationFrame(() => {
                previewTarget.scrollIntoView({ behavior: 'smooth', block: 'center' });
                previewTarget.focus?.({ preventScroll: true });
            });
        }
    }

    const guide = document.getElementById('onboarding-guide');
    if (guide) {
        const user = guide.dataset.onboardingUser || 'anonymous';
        const storageKey = `hesabyar:onboarding:v2:${user}`;
        const steps = [...guide.querySelectorAll('[data-guide-step]')];
        const dots = [...guide.querySelectorAll('[data-guide-dot]')];
        const previousButton = guide.querySelector('[data-guide-prev]');
        const nextButton = guide.querySelector('[data-guide-next]');
        let currentStep = 0;

        const renderGuide = () => {
            steps.forEach((step, index) => step.classList.toggle('hidden', index !== currentStep));
            dots.forEach((dot, index) => dot.classList.toggle('guide-dot-active', index === currentStep));
            previousButton?.classList.toggle('hidden', currentStep === 0);
            if (nextButton) nextButton.textContent = currentStep === steps.length - 1 ? 'شروع استفاده' : 'مرحله بعد';
        };

        const openGuide = () => {
            currentStep = 0;
            renderGuide();
            guide.classList.remove('hidden');
            document.body.classList.add('overflow-hidden');
        };

        const closeGuide = (remember = true) => {
            guide.classList.add('hidden');
            document.body.classList.remove('overflow-hidden');
            if (remember) localStorage.setItem(storageKey, 'done');
        };

        document.querySelectorAll('[data-guide-open]').forEach((button) => button.addEventListener('click', openGuide));
        guide.querySelectorAll('[data-guide-close], [data-guide-skip]').forEach((button) => button.addEventListener('click', () => closeGuide(true)));
        previousButton?.addEventListener('click', () => {
            currentStep = Math.max(0, currentStep - 1);
            renderGuide();
        });
        nextButton?.addEventListener('click', () => {
            if (currentStep >= steps.length - 1) closeGuide(true);
            else {
                currentStep += 1;
                renderGuide();
            }
        });
        guide.addEventListener('click', (event) => {
            if (event.target === guide) closeGuide(true);
        });

        if (!localStorage.getItem(storageKey)) window.setTimeout(openGuide, 450);
    }

    window.setTimeout(() => {
        document.querySelectorAll('[data-toast]').forEach((toast) => toast.remove());
    }, 5000);
})();
