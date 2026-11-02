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
                          el("dt", "Email address"),
                          el("dd", user.email),
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
                  action("Email settings", "/account/email-settings", false)),
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
                    field(
                        "Email address",
                        input("email", "email", values.get("email"))
                            .attr("maxlength", 256)
                            .attr("autocomplete", "email")
                            .flag("readonly", true)
                            .flag("required", true)),
                    field(
                        "Phone number (optional)",
                        input("phone", "tel", values.get("phone"))
                            .attr("maxlength", 30)
                            .attr("autocomplete", "tel")))
                .cls("profile-fields"),
            el("p", "Use Change email below to verify a new Gmail address.")
                .cls("profile-form-note"),
            el("footer", action("Cancel", "/account/profile", false), button("Save changes", true))
                .cls("profile-form-footer"));
    return el(
            "section",
            heading(
                "Account",
                "My Profile",
                "View and manage your personal information.",
                action("Email settings", "/account/email-settings", false)),
            el(
                    "div",
                    summary,
                    el("article", form, managed ? null : emailChange(c))
                        .cls("panel profile-edit-panel"))
                .cls("profile-layout"))
        .cls("profile-page");
  }

  private static Element emailChange(PageContext c) {
    String pending = c.text("pendingEmail");
    return el(
            "section",
            el("h2", "Change email"),
            el(
                "p",
                "Your current email stays active until the code sent to your new Gmail address is"
                    + " verified."),
            c.post(
                "/account/profile/email/send-code",
                "profile-email-request",
                field(
                    "New Gmail address",
                    input("newEmail", "email", pending)
                        .attr("placeholder", "you@gmail.com")
                        .attr("maxlength", 256)
                        .attr("autocomplete", "email")
                        .flag("required", true)),
                field(
                    "Current password",
                    input("currentPassword", "password", "")
                        .attr("autocomplete", "current-password")
                        .flag("required", true)),
                button(pending.isBlank() ? "Send code" : "Resend code", true)),
            pending.isBlank()
                ? null
                : c.post(
                    "/account/profile/email/verify",
                    "profile-email-verify",
                    el(
                        "p",
                        "Enter the 6-digit code sent to "
                            + pending
                            + ". The code expires in 10 minutes."),
                    field(
                        "Verification code",
                        input("code", "text", "")
                            .attr("inputmode", "numeric")
                            .attr("pattern", "[0-9]{6}")
                            .attr("maxlength", 6)
                            .attr("autocomplete", "one-time-code")
                            .flag("required", true)),
                    button("Verify & change email", true)),
            el("p", "After verification, sign in again using your new Gmail address."))
        .cls("profile-email-change");
  }
}
