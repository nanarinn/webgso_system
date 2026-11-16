package my.webgso.ui;

import static my.webgso.ui.Html.el;
import static my.webgso.ui.PageContext.*;

import java.util.*;
import my.webgso.Models.ApplicationUser;
import my.webgso.ui.Html.Element;

final class AdminConsolePage {
  static Element users(PageContext c) {
    Map<String, String> roles = c.value("roles");
    Map<String, List<my.webgso.ProfileService.EmailHistory>> history = c.value("emailHistory");
    Map<String, List<my.webgso.ProfileService.NameHistory>> nameHistory = c.value("nameHistory");
    var rows = new ArrayList<Element>();
    for (var user : c.<ApplicationUser>list("users")) {
      var changes = history.getOrDefault(user.id, List.of());
      var emailCell = el("div", el("p", user.email));
      if (changes.isEmpty()) emailCell.add(el("small", "No email changes recorded"));
      else {
        var entries = new ArrayList<Element>();
        for (var change : changes) entries.add(row(change.oldEmail(), change.newEmail(),
            date(change.changedAt().atZoneSameInstant(my.webgso.Policies.LOCAL), "dd MMM yyyy, HH:mm")));
        emailCell.add(el("details", el("summary", "Email changed · View history"),
            el("p", "Latest 10 changes · MYT (UTC+8)"),
            table(List.of("Previous email", "New email", "Changed at"), entries, "No changes.")));
      }
      var nameCell = el("div", el("p", user.displayName));
      var names = nameHistory.getOrDefault(user.id, List.of());
      if (names.isEmpty()) nameCell.add(el("small", "No name changes recorded"));
      else {
        var entries = new ArrayList<Element>();
        for (var change : names) entries.add(row(change.oldName(), change.newName(),
            date(change.changedAt().atZoneSameInstant(my.webgso.Policies.LOCAL), "dd MMM yyyy, HH:mm")));
        nameCell.add(el("details", el("summary", "Name changed · View history"),
            el("p", "Latest 10 changes · MYT (UTC+8)"),
            table(List.of("Previous name", "New name", "Changed at"), entries, "No changes.")));
      }
      boolean manageable = "User".equals(roles.get(user.id));
      var actions = el("div").cls("schedule-row-actions");
      if (manageable) {
        boolean active = "Approved".equals(user.accessStatus);
        String base = "/admin/registered-users/" + user.id + "/";
        String popup = "delete-user-" + user.id;
        actions.add(c.post(base + (active ? "deactivate" : "activate"), "",
            button(active ? "Deactivate" : "Activate", false)),
            el("button", "Delete").attr("type", "button").attr("popovertarget", popup).cls("plant-delete-button"),
            el("div", el("h2", "Delete user account?").attr("id", popup + "-title"),
                el("p", "Are you sure you want to delete this user account? This cannot be undone."),
                el("strong", user.displayName + " · " + user.email).cls("plant-delete-name"),
                el("p", "The user will lose access. Existing API test results will be kept."),
                c.post(base + "delete", "plant-delete-confirmation", input("confirmation", "hidden", "delete"),
                    el("button", "Cancel").attr("type", "button").attr("popovertarget", popup)
                        .attr("popovertargetaction", "hide").flag("autofocus", true).cls("secondary-action"),
                    button("Delete account", false).cls("plant-delete-button")))
                .attr("id", popup).attr("popover", "auto").attr("role", "alertdialog")
                .attr("aria-labelledby", popup + "-title").cls("plant-delete-popup"));
      } else actions.add(el("small", "Protected Admin account"));
      rows.add(row(nameCell, emailCell, roles.get(user.id),
          "Approved".equals(user.accessStatus) ? "Active" : user.accessStatus,
          date(user.accessRequestedAt == null ? null : user.accessRequestedAt.atZoneSameInstant(my.webgso.Policies.LOCAL), "dd MMM yyyy, HH:mm"), actions));
    }
    var pager = el("nav").cls("java-pagination").attr("aria-label", "Users pages");
    if (c.number("page") > 0) pager.add(action("Previous", c.pageUrl(c.number("page") - 1), false));
    pager.add(el("span", "Page " + (c.number("page") + 1)));
    if (c.yes("hasNext")) pager.add(action("Next", c.pageUrl(c.number("page") + 1), false));
    return el("section",
        heading("Administration", "User Management", "Manage user access and review name and email changes. All times use MYT (UTC+8)."),
        el("article", el("p", "Total registered accounts").cls("section-label"),
            el("h2", c.number("totalUsers")),
            table(List.of("Full name", "Email", "Role", "Status", "Registered at (MYT)", "Actions"),
                rows, "No registered accounts."), pager).cls("panel"));
  }

}
