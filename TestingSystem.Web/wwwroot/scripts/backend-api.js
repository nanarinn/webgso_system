window.sblssBackendApi = {
    get: async function (path) {
        const response = await fetch(path, {
            method: "GET",
            credentials: "same-origin",
            headers: { "Accept": "application/json" }
        });

        if (!response.ok) {
            const message = response.status === 401 || response.status === 403
                ? "Your session is not authorised to access the backend API."
                : `Backend API request failed with HTTP ${response.status}.`;
            throw new Error(message);
        }

        return await response.json();
    }
};
