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
                  "State",
                  "Capacity (MW)",
                  "API",
                  "Status",
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
      case "/admin/lss-plants" -> tabs.add(link("Plant API links", "/admin/api-configurations"));
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
                resource.path().equals("/admin/anomalies")
                    ? action("← Back to Data Quality", "/admin/data-quality", false)
                    : editable ? action("Add record", resource.path() + "/edit", true) : null),
            tabs);
    page.add(
        el(
                "article",
                el(
                        "form",
                        el("label", "Search records").attr("for", "search"),
                        input("q", "text", c.text("q")).attr("id", "search").attr("maxlength", 100),
                        button("Search", false),
                        link("Clear", resource.path()))
                    .cls("java-search")
                    .attr("method", "get"),
                table(headers, rows, "No records found.").attr("data-plant-table", plants),
                OperationsPages.pagination(c, "Record pages", "q"))
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
    addPlantApi(apiRows, "DDQ", plant.get("ddqApiEndpoint"));
    addPlantApi(apiRows, "MMF", plant.get("mmfApiEndpoint"));
    Map<Integer, List<PlantApiConfiguration>> configured = c.value("plantApis");
    for (var api : configured.getOrDefault(((Number) plant.get("id")).intValue(), List.of()))
      addPlantApi(apiRows, api.dataType, api.apiLink);
    if (apiRows.isEmpty()) endpoints.add(el("span", "Not configured").cls("plant-api-empty"));
    else {
      String apiPopup = "plant-apis-" + plant.get("id");
      endpoints.add(
          el("button", apiRows.size() + (apiRows.size() == 1 ? " API" : " APIs"))
              .attr("type", "button")
              .attr("popovertarget", apiPopup)
              .cls("plant-api-count-button"),
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
                  el("div", el("span", "Dataset"), el("span", "API URL")).cls("plant-api-heading"),
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
    boolean active = Boolean.TRUE.equals(plant.get("isActive"));
    var availableTests = new LinkedHashSet<String>();
    if (plant.get("ddqApiEndpoint") != null && !plant.get("ddqApiEndpoint").toString().isBlank())
      availableTests.addAll(Policies.DDQ_TYPES);
    if (plant.get("mmfApiEndpoint") != null && !plant.get("mmfApiEndpoint").toString().isBlank())
      availableTests.add("MMF");
    for (var api : configured.getOrDefault(((Number) plant.get("id")).intValue(), List.of()))
      if (api.apiLink != null && !api.apiLink.isBlank()) availableTests.add(api.dataType);
    var testTypes =
        availableTests.isEmpty() ? List.of("No API configured") : List.copyOf(availableTests);
    String deletePopup = "delete-plant-" + plant.get("id");
    var actions =
        el(
                "div",
                action("Edit", url("/admin/lss-plants/edit", Map.of("id", plant.get("id"))), false),
                c.post(
                    "/admin/plants/" + plant.get("id") + "/test",
                    "plant-test-form",
                    select("dataType", testTypes, testTypes.getFirst())
                        .flag("disabled", availableTests.isEmpty())
                        .attr("aria-label", "Test API dataset for " + plant.get("name")),
                    button("Test API", false).flag("disabled", availableTests.isEmpty())),
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
    return row(
        plant.get("id"),
        plant.get("plantId"),
        plant.get("name"),
        plant.get("state"),
        plant.get("capacityMw"),
        endpoints,
        c.post(
            "/admin/plants/" + plant.get("id") + "/status",
            "plant-status-toggle",
            input("status", "hidden", active ? "Inactive" : "Active"),
            el("button", active ? "Active" : "Inactive")
                .attr("type", "submit")
                .attr("title", "Click to set " + (active ? "Inactive" : "Active"))
                .attr(
                    "aria-label",
                    (active ? "Active" : "Inactive")
                        + " — click to "
                        + (active ? "deactivate " : "activate ")
                        + plant.get("name"))
                .cls("plant-status " + (active ? "plant-active" : "plant-inactive"))),
        actions);
  }

  private static void addPlantApi(List<Element> container, String label, Object endpoint) {
    if (endpoint != null && !endpoint.toString().isBlank()) {
      container.add(
          el("div", el("strong", label), el("span", endpoint).attr("title", endpoint))
              .cls("plant-api-item"));
    }
  }

  static Element edit(PageContext c) {
    Resources.Definition resource = c.value("resource");
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
            "Repository configuration",
            c.text("title"),
            null,
            action("Back to list", resource.path(), false)),
        el("article", form).cls("panel"));
  }

  static Element users(PageContext c) {
    Accounts.Principal account = c.value("account");
    Map<String, String> roles = c.value("roles");
    var rows = new ArrayList<Element>();
    for (ApplicationUser user : c.<ApplicationUser>list("users")) {
      Element review =
          user.id.equals(account.id())
              ? el("span", "Your account")
              : c.post(
                  "/admin/users/" + user.id + "/review",
                  "java-user-review",
                  field(
                      "Decision",
                      el(
                              "select",
                              option("Approved", "Approve", "Approved"),
                              option("Rejected", "Reject", "Approved"),
                              option("Inactive", "Deactivate", "Approved"))
                          .attr("name", "decision")),
                  field("Review note", input("note", "text", null).attr("maxlength", 1000)),
                  button("Save review", false));
      rows.add(
          row(
              el("strong", Accounts.displayName(user.displayName))
                  .add(el("small", user.userName).cls("java-block")),
              user.email,
              user.accessStatus,
              roles.get(user.id).isBlank() ? "Pending approval" : Accounts.ADMIN,
              review));
    }
    return el(
        "section",
        heading(
            "Access control",
            "Admin Accounts",
            "Review access requests and manage Admin accounts."),
        el(
                "article",
                table(
                    List.of("User", "Email", "Status", "Current role", "Review access"),
                    rows,
                    "No users found."))
            .cls("panel"));
  }
}
