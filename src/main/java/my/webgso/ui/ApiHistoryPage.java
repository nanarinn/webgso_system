package my.webgso.ui;

import static my.webgso.ui.Html.el;
import static my.webgso.ui.PageContext.*;

import java.time.ZoneId;
import java.util.*;
import my.webgso.ApiTestHistoryService.Entry;
import my.webgso.Models.LssPlant;
import my.webgso.ui.Html.Element;

final class ApiHistoryPage {
  private ApiHistoryPage() {}

  static Element render(PageContext c) {
    Map<Integer, LssPlant> plants = c.value("historyPlants");
    var exportFilters = new LinkedHashMap<String, Object>();
    for (String key : List.of("filterPlant", "filterApi", "filterResult", "dateFrom", "dateTo"))
      exportFilters.put(key, c.text(key));
    var rows = new ArrayList<Element>();
    for (Entry test : c.<Entry>list("testHistory")) {
      var plant = plants.get(test.plantId());
      rows.add(
          row(
              date(
                  test.testedAt().atZoneSameInstant(ZoneId.of("Asia/Kuala_Lumpur")),
                  "dd MMM yyyy, HH:mm:ss"),
              plant == null
                  ? "Deleted plant #" + test.plantId()
                  : plant.name + " · " + plant.plantId,
              test.dataset(),
              test.actor(),
              el(
                  "div",
                  el("span", test.outcome())
                      .cls(
                          "api-last-result "
                              + (test.outcome().equals("Success")
                                  ? "api-test-success"
                                  : "api-test-failed")),
                  plant != null
                      ? c.post(
                          "/admin/api-test-history/retest",
                          "api-retest-form api-failed-retest plant-inline-api-test",
                          input("plantId", "hidden", test.plantId()),
                          input("dataType", "hidden", test.dataset()),
                          input("page", "hidden", c.number("page")),
                          filterValues(c),
                          button("Test API again", true))
                      : null),
              test.httpStatus() == null ? "No response" : test.httpStatus(),
              test.responseMillis() == null ? "—" : test.responseMillis() + " ms",
              test.durationMillis() + " ms",
              test.message(),
              action("View raw response", "/admin/api-test-history/" + test.id() + "/response", false)));
    }
    return el(
        "section",
        heading(
            "API testing",
            "API test history",
            "Latest test result for each API across all plants.",
            el(
                    "div",
                    action("Back to Plant Config", "/admin/lss-plants", false),
                    action(
                        "Download CSV",
                        url("/admin/api-test-history/export", exportFilters),
                        false),
                    c.post(
                        "/admin/api-test-history/test-all",
                        "api-retest-form",
                        button("Test all APIs", true)
                            .flag("disabled", c.number("historyTotal") == 0)))
                .cls("java-actions")),
        el(
                "article",
                filters(c, plants),
                c.text("filterError").isEmpty()
                    ? null
                    : el("p", c.text("filterError")).attr("role", "alert"),
                el("p", c.number("total") + " API result(s) found.").cls("api-history-note"),
                el(
                        "p",
                        "One latest result per plant and API. Use Test all APIs to recheck"
                            + " previously tested APIs. Times use Malaysia Standard Time (UTC+8).")
                    .cls("api-history-note"),
                table(
                    List.of(
                        "Tested at (MYT)",
                        "Plant",
                        "API",
                        "Tested by",
                        "Result",
                        "HTTP",
                        "Response time",
                        "Test duration",
                        "Message", "Response"),
                    rows,
                    c.number("historyTotal") == 0
                        ? "No tests recorded yet. Press Test in Plant Config to create the first"
                            + " result."
                        : "No results match these filters. Try different filters or Clear."),
                OperationsPages.pagination(
                    c,
                    "Test history pages",
                    "filterPlant",
                    "filterApi",
                    "filterResult",
                    "dateFrom",
                    "dateTo"))
            .cls("panel"));
  }

  static Element response(PageContext c) {
    var saved = c.<my.webgso.ApiTestHistoryService.SavedResponse>value("savedResponse");
    var test = saved.test();
    return el("section",
        heading("API testing", "Raw API response", test.dataset() + " · Tested by " + test.actor(),
            action("Back to API test history", "/admin/api-test-history", false)),
        el("article",
            el("h2", test.outcome()),
            el("p", test.message()).attr("role", "status"),
            el("p", "HTTP status: " + (test.httpStatus() == null ? "No response" : test.httpStatus())
                + " · Response time: " + (test.responseMillis() == null ? "—" : test.responseMillis() + " ms")),
            el("p", "Tested at: " + date(test.testedAt().atZoneSameInstant(ZoneId.of("Asia/Kuala_Lumpur")), "dd MMM yyyy, HH:mm:ss") + " MYT"),
            el("p", "Content type: " + Objects.toString(saved.contentType(), "Not recorded")
                + " · Response size: " + (saved.bytes() == null ? "Not recorded" : saved.bytes() + " bytes")),
            el("div",
                el("h3", "Response body"),
                el("pre", el("code", saved.preview() == null
                    ? "No response body was saved for this test. For older tests, test the API again to capture a response. Connection failures may not return a body."
                    : saved.preview().isEmpty() ? "Empty response" : saved.preview())).attr("tabindex", "0"),
                el("p", "Common JSON credential fields are redacted. "
                    + (saved.truncated() ? "Preview limited to 12,000 characters; the complete response was used for testing." : "")))
                .cls("api-test-response"))
            .cls("panel"));
  }

  private static Element filterValues(PageContext c) {
    var values = el("div");
    for (String name : List.of("filterPlant", "filterApi", "filterResult", "dateFrom", "dateTo"))
      values.add(input(name, "hidden", c.text(name)));
    return values;
  }

  private static Element filters(PageContext c, Map<Integer, LssPlant> plants) {
    var plantOptions =
        el("select")
            .attr("name", "filterPlant")
            .add(option(0, "All plants", c.text("filterPlant")));
    plants.values().stream()
        .sorted(Comparator.comparing(plant -> plant.name, String.CASE_INSENSITIVE_ORDER))
        .forEach(
            plant ->
                plantOptions.add(
                    option(plant.id, plant.name + " · " + plant.plantId, c.text("filterPlant"))));
    var apiOptions =
        el("select").attr("name", "filterApi").add(option("", "All APIs", c.text("filterApi")));
    c.<String>list("historyDatasets")
        .forEach(dataset -> apiOptions.add(option(dataset, dataset, c.text("filterApi"))));
    var resultOptions =
        el("select")
            .attr("name", "filterResult")
            .add(
                option("", "All results", c.text("filterResult")),
                option("Success", "Success", c.text("filterResult")),
                option("Failed", "Failed", c.text("filterResult")));
    return el(
            "form",
            field("Plant", plantOptions),
            field("API", apiOptions),
            field("Result", resultOptions),
            field("From date (MYT)", input("dateFrom", "date", c.text("dateFrom"))),
            field("To date (MYT)", input("dateTo", "date", c.text("dateTo"))),
            el(
                    "div",
                    button("Apply filters", true),
                    action("Clear", "/admin/api-test-history", false))
                .cls("java-actions"))
        .attr("method", "get")
        .attr("action", "/admin/api-test-history")
        .cls("java-filters");
  }
}
