package my.webgso.ui;

import static my.webgso.ui.Html.el;
import static my.webgso.ui.PageContext.*;

import java.util.*;
import my.webgso.Models.AuditEvent;
import my.webgso.Policies;
import my.webgso.ui.Html.Element;

final class AuditLogPage {
  static Element render(PageContext c) {
    var rows = new ArrayList<Element>();
    for (var event : c.<AuditEvent>list("events")) {
      String result = Objects.toString(event.result, "—");
      rows.add(row(el("span", date(event.occurredAt == null ? null : event.occurredAt.atZoneSameInstant(Policies.LOCAL),
              "dd MMM yyyy, HH:mm:ss")).cls("audit-time"),
          el("span", Objects.toString(event.actor, "—")).cls("audit-actor"),
          el("span", Objects.toString(event.action, "—")).cls("audit-action"),
          el("div", el("strong", Objects.toString(event.entityType, "—")),
              el("code", Objects.toString(event.entityReference, "—"))).cls("audit-record"),
          el("span", result).cls("audit-result " + (result.equalsIgnoreCase("Success")
              ? "audit-result-success" : result.toLowerCase(Locale.ROOT).matches(".*(fail|error|denied).*")
                  ? "audit-result-failed" : "audit-result-neutral")),
          Objects.toString(event.detail, "—")));
    }
    var pager = el("nav").cls("java-pagination").attr("aria-label", "Audit log pages");
    if (c.number("page") > 0) pager.add(action("Previous", c.pageUrl(c.number("page") - 1, "q"), false));
    pager.add(el("span", "Page " + (c.number("page") + 1)));
    if (c.yes("hasNext")) pager.add(action("Next", c.pageUrl(c.number("page") + 1, "q"), false));
    return el("section",
        heading("Administration", "Audit Log", "Review recorded system and account activity. All times use MYT (UTC+8)."),
        el("article",
            el("div", el("div", el("h2", "Activity records"),
                    el("p", "A chronological record of system and account changes.")),
                el("span", c.number("totalEvents") + " recorded events").cls("audit-count"))
                .cls("audit-toolbar"),
            el("form", el("div", el("label", "Search activity").attr("for", "audit-search"),
                    el("div", el("svg", el("circle").attr("cx", "11").attr("cy", "11").attr("r", "7"),
                            el("path").attr("d", "m16 16 4 4"))
                            .attr("viewBox", "0 0 24 24").attr("fill", "none")
                            .attr("stroke", "currentColor").attr("stroke-width", "1.8").attr("aria-hidden", "true"),
                        input("q", "search", c.text("q")).attr("id", "audit-search").attr("maxlength", "200")
                            .attr("placeholder", "Search by user, action, record or result…"))
                        .cls("audit-search-control")).cls("audit-search-field"),
                el("div", button("Search", true), action("Clear", "/admin/audit-log", false)).cls("java-actions"))
                .attr("method", "get").attr("action", "/admin/audit-log").cls("audit-search-form"),
            el("div", el("span", c.text("q").isBlank() ? "All activity" : "Search results for “" + c.text("q") + "”"),
                el("span", "Newest first · MYT (UTC+8)")).cls("audit-table-caption"),
            table(List.of("Time (MYT)", "User", "Action", "Record", "Result", "Details"), rows,
                "No audit events match your search."),
            el("div", el("p", rows.isEmpty() ? "No events to display" : "Showing "
                    + (c.number("page") * 50 + 1) + "–" + (c.number("page") * 50 + rows.size())
                    + " of " + c.number("totalEvents") + " events"), pager).cls("audit-footer"))
                .cls("panel audit-panel")).cls("audit-log-page");
  }
}
