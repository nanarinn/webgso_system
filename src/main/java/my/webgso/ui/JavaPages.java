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
    if (name.equals("email-verification")) return AuthPages.verification(c);
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
          case "api-quick-test" -> QuickApiTestPage.render(c);
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
            ? List.of("/styles/login.css?v=7")
            : List.of(
                "/bootstrap/bootstrap.min.css",
                "/styles/app.css",
                "/styles/interface.css",
                "/styles/java.css",
                "/styles/workspace-theme.css?v=4",
                "/styles/workspace-panels.css?v=28",
                "/styles/plant-editor.css?v=4",
                "/styles/workspace-polish.css?v=2",
                "/styles/workspace-finish.css?v=1",
                "/styles/signup.css?v=3",
                "/styles/plant-filters.css?v=3");
    styles.forEach(path -> head.add(el("link").attr("rel", "stylesheet").attr("href", path)));
    if (!login)
      head.add(el("script").attr("src", "/scripts/plant-api-loading.js?v=1").flag("defer", true));
    if (!login)
      head.add(
          el("script").attr("src", "/scripts/plant-location-filters.js?v=1").flag("defer", true));
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
    var link =
        el("a", navigationIcon(href), el("span", title.replaceFirst("^[◫↗☀▤⇣]\\s*", "")))
            .attr("href", href)
            .cls("nav-link");
    String current = c.request().getRequestURI();
    if (current.equals(href) || (!href.equals("/") && current.startsWith(href + "/")))
      link.attr("aria-current", "page");
    return link;
  }

  private static Element navigationIcon(String href) {
    String path =
        switch (href) {
          case "/" -> "M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z";
          case "/ddq-analytics" -> "M4 4v16h16 M7 15l4-5 4 3 5-7";
          case "/mmf-analytics" ->
              "M12 3v2 M12 19v2 M3 12h2 M19 12h2 M5.6 5.6L7 7 M17 17l1.4 1.4 M5.6 18.4L7 17 M17"
                  + " 7l1.4-1.4 M12 7a5 5 0 1 0 0 10a5 5 0 0 0 0-10";
          case "/admin/lss-plants" -> "M4 5h16v12H4z M4 11h16 M9 5v12 M15 5v12 M12 17v4 M8 21h8";
          case "/admin/collection-schedules" ->
              "M5 5h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2 M7 3v4 M17"
                  + " 3v4 M3 11h18 M8 15h2 M14 15h2";
          case "/admin/data-quality" -> "M12 3l8 3v6c0 5-8 9-8 9s-8-4-8-9V6z M8 12l3 3 5-6";
          case "/account/profile" -> "M12 3a4 4 0 1 0 0 8a4 4 0 0 0 0-8 M4 21v-2a8 6 0 0 1 16 0v2";
          case "/collection-management" -> "M4 4h16v6H4z M4 14h16v6H4z M8 7h1 M8 17h1";
          default -> "M6 3h8l4 4v14H6z M14 3v5h4 M9 12h6 M9 16h6";
        };
    return el("svg", el("path").attr("d", path))
        .attr("viewBox", "0 0 24 24")
        .attr("fill", "none")
        .attr("stroke", "currentColor")
        .attr("stroke-width", "1.6")
        .attr("stroke-linecap", "round")
        .attr("stroke-linejoin", "round")
        .attr("aria-hidden", "true")
        .attr("focusable", "false")
        .cls("workspace-nav-icon");
  }

  private static Element shell(PageContext c, Element content) {
    Accounts.Principal account = c.value("account");
    var workspaceNav = el("nav", navigation(c, "◫ Overview", "/"));
    var nav =
        el("div", el("p", "Workspace").cls("nav-section-title"), workspaceNav).cls("nav-body");
    if (c.yes("isAdmin")) {
      for (Resources.Definition item : c.<Resources.Definition>list("navigation")) {
        if (Set.of("/admin/report-automation", "/admin/collection-schedules").contains(item.path()))
          continue;
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
        DatabaseStatusCard.render(c),
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
                        el(
                                "a",
                                el(
                                        "span",
                                        account.displayName().isBlank()
                                            ? "A"
                                            : account
                                                .displayName()
                                                .substring(0, 1)
                                                .toUpperCase(Locale.ROOT))
                                    .cls("workspace-avatar")
                                    .attr("aria-hidden", "true"),
                                el(
                                    "span",
                                    el("strong", account.displayName()),
                                    el("small", "Administrator")))
                            .attr("href", "/account/profile")
                            .cls("workspace-profile")
                            .attr("aria-label", "My profile: " + account.displayName()),
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
