package my.webgso;
import jakarta.servlet.http.*;
import org.springframework.security.core.Authentication;
import org.springframework.security.web.authentication.logout.SecurityContextLogoutHandler;
import org.springframework.stereotype.Controller;
import org.springframework.ui.Model;
import org.springframework.web.bind.annotation.*;
@Controller
public class InitialPasswordController {
  private final Accounts accounts;
  public InitialPasswordController(Accounts accounts) { this.accounts = accounts; }
  @GetMapping("/account/first-password")
  public String page(Authentication auth) {
    return auth != null && auth.getPrincipal() instanceof Accounts.Principal p && accounts.requiresInitialPassword(p.id()) ? "first-password" : "redirect:/";
  }
  @PostMapping("/account/first-password")
  public String save(@RequestParam String password, @RequestParam String confirmPassword, Authentication auth, Model model, HttpServletRequest request, HttpServletResponse response) {
    try { accounts.completeInitialPassword(auth, password, confirmPassword); }
    catch (IllegalArgumentException e) { model.addAttribute("resetError", e.getMessage()); return "first-password"; }
    new SecurityContextLogoutHandler().logout(request, response, auth);
    return "redirect:/login?initialPasswordChanged=true";
  }
}
