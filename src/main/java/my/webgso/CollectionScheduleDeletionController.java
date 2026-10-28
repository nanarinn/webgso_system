package my.webgso;

import org.springframework.security.core.Authentication;
import org.springframework.stereotype.Controller;
import org.springframework.web.bind.annotation.*;
import org.springframework.web.servlet.mvc.support.RedirectAttributes;

@Controller
public class CollectionScheduleDeletionController {
  private final CollectionScheduleDeletionService deletion;

  public CollectionScheduleDeletionController(CollectionScheduleDeletionService deletion) {
    this.deletion = deletion;
  }

  @PostMapping("/admin/collection-schedules/{id}/delete")
  public String delete(
      @PathVariable int id,
      @RequestParam(defaultValue = "") String confirmation,
      Authentication auth,
      RedirectAttributes flash) {
    if (!confirmation.equals("delete"))
      throw new IllegalArgumentException("Confirm schedule deletion first.");
    try {
      String name = deletion.delete(id, WebController.principal(auth).username());
      flash.addFlashAttribute("message", "Collection schedule deleted: " + name + ".");
    } catch (IllegalArgumentException e) {
      flash.addFlashAttribute("message", e.getMessage());
    }
    return "redirect:/admin/collection-schedules";
  }
}
