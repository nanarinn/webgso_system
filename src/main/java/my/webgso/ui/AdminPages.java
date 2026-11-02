package my.webgso.ui;

import static my.webgso.ui.Html.el;
import static my.webgso.ui.PageContext.*;

import java.util.*;
import my.webgso.*;
import my.webgso.Models.*;
import my.webgso.ui.Html.Element;

/** Configuration lists, validated forms and user-access administration in Java. */
final class AdminPages {
  private AdminPages() {}

  static Element resource(PageContext c) {
    Resources.Definition resource = c.value("resource");
    boolean plants = resource.type() == LssPlant.class;
    boolean editable = !resource.editable().isEmpty();
    var columns = c.<Map<String, String>>list("columns");
    var headers = new ArrayList<String>(List.of("ID"));
    for (var column : columns) headers.add(column.get("label"));
    if (editable) headers.add("Action");
    if (plants)
      headers =
          new ArrayList<>(
              List.of(
                  "ID",
                  "Plant ID",
                  "Plant Name",
                  "Zone",
                  "State",
                  "Capacity (MW)",
                  "API",
                  "Actions"));
    var rows = new ArrayList<Element>();
    for (Map<String, Object> record : c.<Map<String, Object>>list("rows")) {
      if (plants) {
        rows.add(plantRow(c, record));
        continue;
      }
      var values = new ArrayList<Object>();
      values.add(record.get("id"));
      columns.forEach(column -> values.add(record.get(column.get("name"))));
      if (editable) {
        var edit = link("Edit", url(resource.path() + "/edit", Map.of("id", record.get("id"))));
        values.add(
            resource.type() == CollectionSchedule.class ? scheduleActions(c, record, edit) : edit);
      }
      rows.add(row(values.toArray()));
    }
    var tabs = el("nav").cls("java-tabs");
    switch (resource.path()) {
      case "/admin/lss-plants" ->
          tabs.add(
              action("Plant API links", "/admin/api-configurations", false),
              action("Test API", "/admin/api-quick-test", false));
      case "/admin/report-automation" ->
          tabs.add(
              link("Recipients", "/admin/report-recipients"),
              link("Delivery monitor", "/admin/delivery-monitor"));
      case "/admin/data-quality" -> tabs.add(link("Data anomalies", "/admin/anomalies"));
      default -> {}
    }
    var page =
        el(
            "section",
            heading(
                "Administration",
                c.text("title"),
                null,
                el(
                        "div",
                        plants
                            ? c.post(
                                "/admin/lss-plants/test-all",
                                "api-retest-form",
                                button("Test all APIs", true))
                            : null,
                        plants
                            ? action("API test history", "/admin/api-test-history", false)
                            : null,
                        resource.path().equals("/admin/anomalies")
                            ? action("← Back to Data Quality", "/admin/data-quality", false)
                            : editable
                                ? action("Add record", resource.path() + "/edit", true)
                                : null)
                    .cls("java-actions")),
            tabs);
    page.add(
        el(
                "article",
                plants
                    ? plantFilters(c)
                    : el(
                            "form",
                            el("label", "Search records").attr("for", "search"),
                            input("q", "text", c.text("q"))
                                .attr("id", "search")
                                .attr("maxlength", 100),
                            button("Search", false),
                            action("Clear", resource.path(), false))
                        .cls("java-search")
                        .attr("method", "get"),
                table(headers, rows, "No records found.").attr("data-plant-table", plants),
                OperationsPages.pagination(
                    c,
                    "Record pages",
                    "q",
                    "filterType",
                    "filterResult",
                    "locationMode",
                    "locationPlants",
                    "locationStates",
                    "locationRegions",
                    "locationZones"))
            .cls("panel"));
    return page;
  }

