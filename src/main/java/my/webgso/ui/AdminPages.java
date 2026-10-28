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
      if (editable)
        values.add(link("Edit", url(resource.path() + "/edit", Map.of("id", record.get("id")))));
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
                table(headers, rows, "No records found."),
                OperationsPages.pagination(c, "Record pages", "q"))
            .cls("panel"));
    return page;
  }

  private static Element plantRow(PageContext c, Map<String, Object> plant) {
    var endpoints = el("div").cls("plant-api-list");
    int endpointCount = 0;
    endpointCount += addPlantApi(endpoints, "DDQ", plant.get("ddqApiEndpoint"));
    endpointCount += addPlantApi(endpoints, "MMF", plant.get("mmfApiEndpoint"));
    Map<Integer, List<PlantApiConfiguration>> configured = c.value("plantApis");
    for (var api : configured.getOrDefault(((Number) plant.get("id")).intValue(), List.of()))
      endpointCount += addPlantApi(endpoints, api.dataType, api.apiLink);
    if (endpointCount == 0) endpoints.add(el("span", "Not configured").cls("plant-api-empty"));
    boolean active = Boolean.TRUE.equals(plant.get("isActive"));
    var actions =
        el(
                "div",
                action("Edit", url("/admin/lss-plants/edit", Map.of("id", plant.get("id"))), false),
                c.post(
                    "/admin/plants/" + plant.get("id") + "/test",
                    "plant-test-form",
                    select(
                            "dataType",
                            List.of(
                                "Rolling 24 Hour",
                                "Day Ahead",
                                "Week Ahead",
                                "4 Month Ahead",
                                "MMF"),
                            "Rolling 24 Hour")
                        .attr("aria-label", "Test API dataset for " + plant.get("name")),
                    button("Test API", false)))
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

  private static int addPlantApi(Element container, String label, Object endpoint) {
    if (endpoint != null && !endpoint.toString().isBlank()) {
      container.add(el("div", el("strong", label), el("span", endpoint)).cls("plant-api-item"));
      return 1;
    }
    return 0;
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
