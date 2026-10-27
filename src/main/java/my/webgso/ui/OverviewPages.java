package my.webgso.ui;

import static my.webgso.ui.Html.el;
import static my.webgso.ui.PageContext.*;

import java.util.*;
import my.webgso.Models.*;
import my.webgso.ui.Html.Element;

/** Staff dashboard and Super Administrator console, composed in Java. */
final class OverviewPages {
  private OverviewPages() {}

  static Element admin(PageContext c) {
    var cards =
        el(
                "div",
                apiCard(
                    "All API",
                    c.number("apiCount"),
                    "Total registered plant API connections",
                    "/admin/lss-plants"),
                apiCard(
                    "Active / Success",
                    c.number("apiSuccessCount"),
                    "Last test passed",
                    "/?apiStatus=success#latest-api-activity"),
                apiCard(
                    "Inactive / Failed",
                    c.number("apiFailedCount"),
                    "Last test failed",
                    "/?apiStatus=failed#latest-api-activity"))
            .cls("admin-api-metrics");
    var rows = new ArrayList<Element>();
    for (Map<String, Object> api : c.<Map<String, Object>>list("apiActivity")) {
      var tested =
          api.get("testedAt") == null
              ? "Not tested"
              : date(
                      (java.time.temporal.TemporalAccessor) api.get("testedAt"),
                      "dd MMM yyyy, HH:mm")
                  + " MYT";
      String dataUrl = Objects.toString(api.get("dataUrl"), "");
      rows.add(
          row(
              el("strong", api.get("name")).add(el("small", api.get("plantCode"))),
              api.get("owner"),
              api.get("category"),
              el("span", api.get("status")).cls("admin-api-status " + api.get("tone")),
              tested,
              dataUrl.isBlank()
                  ? el("span", "No saved data").cls("admin-no-data")
                  : action("View data", dataUrl, false)));
    }
    return el(
            "section",
            el(
                    "div",
                    el("p", el("span", "Workspace /"), " Admin overview"),
                    el("span", "Malaysia Standard Time · UTC+8"))
                .cls("admin-overview-context"),
            el(
                    "header",
                    el(
                        "div",
                        el("p", "Admin console").cls("section-label"),
                        el("h1", "Administration"),
                        el(
                            "p",
                            "Manage visibility across staff accounts and registered API"
                                + " services.")),
                    el(
                            "div",
                            action("Review signups", "/admin/users", false),
                            action("View all APIs →", "/admin/lss-plants", true))
                        .cls("java-actions"))
                .cls("admin-overview-heading"),
            cards,
            el(
                    "article",
                    el(
                        "header",
                        el(
                            "div",
                            el("h2", "Latest API activity"),
                            el("p", "Latest saved connection tests for registered plants.")),
                        action("All API records", "/collection-management", false)),
                    table(
                        List.of(
                            "API name", "Owner", "Category", "Status API", "Last tested", "Data"),
                        rows,
                        "No registered plant API connections yet."),
                    el(
                        "footer",
                        el(
                            "p",
                            "Active / Inactive reflects the last API connection test, not"
                                + " continuous monitoring. Saved collection records remain"
                                + " available separately."),
                        el(
                            "p",
                            c.number("apiUntestedCount")
                                + " connections not tested. API owners are not recorded in the"
                                + " current plant registry.")))
                .cls("admin-api-activity")
                .attr("id", "latest-api-activity"))
        .cls("admin-overview");
  }

  private static Element apiCard(String title, long count, String detail, String url) {
    return el(
            "article",
            el("h2", title),
            el("strong", count),
            el("p", detail),
            link("View APIs →", url))
        .cls("admin-api-card");
  }

  static Element dashboard(PageContext c) {
    var records = el("div").cls("record-list");
    for (CollectionJobEntity job : c.<CollectionJobEntity>list("jobs"))
      records.add(
          el(
                  "a",
                  el(
                          "div",
                          el("strong", job.collectionId),
                          el("span", job.dataType + " · Plant " + job.plantId))
                      .cls("record-name"),
                  el("span", job.status).cls("status-pill"),
                  el("time", date(job.startedAt, "dd MMM yyyy HH:mm")))
              .attr("href", "/collections/" + job.id)
              .cls("record-row java-record"));
    if (c.list("jobs").isEmpty())
      records.add(el("p", "No collections yet. Configure plant API links to begin."));
    var plants = el("div").cls("java-plant-grid");
    for (LssPlant plant : c.<LssPlant>list("plants"))
      plants.add(
          el(
              "article",
              el("small", plant.plantId),
              el("h3", plant.name),
              el("p", plant.state),
              el("strong", plant.capacityMw + " MW"),
              el("span", Boolean.TRUE.equals(plant.isActive) ? "Active" : "Inactive")
                  .cls("status-pill")));
    return el(
        "section",
        heading(
            "Repository overview",
            "Large Scale Solar portfolio",
            "Monitor configured plants, collection outcomes and recent API activity."),
        el(
                "div",
                metric(c.number("plantCount"), "Total plants", "/ddq-analytics"),
                metric(
                    String.format(Locale.ROOT, "%,d", c.number("readingCount")),
                    "Stored readings",
                    "/mmf-analytics"),
                metric(c.number("collectionCount"), "Collections", "/collection-management"),
                metric(c.number("anomalyCount"), "Open anomalies", "/collection-management"))
            .cls("java-metrics"),
        el(
                "div",
                el(
                        "article",
                        el(
                                "div",
                                el("h2", "Recent API collections"),
                                link("View all", "/collection-management"))
                            .cls("panel-heading"),
                        records)
                    .cls("panel panel-wide"),
                el(
                        "aside",
                        el("p", "System status").cls("section-label section-label-light"),
                        el("h2", "Repository connected"),
                        el("p", "Operational data is read from the configured database."),
                        link("View health status →", "/api/status").cls("health-proof-button"))
                    .cls("panel focus-panel"))
            .cls("content-grid"),
        el(
                "article",
                el(
                        "div",
                        el("h2", "Registered solar plants"),
                        c.yes("isAdmin") ? link("Manage plants", "/admin/lss-plants") : null)
                    .cls("panel-heading"),
                plants)
            .cls("panel java-panel"));
  }

  private static Element metric(Object count, String title, String url) {
    return el("a", el("strong", count), el("span", title)).attr("href", url).cls("metric-card");
  }
}
