package my.webgso.ui;

import static my.webgso.ui.Html.el;
import static my.webgso.ui.PageContext.*;

import java.time.LocalDateTime;
import java.util.*;
import my.webgso.Models.*;
import my.webgso.ReadingQuality;
import my.webgso.ui.Html.Element;

/** Repository dashboard and Admin console, composed in Java. */
final class OverviewPages {
  private OverviewPages() {}

  static Element admin(PageContext c) {
    List<LssPlant> plants = c.list("lssPlants");
    List<SolarApiReading> readings = c.list("lssReadings");
    List<PlantApiConfiguration> configuredApis = c.list("configuredLssApis");
    Map<Integer, String> plantNames = c.value("lssPlantNames");
    var plantSelect = el("select").attr("name", "plantId");
    plantSelect.add(option("0", "All plants", c.value("selectedPlantId")));
    for (LssPlant plant : plants)
      plantSelect.add(
          option(
              plant.id,
              plant.name + " — " + plantLocation(plant) + " (" + plant.plantId + ")",
              c.value("selectedPlantId")));
    var typeSelect =
        el(
                "select",
                option("all", "All API types", c.text("selectedDataType")),
                option("Rolling 24 Hour", "Rolling 24 Hour", c.text("selectedDataType")),
                option("Day Ahead", "Day Ahead", c.text("selectedDataType")),
                option("Week Ahead", "Week Ahead", c.text("selectedDataType")),
                option("4 Month Ahead", "4 Month Ahead", c.text("selectedDataType")),
                option("MMF", "MMF", c.text("selectedDataType")))
            .attr("name", "dataType");
    var filters =
        el(
                "form",
                field("Plant", plantSelect),
                field("API type", typeSelect),
                field(
                    "Parameter",
                    input("parameter", "search", c.text("selectedParameter"))
                        .attr("placeholder", "e.g. ForecastValue")),
                el(
                        "label",
                        input("issuesOnly", "checkbox", "true")
                            .flag("checked", c.parameter("issuesOnly")),
                        el("span", "Show warnings and errors only"))
                    .cls("lss-quality-filter"),
                el(
                        "div",
                        button("Apply filters", true),
                        action("Clear", "/", false))
                    .cls("lss-data-filter-actions"))
            .attr("method", "get")
            .attr("action", "/")
            .cls("lss-data-filters");
    var latest = readings.isEmpty() ? null : readings.getFirst().submissionDateTime;
    var addPlantSelect = el("select").attr("name", "plantId");
    addPlantSelect.add(option("0", "Register a new plant", ""));
    for (LssPlant plant : plants)
      addPlantSelect.add(
          option(
              plant.id,
              plant.name + " — " + plantLocation(plant) + " (" + plant.plantId + ")",
              ""));
    var apiEndpointFields = new ArrayList<Element>();
    for (var slot : my.webgso.PlantApiSlots.VIEWER)
      apiEndpointFields.add(
          field(
              slot.label(),
              input(slot.field(), "url", "")
                  .attr(
                      "placeholder",
                      "https://example.com/"
                          + slot.dataType().toLowerCase(Locale.ROOT).replace(" ", "-"))));
    var addApiForm =
        c.post(
                "/lss-api",
                "lss-api-form",
                el(
                        "fieldset",
                        el("legend", "1. Select one plant"),
                        field("Existing plant", addPlantSelect),
                        field(
                            "New plant ID",
                            input("newPlantCode", "text", "").attr("placeholder", "e.g. 05")),
                        field(
                            "New plant name",
                            input("newPlantName", "text", "").attr("placeholder", "e.g. BDWS")))
                    .cls("lss-api-plant-fields"),
                el(
                        "fieldset",
                        el("legend", "2. Add one or more APIs for this plant"),
                        apiEndpointFields)
                    .cls("lss-api-endpoint-fields"),
                el("div", button("Save plant APIs", true)).cls("lss-api-form-actions"))
            .attr("autocomplete", "off")
            .attr("data-loading-title", "Saving plant APIs…")
            .attr(
                "data-loading-message",
                "Please wait while the endpoint configuration is saved. No readings are being collected yet.");
    var plantTables = el("div").cls("lss-plant-groups");
    int visiblePlantGroups = 0;
    boolean issuesOnly = c.parameter("issuesOnly");
    LocalDateTime qualityNow = LocalDateTime.now(my.webgso.Policies.LOCAL);
    for (LssPlant plant : plants) {
      var plantReadings = readings.stream().filter(item -> Objects.equals(item.plantId, plant.id)).toList();
      var plantApis = configuredApis.stream().filter(item -> Objects.equals(item.plantId, plant.id)).toList();
      if (plantReadings.isEmpty() && plantApis.isEmpty() && c.number("selectedPlantId") == 0) continue;
      visiblePlantGroups++;
      var rows = new ArrayList<Element>();
      int warningCount = 0;
      int errorCount = 0;
      int displayedCount = 0;
      for (SolarApiReading reading : plantReadings) {
        var quality = ReadingQuality.inspect(reading, plant, qualityNow);
        if ("warning".equals(quality.tone())) warningCount++;
        if ("error".equals(quality.tone())) errorCount++;
        if (issuesOnly && !quality.issue()) continue;
        displayedCount++;
        rows.add(
            row(
                reading.dataType,
                date(reading.submissionDateTime, "dd MMM yyyy, HH:mm"),
                date(reading.intervalStart, "dd MMM yyyy, HH:mm"),
                reading.parameter,
                reading.value,
                reading.unit,
                el("span", quality.label()).cls("lss-quality-status " + quality.tone()),
                el("span", quality.reason()).cls("lss-quality-reason"))
                .cls("lss-quality-row " + quality.tone()));
      }
      var apiBadges = el("div").cls("lss-plant-api-badges");
      for (PlantApiConfiguration api : plantApis) {
        var datasetRefresh =
            c.post(
                    "/lss-api/" + plant.id + "/collect",
                    "lss-refresh-form lss-dataset-refresh-form",
                    input("dataType", "hidden", api.dataType),
                    button("↻", false)
                        .attr("title", "Refresh " + api.dataType)
                        .attr("aria-label", "Refresh " + api.dataType))
                .attr("data-loading-title", "Refreshing " + api.dataType + "…")
                .attr(
                    "data-loading-message",
                    "Please wait while the latest "
                        + api.dataType
                        + " readings are requested and saved for "
                        + plant.name
                        + ".");
        apiBadges.add(
            el(
                    "div",
                    el(
                            "a",
                            el("i").attr("aria-hidden", "true"),
                            api.dataType)
                        .attr(
                            "href",
                            "/?plantId=" + plant.id + "&dataType=" + encode(api.dataType))
                        .attr("title", "Show " + api.dataType + " readings"),
                    datasetRefresh)
                .cls("lss-dataset-chip"));
      }
      if (plantApis.isEmpty()) apiBadges.add(el("span", "No API configured").cls("muted"));
      Element refreshButton =
          plantApis.isEmpty()
              ? null
              : c.post(
                      "/lss-api/" + plant.id + "/collect",
                      "lss-refresh-form",
                      button("Refresh all APIs", false))
                  .attr("data-loading-title", "Refreshing all API data…")
                  .attr(
                      "data-loading-message",
                      "Please wait while the latest readings are requested and saved for "
                          + plant.name
                          + ".")
                  .attr("aria-label", "Collect the latest API data for " + plant.name);
      Element deleteButton =
          c.post(
                  "/lss-api/" + plant.id + "/delete",
                  "lss-delete-form",
                  input("confirmation", "hidden", "delete"),
                  button("Delete plant", false).cls("danger-action"))
              .attr(
                  "data-confirm",
                  "Delete plant "
                      + plant.name
                      + "? All configured APIs, collected readings and history for this plant will be permanently deleted.");
      Element plantData =
          plantReadings.isEmpty()
              ? el(
                      "div",
                      el("strong", "No readings stored yet"),
                      el(
                          "p",
                          "Run Refresh API data to request and store the latest readings for this plant."))
                  .cls("lss-plant-empty-data")
              : rows.isEmpty() && issuesOnly
                  ? el(
                          "div",
                          el("strong", "No warnings or errors detected"),
                          el("p", "All displayed readings passed the current data-quality checks."))
                      .cls("lss-plant-empty-data lss-quality-clear")
              : table(
                  List.of(
                      "API type",
                      "Submitted (MYT)",
                      "Data time",
                      "Parameter",
                      "Value",
                      "Unit",
                      "Quality",
                      "Why flagged"),
                  rows,
                  "No stored readings are available for this plant yet.");
      plantTables.add(
          el(
                  "article",
                  el(
                          "header",
                          el(
                              "div",
                              el("small", "PLANT ID · " + plant.plantId),
                              el(
                                      "div",
                                      el("h2", plant.name),
                                      el("span", plantLocation(plant)).cls("lss-location-pill"))
                                  .cls("lss-plant-title"),
                              el(
                                      "p",
                                      el("span").cls("lss-live-dot"),
                                      plantApis.size() + " connected API(s)",
                                      el("b", "·"),
                                      displayedCount + " rows displayed",
                                      el("b", "·"),
                                      el("span", errorCount + " error")
                                          .cls(errorCount > 0 ? "quality-count error" : "quality-count"),
                                      el("b", "·"),
                                      el("span", warningCount + " warning")
                                          .cls(
                                              warningCount > 0
                                                  ? "quality-count warning"
                                                  : "quality-count"))
                                  .cls("lss-plant-meta")),
                          el(
                                  "div",
                                  apiBadges,
                                  el("div", refreshButton, deleteButton).cls("lss-plant-buttons"))
                              .cls("lss-plant-actions")),
                  plantData)
              .cls("lss-data-table-panel lss-plant-panel"));
    }
    if (visiblePlantGroups == 0)
      plantTables.add(
          el("article", el("h2", "No plant data yet"), el("p", "Add an LSS API to register the first plant and endpoint."))
              .cls("lss-data-table-panel lss-empty-panel"));
    return el(
            "section",
            el(
                    "div",
                    el(
                            "div",
                            heading(
                                "LIVE OPERATIONS · LSS API MONITOR",
                                "Solar Plant Data Viewer",
                                "View, verify and monitor DDQ forecasts and MMF measurements from every configured LSS endpoint."),
                            el(
                                    "div",
                                    el("span", "● System operational"),
                                    el("span", "Data quality checks enabled"))
                                .cls("lss-hero-status"))
                        .cls("lss-hero-copy"),
                    el("a", "＋ Add LSS API").attr("href", "#add-lss-api").cls("primary-action"))
                .cls("lss-data-page-heading"),
            c.text("lssApiMessage").isBlank()
                ? null
                : el("div", c.text("lssApiMessage"))
                    .cls(c.yes("lssApiSuccess") ? "lss-api-notice success" : "lss-api-notice error"),
            el(
                    "div",
                    dataMetric(String.format(Locale.ROOT, "%,d", c.number("totalReadingCount")), "Stored readings"),
                    dataMetric(plants.size(), "Registered plants"),
                    dataMetric(configuredApis.size(), "Connected APIs"),
                    dataMetric(
                        latest == null ? "No data" : date(latest, "dd MMM yyyy, HH:mm"),
                        "Last data received (MYT)"))
                .cls("lss-data-metrics"),
            el(
                    "article",
                    el(
                        "header",
                        el("div", el("span", "DATA EXPLORER"), el("h2", "Live API readings")),
                        el("p", "Filter by plant, dataset or measurement parameter.")),
                    filters)
                .cls("lss-data-filter-panel"),
            el(
                    "details",
                    el(
                        "summary",
                        el("div", el("span", "+"), el("strong", "Add or update LSS API")),
                        el("small", "Connect multiple datasets to one solar plant")),
                    el(
                        "div",
                        addApiForm,
                        el("p", "The endpoint is validated and its latest readings are collected immediately after saving.").cls("lss-api-help"))
                    .cls("lss-api-details-body"))
                .attr("id", "add-lss-api")
                .cls("lss-data-filter-panel lss-api-panel"),
            plantTables)
        .cls("lss-data-viewer");
  }

  private static Element dataMetric(Object value, String label) {
    return el("article", el("strong", value), el("span", label)).cls("lss-data-metric");
  }

  private static String plantLocation(LssPlant plant) {
    if (plant == null || plant.name == null) return "Location not set";
    return switch (plant.name.trim().toUpperCase(Locale.ROOT)) {
      case "BKHS" -> "Pulau Pinang";
      case "SKNS" -> "Perlis";
      case "SIDS" -> "Perak";
      case "BDWS", "LMMS" -> "Pahang";
      case "KALS" -> "Kedah";
      case "MALS" -> "Melaka";
      case "KMLS" -> "Johor";
      default ->
          plant.state == null || plant.state.isBlank() ? "Location not set" : plant.state.trim();
    };
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
