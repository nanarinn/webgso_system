package my.webgso.ui;

import static my.webgso.ui.Html.el;
import static my.webgso.ui.PageContext.*;

import my.webgso.ConfiguredDatasets;
import my.webgso.Models.SolarApiReading;
import java.util.List;
import java.util.ArrayList;

final class QuickApiTestPage {
  private QuickApiTestPage() {}

  static Html.Element render(PageContext c) {
    String family = c.text("testFamily");
    String prefix = c.yes("isAdmin") ? "/admin" : "/user";
    String route = family.equals("MMF") ? prefix + "/mmf-test" : family.equals("DDQ") ? prefix + "/ddq-test" : "/admin/api-quick-test";
    var dataset =
        el("select").attr("name", "dataType").attr("id", "quick-dataset").flag("required", true);
    String selected = c.text("dataType").isEmpty() ? "Rolling 24 Hour" : c.text("dataType");
    for (String name : ConfiguredDatasets.ALL) {
      if (family.equals("DDQ") && name.equals("MMF") || family.equals("MMF") && !name.equals("MMF")) continue;
      dataset.add(
          option(
              name, name.equals("MMF") ? "MMF - Weather measurements" : "DDQ - " + name, selected));
    }
    return el(
        "section",
        heading(
            "API testing",
            family.isEmpty() ? "Test API" : family + " API testing",
            "Try an API URL without adding a plant or saving results.",
            action("Back to Plant Config", c.yes("isAdmin") ? "/admin/lss-plants" : "/user/plant-config", false)),
        el(
                "article",
                c.post(
                    route,
                    "java-form api-retest-form quick-api-test",
                    el("label", "API endpoint URL").attr("for", "quick-url"),
                    input("apiUrl", "url", c.text("apiUrl"))
                        .attr("id", "quick-url")
                        .attr("maxlength", 2048)
                        .attr("placeholder", "https://example.com/api")
                        .flag("required", true),
                    el("label", "Dataset").attr("for", "quick-dataset"),
                    dataset,
                    el(
                        "p",
                        "Checks connectivity and data structure only. No plant, readings, history,"
                            + " CSV report or email alerts are created. HTTPS and configured host"
                            + " restrictions still apply."),
                    el(
                            "div",
                            button("Test API", true),
                            action("Clear", route, false))
                        .cls("java-actions")))
            .cls("panel quick-test-card"),
        results(c));
  }

  private static Html.Element results(PageContext c) {
    if (!c.model().containsKey("quickTestReadings")) return null;
    var rows = new ArrayList<Html.Element>();
    int index = 1;
    for (SolarApiReading reading : c.<SolarApiReading>list("quickTestReadings")) {
      rows.add(row(index++, reading.intervalStart, reading.parameter, reading.value,
          reading.unit, reading.intervalMinutes == null ? "—" : reading.intervalMinutes + " min"));
    }
    return el("article",
        el("header", el("div", el("p", "API response").cls("section-label"),
            el("h2", "Tested API data")),
            el("span", c.text("quickTestReadingCount") + " readings").cls("status-pill"))
            .cls("quick-result-heading"),
        el("p", c.text("dataType") + " · " + c.text("apiUrl")).cls("quick-result-source"),
        ApiTestPopup.freshness(c),
        c.text("apiTestFreshnessWarning").isBlank() ? null
            : el("p", c.text("apiTestFreshnessWarning")).cls("api-freshness-warning"),
        el("p", "Read-only preview of measurements interpreted by the API test. Nothing is saved. "
            + "For MMF, use a station-specific URL to view one station; the raw response retains all stations."),
        table(List.of("No.", "Timestamp (MYT)", "Parameter", "Value", "Unit", "Interval"), rows, "No measurements returned."),
        c.number("quickTestReadingCount") > rows.size()
            ? el("p", "Showing the first " + rows.size() + " readings of " + c.text("quickTestReadingCount") + ".") : null)
        .cls("panel quick-test-results");
  }
}