  private static Element scheduleActions(PageContext c, Map<String, Object> record, Element edit) {
    String popup = "delete-schedule-" + record.get("id");
    return el(
            "div",
            edit,
            el("button", "Delete")
                .attr("type", "button")
                .attr("popovertarget", popup)
                .cls("plant-delete-button"),
            el(
                    "div",
                    el("h2", "Delete schedule?").attr("id", popup + "-title"),
                    el("p", "Are you sure you want to delete this collection schedule?")
                        .attr("id", popup + "-message"),
                    el("strong", record.get("name") + " (" + record.get("scheduleCode") + ")")
                        .cls("plant-delete-name"),
                    el("p", "Existing collection records will be kept."),
                    c.post(
                        "/admin/collection-schedules/" + record.get("id") + "/delete",
                        "plant-delete-confirmation",
                        input("confirmation", "hidden", "delete"),
                        el("button", "Cancel")
                            .attr("type", "button")
                            .attr("popovertarget", popup)
                            .attr("popovertargetaction", "hide")
                            .flag("autofocus", true)
                            .cls("secondary-action"),
                        button("Delete", false).cls("plant-delete-button")))
                .attr("id", popup)
                .attr("popover", "auto")
                .attr("role", "alertdialog")
                .attr("aria-labelledby", popup + "-title")
                .attr("aria-describedby", popup + "-message")
                .cls("plant-delete-popup"))
        .cls("schedule-row-actions");
  }

