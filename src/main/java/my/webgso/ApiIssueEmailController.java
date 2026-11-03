package my.webgso;

import java.util.List;
import my.webgso.Models.UserAccessNotification;
import org.springframework.security.core.Authentication;
import org.springframework.stereotype.Controller;
import org.springframework.ui.Model;
import org.springframework.web.bind.annotation.*;
import org.springframework.web.servlet.mvc.support.RedirectAttributes;

@Controller
public class ApiIssueEmailController {
  private final ApiIssueEmails emails;
  private final Store db;
  private final AccountToolsController shell;

  public ApiIssueEmailController(ApiIssueEmails emails, Store db, AccountToolsController shell) {
    this.emails = emails;
    this.db = db;
    this.shell = shell;
  }

  @ModelAttribute
  public void common(Model model, Authentication auth) {
    shell.common(model, auth);
  }

  @GetMapping("/admin/api-email")
  public String page(Model model, Authentication auth) {
    model.addAttribute("title", "API email alerts");
    model.addAttribute("emailIssues", emails.issues());
    model.addAttribute("emailRecipients", emails.recipients());
    model.addAttribute(
        "emailDeliveries",
        db.list(
            UserAccessNotification.class,
            "WHERE \"NotificationType\"='ApiIssueMessage' AND \"RelatedUserId\"=? ORDER BY \"Id\""
                + " DESC LIMIT 20",
            WebController.principal(auth).id()));
    return "api-email";
  }

  @PostMapping("/admin/api-email")
  public String send(
      @RequestParam(defaultValue = "") List<Long> issueIds,
      @RequestParam(defaultValue = "") List<String> recipientIds,
      Authentication auth,
      RedirectAttributes flash) {
    try {
      int count = emails.queue(issueIds, recipientIds, WebController.principal(auth).id());
      flash.addFlashAttribute(
          "emailNotice",
          count == 0
              ? "These results have already been queued or sent to the selected recipients."
              : count + " email(s) queued. Delivery status is shown below.");
    } catch (IllegalArgumentException e) {
      flash.addFlashAttribute("emailError", e.getMessage());
    }
    return "redirect:/admin/api-email";
  }
}
