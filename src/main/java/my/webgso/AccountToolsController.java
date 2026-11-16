package my.webgso;

import jakarta.servlet.http.HttpServletResponse;
import org.springframework.security.core.Authentication;
import org.springframework.stereotype.Controller;
import org.springframework.ui.Model;
import org.springframework.web.bind.annotation.*;
import org.springframework.web.servlet.mvc.support.RedirectAttributes;

@Controller
public class AccountToolsController {
  private final PasswordReset reset;
  private final Accounts accounts;
  private final EmailPreferences prefs;
  private final DatabaseBackups backups;
  private final Store db;
  private final DatabaseConnectionStatus connection;

  public AccountToolsController(
      PasswordReset reset,
      Accounts accounts,
      EmailPreferences prefs,
      DatabaseBackups backups,
      Store db,
      DatabaseConnectionStatus connection) {
    this.db = db;
    this.connection = connection;
    this.reset = reset;
    this.accounts = accounts;
    this.prefs = prefs;
    this.backups = backups;
  }

  @ModelAttribute
  public void common(Model model, Authentication auth) {
    model.addAttribute(
        "navigation",
        Resources.ALL.stream()
            .filter(
                r ->
                    !java.util.Set.of(
                            "/admin/api-configurations",
                            "/admin/report-recipients",
                            "/admin/anomalies",
                            "/admin/delivery-monitor")
                        .contains(r.path()))
            .toList());
    model.addAttribute(
        "isAdmin",
        auth != null
            && auth.getAuthorities().stream().anyMatch(a -> a.getAuthority().equals("ROLE_Admin")));
    if (auth != null && auth.getPrincipal() instanceof Accounts.Principal p) {
      model.addAttribute("account", p);
      model.addAttribute("databaseConnection", connection.check());
      model.addAttribute(
          "notifications",
          db.list(
              Models.UserAccessNotification.class,
              "WHERE \"UserId\"=? AND \"NotificationType\" NOT IN"
                  + " ('EmailChangeCode','PasswordChangeCode','PasswordReset','EmailVerification')"
                  + " ORDER BY \"CreatedAt\" DESC LIMIT 15",
              p.id()));
    }
  }

  @GetMapping("/account/forgot-password")
  public String forgot() {
    return "forgot-password";
  }

  @PostMapping("/account/forgot-password")
  public String request(
      @RequestParam(defaultValue = "") String identifier,
      @RequestParam(defaultValue = "") String currentPassword,
      @RequestParam(defaultValue = "") String password,
      @RequestParam(defaultValue = "") String confirmPassword,
      Model model) {
    try {
      accounts.changeWithCurrentPassword(identifier, currentPassword, password, confirmPassword);
      return "redirect:/login?passwordReset=true";
    } catch (org.springframework.security.core.AuthenticationException e) {
      model.addAttribute("resetError", "Check your account ID and current password. If your account is locked, try again after 15 minutes.");
    } catch (IllegalArgumentException e) {
      model.addAttribute("resetError", e.getMessage());
    }
    return "forgot-password";
  }

  @GetMapping("/account/reset-password")
  public String landing(
      @RequestParam(defaultValue = "") String token, Model model, HttpServletResponse response) {
    response.setHeader("Referrer-Policy", "no-referrer");
    model.addAttribute("token", PasswordReset.tokenFormat(token) ? token : "");
    return "reset-password";
  }

  @PostMapping("/account/reset-password")
  public String change(
      @RequestParam String token,
      @RequestParam String password,
      @RequestParam String confirmPassword,
      Model model,
      HttpServletResponse response) {
    response.setHeader("Referrer-Policy", "no-referrer");
    try {
      if (!password.equals(confirmPassword))
        throw new IllegalArgumentException("Passwords do not match.");
      if (reset.reset(token, password)) return "redirect:/login?passwordReset=true";
      model.addAttribute(
          "resetError",
          "This link is invalid, expired or already used. Request a new password reset email.");
    } catch (IllegalArgumentException e) {
      model.addAttribute("resetError", e.getMessage());
    }
    model.addAttribute("token", PasswordReset.tokenFormat(token) ? token : "");
    return "reset-password";
  }

  private String id(Authentication auth) {
    return ((Accounts.Principal) auth.getPrincipal()).id();
  }

  @GetMapping("/account/email-settings")
  public String settings(Model model, Authentication auth) {
    model.addAttribute("title", "Email settings");
    model.addAttribute("emailPreferences", prefs.get(id(auth)));
    model.addAttribute("emailRecipient", prefs.owner(id(auth)).orElseThrow().email);
    model.addAttribute("backupStatus", backups.status());
    return "email-settings";
  }

  @PostMapping("/account/email-settings")
  public String save(
      @RequestParam(defaultValue = "false") boolean failure,
      @RequestParam(defaultValue = "false") boolean recovery,
      @RequestParam(defaultValue = "false") boolean stale,
      @RequestParam(defaultValue = "false") boolean excel,
      @RequestParam(defaultValue = "false") boolean digest,
      Authentication auth,
      RedirectAttributes flash) {
    prefs.save(id(auth), new EmailPreferences.Settings(failure, recovery, stale, excel, digest));
    flash.addFlashAttribute("message", "Your email preferences have been saved.");
    return "redirect:/account/email-settings";
  }

  @GetMapping("/help")
  public String guide(Model model) {
    model.addAttribute("title", "User Guide");
    return "user-guide";
  }
}