  private static Element plantRow(PageContext c, Map<String, Object> plant) {
    var endpoints = el("div").cls("plant-api-list");
    var apiRows = new ArrayList<Element>();
    var apiNames = new LinkedHashSet<String>();
    addPlantApi(apiRows, apiNames, c, plant, "DDQ", plant.get("ddqApiEndpoint"));
    addPlantApi(apiRows, apiNames, c, plant, "MMF", plant.get("mmfApiEndpoint"));
    Map<Integer, List<PlantApiConfiguration>> configured = c.value("plantApis");
    for (var api : configured.getOrDefault(((Number) plant.get("id")).intValue(), List.of()))
      addPlantApi(apiRows, apiNames, c, plant, api.dataType, api.apiLink);
    String apiPopup = "plant-apis-" + plant.get("id");
    if (apiRows.isEmpty()) endpoints.add(el("span", "Not configured").cls("plant-api-empty"));
    else {
      Map<String, ApiTestHistoryService.Entry> latestTests = c.value("latestApiTests");
      for (String name : apiNames) {
        String dataset = name.equals("DDQ") ? "Rolling 24 Hour" : name;
        Map<String, java.time.OffsetDateTime> nextTests = c.value("nextApiTests");
        var nextTest =
            nextTests == null
                ? null
                : nextTests.get(
                    ApiTestHistoryService.key(((Number) plant.get("id")).intValue(), dataset));
        var last =
            latestTests == null
                ? null
                : latestTests.get(
                    ApiTestHistoryService.key(((Number) plant.get("id")).intValue(), dataset));
        endpoints.add(
            el(
                    "div",
                    el(
                            "div",
                            el("button", name)
                                .attr("type", "button")
                                .attr("popovertarget", apiPopup)
                                .cls("plant-api-dataset-button"),
                            el("span", last == null ? "Not tested" : last.outcome())
                                .attr(
                                    "title",
                                    last == null
                                        ? "No tests recorded"
                                        : "Last tested "
                                            + date(
                                                last.testedAt()
                                                    .atZoneSameInstant(
                                                        java.time.ZoneId.of("Asia/Kuala_Lumpur")),
                                                "dd MMM yyyy, HH:mm:ss")
                                            + " MYT")
                                .cls(
                                    "api-last-result "
                                        + (last == null
                                            ? "api-test-untested"
                                            : last.outcome().equals("Success")
                                                ? "api-test-success"
                                                : "api-test-failed")),
                            last != null
                                    && last.message() != null
                                    && last.message().contains(" Warning:")
                                ? el("span", "Data freshness warning")
                                    .cls("api-freshness-badge")
                                    .attr("title", last.message())
                                : null,
                            last == null
                                ? el("small", "Last tested: Not tested").cls("api-history-note")
                                : el(
                                        "small",
                                        "Last tested: "
                                            + date(
                                                last.testedAt()
                                                    .atZoneSameInstant(my.webgso.Policies.LOCAL),
                                                "dd MMM yyyy, HH:mm:ss")
                                            + " MYT")
                                    .cls("api-history-note"),
                            nextTest != null && nextTest.isAfter(java.time.OffsetDateTime.now())
                                ? el(
                                        "small",
                                        "Next test: "
                                            + date(
                                                nextTest.atZoneSameInstant(
                                                    java.time.ZoneId.of("Asia/Kuala_Lumpur")),
                                                "dd MMM yyyy, HH:mm")
                                            + " MYT")
                                    .cls("api-history-note")
                                : el("small", "Next test: Available now").cls("api-history-note"))
                        .cls("plant-api-name-result"),
                    el(
                            "div",
                            c.post(
                                "/admin/plants/" + plant.get("id") + "/test",
                                "plant-table-api-test",
                                input("dataType", "hidden", dataset),
                                button("Test", false)
                                    .attr(
                                        "aria-label",
                                        "Test " + name + " API for " + plant.get("name"))))
                        .cls("plant-api-controls"))
                .cls("plant-api-table-item"));
      }
      endpoints.add(
          el(
                  "div",
                  el(
                          "header",
                          el("span", "↗").cls("plant-api-popup-icon").attr("aria-hidden", "true"),
                          el(
                              "div",
                              el("p", "PLANT CONNECTIONS").cls("plant-api-eyebrow"),
                              el("h2", "API connections").attr("id", apiPopup + "-title"),
                              el("p", plant.get("name") + " · Plant ID " + plant.get("plantId"))
                                  .cls("plant-api-popup-name")),
                          el("button", "×")
                              .attr("type", "button")
                              .attr("popovertarget", apiPopup)
                              .attr("popovertargetaction", "hide")
                              .attr("aria-label", "Close API connections")
                              .cls("plant-api-popup-dismiss"))
                      .cls("plant-api-popup-header"),
                  el(
                          "div",
                          el(
                                  "span",
                                  apiRows.size()
                                      + " configured "
                                      + (apiRows.size() == 1 ? "API" : "APIs"))
                              .cls("plant-api-total"))
                      .cls("plant-api-popup-toolbar"),
                  el("div", el("span", "Dataset"), el("span", "API URL"), el("span", "Action"))
                      .cls("plant-api-heading"),
                  el("div", apiRows)
                      .cls("plant-api-scroll")
                      .attr("tabindex", "0")
                      .attr("role", "region")
                      .attr("aria-label", "API connections for " + plant.get("name")),
                  el(
                          "footer",
                          el("span", "API endpoints for this plant"),
                          el("button", "Done")
                              .attr("type", "button")
                              .attr("popovertarget", apiPopup)
                              .attr("popovertargetaction", "hide")
                              .flag("autofocus", true)
                              .cls("primary-action"))
                      .cls("plant-api-popup-footer"))
              .attr("id", apiPopup)
              .attr("popover", "auto")
              .attr("role", "dialog")
              .attr("aria-labelledby", apiPopup + "-title")
              .cls("plant-api-popup"));
    }
    String deletePopup = "delete-plant-" + plant.get("id");
    var actions =
        el(
                "div",
                action("Edit", url("/admin/lss-plants/edit", Map.of("id", plant.get("id"))), false),
                el("button", "Delete")
                    .attr("type", "button")
                    .attr("popovertarget", deletePopup)
                    .cls("plant-delete-button"),
                el(
                        "div",
                        el("h2", "Delete plant?").attr("id", deletePopup + "-title"),
                        el("p", "Are you sure you want to delete this plant?")
                            .attr("id", deletePopup + "-message"),
                        el("strong", plant.get("name") + " (" + plant.get("plantId") + ")")
                            .cls("plant-delete-name"),
                        c.post(
                            "/admin/plants/" + plant.get("id") + "/delete",
                            "plant-delete-confirmation",
                            input("confirmation", "hidden", "delete"),
                            el("button", "Cancel")
                                .attr("type", "button")
                                .attr("popovertarget", deletePopup)
                                .attr("popovertargetaction", "hide")
                                .flag("autofocus", true)
                                .cls("secondary-action"),
                            button("Delete", false).cls("plant-delete-button")))
                    .attr("popover", "auto")
                    .attr("id", deletePopup)
                    .attr("role", "alertdialog")
                    .attr("aria-labelledby", deletePopup + "-title")
                    .attr("aria-describedby", deletePopup + "-message")
                    .cls("plant-delete-popup"))
            .cls("plant-row-actions");
    return el(
            "tr",
            plantCell("ID", plant.get("id")),
            plantCell("Plant ID", plant.get("plantId")),
            plantCell("Plant name", plant.get("name")),
            plantCell(
                "Zone",
                Objects.toString(plant.get("zone"), "").isBlank() ? "—" : plant.get("zone")),
            plantCell("State", plant.get("state")),
            plantCell(
                "Capacity (MW)",
                plant.get("capacityMw") instanceof java.math.BigDecimal capacity
                    ? capacity.setScale(2, java.math.RoundingMode.HALF_UP).toPlainString()
                    : plant.get("capacityMw")),
            plantCell("Configured APIs", endpoints),
            plantCell("Actions", actions))
        .cls("plant-record");
  }

