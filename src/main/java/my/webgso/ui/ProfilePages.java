package my.webgso.ui;

import static my.webgso.ui.Html.el;
import static my.webgso.ui.PageContext.*;

import java.util.*;
import my.webgso.Accounts;
import my.webgso.Models.ApplicationUser;
import my.webgso.ui.Html.Element;

final class ProfilePages {
  private ProfilePages() {}

  static Element profile(PageContext c) {
    ApplicationUser user = c.value("profile");
    Map<String, String> values = c.value("profileForm");
    boolean managed = Boolean.TRUE.equals(user.isEntraUser);
    String name = Accounts.displayName(user.displayName);
    String initial =
        (name.isBlank() ? user.userName : name).substring(0, 1).toUpperCase(Locale.ROOT);
    var summary =
        el(
                "aside",
                el("div", initial).cls("profile-avatar"),
                el("h2", name),
                el("p", user.email).cls("profile-email"),
                el("span", "Admin").cls("profile-role"),
                el(
                    "dl",
                    el("dt", "Username"),
                    el("dd", user.userName),
                    el("dt", "Account status"),
                    el("dd", "Approved".equals(user.accessStatus) ? "Active" : "Inactive"),
                    el("dt", "Joined"),
                    el("dd", date(user.accessRequestedAt, "dd MMM yyyy"))))
            .cls("panel profile-summary");
    var form =
        c.post(
            "/account/profile",
            "profile-edit-form",
            el(
                    "div",
                    el("h2", "Personal details"),
                    el("p", "Update the information on your own account."))
                .cls("profile-form-heading"),
            c.model().get("profileError") == null
                ? null
                : el("p", c.text("profileError")).cls("profile-error").attr("role", "alert"),
            el(
                    "div",
                    field(
                        "Full name",
                        input("displayName", "text", values.get("displayName"))
                            .attr("maxlength", 160)
                            .attr("autocomplete", "name")
                            .flag("required", true)),
                    field(
                        "Username",
                        input("username", "text", values.get("username"))
                            .attr("maxlength", 100)
                            .attr("autocomplete", "username")
                            .flag("readonly", managed)
                            .flag("required", true)),
                    field(
                        "Email address",
                        input("email", "email", values.get("email"))
                            .attr("maxlength", 256)
                            .attr("autocomplete", "email")
                            .flag("readonly", managed)
                            .flag("required", true)),
                    field(
                        "Phone number (optional)",
                        input("phone", "tel", values.get("phone"))
                            .attr("maxlength", 30)
                            .attr("autocomplete", "tel")))
                .cls("profile-fields"),
            el("p", "Use your username or email address to sign in.").cls("profile-form-note"),
            el("footer", action("Cancel", "/account/profile", false), button("Save changes", true))
                .cls("profile-form-footer"));
    return el(
            "section",
            heading("Account", "My Profile", "View and manage your personal information.", null),
            el("div", summary, el("article", form).cls("panel profile-edit-panel"))
                .cls("profile-layout"))
        .cls("profile-page");
  }
}
