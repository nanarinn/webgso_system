package my.webgso.ui;

import static my.webgso.ui.Html.el;
import static my.webgso.ui.PageContext.*;

import java.util.*;
import my.webgso.*;
import my.webgso.Models.*;
import my.webgso.ui.Html.Element;

/** The shared application shell and the complete Java page registry. */
public final class JavaPages {
  private JavaPages() {}

  public static String render(String name, PageContext c) {
    if (name.equals("login")) return AuthPages.login(c);
    if (name.equals("register")) return AuthPages.register(c);
    if (name.equals("error-page")) return AuthPages.error(c);
    if (name.equals("batch-progress"))
      return document("Batch progress", "", OperationsPages.batchProgress(c), false, refresh(c));
    Element content =
        switch (name) {
          case "dashboard" -> OverviewPages.dashboard(c);
          case "admin-overview" -> OverviewPages.admin(c);
          case "analytics" -> OperationsPages.analytics(c);
          case "collections" -> OperationsPages.collections(c);
          case "collection" -> OperationsPages.collection(c);
          case "reports" -> OperationsPages.reports(c);
          case "resource" -> AdminPages.resource(c);
          case "edit" -> AdminPages.edit(c);
          case "users" -> AdminPages.users(c);
          case "profile" -> ProfilePages.profile(c);
          case "api-history" -> ApiHistoryPage.render(c);
          case "api-retest-progress" -> ApiRetestPage.render(c);
          default -> throw new IllegalArgumentException("Unknown page");
        };
    return document(
        c.text("title"),
        "",
        shell(c, content),
        false,
        name.equals("api-retest-progress")
            && !c.<ApiRetestService.Job>value("apiRetestJob").complete);
  }

  private static boolean refresh(PageContext c) {
    ManualBatchService.Batch batch = c.value("batch");
    return batch != null && !batch.complete();
  }

  static String document(
      String title, String bodyClass, Element content, boolean login, boolean refresh) {
    var head =
        el(
            "head",
            el("meta").attr("charset", "utf-8"),
            el("meta")
                .attr("name", "viewport")
                .attr("content", "width=device-width,initial-scale=1"),
            el("title", title + " | " + Branding.REPOSITORY),
            el("link").attr("rel", "icon").attr("type", "image/png").attr("href", Branding.LOGO));
    List<String> styles =
        login
            ? List.of("/styles/login.css?v=2")
            : List.of(
                "/bootstrap/bootstrap.min.css",
                "/styles/app.css",
                "/styles/interface.css",
                "/styles/java.css",
                "/styles/workspace-theme.css?v=4",
                "/styles/workspace-panels.css?v=22");
    styles.forEach(path -> head.add(el("link").attr("rel", "stylesheet").attr("href", path)));
    if (!login)
      head.add(el("script").attr("src", "/scripts/plant-api-loading.js?v=1").flag("defer", true));
    if (refresh) head.add(el("meta").attr("http-equiv", "refresh").attr("content", "5"));
    return "<!doctype html>"
        + el("html", head, el("body", content, login ? null : apiLoading()).cls(bodyClass))
            .attr("lang", "en")
            .render();
  }

  private static Element apiLoading() {
    return el(
            "dialog",
            el("span").cls("api-loading-spinner").attr("aria-hidden", "true"),
            el("h2", "Testing API…").attr("id", "api-loading-title"),
            el("p", "Please wait while we test the selected API.")
                .attr("id", "api-loading-message"))
        .attr("id", "api-loading")
        .cls("api-loading")
        .attr("aria-labelledby", "api-loading-title")
        .attr("aria-describedby", "api-loading-message");
  }

  private static Element navigation(PageContext c, String title, String href) {
    var link = link(title, href).cls("nav-link");
    if (c.request().getRequestURI().equals(href)) link.attr("aria-current", "page");
    return link;
  }

  private static Element shell(PageContext c, Element content) {
    Accounts.Principal account = c.value("account");
    var workspaceNav =
        el(
            "nav",
            navigation(c, "◫ Overview", "/"),
            navigation(c, "↗ DDQ Analytics", "/ddq-analytics"),
            navigation(c, "☀ MMF Analytics", "/mmf-analytics"),
            navigation(c, "▤ On-Demand Analysis", "/on-demand-analysis"),
            navigation(c, "⇣ Collection Management", "/collection-management"));
    var nav =
        el("div", el("p", "Workspace").cls("nav-section-title"), workspaceNav).cls("nav-body");
    if (c.yes("isAdmin")) {
      for (Resources.Definition item : c.<Resources.Definition>list("navigation")) {
        workspaceNav.add(navigation(c, item.title(), item.path()));
      }
      workspaceNav.add(navigation(c, "My Profile", "/account/profile"));
    }
    nav.add(
        el("div").cls("nav-spacer"),
        el(
                "div",
                el("span").cls("status-light"),
                el("span", el("strong", Branding.NAME), el("small", "Web Services")))
            .cls("sidebar-status"),
        el(
                "footer",
                el(
                        "div",
                        el("img").attr("src", "/sae-green-logo.png").attr("alt", "SAE Green"),
                        el(
                            "span",
                            el("strong", "SAE Green Sdn. Bhd."),
                            el("small", "Technology & delivery partner")))
                    .cls("footer-brand"))
            .cls("system-footer system-footer-sidebar"));
    var sidebar =
        el(
                "aside",
                el(
                        "div",
                        el("img")
                            .cls("brand-logo")
                            .attr("src", Branding.LOGO)
                            .attr("alt", Branding.LOGO_DESCRIPTION),
                        el("span", el("small", "GSO Web Services")))
                    .cls("brand-lockup sidebar-brand"),
                input("navigation", "checkbox", "on")
                    .attr("id", "mobile-nav")
                    .cls("nav-toggle")
                    .attr("aria-label", "Navigation menu"),
                el("label", "☰ Menu").attr("for", "mobile-nav").cls("mobile-nav-label"),
                nav)
            .cls("app-sidebar");
    var notifications = el("div").cls("notification-panel");
    for (UserAccessNotification item : c.<UserAccessNotification>list("notifications"))
      notifications.add(
          el(
              "article",
              el("strong", item.subject),
              el("p", item.body),
              item.readAt == null
                  ? c.post("/notifications/" + item.id + "/read", "", button("Mark read", false))
                  : null));
    if (c.list("notifications").isEmpty()) notifications.add(el("p", "No notifications"));
    var topbar =
        el(
                "header",
                el("div", el("span"), " GSO Web Services").cls("environment-badge"),
                el(
                        "div",
                        el(
                                "details",
                                el(
                                    "summary",
                                    "Notifications (" + c.list("notifications").size() + ")"),
                                notifications)
                            .cls("notification-menu"),
                        el("span", account.displayName()),
                        c.post("/account/logout", "", button("Sign out", false)))
                    .cls("topbar-account"))
            .cls("app-topbar");
    var mainContent =
        el(
                "article",
                c.model().get("message") == null
                    ? null
                    : el("div", c.text("message")).cls("page-inline-notice").attr("role", "status"),
                content)
            .attr("id", "main-content")
            .attr("tabindex", "-1")
            .cls("app-content");
    return el(
        "div",
        link("Skip to content", "#main-content").cls("skip-link"),
        el("div", sidebar, el("main", topbar, mainContent).cls("app-main")).cls("app-shell"),
        c.model().get("apiTestMessage") == null ? null : ApiTestPopup.render(c));
  }
}