  private static Element plantCell(String label, Object value) {
    return el("td", value).attr("data-label", label);
  }

  private static void addPlantApi(
      List<Element> container,
      Set<String> names,
      PageContext c,
      Map<String, Object> plant,
      String label,
      Object endpoint) {
    Map<String, ApiTestHistoryService.Entry> latest = c.value("latestApiTests");
    if (latest != null
        && !new PlantConfigFilter(0, c.text("filterType"), c.text("filterResult"))
            .api(((Number) plant.get("id")).intValue(), label, latest)) return;
    if (endpoint != null && !endpoint.toString().isBlank()) {
      names.add(label);
      container.add(
          el(
                  "div",
                  el("strong", label),
                  el("span", endpoint).attr("title", endpoint),
                  c.post(
                      "/admin/plants/" + plant.get("id") + "/test",
                      "plant-inline-api-test",
                      input("dataType", "hidden", label.equals("DDQ") ? "Rolling 24 Hour" : label),
                      button("Test API", false)
                          .attr("aria-label", "Test " + label + " API for " + plant.get("name"))))
              .cls("plant-api-item"));
    }
  }

  private static Element plantFilters(PageContext c) {
    var location =
        el(
                "div",
                el("h3", "Location"),
                el("p", "Choose a group, then select the locations to include.").cls("filter-hint"))
            .cls("location-filter-panel");
    var all = c.<LssPlant>list("filterPlants");
    var plantOptions = new LinkedHashMap<String, String>();
    all.forEach(p -> plantOptions.put(String.valueOf(p.id), p.plantId + " · " + p.name));
    location.add(
        locationGroup(c, "all", "All locations", "No location restrictions", "", Map.of()));
    location.add(
        locationGroup(
            c, "plant", "Plant", "Select individual plants", "locationPlants", plantOptions));
    for (String mode : List.of("state", "region", "zone")) {
      var options = new TreeMap<String, String>(String.CASE_INSENSITIVE_ORDER);
      for (LssPlant plant : all) {
        String value =
            switch (mode) {
              case "state" -> plant.state;
              case "region" -> PlantLocationFilter.region(plant.state);
              default -> plant.zone;
            };
        if (value != null && !value.isBlank()) options.putIfAbsent(value, value);
      }
      String title = Character.toUpperCase(mode.charAt(0)) + mode.substring(1);
      String name = "location" + title + "s";
      location.add(
          locationGroup(
              c,
              mode,
              title,
              mode.equals("region")
                  ? "Regions derived from plant states"
                  : "Values saved in Plant Config",
              name,
              options));
    }
    var types = el("select").attr("id", "filter-type").attr("name", "filterType");
    for (String type : List.of("", "DDQ", "MMF"))
      types.add(option(type, type.isEmpty() ? "All API types" : type, c.text("filterType")));
    var results = el("select").attr("id", "filter-result").attr("name", "filterResult");
    for (String result : List.of("", "Success", "Failed", "Not tested"))
      results.add(
          option(result, result.isEmpty() ? "All results" : result, c.text("filterResult")));
    return el(
            "form",
            el(
                "header",
                el(
                    "div",
                    el("span", "PLANT DIRECTORY").cls("section-label"),
                    el("h2", "Filter plants")),
                el(
                        "span",
                        all.size() + (all.size() == 1 ? " plant available" : " plants available"))
                    .cls("plant-filter-count")),
            el(
                    "div",
                    location,
                    el(
                            "div",
                            el("h3", "Refine results"),
                            el("p", "Search by name or narrow down the API results.")
                                .cls("filter-hint"),
                            el(
                                "div",
                                el("label", "Search records").attr("for", "search"),
                                input("q", "search", c.text("q"))
                                    .attr("id", "search")
                                    .attr("maxlength", 100)
                                    .attr("placeholder", "Search plant records…")),
                            el("div", el("label", "API type").attr("for", "filter-type"), types),
                            el(
                                "div",
                                el("label", "Test result").attr("for", "filter-result"),
                                results))
                        .cls("api-filter-panel"))
                .cls("plant-filter-columns"),
            el(
                "footer",
                el(
                        "p",
                        "Select × to remove your selection and"
                            + " choose another location filter.")
                    .cls("filter-hint"),
                el(
                        "div",
                        action("Clear", "/admin/lss-plants", false),
                        button("Apply filters", true)
                            .attr("name", "locationMode")
                            .attr(
                                "value",
                                c.text("locationMode").isBlank() ? "all" : c.text("locationMode")))
                    .cls("java-actions")))
        .attr("method", "get")
        .attr("action", "/admin/lss-plants")
        .cls("plant-config-filters location-query");
  }

