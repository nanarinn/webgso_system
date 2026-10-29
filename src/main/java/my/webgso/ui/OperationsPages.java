package my.webgso.ui;

import static my.webgso.ui.Html.el;
import static my.webgso.ui.PageContext.*;

import java.util.*;
import my.webgso.*;
import my.webgso.Models.*;
import my.webgso.ui.Html.Element;

/** Analytics, API collections, batch progress and reports, all composed in Java. */
final class OperationsPages {
  private OperationsPages() {}

  static Element pagination(PageContext c, String label, String... filters) {
    long page = c.number("page");
    return el(
            "nav",
            page > 0 ? link("Previous", c.pageUrl(page - 1, filters)) : null,
            el("span", "Page " + (page + 1)),
            (page + 1) * 25 < c.number("total") ? link("Next", c.pageUrl(page + 1, filters)) : null)
        .cls("java-pagination")
        .attr("aria-label", label);
  }

  private static Element plants(PageContext c, String name, String selected, String all) {
    var select = el("select").attr("name", name);
    if (all != null) select.add(option("", all, selected));
    for (LssPlant plant : c.<LssPlant>list("plants"))
      select.add(option(plant.id, plant.plantId + " · " + plant.name, selected));
    return select;
  }

  private static Element dateField(String label, String name, Object value, boolean required) {
    return field(label, input(name, "datetime-local", dateTime(value)).flag("required", required));
  }

  static Element analytics(PageContext c) {
    boolean mmf = c.yes("mmf");
    var stateOptions =
        el("select", option("", "All states", c.text("state"))).attr("name", "state");
    for (String s : c.<String>list("states")) stateOptions.add(option(s, s, c.text("state")));
    var filters =
        el(
                "form",
                c.list("plants").isEmpty()
                    ? null
                    : field("Plant", plants(c, "plantIds", c.text("plantIds"), "All plants")),
                field("State", stateOptions))
            .cls("panel java-filters")
            .attr("method", "get");
    if (mmf) {
      var parameters =
          el("select", option("", "All parameters", c.text("parameter"))).attr("name", "parameter");
      for (String p : c.<String>list("parameters"))
        parameters.add(option(p, p, c.text("parameter")));
      filters.add(field("Parameter", parameters));
    } else
      filters.add(field("Dataset", select("dataType", c.list("dataTypes"), c.text("dataType"))));
    filters.add(
        dateField("Data from", "from", c.value("from"), false),
        dateField("Data until (exclusive)", "to", c.value("to"), false),
        dateField("Submission (optional)", "submission", c.value("submission"), false),
        el(
                "div",
                button("Apply filters", true),
                action("Clear", mmf ? "/mmf-analytics" : "/ddq-analytics", false))
            .cls("java-actions"));
    var chart =
        el(
                "article",
                el(
                        "div",
                        el("h2", c.text("dataType") + " trend"),
                        el("span", c.number("total") + " readings"))
                    .cls("panel-heading"))
            .cls("panel java-panel");
    if (c.number("total") > 0)
      chart.add(
          el(
                  "div",
                  el(
                          "svg",
                          el("path").attr("d", "M40 35 V230 H965").cls("chart-axis"),
                          el("text", c.text("chartMax")).attr("x", 5).attr("y", 30),
                          el("text", c.text("chartMin")).attr("x", 5).attr("y", 250),
                          el("polyline").cls("chart-series").attr("points", c.text("points")))
                      .attr("viewBox", "0 0 1000 270")
                      .attr("role", "img")
                      .attr("aria-label", "Readings over the selected time range"),
                  el(
                          "p",
                          mmf
                              ? "Average across the selected measurements. Select one parameter"
                                  + " when comparing units."
                              : "Total forecast across the selected plants.")
                      .cls("page-subtitle"))
              .cls("java-chart"));
    else
      chart.add(
          el("p", "No readings match these filters. Select another range or collect API data.")
              .cls("java-empty"));
    Map<Integer, String> names = c.value("plantNames");
    var rows = new ArrayList<Element>();
    for (SolarApiReading r : c.<SolarApiReading>list("rows"))
      rows.add(
          row(
              r.intervalStart,
              names.get(r.plantId),
              r.submissionDateTime,
              r.parameter,
              r.value,
              r.unit,
              r.intervalMinutes + " min"));
    chart.add(
        table(
            List.of("Timestamp", "Plant", "Submission", "Parameter", "Value", "Unit", "Interval"),
            rows,
            null),
        pagination(
            c,
            "Reading pages",
            "plantIds",
            "dataType",
            "from",
            "to",
            "submission",
            "parameter",
            "state"));
    return el(
        "section",
        heading(
            "Portfolio intelligence",
            c.text("title"),
            mmf
                ? "Review meteorological measurements by plant, parameter and timestamp."
                : "Review forecast production across all configured DDQ horizons."),
        filters,
        chart,
        c.text("analyticsFilterError").isEmpty()
            ? null
            : el(
                    "dialog",
                    el("p", "Analytics filters").cls("section-label"),
                    el("h2", "Check date and time").attr("id", "analytics-filter-title"),
                    el("p", c.text("analyticsFilterError")).attr("id", "analytics-filter-message"),
                    el(
                            "form",
                            el("button", "OK")
                                .attr("type", "submit")
                                .cls("primary-action")
                                .flag("autofocus", true))
                        .attr("method", "dialog"))
                .cls("api-test-popup")
                .flag("open", true)
                .attr("aria-labelledby", "analytics-filter-title")
                .attr("aria-describedby", "analytics-filter-message"));
  }

