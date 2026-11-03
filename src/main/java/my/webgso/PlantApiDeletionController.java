package my.webgso;

import org.springframework.security.core.Authentication;
import org.springframework.stereotype.Controller;
import org.springframework.web.bind.annotation.*;
import org.springframework.web.servlet.mvc.support.RedirectAttributes;

@Controller
public class PlantApiDeletionController {
  private final AdminService admin;

  public PlantApiDeletionController(AdminService admin) {
    this.admin = admin;
  }

  @PostMapping("/admin/plants/{id}/apis/delete")
  public String delete(
      @PathVariable int id,
      @RequestParam String dataType,
      @RequestParam(defaultValue = "") String confirmation,
      Authentication auth,
      RedirectAttributes flash) {
    if (!"delete".equals(confirmation))
      throw new IllegalArgumentException("Confirm API deletion first.");
    admin.deletePlantApi(id, dataType, WebController.principal(auth).username());
    flash.addFlashAttribute("message", "API deleted: " + dataType + ".");
    return "redirect:/admin/lss-plants";
  }
}
