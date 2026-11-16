"use strict";

(() => {
  let pending = false;
  const testForms = ".plant-table-api-test, .plant-inline-api-test, .quick-api-test, .plant-editor";
  const overlay = document.getElementById("api-loading");
  if (!(overlay instanceof HTMLDialogElement)) return;

  overlay.addEventListener("cancel", (event) => event.preventDefault());
  document.addEventListener("submit", (event) => {
    const form = event.target;
    if (event.defaultPrevented || !(form instanceof HTMLFormElement) ||
        !form.matches(testForms)) return;
    if (pending) {
      event.preventDefault();
      return;
    }
    pending = true;
    form.setAttribute("aria-busy", "true");
    const savingPlant = form.matches(".plant-editor");
    const savingSingleApi = event.submitter?.getAttribute("name") === "saveApi";
    document.getElementById("api-loading-title").textContent =
      savingPlant && !savingSingleApi ? "Testing APIs and saving plant…" : "Testing API…";
    document.getElementById("api-loading-message").textContent = savingPlant
      ? savingSingleApi
        ? "Please wait while we test and save this API connection."
        : "Please wait while we check the configured APIs before saving your plant. This may take a moment."
      : "Please wait while we test the selected API.";
    overlay.showModal();
    document.querySelectorAll(testForms).forEach((testForm) => {
      testForm.querySelectorAll('button[type="submit"]')
        .forEach((button) => {
          // Named submitters carry the selected API field in the native POST.
          // Keep their value in the request; the modal and pending guard block repeats.
          if (!button.name) button.disabled = true;
        });
    });
  });

  window.addEventListener("pageshow", () => {
    pending = false;
    if (overlay.open) overlay.close();
    document.querySelectorAll(testForms)
      .forEach((form) => {
        form.removeAttribute("aria-busy");
        form.querySelectorAll("button").forEach((button) => { button.disabled = false; });
      });
  });
})();
