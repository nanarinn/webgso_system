"use strict";

document.addEventListener("submit", async (event) => {
  const form = event.target;
  if (!(form instanceof HTMLFormElement) || !form.matches(".plant-status-toggle")) return;
  event.preventDefault();
  const button = form.querySelector('button[type="submit"]');
  const target = form.querySelector('input[name="status"]');
  const feedback = form.querySelector(".plant-status-feedback");
  if (!button || !target || button.disabled) return;
  button.disabled = true;
  button.setAttribute("aria-busy", "true");
  if (feedback) {
    feedback.textContent = "";
    feedback.classList.remove("has-error");
  }
  try {
    const response = await fetch(form.action + "/inline", {
      method: "POST",
      credentials: "same-origin",
      redirect: "error",
      headers: { Accept: "application/json" },
      body: new URLSearchParams(new FormData(form)),
    });
    if (!response.ok) {
      throw new Error(response.status === 403
        ? "Session expired. Sign in again to save."
        : "Unable to save status. Please try again.");
    }
    const result = await response.json();
    if (!["Active", "Inactive"].includes(result.status) ||
        result.nextStatus !== (result.status === "Active" ? "Inactive" : "Active")) {
      throw new Error("Unable to confirm status. Please try again.");
    }
    const active = result.status === "Active";
    target.value = result.nextStatus;
    button.textContent = result.status;
    button.classList.toggle("plant-active", active);
    button.classList.toggle("plant-inactive", !active);
    button.setAttribute("aria-pressed", String(active));
    button.title = "Click to set " + result.nextStatus;
    button.setAttribute("aria-label", result.status + " — click to " +
      (active ? "deactivate " : "activate ") + form.dataset.plantName);
    if (feedback) feedback.textContent = "Saved";
  } catch (error) {
    if (feedback) {
      feedback.textContent = error instanceof Error && error.message !== "Failed to fetch"
        ? error.message : "Unable to save. Check your connection and sign-in session.";
      feedback.classList.add("has-error");
    }
  } finally {
    button.disabled = false;
    button.removeAttribute("aria-busy");
  }
});
