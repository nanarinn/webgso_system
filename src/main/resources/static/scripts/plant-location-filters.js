"use strict";

// Progressive enhancement: location choices never navigate or submit until Apply filters.
(() => {
  document.querySelectorAll("form.location-query").forEach((form) => {
    const cards = [...form.querySelectorAll(".location-choice")];
    const apply = form.querySelector(".java-actions button[name='locationMode']");
    if (!apply || !cards.length) return;
    function choose(mode, clear = false) {
      apply.value = mode;
      for (const card of cards) {
        const button = card.querySelector(".location-mode-button");
        const active = button.value === mode;
        const locked = mode !== "all" && !active;
        button.type = "button";
        card.classList.toggle("is-active", active);
        card.classList.toggle("is-locked", locked);
        button.disabled = locked || active;
        button.setAttribute("aria-pressed", String(active));
        button.title = locked ? "Remove the selected filter with × to unlock." : "Select this location filter";
        card.querySelector(".location-indicator").textContent = active ? "●" : "○";
        card.querySelector(".location-lock-badge").hidden = !locked;
        const values = card.querySelector("fieldset");
        const remove = card.querySelector(".location-unlock");
        if (values) {
          remove.type = "button";
          values.disabled = !active;
          values.hidden = !active;
          remove.hidden = !active;
          if (clear) values.querySelectorAll("input").forEach((input) => { input.checked = false; });
        }
      }
    }
    form.addEventListener("click", (event) => {
      const target = event.target.closest(".location-mode-button, .location-unlock");
      if (!target || target.disabled) return;
      event.preventDefault();
      const removing = target.classList.contains("location-unlock");
      choose(target.value, removing);
      if (removing) form.querySelector(".location-mode-button:not(:disabled)")?.focus();
    });
    form.addEventListener("submit", (event) => {
      // Enter in the search field applies the current filters, rather than choosing a group.
      if (event.submitter?.classList.contains("location-mode-button")) {
        event.preventDefault();
        form.requestSubmit(apply);
      }
    });
    window.addEventListener("pageshow", () => choose(apply.value));
    choose(apply.value);
  });
})();
