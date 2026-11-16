package my.webgso.ui;

import static my.webgso.ui.Html.el;
import static my.webgso.ui.PageContext.*;

import my.webgso.EmailPreferences;
import my.webgso.ui.Html.Element;

final class AccountToolsPages {
  private AccountToolsPages() {}

  static String firstPassword(PageContext c) {
    var card = el("main",
      el("img").attr("src", Branding.LOGO).attr("alt", Branding.LOGO_DESCRIPTION).cls("account-tools-logo"),
      el("p", "ACCOUNT SECURITY").cls("section-label"), el("h1", "Set your password"),
      el("p", "Replace your temporary password before using GSO Web Services. After saving, sign in again with your new password."));
    if (!c.text("resetError").isBlank()) card.add(el("p", c.text("resetError")).cls("profile-error").attr("role", "alert"));
    card.add(c.post("/account/first-password", "account-tools-form",
      field("New password", input("password", "password", null).attr("autocomplete", "new-password").attr("minlength", 12).attr("maxlength", 72).flag("required", true)),
      field("Confirm new password", input("confirmPassword", "password", null).attr("autocomplete", "new-password").attr("minlength", 12).attr("maxlength", 72).flag("required", true)),
      el("small", "Use 12–72 characters including uppercase, lowercase, a number and a symbol."),
      button("Save password and sign out", true)),
      c.post("/account/logout", "account-tools-form", button("Sign out", false)));
    return JavaPages.document("Set your password", "", card.cls("registration-shell account-tools-auth"), false, false);
  }

  static String password(PageContext c, boolean reset) {
    var card =
        el(
            "main",
            el("img")
                .attr("src", Branding.LOGO)
                .attr("alt", Branding.LOGO_DESCRIPTION)
                .cls("account-tools-logo"),
            el("p", "ACCOUNT SECURITY").cls("section-label"),
            el("h1", reset ? "Choose a new password" : "Forgot password?"),
            el(
                "p",
                reset
                    ? "Use a unique password to protect your GSO account."
                    : "Confirm your current password, then choose and confirm your new password. No email or verification code is required."));
    if (!c.text("resetError").isBlank())
      card.add(el("p", c.text("resetError")).cls("profile-error").attr("role", "alert"));
    if (reset) {
      if (!c.text("token").isBlank())
        card.add(
            c.post(
                "/account/reset-password",
                "account-tools-form",
                input("token", "hidden", c.text("token")),
                field(
                    "New password",
                    input("password", "password", null)
                        .attr("autocomplete", "new-password")
                        .attr("minlength", 12)
                        .attr("maxlength", 72)
                        .flag("required", true)),
                field(
                    "Confirm password",
                    input("confirmPassword", "password", null)
                        .attr("autocomplete", "new-password")
                        .attr("minlength", 12)
                        .attr("maxlength", 72)
                        .flag("required", true)),
                el(
                    "small",
                    "Use 12–72 characters including uppercase, lowercase, a number and a symbol."
                        + " This link expires after 30 minutes."),
                button("Reset password", true)));
      card.add(action("Request a new link", "/account/forgot-password", false));
    } else
      card.add(
          c.post(
              "/account/forgot-password",
              "account-tools-form",
              field(
                  "Account ID",
                  input("identifier", "text", null)
                      .attr("placeholder", "Enter your sign-in account ID")
                      .attr("autocomplete", "username")
                      .attr("maxlength", 256)
                      .flag("required", true)),
              field("Current password", input("currentPassword", "password", null)
                  .attr("autocomplete", "current-password").attr("maxlength", 256).flag("required", true)),
              field("New password", input("password", "password", null)
                  .attr("autocomplete", "new-password").attr("minlength", 12).attr("maxlength", 72).flag("required", true)),
              field("Confirm new password", input("confirmPassword", "password", null)
                  .attr("autocomplete", "new-password").attr("minlength", 12).attr("maxlength", 72).flag("required", true)),
              el("small", "Use 12–72 characters including uppercase, lowercase, a number and a symbol."),
              button("Change password", true)));
    card.add(link("Back to sign in", "/login"));
    return JavaPages.document(
        reset ? "Reset password" : "Forgot password",
        "",
        card.cls("registration-shell account-tools-auth"),
        false,
        false);
  }

