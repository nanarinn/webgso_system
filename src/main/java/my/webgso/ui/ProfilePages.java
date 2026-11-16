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
                el("p", user.userName).cls("profile-email"),
                el("span", c.yes("isAdmin") ? "Admin" : "User").cls("profile-role"),
                el(
                    "dl",
                    el("dt", "Account status"),
                    el("dd", "Approved".equals(user.accessStatus) ? "Active" : "Inactive"),
                    el("dt", "Joined"),
                    el("dd", date(user.accessRequestedAt, "dd MMM yyyy"))))
            .cls("panel profile-summary");
    if (!Boolean.TRUE.equals(c.model().get("profileEditing"))) {
      var details =
          el(
                  "article",
                  el(
                          "div",
                          el("h2", "Personal details"),
                          el("p", "Your account information. Select Edit profile to make changes."))
                      .cls("profile-form-heading"),
                  el(
                          "dl",
                          el("dt", "Full name"),
                          el("dd", name),
                          el("dt", "Email"),
                          el("dd", user.email == null || user.email.isBlank() ? user.userName : user.email),
                          el("dt", "Phone number"),
                          el(
                              "dd",
                              user.phoneNumber == null || user.phoneNumber.isBlank()
                                  ? "Not provided"
                                  : user.phoneNumber))
                      .cls("profile-read-details"),
                  el("footer", action("Edit profile", "/account/profile/edit", true))
                      .cls("profile-form-footer"))
              .cls("panel profile-edit-panel");
      return el(
              "section",
              heading(
                  "Account",
                  "My Profile",
                  "View and manage your personal information.",
                  null),
              el("div", summary, details).cls("profile-layout"))
          .cls("profile-page");
    }
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
                    input("email", "hidden", values.get("email")),
                    field(
                        "Phone number (optional)",
                        input("phone", "tel", values.get("phone"))
                            .attr("maxlength", 30)
                            .attr("autocomplete", "tel")))
                .cls("profile-fields"),

            el("footer", action("Cancel", "/account/profile", false), button("Save changes", true))
                .cls("profile-form-footer"));
    return el(
            "section",
            heading(
                "Account",
                "My Profile",
                "View and manage your personal information.",
                null),
            el(
                    "div",
                    summary,
                    el("article", form, managed ? null : securityActions())
                        .cls("panel profile-edit-panel"))
                .cls("profile-layout"))
        .cls("profile-page");
  }

  private static Element securityActions() {
    return el("section", el("h2", "Account security"),
        el("p", "Confirm your current password to change your email or password."),
        action("Change email", "/account/profile/email", false),
        action("Change password", "/account/profile/password", false)).cls("profile-email-change");
  }
  static Element dialog(PageContext c) {
    ApplicationUser user = c.value("profile");
    if (user == null || Boolean.TRUE.equals(user.isEntraUser)) return null;
    boolean email = "email".equals(c.text("profileDialog"));
    return el(
            "div",
            el(
                    "dialog",
                    el(
                        "header",
                        el("span", "ACCOUNT SECURITY"),
                        action("×", "/account/profile/edit", false)
                            .cls("profile-dialog-close")
                            .attr("aria-label", "Close verification popup")),
                    c.model().get("message") == null
                        ? null
                        : el("p", c.text("message"))
                            .cls("profile-dialog-notice")
                            .attr("role", "status"),
                    c.model().get("profileError") != null
                        ? el("p", c.text("profileError")).cls("profile-error").attr("role", "alert")
                        : null,
                    email ? emailChange(c) : passwordChange(c))
                .flag("open", true)
                .attr("aria-modal", "true")
                .attr("aria-label", email ? "Change email" : "Change password")
                .cls("profile-security-dialog"))
        .cls("profile-security-overlay");
  }

  private static Element emailChange(PageContext c) {
    return el("section", el("h2", "Change email"),
        el("p", "Enter your new SAE email address and confirm your current password."),
        c.post("/account/profile/email/change", "profile-email-request",
            field("New SAE email address", input("newEmail", "email", "")
                .attr("placeholder", "name@sae-malaysia.com").attr("maxlength", 254)
                .attr("autocomplete", "email").flag("required", true)),
            field("Current password", input("currentPassword", "password", "")
                .attr("autocomplete", "current-password").attr("maxlength", 256).flag("required", true)),
            el("p", "Sign in again with your new email and existing password after saving. Your admin can review email changes."),
            el("div", action("Cancel", "/account/profile/edit", false), button("Change email", true))
                .cls("verification-actions")),
        el("p", "Only @sae-malaysia.com addresses are allowed. No verification email is sent."))
        .cls("profile-email-change");
  }

  private static Element passwordChange(PageContext c) {
    return el("section", el("h2", "Change password"),
        el("p", "Confirm your current password, then choose your new password."),
        c.post("/account/profile/password/update", "profile-password-change",
            field("Current password", input("currentPassword", "password", "")
                .attr("autocomplete", "current-password").attr("maxlength", 256).flag("required", true)),
            field("New password", input("password", "password", "")
                .attr("autocomplete", "new-password").attr("minlength", 12).attr("maxlength", 72).flag("required", true)),
            field("Confirm new password", input("confirmPassword", "password", "")
                .attr("autocomplete", "new-password").attr("minlength", 12).attr("maxlength", 72).flag("required", true)),
            el("p", "Use 12–72 characters including uppercase, lowercase, a number and a symbol."),
            el("div", action("Cancel", "/account/profile/edit", false), button("Change password", true))
                .cls("verification-actions")),
        el("p", "Sign in again after saving. No email or verification code is required."))
        .cls("profile-email-change");
  }
}
