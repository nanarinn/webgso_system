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
              el(
                      "div",
                      summary,
                      el("div", details, googleConnection(c, user)).cls("profile-detail-stack"))
                  .cls("profile-layout"))
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
                    el(
                            "article",
                            form,
                            managed ? null : securityActions(),
                            googleConnection(c, user))
                        .cls("panel profile-edit-panel"))
                .cls("profile-layout"))
        .cls("profile-page");
  }

  private static Element securityActions() {
    return el(
            "section",
            el("h2", "Account security"),
            el("p", "Manage your sign-in details with email verification."),
            el(
                    "div",
                    el(
                        "div",
                        el("strong", "Email address"),
                        el("p", "Verify a new Gmail address before changing it."),
                        action("Change email", "/account/profile/email", false)),
                    el(
                        "div",
                        el("strong", "Password"),
                        el("p", "Confirm your identity and choose a new password."),
                        action("Change password", "/account/profile/password", false)))
                .cls("profile-security-actions"))
        .cls("profile-email-change");
  }

  private static Element googleConnection(PageContext c, ApplicationUser user) {
    boolean linked = c.yes("googleLinked");
    String result = c.request().getParameter("googleLink");
    return el(
            "section",
            el(
                    "div",
                    el("span", "G").cls("profile-google-icon"),
                    el(
                        "div",
                        el("h2", "Google account"),
                        el("p", "Choose how you sign in to GSO.")))
                .cls("profile-google-heading"),
            result == null
                ? null
                : el(
                        "p",
                        result.equals("linked")
                            ? "Google account linked successfully. You can now use Google or your"
                                  + " existing GSO password."
                            : "Google account was not linked. Choose the same Gmail address as your"
                                  + " profile and try again.")
                    .cls(result.equals("linked") ? "account-tools-notice" : "profile-error")
                    .attr("role", "status"),
            linked
                ? el(
                        "div",
                        el("strong", "Google connected"),
                        el("p", user.email),
                        el(
                            "small",
                            "Your existing GSO password remains available if you have set one."))
                    .cls("profile-google-connected")
                : el(
                    "div",
                    el(
                        "p",
                        "Link "
                            + user.email
                            + " to sign in with Google without creating another GSO account."),
                    c.post(
                        "/account/profile/google/link",
                        "account-tools-form",
                        field(
                            "Current GSO password",
                            input("currentPassword", "password", null)
                                .attr("autocomplete", "current-password")
                                .flag("required", true)),
                        el(
                            "small",
                            "Next, choose the Google account with the same Gmail address. Your"
                                + " password will stay unchanged."),
                        button("Link Google account", true))))
        .cls("panel profile-google-panel");
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
                    email && c.model().get("profileError") != null
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
    String pending = c.text("pendingEmail");
    return el(
            "section",
            el("h2", "Change email"),
            verificationSteps(pending.isBlank() ? 1 : 2),
            el(
                "p",
                "Your current email stays active until the code sent to your new Gmail address is"
                    + " verified."),
            el(
                    "details",
                    el(
                        "summary",
                        pending.isBlank()
                            ? "Enter your new email"
                            : "Didn't receive a code? Resend"),
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
                        button(pending.isBlank() ? "Send code" : "Resend code", true)))
                .cls("verification-resend")
                .flag("open", pending.isBlank()),
            pending.isBlank()
                ? null
                : verificationForm(
                    c,
                    "/account/profile/email/verify",
                    "profile-email-verify",
                    pending,
                    "Verify & change email"),
            el("p", "After verification, sign in again using your new Gmail address."))
        .cls("profile-email-change");
  }

  private static Element passwordChange(PageContext c) {
    ApplicationUser user = c.value("profile");
    String stage = c.text("passwordChangeStage");
    return el(
            "section",
            el("h2", "Change password"),
            verificationSteps("change".equals(stage) ? 3 : "verify".equals(stage) ? 2 : 1),
            el(
                "p",
                "Confirm your current password, verify the code sent to your account email, then"
                    + " choose a new password."),
            c.model().get("passwordChangeError") == null
                ? null
                : el("p", c.text("passwordChangeError")).cls("profile-error").attr("role", "alert"),
            "change".equals(stage)
                ? c.post(
                    "/account/profile/password/change",
                    "profile-password-change",
                    el("p", "Email verified. Enter your new password below."),
                    field(
                        "New password",
                        input("password", "password", "")
                            .attr("autocomplete", "new-password")
                            .attr("minlength", 12)
                            .attr("maxlength", 72)
                            .flag("required", true)),
                    field(
                        "Confirm new password",
                        input("confirmPassword", "password", "")
                            .attr("autocomplete", "new-password")
                            .attr("minlength", 12)
                            .attr("maxlength", 72)
                            .flag("required", true)),
                    el(
                        "p",
                        "Use 12–72 characters including uppercase and lowercase letters, a number"
                            + " and a symbol."),
                    button("Change password", true))
                : el(
                    "div",
                    el(
                            "details",
                            el(
                                "summary",
                                "verify".equals(stage)
                                    ? "Didn't receive a code? Resend"
                                    : "Confirm your identity"),
                            c.post(
                                "/account/profile/password/send-code",
                                "profile-password-request",
                                field(
                                    "Current password",
                                    input("currentPassword", "password", "")
                                        .attr("autocomplete", "current-password")
                                        .attr("maxlength", 72)
                                        .flag("required", true)),
                                el("p", "A verification code will be sent to " + user.email + "."),
                                button("verify".equals(stage) ? "Resend code" : "Send code", true)))
                        .cls("verification-resend")
                        .flag("open", !"verify".equals(stage)),
                    !"verify".equals(stage)
                        ? null
                        : verificationForm(
                            c,
                            "/account/profile/password/verify",
                            "profile-password-verify",
                            user.email,
                            "Verify code")),
            el("p", "After changing your password, sign in again with your new password."))
        .cls("profile-email-change")
        .attr("id", "change-password");
  }

  private static Element verificationSteps(int current) {
    var steps = el("ol").cls("verification-steps").attr("aria-label", "Verification progress");
    String[] labels = {"Confirm identity", "Verify email", "Update account"};
    for (int i = 1; i <= 3; i++) {
      steps.add(
          el("li", el("span", i < current ? "✓" : Integer.toString(i)), el("small", labels[i - 1]))
              .cls(i == current ? "is-current" : i < current ? "is-complete" : "")
              .attr("aria-current", i == current ? "step" : null));
    }
    return steps;
  }

  private static Element verificationForm(
      PageContext c, String path, String id, String email, String submit) {
    return c.post(
            path,
            id,
            el("div", "✉").cls("verification-mail-icon").attr("aria-hidden", "true"),
            el("p", "EMAIL VERIFICATION").cls("verification-eyebrow"),
            el("h3", "Check your inbox"),
            el("p", "Enter the code sent to your Gmail address.").cls("verification-description"),
            el("div", el("span", "Sent to"), el("strong", email)).cls("verification-recipient"),
            el("div", el("span", "6-digit code"), el("span", "Valid for 10 minutes"))
                .cls("verification-meta"),
            field(
                "Verification code",
                input("code", "text", "")
                    .cls("verification-code-input")
                    .attr("placeholder", "000000")
                    .attr("inputmode", "numeric")
                    .attr("pattern", "[0-9]{6}")
                    .attr("minlength", 6)
                    .attr("maxlength", 6)
                    .attr("autocomplete", "one-time-code")
                    .attr("spellcheck", "false")
                    .attr("aria-describedby", id + "-hint")
                    .flag("required", true)),
            el(
                    "p",
                    "Check your Spam folder if the email hasn't arrived. Use the latest code"
                        + " received.")
                .cls("verification-help")
                .attr("id", id + "-hint"),
            el("div", button(submit, true), action("Cancel", "/account/profile/edit", false))
                .cls("verification-actions"),
            el("p", "Your account details stay unchanged until verification is complete.")
                .cls("verification-assurance"))
        .cls("verification-card");
  }
}