  static Element collections(PageContext c) {
    var single =
        c.post(
            "/collections/run",
            "java-filters",
            field("Plant", plants(c, "plantId", "", null).flag("required", true)),
            field("Dataset", select("dataType", c.list("dataTypes"), "Rolling 24 Hour")),
            dateField("Submission date and time", "submission", c.value("now"), true),
            replaceCheckbox(),
            button("Collect data", true));
    var batchForm =
        c.post(
            "/collections/batch",
            "java-filters",
            field("Dataset", select("dataType", c.list("dataTypes"), "Rolling 24 Hour")),
            dateField("Submission date and time", "submission", c.value("now"), true),
            replaceCheckbox(),
            button("Collect all active plants", true));
    var page =
        el(
            "section",
            heading(
                "Data operations",
                "Collection Management",
                "Fetch registered plant APIs and inspect collection outcomes."),
            el("article", el("h2", "Run a manual collection"), single).cls("panel"),
            el("details", el("summary", "Collect a dataset from all active plants"), batchForm)
                .cls("panel java-panel"));
    ManualBatchService.Batch batch = c.value("batch");
    if (batch != null) {
      page.add(
          el(
                  "article",
                  el("h2", "Batch progress"),
                  el("iframe")
                      .attr("src", "/collections/batches/" + batch.id + "/status")
                      .attr("title", "Current batch collection status")
                      .cls("java-batch-frame"))
              .cls("panel java-panel"));
    }
    var rows = new ArrayList<Element>();
    for (CollectionJobEntity job : c.<CollectionJobEntity>list("jobs"))
      rows.add(
          row(
              link(job.collectionId, "/collections/" + job.id),
              job.plantId,
              job.dataType,
              job.mode,
              job.submissionDateTime,
              el("span", job.status).cls("status-pill"),
              job.errorMessage == null ? job.responseMessage : job.errorMessage));
    return page.add(
        el(
                "article",
                el("h2", "Collection history"),
                table(
                    List.of(
                        "Collection ID",
                        "Plant",
                        "Dataset",
                        "Mode",
                        "Submission",
                        "Status",
                        "Outcome"),
                    rows,
                    "No collections yet."),
                pagination(c, "Collection pages", "batch"))
            .cls("panel java-panel"));
  }

  static Element batchProgress(PageContext c) {
    ManualBatchService.Batch batch = c.value("batch");
    var rows = new ArrayList<Element>();
    batch.items.values().stream()
        .sorted(Comparator.comparingInt(ManualBatchService.Item::plantId))
        .forEach(item -> rows.add(row(item.plantName(), item.status(), item.message())));
    return el(
            "main",
            el(
                "p",
                batch.complete()
                    ? "Batch completed"
                    : "Collection is running. Status updates automatically every five seconds."),
            table(List.of("Plant", "Status", "Outcome"), rows, "No batch items."),
            link("Refresh status", "/collections/batches/" + batch.id + "/status"))
        .cls("java-batch-status");
  }

