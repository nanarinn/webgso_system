(() => {
    let active = null;
    const months = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
    const weekdays = ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];
    const pad = value => String(value).padStart(2, "0");
    const twentyFourHour = (hour, minute) => `${pad(hour)}:${pad(minute)}`;

    function parseDate(value) {
        const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value || "");
        return match ? new Date(+match[1], +match[2] - 1, +match[3]) : null;
    }

    function parseTime(value) {
        const match = /(?:T|^)(\d{2}):(\d{2})/.exec(value || "");
        return match ? { hour: +match[1], minute: +match[2] } : { hour: 0, minute: 0 };
    }

    function dateKey(date) {
        return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
    }

    function closePicker() {
        if (!active) return;
        active.popover.remove();
        active = null;
    }

    function commit(value) {
        const current = active;
        const input = current.input;
        const nativeSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
        if (nativeSetter) nativeSetter.call(input, value);
        else input.value = value;
        const visibleValue = current.anchor.querySelector(".sblss-picker-copy b");
        if (visibleValue) {
            if (!value) visibleValue.textContent = input.type === "time" ? "HH:mm" : input.type === "date" ? "DD/MM/YYYY" : "DD/MM/YYYY HH:mm";
            else if (input.type === "time") {
                const time = parseTime(value);
                visibleValue.textContent = twentyFourHour(time.hour, time.minute);
            }
            else {
                const date = parseDate(value);
                const time = parseTime(value);
                visibleValue.textContent = date
                    ? `${pad(date.getDate())}/${pad(date.getMonth() + 1)}/${date.getFullYear()}${input.type === "datetime-local" ? ` ${twentyFourHour(time.hour, time.minute)}` : ""}`
                    : value;
            }
        }
        input.dispatchEvent(new Event("change", { bubbles: true }));
        closePicker();
    }

    function isDateAllowed(date) {
        const key = dateKey(date);
        const min = active.input.min ? active.input.min.substring(0, 10) : "";
        const max = active.input.max ? active.input.max.substring(0, 10) : "";
        return (!min || key >= min) && (!max || key <= max);
    }

    function renderCalendar() {
        const { popover, viewDate, selectedDate } = active;
        popover.querySelector(".sblss-dtp-month-label").textContent = `${months[viewDate.getMonth()]} ${viewDate.getFullYear()}`;
        const grid = popover.querySelector(".sblss-dtp-days");
        grid.replaceChildren();
        const first = new Date(viewDate.getFullYear(), viewDate.getMonth(), 1);
        const cursor = new Date(viewDate.getFullYear(), viewDate.getMonth(), 1 - first.getDay());
        const todayKey = dateKey(new Date());
        for (let index = 0; index < 42; index++) {
            const date = new Date(cursor.getFullYear(), cursor.getMonth(), cursor.getDate() + index);
            const button = document.createElement("button");
            button.type = "button";
            button.textContent = date.getDate();
            button.className = "sblss-dtp-day";
            if (date.getMonth() !== viewDate.getMonth()) button.classList.add("outside");
            if (dateKey(date) === todayKey) button.classList.add("today");
            if (selectedDate && dateKey(date) === dateKey(selectedDate)) button.classList.add("selected");
            if (!isDateAllowed(date)) button.disabled = true;
            button.addEventListener("click", () => {
                active.selectedDate = date;
                active.viewDate = new Date(date.getFullYear(), date.getMonth(), 1);
                renderCalendar();
            });
            grid.append(button);
        }
    }

    function positionPicker() {
        if (!active) return;
        const rect = active.anchor.getBoundingClientRect();
        const popup = active.popover;
        const margin = 12;
        const width = popup.offsetWidth;
        const height = popup.offsetHeight;
        let left = Math.min(rect.left, window.innerWidth - width - margin);
        left = Math.max(margin, left);
        let top = rect.bottom + 8;
        if (top + height > window.innerHeight - margin) top = Math.max(margin, rect.top - height - 8);
        popup.style.left = `${left}px`;
        popup.style.top = `${top}px`;
    }

    function refreshTimeDisplay() {
        if (!active) return;
        const hourValue = active.popover.querySelector(".sblss-dtp-hour-value");
        const minuteValue = active.popover.querySelector(".sblss-dtp-minute-value");
        if (hourValue) hourValue.value = pad(active.hour);
        if (minuteValue) minuteValue.value = pad(active.minute);
    }

    function openPicker(anchor) {
        closePicker();
        const input = anchor.querySelector(".sblss-picker-native");
        if (!input || input.disabled || input.readOnly) return;
        const kind = input.type;
        const hasDate = kind === "date" || kind === "datetime-local";
        const hasTime = kind === "time" || kind === "datetime-local";
        const initialDate = parseDate(input.value) || new Date();
        const initialTime = parseTime(input.value || (kind === "time" ? input.value : ""));

        const popover = document.createElement("section");
        popover.className = `sblss-dtp-popover ${hasDate && hasTime ? "with-time" : ""}`;
        popover.setAttribute("role", "dialog");
        popover.setAttribute("aria-label", hasDate && hasTime ? "Choose date and time" : hasDate ? "Choose date" : "Choose time");
        popover.innerHTML = `
            <header class="sblss-dtp-heading">
                <div><small>${hasDate ? "SELECT DATE" : "SELECT TIME"}</small><strong>${hasDate ? "Day / Month / Year" : "24-hour time"}</strong></div>
                <button type="button" class="sblss-dtp-close" aria-label="Close">×</button>
            </header>
            <div class="sblss-dtp-content">
                ${hasDate ? `<div class="sblss-dtp-calendar">
                    <div class="sblss-dtp-month"><button type="button" class="sblss-dtp-prev" aria-label="Previous month">‹</button><strong class="sblss-dtp-month-label"></strong><button type="button" class="sblss-dtp-next" aria-label="Next month">›</button></div>
                    <div class="sblss-dtp-weekdays">${weekdays.map(day => `<span>${day}</span>`).join("")}</div>
                    <div class="sblss-dtp-days"></div>
                </div>` : ""}
                ${hasTime ? `<div class="sblss-dtp-time"><div class="sblss-dtp-time-title"><span>◷</span><div><strong>Time</strong><small>24-hour format</small></div></div><div class="sblss-dtp-time-controls">
                    <div class="sblss-dtp-time-unit"><button type="button" class="sblss-dtp-hour-up" aria-label="Increase hour">▲</button><input class="sblss-dtp-hour-value" inputmode="numeric" maxlength="2" aria-label="Type hour from 0 to 23" /><small>Hour</small><button type="button" class="sblss-dtp-hour-down" aria-label="Decrease hour">▼</button></div>
                    <b class="sblss-dtp-time-separator">:</b>
                    <div class="sblss-dtp-time-unit"><button type="button" class="sblss-dtp-minute-up" aria-label="Increase minute">▲</button><input class="sblss-dtp-minute-value" inputmode="numeric" maxlength="2" aria-label="Type minute from 0 to 59" /><small>Minute</small><button type="button" class="sblss-dtp-minute-down" aria-label="Decrease minute">▼</button></div>
                </div><small class="sblss-dtp-zone">Malaysia time · 24-hour format</small></div>` : ""}
            </div>
            <footer class="sblss-dtp-actions">
                ${input.required ? "" : `<button type="button" class="sblss-dtp-clear">Clear</button>`}
                ${hasDate ? `<button type="button" class="sblss-dtp-today">Today</button>` : ""}
                <span></span><button type="button" class="sblss-dtp-cancel">Cancel</button>
                <button type="button" class="sblss-dtp-apply">Apply</button>
            </footer>`;
        document.body.append(popover);
        active = {
            anchor, input, popover,
            selectedDate: hasDate ? initialDate : null,
            viewDate: new Date(initialDate.getFullYear(), initialDate.getMonth(), 1),
            hour: initialTime.hour,
            minute: initialTime.minute
        };

        if (hasDate) {
            popover.querySelector(".sblss-dtp-prev").addEventListener("click", () => {
                active.viewDate = new Date(active.viewDate.getFullYear(), active.viewDate.getMonth() - 1, 1);
                renderCalendar();
            });
            popover.querySelector(".sblss-dtp-next").addEventListener("click", () => {
                active.viewDate = new Date(active.viewDate.getFullYear(), active.viewDate.getMonth() + 1, 1);
                renderCalendar();
            });
            popover.querySelector(".sblss-dtp-today").addEventListener("click", () => {
                const today = new Date();
                if (!isDateAllowed(today)) return;
                active.selectedDate = today;
                active.viewDate = new Date(today.getFullYear(), today.getMonth(), 1);
                renderCalendar();
            });
            renderCalendar();
        }

        if (hasTime) {
            const requestedStep = Number(input.step);
            const minuteStep = Number.isFinite(requestedStep) && requestedStep > 60
                ? Math.max(1, Math.round(requestedStep / 60))
                : 1;
            const adjustHour = amount => {
                active.hour = (active.hour + amount + 24) % 24;
                refreshTimeDisplay();
            };
            const adjustMinute = amount => {
                active.minute = (active.minute + amount + 60) % 60;
                refreshTimeDisplay();
            };
            popover.querySelector(".sblss-dtp-hour-up").addEventListener("click", () => adjustHour(1));
            popover.querySelector(".sblss-dtp-hour-down").addEventListener("click", () => adjustHour(-1));
            popover.querySelector(".sblss-dtp-minute-up").addEventListener("click", () => adjustMinute(minuteStep));
            popover.querySelector(".sblss-dtp-minute-down").addEventListener("click", () => adjustMinute(-minuteStep));
            const hourInput = popover.querySelector(".sblss-dtp-hour-value");
            const minuteInput = popover.querySelector(".sblss-dtp-minute-value");
            hourInput.addEventListener("focus", () => hourInput.select());
            minuteInput.addEventListener("focus", () => minuteInput.select());
            hourInput.addEventListener("input", () => {
                hourInput.value = hourInput.value.replace(/\D/g, "").slice(0, 2);
                const typedHour = Number(hourInput.value);
                if (typedHour < 0 || typedHour > 23 || hourInput.value === "") return;
                active.hour = typedHour;
            });
            minuteInput.addEventListener("input", () => {
                minuteInput.value = minuteInput.value.replace(/\D/g, "").slice(0, 2);
                const typedMinute = Number(minuteInput.value);
                if (typedMinute < 0 || typedMinute > 59 || minuteInput.value === "") return;
                active.minute = typedMinute;
            });
            hourInput.addEventListener("blur", refreshTimeDisplay);
            minuteInput.addEventListener("blur", refreshTimeDisplay);
            for (const typedInput of [hourInput, minuteInput]) {
                typedInput.addEventListener("keydown", event => {
                    if (event.key === "Enter") {
                        event.preventDefault();
                        typedInput.blur();
                    }
                });
            }
            refreshTimeDisplay();
        }

        popover.querySelector(".sblss-dtp-close").addEventListener("click", closePicker);
        popover.querySelector(".sblss-dtp-cancel").addEventListener("click", closePicker);
        const clearButton = popover.querySelector(".sblss-dtp-clear");
        clearButton?.addEventListener("click", () => commit(""));
        popover.querySelector(".sblss-dtp-apply").addEventListener("click", event => {
            event.preventDefault();
            event.stopPropagation();
            if (hasDate && !active.selectedDate) return;
            const date = hasDate ? dateKey(active.selectedDate) : "";
            const time = hasTime ? `${pad(active.hour)}:${pad(active.minute)}` : "";
            commit(kind === "datetime-local" ? `${date}T${time}` : kind === "date" ? date : time);
        });
        positionPicker();
        popover.querySelector(hasDate ? ".sblss-dtp-month-label" : ".sblss-dtp-hour-up")?.focus();
    }

    document.addEventListener("click", event => {
        if (active && event.composedPath().includes(active.popover)) return;
        const anchor = event.target.closest?.(".sblss-picker");
        if (anchor) {
            event.preventDefault();
            openPicker(anchor);
            return;
        }
        if (active) closePicker();
    });
    document.addEventListener("keydown", event => {
        if (event.key === "Escape") closePicker();
    });
    window.addEventListener("resize", positionPicker);
    window.addEventListener("scroll", closePicker, true);
})();
