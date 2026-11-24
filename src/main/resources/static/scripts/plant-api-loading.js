"use strict";

(() => {
  let pending = false;
  const overlay = document.getElementById("api-loading");
  const title = document.getElementById("api-loading-title");
  const message = document.getElementById("api-loading-message");
  if (!(overlay instanceof HTMLDialogElement)) return;

  const forms = ".plant-table-api-test, .plant-inline-api-test, .lss-api-form, .lss-refresh-form";

  overlay.addEventListener("cancel", (event) => event.preventDefault());
  document.addEventListener("submit", (event) => {
    const form = event.target;
    if (form instanceof HTMLFormElement && form.matches(".lss-delete-form")) {
      if (!window.confirm(form.dataset.confirm || "Delete this plant?")) event.preventDefault();
      return;
    }
    if (!(form instanceof HTMLFormElement) || !form.matches(forms)) return;
    if (pending) {
      event.preventDefault();
      return;
    }
    pending = true;
    form.setAttribute("aria-busy", "true");
    if (title) title.textContent = form.dataset.loadingTitle || "Testing API…";
    if (message) message.textContent = form.dataset.loadingMessage ||
      "Please wait while we test the selected API.";
    overlay.showModal();
    document.querySelectorAll(`${forms} button`)
      .forEach((button) => { button.disabled = true; });
  });

  window.addEventListener("pageshow", () => {
    pending = false;
    if (overlay.open) overlay.close();
    document.querySelectorAll(forms)
      .forEach((form) => {
        form.removeAttribute("aria-busy");
        form.querySelectorAll("button").forEach((button) => { button.disabled = false; });
      });
  });
})();