  private static Element locationGroup(
      PageContext c,
      String mode,
      String title,
      String hint,
      String name,
      Map<String, String> options) {
    String selected = c.text("locationMode");
    if (selected.isBlank()) selected = "all";
    boolean active = selected.equals(mode);
    boolean locked = !selected.equals("all") && !active;
    var card =
        el(
                "section",
                el(
                        "button",
                        el("span", active ? "●" : "○")
                            .cls("location-indicator")
                            .attr("aria-hidden", "true"),
                        el("span", el("strong", title), el("small", hint))
                            .cls("location-mode-label"))
                    .attr("type", "submit")
                    .attr("name", "locationMode")
                    .attr("value", mode)
                    .attr("id", "location-mode-" + mode)
                    .flag("disabled", locked || active)
                    .attr("aria-pressed", active)
                    .attr(
                        "title",
                        locked
                            ? "Remove the selected location filter with × to unlock this option."
                            : "Select " + title)
                    .cls("location-mode-button"))
            .cls("location-choice" + (active ? " is-active" : "") + (locked ? " is-locked" : ""));
    if (!mode.equals("all")) {
      var choices =
          el("fieldset")
              .cls("location-values")
              .flag("disabled", !active)
              .flag("hidden", !active)
              .attr("aria-label", title + " selections");
      var picked = Arrays.asList(c.text(name).split(","));
      options.forEach(
          (value, label) ->
              choices.add(
                  el(
                          "label",
                          input(name, "checkbox", value)
                              .flag(
                                  "checked",
                                  picked.stream().anyMatch(v -> v.equalsIgnoreCase(value))),
                          el("span", label))
                      .cls("location-value")));
      if (options.isEmpty())
        choices.add(
            el(
                    "p",
                    "No "
                        + title.toLowerCase(Locale.ROOT)
                        + " values available. Add them in Plant Config.")
                .cls("filter-hint"));
      card.add(choices);
      card.add(
          el("button", "×")
              .attr("type", "submit")
              .attr("name", "locationMode")
              .attr("value", "all")
              .attr("aria-label", "Remove " + title + " filter")
              .attr("title", "Remove " + title + " filter")
              .flag("hidden", !active)
              .cls("secondary-action location-unlock"));
    }
    return card;
  }

  static Element edit(PageContext c) {
    Resources.Definition resource = c.value("resource");
    if (resource.type() == LssPlant.class) return plantEdit(c);
    var form =
        c.post(
            resource.path() + "/save",
            "java-form java-edit",
            input("id", "hidden", c.model().get("id")));
    for (Map<String, Object> item : c.<Map<String, Object>>list("fields")) {
      String name = item.get("name").toString();
      List<?> options = (List<?>) item.get("options");
      Element control;
      if (!options.isEmpty()) control = select(name, options, item.get("value"));
      else if (item.get("type").equals("checkbox"))
        control =
            input(name, "checkbox", "true")
                .flag("checked", Boolean.TRUE.equals(item.get("checked")));
      else
        control = input(name, item.get("type").toString(), item.get("value")).attr("step", "any");
      form.add(field(item.get("label").toString(), control));
    }
    form.add(
        el(
                "p",
                "Times use HH:mm. Weekly schedules use a weekday name. Four-month schedules use a"
                    + " day number and comma-separated months. Uncheck Active to suspend a"
                    + " configuration.")
            .cls("page-subtitle"),
        el("div", button("Save configuration", true), action("Cancel", resource.path(), false))
            .cls("java-actions"));
    return el(
        "section",
        heading(
            "Web services configuration",
            c.text("title"),
            null,
            action("Back to list", resource.path(), false)),
        el("article", form).cls("panel"));
  }