  static Element settings(PageContext c) {
    EmailPreferences.Settings s = c.value("emailPreferences");
    return el(
        "section",
        heading(
            "Your account",
            "Email settings",
            "Choose the notifications sent to your Gmail address.",
            action("My Profile", "/account/profile", false)),
        el(
                "article",
                el("h2", "Notification preferences"),
                el("p", "Recipient: " + c.text("emailRecipient")),
                c.post(
                    "/account/email-settings",
                    "account-tools-form",
                    option(
                        "failure",
                        "API failure alerts",
                        "Notify me when an API first fails or changes from success to failure.",
                        s.failure()),
                    option(
                        "recovery",
                        "API recovery alerts",
                        "Notify me when a failed API works again.",
                        s.recovery()),
                    option(
                        "stale",
                        "Stale data warnings",
                        "Notify me when an API starts returning old MMF data or expired DDQ"
                            + " forecasts.",
                        s.stale()),
                    option(
                        "excel",
                        "Automatic Excel reports",
                        "Email the Excel test summary when all APIs in my test run succeed.",
                        s.excel()),
                    option(
                        "digest",
                        "Daily summary",
                        "Email the latest API status summary at 09:00 MYT while the server is"
                            + " running.",
                        s.digest()),
                    el(
                        "p",
                        "Account verification and password reset emails are always enabled. Turning"
                            + " off automatic reports does not disable the Email Excel button."),
                    button("Save email settings", true)))
            .cls("panel account-tools-card"),
        el(
                "article",
                el("h2", "Database backup"),
                el("p", c.text("backupStatus")).attr("role", "status"),
                el(
                    "p",
                    "Daily local backup while the server runs. The seven latest completed copies"
                        + " are kept. Backups are stored on this server and are not sent by"
                        + " email."))
            .cls("panel account-tools-card"));
  }

  private static Element option(String name, String title, String detail, boolean checked) {
    return el(
            "label",
            input(name, "checkbox", "true").flag("checked", checked),
            el("span", el("strong", title), el("small", detail)))
        .cls("account-tools-option");
  }

  static Element guide(PageContext c) {
    return el(
        "section",
        heading(
            "Support",
            "User Guide",
            "A quick guide to using GSO Web Services.",
            action("Plant Config", "/admin/lss-plants", false)),
        step(
            "01",
            "Create your account",
            "Register with your own Gmail address, then open the verification email and select"
                + " Verify email. You can verify on your phone using the public HTTPS link."
                + " Temporary links require the server laptop to stay on."),
        step(
            "02",
            "Add a plant and its APIs",
            "Open Plant Config → Add record. Use a unique plant name and set its state, zone and"
                + " capacity. Add only the DDQ or MMF URLs available for this plant. Test & Save"
                + " API checks the response before saving."),
        step(
            "03",
            "Test a saved API",
            "Select Test next to the API name, or Test all APIs for a batch. The loading screen"
                + " shows that the request is running. Do not repeatedly press Test. Next-test"
                + " timing follows any active Collection Schedule; if none exists, the API is"
                + " available for another test."),
        step(
            "04",
            "Understand the result",
            "Success means the API responded with readable data. It does not guarantee current"
                + " data: an amber warning appears for MMF older than 60 minutes or DDQ forecast"
                + " coverage that has ended. Failed tests show the connection or validation reason."
                + " Preview response shows bounded, redacted raw data."),
        step(
            "05",
            "Review history and export",
            "API test history shows the latest result for each plant and API. Filter to find a"
                + " plant or failed API, then retry when appropriate. A batch report can be"
                + " downloaded or emailed as Excel; automatic Excel emails follow your"
                + " preferences."),
        step(
            "06",
            "Manage email and password",
            "My Profile → Email settings controls API notifications. Forgot password on the login"
                + " page sends a one-use, 30-minute reset link. Resetting your password signs out"
                + " existing sessions."),
        step(
            "07",
            "Backups and recovery",
            "Check database backup status in My Profile → Email settings. The server stores up to"
                + " seven daily PostgreSQL archives. Keep a secure copy off this computer for"
                + " disaster recovery. A trusted maintainer should follow docs/ACCOUNT_TOOLS.md to"
                + " restore into a separate database first."));
  }

  private static Element step(String n, String title, String text) {
    return el(
            "article", el("span", n).cls("guide-number"), el("div", el("h2", title), el("p", text)))
        .cls("panel account-guide-step");
  }
}
