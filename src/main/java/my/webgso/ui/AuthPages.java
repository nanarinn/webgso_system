package my.webgso.ui;

import static my.webgso.ui.Html.el;
import static my.webgso.ui.PageContext.*;

import my.webgso.ui.Html.Element;

/** Login, registration and error screens authored entirely as Java component trees. */
final class AuthPages {
  private AuthPages() {}

  private static Element authField(String label, String name, String type, String autocomplete) {
    var control =
        input(name, type, null)
            .attr("id", name)
            .attr("autocomplete", autocomplete)
            .flag("required", true);
    if (name.equals("identifier"))
      control.attr("maxlength", 256).attr("autocapitalize", "none").attr("spellcheck", "false");
    return el("div", el("label", label).attr("for", name), control).cls("auth-field");
  }

  static String login(PageContext c) {
    var brand =
        el(
                "a",
                el("img")
                    .attr("src", Branding.LOGO)
                    .attr("alt", Branding.LOGO_DESCRIPTION)
                    .cls("auth-brand-logo"),
                el("small", "SOLAR DATA MANAGEMENT"))
            .cls("auth-brand")
            .attr("href", "/login")
            .attr("aria-label", "GSO home");
    var story =
        el(
                "section",
                brand,
                el(
                        "div",
                        el("p", "FORECAST. CONNECT. MONITOR.").cls("auth-eyebrow"),
                        el("h1", "Clear visibility.", el("br"), el("span", "Across every horizon."))
                            .attr("id", "story-title"),
                        el(
                                "p",
                                "A connected workspace for solar forecasts, declared capacity",
                                el("br").cls("auth-desktop-break"),
                                " and meteorological data services.")
                            .cls("auth-description"),
                        el(
                                "div",
                                service("Rolling24", "Short-term forecasts"),
                                service("DDQ", "Capacity declarations"),
                                service("MMF", "Weather measurements"))
                            .cls("auth-services")
                            .attr("aria-label", "Repository services"))
                    .cls("auth-story-copy"),
                el(
                        "footer",
                        el("span", "GRID SYSTEM OPERATOR"),
                        el("span", "/").attr("aria-hidden", "true"),
                        el("span", "Solar plant data portal"))
                    .cls("auth-story-footer"),
                el("div").cls("auth-orbits").attr("aria-hidden", "true"))
            .cls("auth-story")
            .attr("aria-labelledby", "story-title");
    var access =
        el(
                "div",
                el("p", "ACCOUNT ACCESS").cls("auth-access-eyebrow"),
                el(
                        "nav",
                        link("Login", "/login").attr("aria-current", "page"),
                        link("Sign up", "/register"))
                    .cls("auth-tabs")
                    .attr("aria-label", "Account access"),
                el(
                        "header",
                        el("h2", "Sign in to your workspace").attr("id", "login-title"),
                        el("p", "Access your registered services, forecasts and API validations."))
                    .cls("auth-form-heading"))
            .cls("auth-access-content");
    if (c.parameter("error"))
      access.add(
          alert(
              "Sign-in failed. Check your username/email, password and account approval. If your"
                  + " account is locked, try again after 15 minutes.",
              "error"));
    if (c.parameter("registered"))
      access.add(
          alert(
              "Account created. An administrator must approve your access before you can sign in.",
              "success"));
    if (c.parameter("reason")) access.add(alert("Your session ended. Sign in to continue.", ""));
    if (c.parameter("logout")) access.add(alert("You have signed out successfully.", "success"));
    var identifier = authField("Username or email", "identifier", "text", "username");
    // Controls retain their native browser validation, password-manager support and keyboard
    // behavior.
    access.add(
        el("div", el("span", "Account credentials")).cls("auth-divider"),
        c.post(
            "/account/login",
            "auth-form",
            identifier,
            authField("Password", "password", "password", "current-password"),
            el("button", "Login").cls("auth-submit").attr("type", "submit")),
        el("p", "Don't have an account? ", link("Sign up", "/register")).cls("auth-signup"),
        el(
                "details",
                el("summary", "Information protection notice"),
                el(
                    "p",
                    "This application contains TNB information. Any modification, copying,"
                        + " reproduction, republication, uploading, posting, transmission or"
                        + " distribution of this information without prior written permission is"
                        + " prohibited."))
            .cls("auth-notice"),
        el("footer", "GRID SYSTEM OPERATOR · Forecast & meteorological services")
            .cls("auth-access-footer"));
    return JavaPages.document(
        "Sign in",
        "auth-page",
        el(
                "main",
                story,
                el("section", access).cls("auth-access").attr("aria-labelledby", "login-title"))
            .cls("auth-layout"),
        true,
        false);
  }

  private static Element service(String name, String description) {
    return el("div", el("strong", name), el("span", description));
  }

  private static Element alert(String text, String tone) {
    return el("div", text)
        .cls("auth-alert auth-alert-" + tone)
        .attr("role", tone.equals("error") ? "alert" : "status");
  }

  static String register(PageContext c) {
    var form =
        c.post(
            "/account/register",
            "java-form",
            field(
                "Display name",
                input("displayName", "text", null)
                    .attr("autocomplete", "name")
                    .attr("maxlength", 160)
                    .flag("required", true)),
            field(
                "Username",
                input("username", "text", null)
                    .attr("autocomplete", "username")
                    .attr("minlength", 3)
                    .attr("maxlength", 100)
                    .attr("pattern", "[a-zA-Z0-9_.@-]+")
                    .flag("required", true)),
            field(
                "Email",
                input("email", "email", null)
                    .attr("autocomplete", "email")
                    .attr("maxlength", 256)
                    .flag("required", true)),
            field(
                "Password",
                input("password", "password", null)
                    .attr("autocomplete", "new-password")
                    .attr("minlength", 12)
                    .attr("maxlength", 72)
                    .flag("required", true)),
            el("p", "Use 12–72 characters including uppercase, lowercase, a number and a symbol.")
                .cls("page-subtitle"),
            field(
                "Confirm password",
                input("confirmPassword", "password", null)
                    .attr("autocomplete", "new-password")
                    .flag("required", true)),
            button("Request access", true),
            link("Back to sign in", "/login"));
    return JavaPages.document(
        "Create an account",
        "",
        el(
                "main",
                el("img")
                    .cls("brand-logo")
                    .attr("src", Branding.LOGO)
                    .attr("alt", Branding.LOGO_DESCRIPTION),
                el("p", "Authorised access").cls("section-label"),
                el("h1", "Create an Admin account"),
                el(
                    "p",
                    "Your Admin account will remain pending until another Admin approves access."),
                form)
            .cls("registration-shell"),
        false,
        false);
  }

  static String error(PageContext c) {
    return JavaPages.document(
        "Request could not be completed",
        "",
        el(
                "main",
                el("p", Branding.REPOSITORY).cls("section-label"),
                el("h1", "Request could not be completed"),
                el("p", c.text("message")).attr("role", "alert"),
                action("Return to repository", "/", true),
                action("Sign in", "/login", false))
            .cls("registration-shell"),
        false,
        false);
  }
}
