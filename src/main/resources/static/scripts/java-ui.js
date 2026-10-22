// Progressive enhancement only: every application workflow uses server-rendered forms.
const batchPanel = document.querySelector('[data-batch-id]');
if (batchPanel) {
  const poll = async () => {
    try {
      const response = await fetch('/api/collections/batches/' + encodeURIComponent(batchPanel.dataset.batchId), {headers: {'Accept': 'application/json'}});
      if (!response.ok) { batchPanel.querySelector('[data-batch-summary]').textContent = 'Status could not be refreshed. Reload this page or view collection history.'; return; }
      const batch = await response.json();
      const body = batchPanel.querySelector('[data-batch-items]');
      body.replaceChildren(...batch.items.map(item => {
        const row = document.createElement('tr');
        [item.plantName, item.status, item.message].forEach(value => { const cell = document.createElement('td'); cell.textContent = value; row.append(cell); });
        return row;
      }));
      batchPanel.querySelector('[data-batch-summary]').textContent = batch.complete ? 'Batch completed. Outcomes are saved in collection history.' : 'Collection is running…';
      if (!batch.complete) window.setTimeout(poll, 2000);
    } catch { batchPanel.querySelector('[data-batch-summary]').textContent = 'Connection interrupted. Reload to check collection history.'; }
  };
  poll();
}
document.querySelectorAll(".nav-link").forEach((link) => {
  if (new URL(link.href).pathname === window.location.pathname)
    link.setAttribute("aria-current", "page");
});
document.querySelectorAll("form[data-plant-test]").forEach((form) => {
  form.addEventListener("submit", () => {
    const id = Number(form.elements.testPlantId.value);
    if (Number.isSafeInteger(id) && id > 0)
      form.action = "/admin/plants/" + id + "/test";
  });
});
document.querySelectorAll('form[method="post"]').forEach((form) => {
  form.addEventListener("submit", (event) => {
    if (form.dataset.submitted) {
      event.preventDefault();
      return;
    }
    if (form.checkValidity()) {
      form.dataset.submitted = "true";
      const submit = form.querySelector(
        'button[type="submit"], button:not([type])',
      );
      if (submit) {
        submit.disabled = true;
        submit.textContent = "Processing…";
      }
    }
  });
});
