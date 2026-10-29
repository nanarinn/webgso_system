package my.webgso.ui;

import static my.webgso.ui.Html.el;

import java.time.format.DateTimeFormatter;
import java.util.Locale;
import my.webgso.DatabaseConnectionStatus;
import my.webgso.Policies;
import my.webgso.ui.Html.Element;

final class DatabaseStatusCard {
  private DatabaseStatusCard() {}

  static Element render(PageContext c) {
    DatabaseConnectionStatus.Snapshot state = c.value("databaseConnection");
    String status =
        state == null
            ? "Database status unavailable"
            : state.connected() ? "Database connected" : "Database disconnected";
    String checked =
        state == null
            ? "Refresh the page to check"
            : "Checked "
                + state
                    .checkedAt()
                    .atZoneSameInstant(Policies.LOCAL)
                    .format(DateTimeFormatter.ofPattern("HH:mm:ss", Locale.ENGLISH))
                + " MYT";
    return el(
            "div",
            el("span").cls("database-status-dot").attr("aria-hidden", "true"),
            el("div", el("strong", status), el("small", checked)))
        .cls(
            "sidebar-database-status "
                + (state == null ? "unknown" : state.connected() ? "connected" : "disconnected"))
        .attr("role", "status")
        .attr(
            "title",
            "Database connection checked when this page loaded. Refresh for the latest status.");
  }
}
