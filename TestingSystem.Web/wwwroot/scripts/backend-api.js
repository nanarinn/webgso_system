window.sblssBackendApi = {
    get: async function (path, timeoutMs = 30000) {
        const controller = new AbortController();
        const timeoutId = window.setTimeout(() => controller.abort(), timeoutMs);
        try {
            const response = await fetch(path, {
                method: "GET",
                credentials: "same-origin",
                headers: { "Accept": "application/json" },
                signal: controller.signal
            });

            if (!response.ok) {
                const message = response.status === 401 || response.status === 403
                    ? "Your session is not authorised to access the backend API."
                    : `Backend API request failed with HTTP ${response.status}.`;
                throw new Error(message);
            }

            return await response.json();
        } catch (error) {
            if (error.name === "AbortError")
                throw new Error(`Backend API request timed out after ${Math.round(timeoutMs / 1000)} seconds.`);
            throw error;
        } finally {
            window.clearTimeout(timeoutId);
        }
    }
};
