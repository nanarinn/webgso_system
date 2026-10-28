package my.webgso;

import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.security.core.Authentication;
import org.springframework.stereotype.Controller;
import org.springframework.web.bind.annotation.*;
import org.springframework.web.servlet.mvc.support.RedirectAttributes;

@Controller
public class PlantDeletionController {
  private final PlantDeletionService deletion;

  public PlantDeletionController(PlantDeletionService deletion) {
    this.deletion = deletion;
  }

  @PostMapping("/admin/plants/{id}/delete")
  public String delete(
      @PathVariable int id,
      @RequestParam(defaultValue = "") String confirmation,
      Authentication auth,
      RedirectAttributes flash) {
    if (!confirmation.equals("delete"))
      throw new IllegalArgumentException("Confirm plant deletion first.");
    try {
      String name = deletion.delete(id, WebController.principal(auth).username());
      flash.addFlashAttribute("message", "Plant deleted: " + name + ".");
    } catch (IllegalArgumentException e) {
      flash.addFlashAttribute("message", e.getMessage());
    } catch (DataIntegrityViolationException e) {
      flash.addFlashAttribute(
          "message",
          "This plant is linked to saved records and cannot be deleted. Set it to Inactive"
              + " instead.");
    }
    return "redirect:/admin/lss-plants";
  }
}
