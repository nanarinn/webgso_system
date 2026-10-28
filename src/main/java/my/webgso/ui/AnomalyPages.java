package my.webgso.ui;

import static my.webgso.ui.Html.el;
import static my.webgso.ui.PageContext.*;

import java.util.*;
import my.webgso.ApiAnomalyService.Report;
import my.webgso.ui.Html.Element;

final class AnomalyPages {
  private AnomalyPages() {}

  static Element reports(PageContext c) {
    Report selected = c.value("validationReport");
    var page =
        el(
            "section",
            heading(
                "Data validation",
                "Anomalies",
                "Review data quality findings from individual API tests.",
                action("Back to plants", "/admin/lss-plants", false)),
            el(
                    "nav",
                    action("API test reports", "/admin/anomaly-reports", true),
                    action("Collection anomalies", "/admin/anomalies", false),
                    action("Validation rules", "/admin/data-quality", false))
                .cls("anomaly-navigation"));
    if (c.yes("reportNotFound"))
      page.add(
          el("p", "Report not found. Select a saved test below.")
              .cls("anomaly-notice")
              .attr("role", "alert"));
    if (selected != null) {
      var findings = new ArrayList<Element>();
      selected
          .issues()
          .forEach(
              issue ->
                  findings.add(
                      row(
                          issue.code(),
                          issue.field(),
                          issue.value() == null ? "—" : issue.value(),
                          issue.reason())));
      page.add(
          el(
                  "article",
                  el(
                          "div",
                          el(
                              "div",
                              el("p", selected.plantCode() + " · " + selected.dataset())
                                  .cls("section-label"),
                              el("h2", selected.plantName()),
                              el(
                                  "p",
                                  "Tested "
                                      + date(
                                          selected
                                              .testedAt()
                                              .atZoneSameInstant(
                                                  java.time.ZoneId.of("Asia/Kuala_Lumpur")),
                                          "dd MMM yyyy, HH:mm")
                                      + " MYT")),
                          el(
                                  "span",
                                  selected.rules() == 0
                                      ? "Not validated"
                                      : selected.issueCount() > 0
                                          ? "Anomalies detected"
                                          : "No anomalies detected")
                              .cls(
                                  "anomaly-result "
                                      + (selected.issueCount() > 0 || selected.rules() == 0
                                          ? "anomaly-warning"
                                          : "anomaly-clear")))
                      .cls("anomaly-report-heading"),
                  el(
                          "div",
                          metric("Measurements", selected.readings()),
                          metric("Active rules", selected.rules()),
                          metric("Anomalies", selected.issueCount()))
                      .cls("anomaly-summary"),
                  selected.rules() == 0
                      ? el(
                              "p",
                              "No active validation rules are configured for this data type."
                                  + " Configure rules and test again to check for anomalies.")
                          .cls("anomaly-notice")
                      : el(
                              "p",
                              "Findings use the validation rules active at the time of this test. A"
                                  + " successful connection can still return data with anomalies.")
                          .cls("anomaly-note"),
                  table(
                      List.of("Rule", "Field / parameter", "Original value", "Reason"),
                      findings,
                      selected.rules() == 0
                          ? "No validation was performed."
                          : "No anomalies found by the active rules."))
              .cls("panel anomaly-report"));
    }
    var history = new ArrayList<Element>();
    for (Report report : c.<Report>list("validationReports"))
      history.add(
          row(
              el("strong", report.plantName()),
              report.plantCode(),
              report.dataset(),
              date(
                  report.testedAt().atZoneSameInstant(java.time.ZoneId.of("Asia/Kuala_Lumpur")),
                  "dd MMM yyyy, HH:mm"),
              report.readings(),
              report.rules() == 0 ? "Not validated" : report.issueCount(),
              action("View report", "/admin/anomaly-reports?reportId=" + report.id(), false)));
    return page.add(
        el(
                "article",
                el("h2", "API test history"),
                el("p", "Latest 50 saved tests · Malaysia Standard Time (UTC+8)")
                    .cls("anomaly-note"),
                table(
                    List.of(
                        "Plant",
                        "Plant ID",
                        "API",
                        "Tested at",
                        "Measurements",
                        "Anomalies",
                        "Details"),
                    history,
                    "No API tests yet. Open LSS Plant Config and press Test beside an API."))
            .cls("panel anomaly-report"));
  }

  private static Element metric(String title, int count) {
    return el("div", el("span", title), el("strong", count));
  }
}