  private static Element plantState(String current) {
    var states =
        List.of(
            "Johor",
            "Kedah",
            "Kelantan",
            "Melaka",
            "Negeri Sembilan",
            "Pahang",
            "Perak",
            "Perlis",
            "Pulau Pinang",
            "Sabah",
            "Sarawak",
            "Selangor",
            "Terengganu",
            "Kuala Lumpur",
            "Labuan",
            "Putrajaya");
    var control = el("select").attr("name", "state");
    control.add(
        el("option", "Select a state")
            .attr("value", "")
            .flag("selected", current == null || current.isBlank()));
    for (String state : states)
      control.add(
          el("option", state)
              .attr("value", state)
              .flag("selected", state.equalsIgnoreCase(current)));
    if (current != null
        && !current.isBlank()
        && states.stream().noneMatch(state -> state.equalsIgnoreCase(current)))
      control.add(el("option", current).attr("value", current).flag("selected", true));
    return control;
  }

  private static Element plantEdit(PageContext c) {
    LssPlant plant = c.value("plant");
    Map<String, String> endpoints = c.value("plantApiEndpoints");
    Map<String, String> savedEndpoints = c.value("savedPlantApiEndpoints");
    var selectedGroups = c.request().getParameterValues("apiGroups");
    boolean ddqSelected =
        selectedGroups == null
            ? !plant.ddqApiEndpoint.isBlank()
                || PlantApiSlots.ALL.stream()
                    .anyMatch(
                        slot ->
                            slot.apiType().equals("DDQ")
                                && (!endpoints.getOrDefault(slot.field(), "").isBlank()
                                    || slot.field().equals(c.text("apiSaveField"))))
            : Arrays.asList(selectedGroups).contains("DDQ");
    boolean mmfSelected =
        selectedGroups == null
            ? !plant.mmfApiEndpoint.isBlank()
                || PlantApiSlots.ALL.stream()
                    .anyMatch(
                        slot ->
                            slot.apiType().equals("MMF")
                                && (!endpoints.getOrDefault(slot.field(), "").isBlank()
                                    || slot.field().equals(c.text("apiSaveField"))))
            : Arrays.asList(selectedGroups).contains("MMF");
    long count = savedEndpoints.values().stream().filter(value -> !value.isBlank()).count();
    var form =
        c.post(
            "/admin/lss-plants/save",
            "java-form plant-editor",
            input("id", "hidden", plant.id == 0 ? "" : plant.id));
    var identity =
        el(
                "section",
                el(
                        "div",
                        el("span", "01").cls("plant-step"),
                        el(
                            "div",
                            el("h2", "Plant details"),
                            el(
                                "p",
                                "Register the plant first. API connections can be added whenever"
                                    + " they are ready.")))
                    .cls("plant-section-heading"),
                el(
                        "div",
                        field(
                            "Plant ID",
                            input("plantId", "text", plant.plantId)
                                .attr("maxlength", 20)
                                .attr("inputmode", "numeric")
                                .attr("pattern", "[0-9]+")
                                .attr("title", "Use numbers only, for example 01 or 02.")
                                .flag("required", plant.id != 0)
                                .flag("readonly", plant.id == 0)
                                .attr(
                                    "placeholder",
                                    plant.id == 0
                                        ? "Assigned automatically when saved"
                                        : "e.g. 01")),
                        field(
                            "Plant name",
                            input("name", "text", plant.name)
                                .attr("maxlength", 160)
                                .flag("required", true)
                                .attr("placeholder", "e.g. BDNS or KMPR")),
                        field(
                            "Zone",
                            input("zone", "text", plant.zone)
                                .attr("maxlength", 60)
                                .attr("list", "plant-zone-options")
                                .attr("placeholder", "Select or enter a zone")),
                        field("State", plantState(plant.state)),
                        field(
                            "Installed capacity (MW)",
                            input("capacityMw", "number", plant.capacityMw)
                                .attr("step", "any")
                                .attr("min", 0)
                                .flag("required", true)))
                    .cls("plant-detail-grid"),
                el(
                        "datalist",
                        List.of("Northern", "Central", "Southern", "Eastern", "Sabah", "Sarawak")
                            .stream()
                            .map(zone -> el("option").attr("value", zone))
                            .toList())
                    .attr("id", "plant-zone-options"))
            .cls("plant-editor-card");
    var connections =
        el("details")
            .cls("plant-connections plant-editor-card")
            .flag("open", count > 0 || !c.text("apiSaveField").isBlank());
    connections.add(
        el(
            "summary",
            el("span", "02").cls("plant-step"),
            el(
                "span",
                el("strong", "API connections"),
                el("small", "Optional · " + count + " dataset links configured")),
            el("span", count == 0 ? "+ Add APIs" : "Manage APIs").cls("plant-add-api")));
    var slots = el("div").cls("plant-api-grid");
    Map<String, String> apiErrors = c.value("apiSaveErrors");
    if (apiErrors == null) apiErrors = Map.of();
    for (var slot : PlantApiSlots.ALL) {
      String value = endpoints.getOrDefault(slot.field(), "");
      String savedValue = savedEndpoints.getOrDefault(slot.field(), "");
      String datasetWarning = PlantApiSlots.datasetWarning(value, slot.dataType());
      boolean failed = apiErrors.containsKey(slot.field());
      boolean result = failed || slot.field().equals(c.text("apiSaveField"));
      var input =
          input(slot.field(), "url", value)
              .attr("maxlength", 1000)
              .attr(
                  "placeholder",
                  "https://provider.example/" + plant.plantId.toLowerCase(Locale.ROOT) + "/…")
              .attr("aria-label", slot.label() + " API URL");
      slots.add(
          el(
                  "details",
                  el(
                      "summary",
                      el(
                          "span",
                          el("small", slot.apiType()).cls("plant-api-kind"),
                          el("strong", slot.label())),
                      el(
                              "span",
                              !value.equals(savedValue)
                                  ? "Not saved"
                                  : savedValue.isBlank() ? "+ Add URL" : "Configured")
                          .cls("plant-api-badge")),
                  el(
                          "div",
                          el("p", slot.hint()),
                          field("API endpoint URL", input),
                          button("Test & Save API", true)
                              .attr("name", "saveApi")
                              .attr("value", slot.field())
                              .attr(
                                  "formaction",
                                  "/admin/lss-plants/save-api#plant-api-" + slot.field())
                              .cls("primary-action plant-api-save"),
                          result
                              ? el(
                                      "p",
                                      failed
                                          ? apiErrors.get(slot.field())
                                          : c.text("apiSaveMessage"))
                                  .cls(
                                      "plant-api-result "
                                          + (!failed && c.yes("apiSaveSuccess")
                                              ? "api-save-success"
                                              : "api-save-failed"))
                                  .attr("role", "status")
                                  .attr("aria-live", "polite")
                              : null,
                          datasetWarning.isBlank()
                              ? null
                              : el("p", datasetWarning)
                                  .cls("plant-api-result api-dataset-warning")
                                  .attr("role", "status"),
                          el(
                              "small",
                              "The connection is tested before saving. Failed URLs are not saved."
                                  + " Clear the URL and save to remove this connection."))
                      .cls("plant-api-body"))
              .cls("plant-api-slot")
              .attr("data-api-type", slot.apiType())
              .attr("id", "plant-api-" + slot.field())
              .flag("open", !value.isBlank() || result));
    }
    connections.add(
        el(
                "div",
                el(
                        "p",
                        "Add only the APIs available for this plant. Each dataset can use its own"
                            + " URL; all fields are optional.")
                    .cls("plant-api-help"),
                el(
                        "fieldset",
                        el("legend", "Choose API types"),
                        el("p", "Select DDQ, MMF or both."),
                        el(
                                "div",
                                el(
                                    "label",
                                    input("apiGroups", "checkbox", "DDQ")
                                        .attr("id", "plant-group-ddq")
                                        .flag("checked", ddqSelected),
                                    el(
                                        "span",
                                        el("strong", "DDQ"),
                                        el("small", "Solar forecasts"))),
                                el(
                                    "label",
                                    input("apiGroups", "checkbox", "MMF")
                                        .attr("id", "plant-group-mmf")
                                        .flag("checked", mmfSelected),
                                    el(
                                        "span",
                                        el("strong", "MMF"),
                                        el("small", "Weather measurements"))))
                            .cls("plant-api-type-options"))
                    .cls("plant-api-type-picker"),
                el("p", "Select an API type above to add its connections.")
                    .cls("plant-api-type-empty"),
                slots,
                el(
                        "details",
                        el("summary", "General DDQ / MMF links (optional)"),
                        el(
                            "p",
                            "Use these if your provider supplies one general URL. A"
                                + " dataset-specific URL takes priority."),
                        el(
                                "div",
                                field(
                                        "General DDQ URL",
                                        input("ddqApiEndpoint", "url", plant.ddqApiEndpoint)
                                            .attr("maxlength", 1000))
                                    .cls("plant-general-ddq"),
                                field(
                                        "General MMF URL",
                                        input("mmfApiEndpoint", "url", plant.mmfApiEndpoint)
                                            .attr("maxlength", 1000))
                                    .cls("plant-general-mmf"))
                            .cls("plant-detail-grid"))
                    .cls("plant-general-apis")
                    .flag(
                        "open", !plant.ddqApiEndpoint.isBlank() || !plant.mmfApiEndpoint.isBlank()))
            .cls("plant-connections-body"));
    var status =
        el(
                "aside",
                el("span", "PLANT STATUS").cls("section-label"),
                el("h2", "Ready when you are"),
                field(
                    "Active plant",
                    input("isActive", "checkbox", "true").flag("checked", plant.isActive)),
                el("p", "Keep this plant active to include it in collection selections."),
                el(
                        "div",
                        el("strong", "No API yet?"),
                        el(
                            "p",
                            "Save the plant now and add its connections later. Data collection"
                                + " requires a configured API."))
                    .cls("plant-optional-note"))
            .cls("plant-editor-card plant-status-card");
    form.add(
        el("div", el("div", identity, connections).cls("plant-editor-main"), status)
            .cls("plant-editor-layout"),
        el(
                "footer",
                el(
                    "p",
                    plant.id == 0
                        ? "Plant ID is assigned automatically. Only plant name and capacity are"
                            + " required."
                        : "Plant name and capacity are required."),
                el("div", action("Cancel", "/admin/lss-plants", false), button("Save plant", true))
                    .cls("java-actions"))
            .cls("plant-editor-footer"));
    return el(
            "section",
            heading(
                "Plant configuration",
                plant.id == 0 ? "Add plant" : "Edit plant",
                "Manage plant information and connect the APIs available for this site.",
                action("Back to plants", "/admin/lss-plants", false)),
            form,
            c.text("plantSaveError").isBlank()
                ? null
                : RequestErrorPopup.render(c.text("plantSaveError"), false))
        .cls("plant-editor-page");
  }

