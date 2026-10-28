package my.webgso;

import java.util.Map;
import org.springframework.security.core.Authentication;
import org.springframework.stereotype.Controller;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.web.bind.annotation.*;
import org.springframework.web.servlet.mvc.support.RedirectAttributes;

@Controller
public class PlantStatusController {
  private final Store db;
  private final Accounts accounts;

  public PlantStatusController(Store db, Accounts accounts) {
    this.db = db;
    this.accounts = accounts;
  }

  @PostMapping("/admin/plants/{id}/status")
  @Transactional
  public String update(
      @PathVariable int id,
      @RequestParam String status,
      Authentication auth,
      RedirectAttributes flash) {
    updateStatus(id, status, auth);
    flash.addFlashAttribute("message", "Plant status saved: " + status + ".");
    return "redirect:/admin/lss-plants";
  }

  @PostMapping(value = "/admin/plants/{id}/status/inline", produces = "application/json")
  @ResponseBody
  @Transactional
  public Map<String, String> updateInline(
      @PathVariable int id, @RequestParam String status, Authentication auth) {
    updateStatus(id, status, auth);
    return Map.of("status", status, "nextStatus", status.equals("Active") ? "Inactive" : "Active");
  }

  private void updateStatus(int id, String status, Authentication auth) {
    if (!status.equals("Active") && !status.equals("Inactive"))
      throw new IllegalArgumentException("Choose Active or Inactive.");
    int changed =
        db.jdbc()
            .update(
                "UPDATE \"LssPlants\" SET \"IsActive\"=? WHERE \"Id\"=?",
                status.equals("Active"),
                id);
    if (changed != 1) throw new IllegalArgumentException("Plant not found.");
    accounts.audit(
        WebController.principal(auth).username(),
        "Update plant status",
        "LssPlant",
        Integer.toString(id),
        "Success",
        status);
  }
}
