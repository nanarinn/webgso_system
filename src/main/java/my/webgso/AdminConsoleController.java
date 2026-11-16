package my.webgso;

import java.util.*;
import my.webgso.Models.ApplicationUser;

import org.springframework.security.core.Authentication;

import org.springframework.stereotype.Controller;
import org.springframework.ui.Model;
import org.springframework.web.bind.annotation.*;

@Controller
public class AdminConsoleController {
  private final Store db;
  private final Accounts accounts;

  private final WebController pages;
  private final ProfileService profiles;
  private final UserManagement management;

  public AdminConsoleController(Store db, Accounts accounts, WebController pages, ProfileService profiles, UserManagement management) {
    this.db = db;
    this.accounts = accounts;

    this.pages = pages;
    this.profiles = profiles;
    this.management = management;
  }

  @ModelAttribute
  public void common(Model model, Authentication auth) { pages.common(model, auth); }

  @GetMapping("/admin/registered-users")
  public String users(@RequestParam(defaultValue = "0") int page, Model model) {
    int current = Math.max(0, page);
    long total = db.jdbc().queryForObject("SELECT COUNT(*) FROM \"AspNetUsers\"", Long.class);
    current = (int) Math.min(current, Math.max(0, (total - 1) / 50));
    var users = db.list(ApplicationUser.class,
        "ORDER BY \"AccessRequestedAt\" DESC, \"Id\" LIMIT 50 OFFSET ?", current * 50);
    var roles = new HashMap<String, String>();
    for (var user : users) roles.put(user.id, String.join(", ", accounts.roles(user.id)));
    var emailHistory = new HashMap<String, List<ProfileService.EmailHistory>>();
    for (var user : users) emailHistory.put(user.id, profiles.emailHistory(user.id));
    model.addAttribute("emailHistory", emailHistory);
    var nameHistory = new HashMap<String, List<ProfileService.NameHistory>>();
    for (var user : users) nameHistory.put(user.id, profiles.nameHistory(user.id));
    model.addAttribute("nameHistory", nameHistory);
    model.addAttribute("title", "User Management");
    model.addAttribute("users", users);
    model.addAttribute("roles", roles);
    model.addAttribute("totalUsers", total);
    model.addAttribute("page", current);
    model.addAttribute("hasNext", (current + 1L) * 50 < total);
    return "registered-users";
  }

  @PostMapping("/admin/registered-users/{id}/{operation}")
  public String manage(@PathVariable String id, @PathVariable String operation,
      @RequestParam(defaultValue="") String confirmation, Authentication auth,
      org.springframework.web.servlet.mvc.support.RedirectAttributes flash) {
    try {
      management.manage(auth, id, operation, confirmation);
      flash.addFlashAttribute("message", "Account " + switch(operation) {
        case "activate" -> "activated."; case "deactivate" -> "deactivated."; default -> "deleted."; });
    } catch (IllegalArgumentException e) { flash.addFlashAttribute("message", e.getMessage()); }
      catch (org.springframework.dao.DataIntegrityViolationException e) {
        flash.addFlashAttribute("message", "This account has linked records and could not be deleted. Deactivate it instead.");
      }
    return "redirect:/admin/registered-users";
  }

  @GetMapping("/admin/audit-log")
  public String auditLog(@RequestParam(defaultValue = "0") int page,
      @RequestParam(defaultValue = "") String q, Model model) {
    String search = q.strip();
    if (search.length() > 200) search = search.substring(0, 200);
    String where = "";
    var parameters = new ArrayList<Object>();
    if (!search.isEmpty()) {
      where = "WHERE LOWER(COALESCE(\"Actor\", '') || ' ' || COALESCE(\"Action\", '') || ' ' || "
          + "COALESCE(\"EntityType\", '') || ' ' || COALESCE(\"EntityReference\", '') || ' ' || "
          + "COALESCE(\"Result\", '') || ' ' || COALESCE(\"Detail\", '')) LIKE ? ESCAPE '!'";
      parameters.add("%" + search.toLowerCase(Locale.ROOT).replace("!", "!!").replace("%", "!%")
          .replace("_", "!_") + "%");
    }
    long total = db.jdbc().queryForObject("SELECT COUNT(*) FROM \"AuditEvents\" " + where,
        Long.class, parameters.toArray());
    int current = (int) Math.min(Math.max(0, page), Math.max(0, (total - 1) / 50));
    parameters.add(current * 50);
    model.addAttribute("events", db.list(Models.AuditEvent.class,
        where + " ORDER BY \"OccurredAt\" DESC, \"Id\" DESC LIMIT 50 OFFSET ?", parameters.toArray()));
    model.addAttribute("title", "Audit Log");
    model.addAttribute("q", search);
    model.addAttribute("totalEvents", total);
    model.addAttribute("page", current);
    model.addAttribute("hasNext", (current + 1L) * 50 < total);
    return "audit-log";
  }

}