  static Element users(PageContext c) {
    Accounts.Principal account = c.value("account");
    Map<String, String> roles = c.value("roles");
    var rows = new ArrayList<Element>();
    for (ApplicationUser user : c.<ApplicationUser>list("users")) {
      Element review =
          user.id.equals(account.id())
              ? el("span", "Your account")
              : user.accessStatus.equals("Pending")
                  ? c.post(
                      "/admin/users/" + user.id + "/review",
                      "java-user-review",
                      button("Approve", true).attr("name", "decision").attr("value", "Approved"),
                      button("Reject", false).attr("name", "decision").attr("value", "Rejected"))
                  : c.post(
                      "/admin/users/" + user.id + "/review",
                      "java-user-review",
                      field(
                          "Account status",
                          el(
                                  "select",
                                  option("Approved", "Active", user.accessStatus),
                                  option("Rejected", "Rejected", user.accessStatus),
                                  option("Inactive", "Inactive", user.accessStatus))
                              .attr("name", "decision")),
                      button("Save status", false));
      rows.add(
          row(
              el("strong", Accounts.displayName(user.displayName))
                  .add(el("small", user.userName).cls("java-block")),
              user.email,
              el("span", "Approved".equals(user.accessStatus) ? "Active" : user.accessStatus)
                  .cls(
                      "account-state account-state-"
                          + switch (user.accessStatus) {
                            case "Approved" -> "approved";
                            case "Pending" -> "pending";
                            case "Rejected" -> "rejected";
                            default -> "inactive";
                          }),
              roles.get(user.id).isBlank() ? "Unassigned" : Accounts.ADMIN,
              review));
    }
    return el(
        "section",
        heading(
            "Access control",
            "Access requests",
            "Approve or reject new accounts and manage existing access."),
        el(
                "article",
                table(
                    List.of("User", "Email", "Status", "Current role", "Manage status"),
                    rows,
                    "No users found."))
            .cls("panel"));
  }
}
