package my.webgso.ui;

import static my.webgso.ui.Html.el;
import static my.webgso.ui.PageContext.*;

import java.util.*;
import my.webgso.ApiRetestService.Job;
import my.webgso.ui.Html.Element;

final class ApiRetestPage {
  private ApiRetestPage() {}

  static Element render(PageContext c) {
    Job job = c.value("apiRetestJob");
    var items = job.items;
    long done =
        items.stream().filter(item -> Set.of("Success", "Failed").contains(item.status())).count();
    long failed = items.stream().filter(item -> item.status().equals("Failed")).count();
    var rows = new ArrayList<Element>();
    items.forEach(
        item ->
            rows.add(
                row(
                    item.plantName(),
                    item.dataset(),
                    el("span", item.status())
                        .cls(
                            "api-last-result "
                                + (item.status().equals("Success")
                                    ? "api-test-success"
                                    : item.status().equals("Failed")
                                        ? "api-test-failed"
                                        : "api-test-untested")),
                    item.httpStatus() == null ? "—" : item.httpStatus(),
                    item.responseMillis() == null ? "—" : item.responseMillis() + " ms",
                    item.message())));
    return el(
        "section",
        heading(
            "API testing",
            job.complete ? "API check complete" : "Checking APIs…",
            done
                + " of "
                + items.size()
                + " completed · "
                + (done - failed)
                + " success · "
                + failed
                + " failed",
            job.configured
                ? action("Back to Plant Config", "/admin/lss-plants", false)
                : action("Back to API test history", "/admin/api-test-history", false)),
        el(
                "article",
                el(
                        "p",
                        job.complete
                            ? "Results have been saved. API test history displays one latest result"
                                + " per API."
                            : "This page updates automatically. You can leave this page while the"
                                + " check continues.")
                    .cls("api-history-note"),
                el("progress")
                    .attr("value", done)
                    .attr("max", items.size())
                    .attr("aria-label", "APIs checked")
                    .cls("api-retest-progress"),
                table(
                    List.of("Plant", "API", "Status", "HTTP", "Response time", "Message"),
                    rows,
                    "No APIs to check."))
            .cls("panel"));
  }
}
