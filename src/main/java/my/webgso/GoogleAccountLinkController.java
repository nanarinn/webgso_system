package my.webgso;

import jakarta.servlet.http.HttpServletRequest;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.security.core.Authentication;
import org.springframework.security.core.AuthenticationException;
import org.springframework.stereotype.Controller;
import org.springframework.web.bind.annotation.*;
import org.springframework.web.servlet.mvc.support.RedirectAttributes;

@Controller
public class GoogleAccountLinkController {
  private final GoogleAccountLink links;

  public GoogleAccountLinkController(GoogleAccountLink links) {
    this.links = links;
  }

  @PostMapping("/account/profile/google/link")
  public String link(
      @RequestParam(defaultValue = "") String currentPassword,
      Authentication auth,
      HttpServletRequest request,
      RedirectAttributes flash,
      @Value("${webgso.google.enabled:false}") boolean enabled) {
    request.getSession().removeAttribute(GoogleAccountLink.SESSION_KEY);
    if (!enabled) {
      flash.addFlashAttribute("message", "Google sign-in is not configured yet.");
      return "redirect:/account/profile";
    }
    try {
      request
          .getSession()
          .setAttribute(GoogleAccountLink.SESSION_KEY, links.begin(auth, currentPassword));
      return "redirect:/oauth2/authorization/google";
    } catch (IllegalArgumentException | AuthenticationException e) {
      flash.addFlashAttribute(
          "message",
          "Google account was not linked. Check your current GSO password and account status.");
      return "redirect:/account/profile";
    }
  }
}
