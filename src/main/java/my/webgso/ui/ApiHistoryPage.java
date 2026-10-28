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
                          "api-retest-form api-failed-retest",
                          input("plantId", "hidden", test.plantId()),
                          input("dataType", "hidden", test.dataset()),
                          button("Test API again", true))
                      : null),
              test.httpStatus() == null ? "No response" : test.httpStatus(),
              test.responseMillis() == null ? "—" : test.responseMillis() + " ms",
              test.durationMillis() + " ms",
              test.message()));
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
                    c.post(
                        "/admin/api-test-history/test-all",
                        "api-retest-form",
                        button("Test all APIs", true).flag("disabled", c.number("total") == 0)))
                .cls("java-actions")),
        el(
                "article",
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
                        "Message"),
                    rows,
                    "No tests recorded yet. Press Test in Plant Config to create the"
                        + " first result."),
                OperationsPages.pagination(c, "Test history pages"))
            .cls("panel"));
  }
}
