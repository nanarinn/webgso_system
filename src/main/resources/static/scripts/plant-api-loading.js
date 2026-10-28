"use strict";

(() => {
  let pending = false;
  const overlay = document.getElementById("api-loading");
  if (!(overlay instanceof HTMLDialogElement)) return;

  overlay.addEventListener("cancel", (event) => event.preventDefault());
  document.addEventListener("submit", (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) ||
        !form.matches(".plant-table-api-test, .plant-inline-api-test")) return;
    if (pending) {
      event.preventDefault();
      return;
    }
    pending = true;
    form.setAttribute("aria-busy", "true");
    overlay.showModal();
    document.querySelectorAll(".plant-table-api-test button, .plant-inline-api-test button")
      .forEach((button) => { button.disabled = true; });
  });

  window.addEventListener("pageshow", () => {
    pending = false;
    if (overlay.open) overlay.close();
    document.querySelectorAll(".plant-table-api-test, .plant-inline-api-test")
      .forEach((form) => {
        form.removeAttribute("aria-busy");
        form.querySelectorAll("button").forEach((button) => { button.disabled = false; });
      });
  });
})();
