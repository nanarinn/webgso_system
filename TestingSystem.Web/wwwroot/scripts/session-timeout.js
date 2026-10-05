(() => {
    const timeoutMinutes = Number(document.body.dataset.sessionTimeoutMinutes || "15");
    const timeoutMilliseconds = timeoutMinutes * 60 * 1000;
    const keepAliveIntervalMilliseconds = 60 * 1000;
    const sharedActivityKey = "sblss:last-activity";
    const activityEvents = ["pointerdown", "keydown", "scroll", "touchstart"];

    let lastActivity = Date.now();
    let lastRecordedActivity = 0;
    let lastKeepAlive = 0;
    let timeoutHandle;
    let logoutStarted = false;

    const authenticatedLogoutForm = () => document.querySelector(".topbar-logout-form");

    const logoutForInactivity = () => {
        const form = authenticatedLogoutForm();
        if (logoutStarted || !form) return;
        logoutStarted = true;
        fetch("/account/session/timeout", {
            method: "POST",
            credentials: "same-origin",
            cache: "no-store",
            body: new FormData(form),
            headers: { "X-Requested-With": "XMLHttpRequest" }
        }).finally(() => window.location.replace("/login?reason=session-timeout"));
    };

    const scheduleTimeout = () => {
        window.clearTimeout(timeoutHandle);
        const remaining = timeoutMilliseconds - (Date.now() - lastActivity);
        if (remaining <= 0) {
            logoutForInactivity();
            return;
        }
        timeoutHandle = window.setTimeout(logoutForInactivity, remaining);
    };

    const keepSessionAlive = async now => {
        if (now - lastKeepAlive < keepAliveIntervalMilliseconds) return;
        lastKeepAlive = now;

        try {
            const response = await fetch("/account/session/keep-alive", {
                method: "GET",
                credentials: "same-origin",
                cache: "no-store",
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });
            if (response.status === 401 || response.redirected) logoutForInactivity();
        } catch {
            // A temporary network interruption must not reset the inactivity clock.
        }
    };

    const recordActivity = () => {
        if (!authenticatedLogoutForm()) return;

        const now = Date.now();
        if (now - lastRecordedActivity < 1000) return;
        lastRecordedActivity = now;
        lastActivity = now;

        try {
            window.localStorage.setItem(sharedActivityKey, now.toString());
        } catch {
            // Session timeout still works when browser storage is unavailable.
        }

        scheduleTimeout();
        void keepSessionAlive(now);
    };

    for (const eventName of activityEvents) {
        document.addEventListener(eventName, recordActivity, { passive: true });
    }

    window.addEventListener("storage", event => {
        if (event.key !== sharedActivityKey) return;
        const timestamp = Number(event.newValue);
        if (!Number.isFinite(timestamp) || timestamp <= lastActivity) return;
        lastActivity = timestamp;
        scheduleTimeout();
    });

    document.addEventListener("visibilitychange", () => {
        if (document.visibilityState !== "visible" || !authenticatedLogoutForm()) return;
        if (Date.now() - lastActivity >= timeoutMilliseconds) logoutForInactivity();
        else recordActivity();
    });

    if (authenticatedLogoutForm()) recordActivity();
})();