  private static Element replaceCheckbox() {
    return el(
            "label",
            input("replace", "checkbox", "true"),
            "Replace existing DDQ data for this submission")
        .cls("java-check");
  }

  static Element collection(PageContext c) {
    CollectionJobEntity job = c.value("job");
    var rows = new ArrayList<Element>();
    for (DataAnomaly anomaly : c.<DataAnomaly>list("anomalies"))
      rows.add(row(anomaly.fieldName, anomaly.originalValue, anomaly.reason, anomaly.status));
    var raw = el("details", el("summary", "Raw API response")).cls("panel java-panel");
    for (RawApiResponse r : c.<RawApiResponse>list("raw"))
      raw.add(
          el(
              "article",
              el("p", "HTTP " + r.httpStatusCode + " · " + r.contentType),
              el("p", "SHA-256: " + r.sha256).cls("java-hash"),
              el("pre", r.payload)));
    if (c.list("raw").isEmpty()) raw.add(el("p", "No raw response was received."));
    return el(
        "section",
        heading(
            "Collection details",
            job.collectionId,
            job.dataType + " · " + job.status,
            action("Collection history", "/collection-management", false)),
        el(
                "article",
                el("h2", "Outcome"),
                el("p", job.responseMessage),
                job.errorMessage == null
                    ? null
                    : el("p", job.errorMessage).cls("page-inline-error"),
                el(
                        "dl",
                        el("dt", "Submission"),
                        el("dd", job.submissionDateTime),
                        el("dt", "Requested by"),
                        el("dd", job.requestedBy),
                        el("dt", "Duration"),
                        el("dd", job.durationSeconds + " seconds"),
                        el("dt", "Retries"),
                        el("dd", job.retryCount))
                    .cls("java-details"))
            .cls("panel"),
        el(
                "article",
                el("h2", "Detected anomalies"),
                table(List.of("Parameter", "Original value", "Reason", "Status"), rows, null))
            .cls("panel java-panel"),
        raw);
  }

  static Element reports(PageContext c) {
    var interval =
        el(
                "select",
                option(15, "15 minutes", 15),
                option(30, "30 minutes", 15),
                option(60, "1 hour", 15))
            .attr("name", "interval");
    var form =
        c.post(
            "/reports/generate",
            "java-form java-report-form",
            field(
                "Report name",
                input("name", "text", null)
                    .flag("required", true)
                    .attr("maxlength", 200)
                    .attr("placeholder", "Daily portfolio report")),
            field("Dataset", select("dataType", c.list("dataTypes"), "Rolling 24 Hour")),
            field("Plant", plants(c, "plantIds", "", "All active plants")),
            dateField("Submission date and time", "submission", c.text("today") + "T00:00", true),
            field("Report interval", interval),
            dateField("Data range start (optional)", "from", null, false),
            dateField("Data range end, exclusive (optional)", "to", null, false),
            el(
                    "p",
                    "Leave the data range empty to report a specific submission. A range selects"
                        + " the latest readings at each data timestamp.")
                .cls("page-subtitle"),
            button("Generate report", true));
    var rows = new ArrayList<Element>();
    for (ReportExecution r : c.<ReportExecution>list("reports")) {
      var downloads = el("div").cls("java-actions");
      if ("Completed".equals(r.generationStatus))
        downloads.add(
            link("Excel", "/reports/" + r.id + "/download?format=xlsx"),
            link("CSV", "/reports/" + r.id + "/download?format=csv"),
            link("Anomalies", "/reports/" + r.id + "/download?format=anomalies.csv"));
      rows.add(
          row(
              el("strong", r.name).add(el("small", r.reportId).cls("java-block")),
              r.dataSelection,
              r.requestedAt,
              el("span", r.generationStatus, el("small", r.errorMessage).cls("java-block")),
              r.recordCount,
              r.anomalyCount,
              downloads));
    }
    return el(
        "section",
        heading(
            "Reports and analysis",
            "On-Demand Analysis",
            "Generate Excel and CSV reports with a separate substitution summary."),
        el("article", form).cls("panel"),
        el(
                "article",
                el("h2", "Your reports"),
                table(
                    List.of(
                        "Report",
                        "Dataset",
                        "Requested",
                        "Status",
                        "Readings",
                        "Substitutions",
                        "Downloads"),
                    rows,
                    "Your generated reports will appear here."))
            .cls("panel java-panel"));
  }
}
