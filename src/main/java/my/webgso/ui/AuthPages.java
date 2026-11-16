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
      control.attr("maxlength", 256).attr("autocapitalize", "none")
          .attr("spellcheck", "false").attr("placeholder", "you@sae-malaysia.com")
          .attr("pattern", "[A-Za-z0-9._+\\-]+@[Ss][Aa][Ee]-[Mm][Aa][Ll][Aa][Yy][Ss][Ii][Aa]\\.[Cc][Oo][Mm]|demo\\.admin@example\\.test")
          .attr("title", "Use your GSO work email ending in @sae-malaysia.com.");
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
    if (c.parameter("accountDeleted"))
      access.add(alert("Your account has been deleted. Please sign up again.", "error"));
    if (c.parameter("error"))
      access.add(
          alert(
              "Sign-in failed. Use your GSO email and check your password and account status. If your"
                  + " account is locked, try again after 15 minutes.",
              "error"));
    if (c.parameter("registered"))
      access.add(
          alert(
              "Account created. Sign in with your GSO email and temporary password CHANGEME. You must change it before using the system.",
              "success"));
    if (c.parameter("initialPasswordChanged"))
      access.add(alert("Password updated successfully. Sign in with your new password.", "success"));
    if (c.parameter("reason"))
      access.add(
          alert(
              "form-expired".equals(c.request().getParameter("reason"))
                  ? "Your sign-in form expired. Please enter your details and sign in again."
                  : "request-expired".equals(c.request().getParameter("reason"))
                      ? "Your API test form expired. Nothing was tested or saved. Sign in again and retry the test."
                      : "Your session ended. Sign in to continue.",
              ""));
    if (c.parameter("logout")) access.add(alert("You have signed out successfully.", "success"));
    if (c.parameter("emailChanged"))
      access.add(
          alert(
              "Your email address has been changed. Sign in with your new SAE"
                  + " address.",
              "success"));
    if (c.parameter("verification"))
      access.add(
          alert(
              "If this email has an unverified account, a verification link will be sent. Check"
                  + " your inbox and spam folder. Wait one minute before requesting another link.",
              "success"));
    if (c.parameter("passwordReset"))
      access.add(alert("Your password has been reset. Sign in with your new password.", "success"));
    var identifier = authField("GSO email address", "identifier", "email", "username");
    // Controls retain their native browser validation, password-manager support and keyboard
    // behavior.
    access.add(
        el("div", el("span", "Account credentials")).cls("auth-divider"),
        c.post(
            "/account/login",
            "auth-form",
            identifier,
            el("small", "Only GSO work email accounts are allowed. Personal and free email accounts are not accepted.").cls("signup-password-help"),
            authField("Password", "password", "password", "current-password"),
            el("button", "Login").cls("auth-submit").attr("type", "submit")),
        el("p", link("Forgot password?", "/account/forgot-password")).cls("auth-signup"),
        el("p", "Don't have an account? ", link("Sign up", "/register")).cls("auth-signup"),
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

  private static Element resendVerification(PageContext c) {
    return el("details", el("summary", "Resend verification email"),
        c.post("/account/resend-verification", "auth-form",
            field("GSO email address", input("email", "email", null)
                .attr("autocomplete", "email").attr("maxlength", 256).flag("required", true)),
            button("Send verification link", true).cls("auth-submit")))
        .cls("auth-signup");
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
            "java-form signup-form",
            field(
                    "GSO email address",
                    input("email", "email", null)
                        .attr("pattern", "[A-Za-z0-9._+\\-]+@[Ss][Aa][Ee]-[Mm][Aa][Ll][Aa][Yy][Ss][Ii][Aa]\\.[Cc][Oo][Mm]")
                        .attr("title", "Use your GSO work email ending in @sae-malaysia.com.")
                        .attr("autocomplete", "username")
                        .attr("maxlength", 256)
                        .attr("placeholder", "you@sae-malaysia.com")
                        .flag("required", true))
                .cls("signup-wide"),
            el("small", "Use your @sae-malaysia.com work email address to register. Personal and free email addresses are not accepted.")
                .cls("signup-password-help signup-wide"),
            el("p", "Your temporary password is CHANGEME. After signing in, create and confirm a new password. You will then sign in again with your new password.")
                .cls("signup-password-help signup-wide"),
            button("Create account", true).cls("primary-action signup-submit signup-wide"),
            el("p", "Already have an account? ", link("Sign in", "/login"))
                .cls("signup-signin signup-wide"));
    var story =
        el(
                "aside",
                link("", "/login")
                    .add(
                        el("img").attr("src", Branding.LOGO).attr("alt", Branding.LOGO_DESCRIPTION))
                    .cls("signup-logo"),
                el(
                        "div",
                        el("p", "GSO WEB SERVICES").cls("signup-eyebrow"),
                        el(
                            "h2",
                            "One workspace.",
                            el("br"),
                            el("span", "Connected possibilities.")),
                        el(
                                "p",
                                "Your access to plant connections, API testing and data quality"
                                    + " insights.")
                            .cls("signup-story-description"))
                    .cls("signup-story-copy"),
                el(
                        "ol",
                        signupStep(
                            "01",
                            "Create your account",
                            "Register with your GSO work email."),
                        signupStep(
                            "02", "First sign-in", "Use your GSO email and temporary password CHANGEME."),
                        signupStep(
                            "03", "Secure your account", "Create a new password, then sign in again to access your workspace."))
                    .cls("signup-steps"),
                el("p", "GRID SYSTEM OPERATOR", el("span", "Solar data management"))
                    .cls("signup-story-footer"))
            .cls("signup-story");
    var card =
        el(
                "section",
                el(
                        "header",
                        el("p", "ACCOUNT REGISTRATION").cls("signup-eyebrow"),
                        el("h1", "Create your account"),
                        el("p", "Get started with your GSO work email."))
                    .cls("signup-heading"),
                form)
            .cls("signup-card")
            .attr("aria-label", "Account registration");
    return JavaPages.document(
        "Create an account",
        "register-page",
        el("main", story, card).cls("signup-layout"),
        false,
        false);
  }

  static String verification(PageContext c) {
    String result = c.text("verificationResult");
    String token = c.text("verificationToken");
    boolean success = result.equals("success");
    boolean landing = result.isBlank() && !token.isBlank();
    return JavaPages.document(
        "Verify your email",
        "register-page",
        el(
                "main",
                el("img")
                    .attr("src", Branding.LOGO)
                    .attr("alt", Branding.LOGO_DESCRIPTION)
                    .cls("verification-logo"),
                el("p", "GSO WEB SERVICES").cls("section-label"),
                el(
                    "h1",
                    success
                        ? "Email verified"
                        : landing ? "Verify your email" : "Verification link unavailable"),
                el(
                    "p",
                    success
                        ? "Your account is active. You can now sign in with your email and"
                            + " password."
                        : landing
                            ? "Confirm your email address to activate your account. This will not"
                                + " sign you in automatically."
                            : "This link is invalid, expired or already used. Sign in if your"
                                + " account is already verified, or request a new link."),
                landing
                    ? c.post(
                        "/account/verify-email",
                        "java-actions",
                        input("token", "hidden", token),
                        button("Verify email", true))
                    : null,
                !success && !landing ? resendVerification(c) : null,
                action("Back to sign in", "/login", !landing))
            .cls("registration-shell verification-shell"),
        false,
        false);
  }

  private static Element signupStep(String number, String title, String detail) {
    return el(
        "li",
        el("span", number).cls("signup-step-number").attr("aria-hidden", "true"),
        el("div", el("strong", title), el("p", detail)));
  }

  static String error(PageContext c) {
    return JavaPages.document(
        "Request could not be completed",
        "",
        el(
                "main",
                el("p", Branding.REPOSITORY).cls("section-label"),
                action("Return to web services", "/", true),
                RequestErrorPopup.render(c.text("message"), true))
            .cls("registration-shell"),
        false,
        false);
  }
}

